[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BasePayload,
    [Parameter(Mandatory)][string]$UpdateInstaller,
    [string]$OutputDirectory,
    [switch]$UseRealNetwork,
    [switch]$DirectInstaller,
    [ValidateRange(30,600)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$BasePayload = [IO.Path]::GetFullPath($BasePayload)
$UpdateInstaller = [IO.Path]::GetFullPath($UpdateInstaller)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('.work/artifacts/update-e2e/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.StartsWith($BasePayload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $BasePayload.StartsWith($OutputDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Base payload and evidence must be separate folders.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; existing evidence is preserved.' }
if (-not (Test-Path -LiteralPath (Join-Path $BasePayload '.vrcsetup-payload.json'))) { throw 'Base must be a published payload.' }
$installerName = [IO.Path]::GetFileName($UpdateInstaller)
if ($installerName -notmatch '^(?:Install|Update)-VrcSetup-(\d+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?)(?:-from-\d+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?)?-win-x64\.exe$') { throw 'Use a published versioned installer.' }
$targetVersion = $Matches[1]
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
[IO.File]::WriteAllText((Join-Path $OutputDirectory '.vrcsetup-update-smoke'), 'Owned isolated updater fixture. No user projects.')
foreach ($item in Get-ChildItem -LiteralPath $BasePayload -Force) { Copy-Item -LiteralPath $item.FullName -Destination $install -Recurse }
$record = @{Version=$manifest.Version;Root=$install;InstalledUtc=[DateTime]::UtcNow;PreviousInstall=$null}
$record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $install '.vrcsetup-install.json') -Encoding utf8
$config = @{UnityProjectsRoot=(Join-Path $OutputDirectory 'projects');CheckUpdatesOnStartup=$false;IncludeBetaUpdates=$true;Theme='Dark';SavedProjectNames=@{'update-test-sentinel'='preserve me'}}
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
$start.ArgumentList.Add('--update-smoke-test'); $start.ArgumentList.Add($specFile)
if ($DirectInstaller) {
    if ($UseRealNetwork) { throw 'Direct installer mode does not test network discovery.' }
    $start = [Diagnostics.ProcessStartInfo]::new($UpdateInstaller)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WorkingDirectory = $install
    foreach ($argument in @('--silent','--root',$install,'--home',$testHome,'--result',(Join-Path $testHome 'updates/last-result.json'),'--launch')) { $start.ArgumentList.Add($argument) }
    $start.Environment['VRCSETUP_UPDATE_SMOKE_SPEC'] = $specFile
    $start.Environment['VRCSETUP_HOME'] = $testHome
    $start.Environment['VRCSETUP_SKIP_PATH_UPDATE'] = '1'
    $start.Environment['VRCSETUP_START_MENU_ROOT'] = Join-Path $OutputDirectory 'start-menu'
    @{ DirectInstaller=$true; UsesProductionInstaller=$true } | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'handoff.json')
}
$process = [Diagnostics.Process]::Start($start)
$timer = [Diagnostics.Stopwatch]::StartNew()
$receipt = Join-Path $OutputDirectory 'passed.json'; $failure = Join-Path $OutputDirectory 'failure.txt'
while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
    if ($process.HasExited -and -not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'handoff.json'))) { throw "Base app exited before updater handoff. Use a payload containing --update-smoke-test; inspect $OutputDirectory." }
    $installerResult = Join-Path $testHome 'updates/last-result.json'
    if (Test-Path -LiteralPath $installerResult) { $outcome = Get-Content -LiteralPath $installerResult -Raw | ConvertFrom-Json; if (-not $outcome.Success) { throw $outcome.Error } }
    if (Test-Path -LiteralPath $receipt) {
        $result = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
        if (-not $result.Passed) { throw 'Updater did not report success.' }
        $result | Add-Member -NotePropertyName BaseVersion -NotePropertyValue $manifest.Version
        $result | Add-Member -NotePropertyName ElapsedSeconds -NotePropertyValue ([math]::Round($timer.Elapsed.TotalSeconds,1))
        $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Encoding utf8
        $result; return
    }
    Start-Sleep -Milliseconds 250
}
throw "Update test timed out. Evidence is preserved at $OutputDirectory; inspect child processes before retrying."
