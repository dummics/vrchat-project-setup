[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory,
    [string]$AssetBaseUri,
    [string]$UpdateFeedUri,
    [string]$UpdateAssetPrefix,
    [switch]$SkipInstaller,
    [switch]$ReactCandidate,
    [string]$FixedWebView2Runtime
)
$ErrorActionPreference = 'Stop'
$buildClock = [Diagnostics.Stopwatch]::StartNew()
$stageSeconds = [ordered]@{}
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
$release = Get-ReleaseVersion $Version
$Version = $release.Text
$assetId = $release.AssetId
$reactDesktop = $release.Core -ge [version]'0.4.0'
if ($ReactCandidate -and -not $reactDesktop) {
    throw 'React packaging requires version 0.4.0 or later.'
}
if ($release.Build -and -not $reactDesktop) {
    throw 'Numbered builds require the React desktop.'
}
if ($FixedWebView2Runtime -and -not $reactDesktop) {
    throw 'Fixed Version packaging requires the React desktop.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot '.work\artifacts\release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$payload = Join-Path $OutputDirectory ('VrcSetup-' + $assetId + '-win-x64')
if (Test-Path -LiteralPath $payload) { throw "Output already exists: $payload. Choose a new directory; existing releases are preserved." }
[void][IO.Directory]::CreateDirectory($payload)
function Invoke-Dotnet {
    param([string[]]$Arguments)
    $hosting = @()
    if ($UpdateFeedUri) { $hosting += '-p:UpdateFeedUri=' + $UpdateFeedUri }
    if ($UpdateAssetPrefix) { $hosting += '-p:UpdateAssetPrefix=' + $UpdateAssetPrefix }
    $stageClock = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @Arguments @hosting '-p:Platform=x64' '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
    $stageSeconds[('publish-' + [IO.Path]::GetFileNameWithoutExtension($Arguments[1]))] = [math]::Round($stageClock.Elapsed.TotalSeconds, 2)
}
Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Cli\VrcSetup.Cli.csproj'),'-c','Release','-r','win-x64','--self-contained','true',('-p:Version=' + $Version),'-o',(Join-Path $payload 'app\cli'),'--nologo')
$desktopOutput = Join-Path $payload 'app\desktop'
if ($reactDesktop) {
    $desktopSource = Join-Path $repoRoot 'src\VrcSetup.ReactDesktop'
    $uiClock = [Diagnostics.Stopwatch]::StartNew()
    & npm.cmd --prefix (Join-Path $desktopSource 'ui') ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'React dependency restore failed.' }
    & npm.cmd --prefix (Join-Path $desktopSource 'ui') run build
    if ($LASTEXITCODE -ne 0) { throw 'React build failed.' }
    $stageSeconds['build-react-ui'] = [math]::Round($uiClock.Elapsed.TotalSeconds, 2)
    Invoke-Dotnet -Arguments @('publish',(Join-Path $desktopSource 'VrcSetup.ReactDesktop.csproj'),'-c','Release',
        '-p:ReactInstalledEntryPoint=true',('-p:Version=' + $Version),'-o',$desktopOutput,'--nologo')
    foreach ($resource in @('VrcSetup.Desktop.exe','VrcSetup.Desktop.dll','web\index.html','WebView2Loader.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $desktopOutput $resource) -PathType Leaf)) {
            throw "React publish resource missing: $resource"
        }
    }
} else {
    Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Desktop\VrcSetup.Desktop.csproj'),'-c','Release','-p:Platform=x64',('-p:Version=' + $Version),'-o',$desktopOutput,'--nologo')
    foreach ($resource in @('MainWindow.xbf','MainPage.xbf','PackageBrowserView.xbf','AppearanceEditor.xbf','VrcSetup.Desktop.pri')) {
        if (-not (Test-Path -LiteralPath (Join-Path $desktopOutput $resource))) { throw "Native XAML publish resource missing: $resource" }
    }
}
foreach ($client in @('cli','desktop')) {
    $resources = Join-Path $payload ('app\' + $client + '\Resources')
    foreach ($seedFile in @('dependency-signatures.json', 'dependency-publisher-signatures.json', 'dependency-publisher-signatures.provenance.json', 'dependency-publisher-signatures.LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $resources $seedFile) -PathType Leaf)) {
            throw "Dependency signature resource $seedFile missing from published $client."
        }
    }
}
foreach ($name in @('VRC Project Setup.bat','Install VRC Project Setup.bat','Repair VRC Project Setup.bat','Uninstall VRC Project Setup.bat')) {
    $text = switch ($name) {
        'VRC Project Setup.bat' { "@echo off`r`nstart `"`" `"%~dp0app\desktop\VrcSetup.Desktop.exe`"`r`nexit /b`r`n" }
        'Install VRC Project Setup.bat' { "@echo off`r`n`"%~dp0app\cli\vrcsetup.exe`" install %*`r`nif errorlevel 1 pause`r`nexit /b %errorlevel%`r`n" }
        'Repair VRC Project Setup.bat' { "@echo off`r`n`"%~dp0app\cli\vrcsetup.exe`" repair %*`r`nif errorlevel 1 pause`r`nexit /b %errorlevel%`r`n" }
        default { "@echo off`r`n`"%~dp0app\cli\vrcsetup.exe`" uninstall %*`r`nif errorlevel 1 pause`r`nexit /b %errorlevel%`r`n" }
    }
    [IO.File]::WriteAllText((Join-Path $payload $name),$text,[Text.UTF8Encoding]::new($false))
}
[void][IO.Directory]::CreateDirectory((Join-Path $payload 'bin'))
[IO.File]::WriteAllText((Join-Path $payload 'bin\vrcsetup.cmd'),"@echo off`r`n`"%~dp0..\app\cli\vrcsetup.exe`" %*`r`nexit /b %errorlevel%`r`n",[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $payload 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\desktop\DISTRIBUTION.md') -Destination (Join-Path $payload 'README.md')
$hashes = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
    $relative = $file.FullName.Substring($payload.Length + 1).Replace('\','/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$manifest = [ordered]@{ Version=$Version; Files=$hashes }
[IO.File]::WriteAllText((Join-Path $payload '.vrcsetup-payload.json'),($manifest | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
# Portable marker is intentionally outside the integrity manifest, and is not installed.
[IO.File]::WriteAllText((Join-Path $payload '.vrcsetup-portable'),'Portable user data lives in data/ next to this payload.')
$archive = Join-Path $OutputDirectory ('VrcSetup-' + $assetId + '-win-x64.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveClock = [Diagnostics.Stopwatch]::StartNew()
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$archive,[IO.Compression.CompressionLevel]::SmallestSize,$false)
$stageSeconds['portable-compression'] = [math]::Round($archiveClock.Elapsed.TotalSeconds, 2)
$installer = $null
if (-not $SkipInstaller) {
    $installerOutput = Join-Path $OutputDirectory 'installer-build'
    Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Installer\VrcSetup.Installer.csproj'),'-c','Release',('-p:Version=' + $Version),'-p:PublishSingleFile=false','-p:IncludeAllContentForSelfExtract=false','-o',$installerOutput,'--nologo')
    if (-not $AssetBaseUri) { $AssetBaseUri = "https://github.com/dummics/vrchat-project-setup/releases/download/$([uri]::EscapeDataString("v$Version"))/" }
    $componentClock = [Diagnostics.Stopwatch]::StartNew()
    $componentManifest = & (Join-Path $PSScriptRoot 'Publish-Components.ps1') -Payload $payload -Engine $installerOutput -Version $Version -OutputDirectory $OutputDirectory -AssetBaseUri $AssetBaseUri
    $stageSeconds['component-compression'] = [math]::Round($componentClock.Elapsed.TotalSeconds, 2)
    $bootstrapOutput = Join-Path $OutputDirectory 'bootstrap-build'
    Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Bootstrapper\VrcSetup.Bootstrapper.csproj'),'-c','Release',('-p:Version=' + $Version),('-p:ComponentManifest=' + $componentManifest),'-o',$bootstrapOutput,'--nologo')
    $installer = Join-Path $OutputDirectory ('Install-VrcSetup-' + $assetId + '-win-x64.exe')
    Copy-Item -LiteralPath (Join-Path $bootstrapOutput 'VrcSetupSetup.exe') -Destination $installer
}
$outputs = @($archive) + @($installer | Where-Object { $_ })
$offlineArchive = $null
if ($FixedWebView2Runtime) {
    $offlineArchive = Join-Path $OutputDirectory ('VrcSetup-' + $assetId + '-win-x64-offline.zip')
    $outputs += & (Join-Path $PSScriptRoot 'Publish-FixedWebView2Portable.ps1') -Payload $payload -Runtime $FixedWebView2Runtime -Output $offlineArchive
}
if (-not $SkipInstaller) { $outputs += @(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.zip' -File | Where-Object { $_.Name -match '^[a-f0-9]{64}\.zip$' } | ForEach-Object FullName); $outputs += $componentManifest }
$checksums = @($outputs | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash,[IO.Path]::GetFileName($_) })
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
$report = [ordered]@{Version=$Version;Payload=$payload;Archive=$archive;Installer=$installer;Checksums=(Join-Path $OutputDirectory 'SHA256SUMS.txt');Artifacts=@($outputs|ForEach-Object{[ordered]@{Name=[IO.Path]::GetFileName($_);Bytes=(Get-Item -LiteralPath $_).Length}})}
$report['StageSeconds'] = $stageSeconds
$report['Desktop'] = $(if ($reactDesktop) { 'React' } else { 'WinUI' })
$report['BuildNumber'] = $release.Build
$report['OfflineArchive'] = $offlineArchive
$report['TotalSeconds'] = [math]::Round($buildClock.Elapsed.TotalSeconds, 2)
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'release-build.json') -Encoding utf8
[pscustomobject]$report
