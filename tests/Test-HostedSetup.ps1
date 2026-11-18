[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer, [Parameter(Mandatory)][string]$TestRoot, [Parameter(Mandatory)][string]$Version, [string]$DesktopPayload)
$ErrorActionPreference = 'Stop'
$TestRoot = [IO.Path]::GetFullPath($TestRoot)
if ($DesktopPayload) {
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $env:RUNNER_TEMP) {
        throw 'Visible application entrypoint verification requires a disposable GitHub-hosted runner.'
    }
    $temporary = (Resolve-Path -LiteralPath $env:RUNNER_TEMP).Path.TrimEnd('\')
    if (-not $TestRoot.StartsWith($temporary + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The hosted entrypoint fixture must be inside RUNNER_TEMP.'
    }
    $DesktopPayload = (Resolve-Path -LiteralPath $DesktopPayload).Path
}
if (Test-Path -LiteralPath $TestRoot) { throw 'Use a fresh hosted-setup fixture.' }
New-Item -ItemType Directory -Path $TestRoot | Out-Null
$payload = Join-Path $TestRoot 'payload'; $root = Join-Path $TestRoot 'installation'; $dataRoot = Join-Path $TestRoot 'home'
$shortcutRoot = $TestRoot
$files = @{}
foreach ($relative in @('app/cli/vrcsetup.exe','app/desktop/VrcSetup.Desktop.exe')) {
    $path=Join-Path $payload $relative; New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    [IO.File]::WriteAllText($path,'Fixture only, never executed.'); $files[$relative]=(Get-FileHash -LiteralPath $path).Hash
}
[IO.File]::WriteAllText((Join-Path $payload '.vrcsetup-payload.json'), (@{Version=$Version;Files=$files}|ConvertTo-Json -Depth 5))
$checks=[Collections.Generic.List[string]]::new()
function Require([bool]$condition,[string]$message) { if(-not $condition){throw $message}; $checks.Add($message) }
function Test-Entrypoints {
    $expectedExe = Join-Path $root 'app/desktop/VrcSetup.Desktop.exe'
    $expectedProfile = Join-Path $dataRoot 'webview'
    $paths = [ordered]@{
        'Start menu shortcut' = Join-Path $shortcutRoot 'start/VRChat Project Setup/VRC Project Setup.lnk'
        'Desktop shortcut' = Join-Path $shortcutRoot 'desktop/VRC Project Setup.lnk'
        'CLI gui' = Join-Path $root 'app/cli/vrcsetup.exe'
    }
    $previousHome = $env:VRCSETUP_HOME
    $env:VRCSETUP_HOME = $dataRoot
    try {
        foreach ($label in $paths.Keys) {
            $launcher = $null
            $desktop = $null
            try {
                $start = [Diagnostics.ProcessStartInfo]::new($paths[$label])
                $start.UseShellExecute = $label -ne 'CLI gui'
                $start.WorkingDirectory = $root
                if ($label -eq 'CLI gui') {
                    $start.CreateNoWindow = $true
                    $start.ArgumentList.Add('gui')
                    $start.ArgumentList.Add('--no-catalog-refresh')
                }
                $launcher = [Diagnostics.Process]::Start($start)
                if ($label -eq 'CLI gui' -and (-not $launcher.WaitForExit(10000) -or $launcher.ExitCode -ne 0)) {
                    throw 'The installed CLI gui command did not launch the desktop.'
                }

                $deadline = [DateTime]::UtcNow.AddSeconds(30)
                do {
                    $desktop = Get-Process VrcSetup.Desktop -ErrorAction SilentlyContinue | Where-Object Path -eq $expectedExe | Select-Object -First 1
                    $browser = Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object {
                        $_.CommandLine -and $_.CommandLine.Contains($expectedProfile, [StringComparison]::OrdinalIgnoreCase) -and $_.ParentProcessId -eq $desktop.Id
                    } | Select-Object -First 1
                    if ($desktop -and $desktop.MainWindowHandle -ne 0 -and $browser) { break }
                    Start-Sleep -Milliseconds 300
                } while ([DateTime]::UtcNow -lt $deadline)

                Require ($desktop -and $desktop.MainWindowHandle -ne 0 -and $browser -and (Test-Path -LiteralPath (Join-Path $dataRoot 'config.json'))) "$label opens the installed React app and its scoped WebView2 profile"
                Require ($desktop.CloseMainWindow() -and $desktop.WaitForExit(15000)) "$label closes the app normally"
            }
            finally {
                if ($desktop) {
                    if (-not $desktop.HasExited -and $desktop.Path -eq $expectedExe) { $desktop.Kill($true) }
                    $desktop.Dispose()
                }
                if ($launcher) { $launcher.Dispose() }
            }
        }
    }
    finally { $env:VRCSETUP_HOME = $previousHome }
}
function Run-Hosted([string]$name,[bool]$automatic,[bool]$accept) {
    $pipeName='vrcsetup-setup-'+[guid]::NewGuid().ToString('N')
    $options=[IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
    $pipe=[IO.Pipes.NamedPipeServerStream]::new($pipeName,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,$options)
    $receipt=Join-Path $TestRoot ($name+'.json')
    $info=[Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Installer)); $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=[IO.Path]::GetTempPath()
    foreach($arg in @('--ui-pipe',$pipeName,'--source-payload',$payload,'--root',$root,'--home',$dataRoot,'--result',$receipt)){$info.ArgumentList.Add($arg)}
    if($automatic){$info.ArgumentList.Add('--silent')}
    $info.Environment['VRCSETUP_SKIP_PATH_UPDATE']='1'; $info.Environment['VRCSETUP_START_MENU_ROOT']=Join-Path $shortcutRoot 'start'; $info.Environment['VRCSETUP_DESKTOP_ROOT']=Join-Path $shortcutRoot 'desktop'
    $process=[Diagnostics.Process]::Start($info); $events=[Collections.Generic.List[object]]::new()
    try {
        if(-not $pipe.WaitForConnectionAsync().Wait(30000)){throw 'Setup engine did not connect.'}
        $reader=[IO.StreamReader]::new($pipe); $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true); $writer.AutoFlush=$true
        while($true) {
            $read=$reader.ReadLineAsync(); if(-not $read.Wait(30000)){throw 'Setup engine stalled.'}
            if($null -eq $read.Result){break}; $event=$read.Result|ConvertFrom-Json; $events.Add($event)
            $process.Refresh(); Require ($process.MainWindowHandle -eq 0) ($name+': engine creates no window')
            if($event.Kind -eq 'confirm'){$writer.WriteLine((@{Kind='confirm';Accepted=$accept}|ConvertTo-Json -Compress))}
            if($event.Kind -eq 'complete'){$writer.WriteLine((@{Kind='finish';StartMenu=$true;Desktop=$true;Launch=$false}|ConvertTo-Json -Compress))}
            if($event.Kind -eq 'failed'){throw 'Hosted installation failed.'}
            if($event.Kind -in @('finished','cancelled')){break}
        }
        if(-not $process.WaitForExit(15000)){throw 'Setup engine did not exit.'}
        Require ($process.ExitCode -eq 0) ($name+': engine exits successfully')
        if($name -eq 'manual') {
            $measured=@($events | Where-Object {$_.Kind -eq 'progress' -and $_.Phase -eq 'install'})
            Require ($measured.Count -gt 2 -and $measured[0].Completed -eq 0 -and $measured[-1].Completed -eq $measured[-1].Total) 'Installation reports measured file work through completion'
            Require (Test-Path (Join-Path $TestRoot 'desktop/VRC Project Setup.lnk')) 'Manual Finish applies desktop choice'
            Require (Test-Path (Join-Path $TestRoot 'start/VRChat Project Setup/VRC Project Setup.lnk')) 'Manual Finish applies Start choice'
        }
        if($automatic){ Require (@($events|Where-Object Kind -eq 'complete').Count -eq 0) 'Automatic update has no manual completion page' }
        return @($events)
    }
    finally { $pipe.Dispose(); if(-not $process.HasExited){$process.WaitForExit(5000)|Out-Null}; $process.Dispose() }
}
$null=Run-Hosted 'manual' $false $true
if ($DesktopPayload) {
    $fixturePayload = $payload
    $fixtureInstall = $root
    $fixtureHome = $dataRoot
    $payload = $DesktopPayload
    $root = Join-Path $TestRoot 'desktop-installation'
    $dataRoot = Join-Path $TestRoot 'desktop-home'
    $shortcutRoot = Join-Path $TestRoot 'desktop-entrypoints'
    $null = Run-Hosted 'entrypoints' $false $true
    Test-Entrypoints
    $payload = $fixturePayload
    $root = $fixtureInstall
    $dataRoot = $fixtureHome
    $shortcutRoot = $TestRoot
}
$marker=Join-Path $root '.vrcsetup-install.json'; $before=[IO.File]::ReadAllText($marker)
$events=Run-Hosted 'reinstall-declined' $false $false
Require (@($events|Where-Object {$_.Kind -eq 'confirm' -and $_.Phase -eq 'reinstall'}).Count -eq 1) 'Reinstall confirmation is requested'
Require ([IO.File]::ReadAllText($marker) -eq $before) 'Declining reinstall preserves the installation'
$record=$before|ConvertFrom-Json; $record.Version='99.0.0'; [IO.File]::WriteAllText($marker,($record|ConvertTo-Json -Depth 5)); $downgrade=[IO.File]::ReadAllText($marker)
$events=Run-Hosted 'downgrade-declined' $false $false
Require (@($events|Where-Object {$_.Kind -eq 'confirm' -and $_.Phase -eq 'downgrade'}).Count -eq 1) 'Downgrade confirmation is requested'
Require ([IO.File]::ReadAllText($marker) -eq $downgrade) 'Declining downgrade preserves the installation'
[IO.File]::WriteAllText($marker,$before)
$null=Run-Hosted 'automatic' $true $true
$outcome=Get-Content (Join-Path $TestRoot 'automatic.json') -Raw|ConvertFrom-Json
Require ($outcome.Success -and $outcome.ExternalUi -and -not $outcome.EngineWindowCreated) 'Receipt confirms a single external UI owner'
[IO.File]::WriteAllText((Join-Path $TestRoot 'checks.json'),(@{Passed=$true;Checks=@($checks)}|ConvertTo-Json -Depth 5))
[pscustomobject]@{Passed=$true;Checks=$checks.Count}
