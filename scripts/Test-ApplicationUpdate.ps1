[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BasePayload,
    [Parameter(Mandatory)][string]$UpdateInstaller,
    [string]$OutputDirectory,
    [switch]$UseRealNetwork,
    [switch]$DirectInstaller,
    [switch]$NextLaunch,
    [switch]$ReactTarget,
    [switch]$Rollback,
    [string]$ComponentsDirectory,
    [ValidateRange(30,600)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
if ($Rollback -and ($ReactTarget -or $NextLaunch -or $DirectInstaller -or $UseRealNetwork)) { throw 'Rollback verifies React Settings with an isolated feed and a stable WinUI target.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$BasePayload = [IO.Path]::GetFullPath($BasePayload)
$UpdateInstaller = [IO.Path]::GetFullPath($UpdateInstaller)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('.work/artifacts/update-e2e/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.StartsWith($BasePayload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $BasePayload.StartsWith($OutputDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Base payload and evidence must be separate folders.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing evidence is preserved.' }
if (-not (Test-Path -LiteralPath (Join-Path $BasePayload '.vrcsetup-payload.json'))) { throw 'Base must be a published payload.' }
$installerName = [IO.Path]::GetFileName($UpdateInstaller)
if ($installerName -notmatch '^(?:Install|Update)-VrcSetup-(\d+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?)(?:-build\.([1-9]\d*)|-from-\d+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?)?-win-x64\.exe$') { throw 'Use a published versioned installer.' }
$targetVersion = $Matches[1]
if ($Matches[2]) { $targetVersion += '+build.' + $Matches[2] }
$checksums = Join-Path ([IO.Path]::GetDirectoryName($UpdateInstaller)) 'SHA256SUMS.txt'
$checksumLine = @(Get-Content -LiteralPath $checksums | Where-Object { $_ -match ('^[A-Fa-f0-9]{64}\s+\*?' + [regex]::Escape($installerName) + '$') })
if ($checksumLine.Count -ne 1 -or ($checksumLine[0] -split '\s+')[0] -ne (Get-FileHash -LiteralPath $UpdateInstaller -Algorithm SHA256).Hash) { throw 'Candidate checksum mismatch.' }
$manifest = Get-Content -LiteralPath (Join-Path $BasePayload '.vrcsetup-payload.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.Files.psobject.Properties) {
    $file = [IO.Path]::GetFullPath((Join-Path $BasePayload $entry.Name))
    if (-not $file.StartsWith($BasePayload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Base payload path escaped its root.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Value) { throw ('Base payload checksum mismatch: ' + $entry.Name) }
}
$install = Join-Path $OutputDirectory 'install'; $testHome = Join-Path $OutputDirectory 'home'
[void][IO.Directory]::CreateDirectory($install); [void][IO.Directory]::CreateDirectory($testHome)
if ($ComponentsDirectory) {
    $cache = Join-Path $testHome 'updates/components'; [void][IO.Directory]::CreateDirectory($cache)
    Get-ChildItem -LiteralPath $ComponentsDirectory -File -Filter '*.zip' | Where-Object Name -Match '^[a-f0-9]{64}\.zip$' | Copy-Item -Destination $cache
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory '.vrcsetup-update-smoke'), 'Owned isolated updater fixture. No user projects.')
if ($ReactTarget -or $Rollback) { [IO.File]::WriteAllText((Join-Path $OutputDirectory '.react-profile-fixture'), 'Owned isolated React profile.') }
foreach ($item in Get-ChildItem -LiteralPath $BasePayload -Force) { Copy-Item -LiteralPath $item.FullName -Destination $install -Recurse }
$record = @{Version=$manifest.Version;Root=$install;InstalledUtc=[DateTime]::UtcNow;PreviousInstall=$null}
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $install '.vrcsetup-install.json') -Encoding utf8
$config = @{UnityProjectsRoot=(Join-Path $OutputDirectory 'projects');CheckUpdatesOnStartup=[bool]$NextLaunch;AutomaticUpdates=$true;IncludeBetaUpdates=$true;IncludeAlphaUpdates=[bool]$ReactTarget;Theme='Dark';SavedProjectNames=@{'update-test-sentinel'='preserve me'}}
$config.UnityEditorPath = 'Z:\Saved Unity\2022.3.22f1\Editor\Unity.exe'
$config.AutoHideCompletedImports = $true
[void][IO.Directory]::CreateDirectory($config.UnityProjectsRoot)
$configFile = Join-Path $testHome 'config.json'
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configFile -Encoding utf8
$specFile = Join-Path $OutputDirectory 'update-test.json'
@{Root=$OutputDirectory;Installer=$UpdateInstaller;Version=$targetVersion;SettingsHash=(Get-FileHash -LiteralPath $configFile -Algorithm SHA256).Hash;UseRealNetwork=[bool]$UseRealNetwork} | ConvertTo-Json | Set-Content -LiteralPath $specFile -Encoding utf8
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $install 'app/desktop/VrcSetup.Desktop.exe'))
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.WorkingDirectory = $install
if ($Rollback) {
    $start.ArgumentList.Add('--verify-stable-rollback')
    $start.ArgumentList.Add($OutputDirectory)
    $start.Environment['VRCSETUP_HOME'] = $testHome
} else {
    $start.ArgumentList.Add('--update-smoke-test')
    $start.ArgumentList.Add($specFile)
}
if ($NextLaunch) {
    if ($DirectInstaller) { throw 'Choose next-launch or direct-installer verification.' }
    $prefetch = [Diagnostics.ProcessStartInfo]::new((Join-Path $install 'app/desktop/VrcSetup.Desktop.exe')); $prefetch.UseShellExecute=$false; $prefetch.CreateNoWindow=$true; $prefetch.WorkingDirectory=[IO.Path]::GetTempPath()
    $prefetch.ArgumentList.Add('--prepare-automatic-update-smoke'); $prefetch.ArgumentList.Add($specFile)
    $before = (Get-FileHash (Join-Path $install '.vrcsetup-payload.json')).Hash
    $worker=[Diagnostics.Process]::Start($prefetch)
    if (-not $worker.WaitForExit($TimeoutSeconds * 1000)) { throw 'Background prefetch timed out.' }
    if ($worker.ExitCode -ne 0 -or -not (Test-Path (Join-Path $OutputDirectory 'prepared.json'))) {
        $failure = Join-Path $OutputDirectory 'failure.txt'; if(Test-Path $failure){Get-Content $failure}; throw 'Background preparation failed.'
    }
    if ((Get-FileHash (Join-Path $install '.vrcsetup-payload.json')).Hash -ne $before) { throw 'Prefetch modified the running installation.' }
    if ((Get-FileHash $configFile).Hash -ne (Get-Content $specFile -Raw|ConvertFrom-Json).SettingsHash) { throw 'Prefetch changed settings.' }
    $start.ArgumentList.Clear(); $start.ArgumentList.Add('--automatic-update-smoke'); $start.ArgumentList.Add($specFile)
}
if ($DirectInstaller) {
    if ($UseRealNetwork) { throw 'Direct installer mode does not test network discovery.' }
    $start = [Diagnostics.ProcessStartInfo]::new($UpdateInstaller)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WorkingDirectory = [IO.Path]::GetTempPath()
    foreach ($argument in @('--silent','--root',$install,'--home',$testHome,'--result',(Join-Path $testHome 'updates/last-result.json'),'--launch')) { $start.ArgumentList.Add($argument) }
    $start.Environment['VRCSETUP_UPDATE_SMOKE_SPEC'] = $specFile
    $start.Environment['VRCSETUP_HOME'] = $testHome
    $start.Environment['VRCSETUP_SKIP_PATH_UPDATE'] = '1'
    $start.Environment['VRCSETUP_START_MENU_ROOT'] = Join-Path $OutputDirectory 'start-menu'
    @{ DirectInstaller=$true; UsesProductionInstaller=$true } | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'handoff.json')
}
$process = [Diagnostics.Process]::Start($start)
$timer = [Diagnostics.Stopwatch]::StartNew()
$receipt = Join-Path $OutputDirectory $(if ($ReactTarget) { 'restarted-profile.json' } else { 'passed.json' })
$failure = Join-Path $OutputDirectory 'failure.txt'
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
    if ($process.HasExited -and -not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'handoff.json'))) { throw "Base app exited before updater handoff. Use a payload containing --update-smoke-test; inspect $OutputDirectory." }
    $installerResult = Join-Path $testHome 'updates/last-result.json'
    if (Test-Path -LiteralPath $installerResult) { $outcome = Get-Content -LiteralPath $installerResult -Raw | ConvertFrom-Json; if (-not $outcome.Success) { throw $outcome.Error } }
    if (Test-Path -LiteralPath $receipt) {
        $result = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
        if ($ReactTarget) {
            $installation = Get-Content -LiteralPath (Join-Path $install '.vrcsetup-install.json') -Raw | ConvertFrom-Json
            $previous = Get-Content -LiteralPath (Join-Path $installation.PreviousInstall '.vrcsetup-install.json') -Raw | ConvertFrom-Json
            $settingsHash = (Get-Content -LiteralPath $specFile -Raw | ConvertFrom-Json).SettingsHash
            if (-not $result.workspaceSettingsVisible -or $result.version -ne $targetVersion -or
                $installation.Version -ne $targetVersion -or $previous.Version -ne $manifest.Version -or
                (Get-FileHash -LiteralPath $configFile -Algorithm SHA256).Hash -ne $settingsHash) {
                throw 'React restart, profile preservation or retained installation check failed.'
            }
            $result | Add-Member -NotePropertyName Passed -NotePropertyValue $true
        }
        if (-not $result.Passed) { throw 'Updater did not report success.' }
        if ($Rollback) {
            $installed = Get-Content -LiteralPath (Join-Path $install '.vrcsetup-install.json') -Raw | ConvertFrom-Json
            $previous = Get-Content -LiteralPath (Join-Path $installed.PreviousInstall '.vrcsetup-install.json') -Raw | ConvertFrom-Json
            if ($installed.Version -ne $targetVersion -or $previous.Version -ne $manifest.Version) { throw 'Rollback installed or retained the wrong version.' }
        }
        if ($outcome.PSObject.Properties['DispatcherTicks']) {
            if ($outcome.DispatcherTicks -lt 2 -or $outcome.DispatcherMaximumGapMs -gt 1000) { throw 'Updater dispatcher stalled during installation.' }
            $result | Add-Member -NotePropertyName DispatcherMaximumGapMs -NotePropertyValue $outcome.DispatcherMaximumGapMs
        }
        $result | Add-Member -NotePropertyName BaseVersion -NotePropertyValue $manifest.Version
        $result | Add-Member -NotePropertyName ElapsedSeconds -NotePropertyValue ([math]::Round($timer.Elapsed.TotalSeconds,1))
        $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Encoding utf8
        $result; return
    }
    Start-Sleep -Milliseconds 250
}
throw "Update test timed out. Evidence is preserved at $OutputDirectory; inspect child processes before retrying."
