[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$RequireWindows10,
    [switch]$RequireDisconnectedNetwork
)

$ErrorActionPreference = 'Stop'
$zip = (Resolve-Path -LiteralPath $Archive).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh directory for the portable startup fixture.' }
$os = Get-CimInstance Win32_OperatingSystem
$windows10 = $os.ProductType -eq 1 -and [int]$os.BuildNumber -ge 10240 -and [int]$os.BuildNumber -lt 22000
if ($RequireWindows10 -and -not $windows10) { throw "This test requires Windows 10. Detected $($os.Caption), build $($os.BuildNumber)." }
$networkActive = @(Get-NetAdapter -ErrorAction Stop | Where-Object Status -eq 'Up').Count -gt 0
if ($RequireDisconnectedNetwork -and $networkActive) { throw 'Disconnect the fixture network adapters before running this test.' }

[IO.Compression.ZipFile]::ExtractToDirectory($zip, $output)
$desktop = Join-Path $output 'app/desktop'
$browser = Join-Path $desktop 'webview2-runtime/msedgewebview2.exe'
$manifestPath = Join-Path $output '.vrcsetup-payload.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$browserEntry = 'app/desktop/webview2-runtime/msedgewebview2.exe'
if ((Get-FileHash -LiteralPath $browser).Hash -ne $manifest.Files[$browserEntry]) {
    throw 'The bundled browser does not match the archive manifest.'
}

[IO.File]::WriteAllText((Join-Path $output '.react-profile-fixture'), 'Disposable Fixed Version startup verification')
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $desktop 'VrcSetup.Desktop.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('--verify-portable-startup')
$start.ArgumentList.Add($output)
$start.Environment['VRCSETUP_HOME'] = Join-Path $output 'data'
# Block external host resolution for this WebView2 instance, even when the
# current machine has an active network adapter. The app URI maps to local files.
$start.Environment['WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS'] = '--host-resolver-rules="MAP * ~NOTFOUND"'
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(45000)) {
        $process.Kill($true)
        throw 'Portable startup timed out.'
    }
    $report = Join-Path $output 'portable-startup.json'
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        $failure = Join-Path $output 'failure.txt'
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
        throw 'Portable startup did not produce evidence.'
    }
}
finally { $process.Dispose() }

$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (-not $result.freshConfiguration -or $result.browserExecutable -ne $browser -or
    $result.home -ne (Join-Path $output 'data')) {
    throw 'The portable app did not start its bundled browser and create its own profile.'
}

if ($windows10) {
    $directory = Split-Path -Parent $browser
    $rules = (Get-Acl -LiteralPath $directory).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])
    foreach ($sid in 'S-1-15-2-1', 'S-1-15-2-2') {
        $expectedRights = [Security.AccessControl.FileSystemRights]::ReadAndExecute
        $expectedInheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
        if (-not @($rules | Where-Object {
            $_.IdentityReference.Value -eq $sid -and $_.AccessControlType -eq 'Allow' -and
            ($_.FileSystemRights -band $expectedRights) -eq $expectedRights -and
            ($_.InheritanceFlags -band $expectedInheritance) -eq $expectedInheritance -and
            $_.PropagationFlags -eq 'None'
        }).Count) { throw "The bundled runtime is missing the AppContainer grant for $sid." }
    }
}

[pscustomobject]@{
    Passed = $true
    OperatingSystem = $os.Caption
    Build = $os.BuildNumber
    ProductType = $os.ProductType
    NetworkAdaptersDisconnected = -not $networkActive
    ExternalWebViewResolutionDisabled = $true
    Windows10AppContainerGrantsVerified = $windows10
    ArchiveSha256 = (Get-FileHash -LiteralPath $zip).Hash
    BrowserExecutable = $result.browserExecutable
    BrowserVersion = $result.runtime
    ProfileCreated = $result.freshConfiguration
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'offline-portable-verification.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $output 'offline-portable-verification.json') -Raw
