[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory,
    [string]$BaseArchive,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
$Version = (Get-ReleaseVersion $Version).Text
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot '.work\artifacts\release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$payload = Join-Path $OutputDirectory ('VrcSetup-' + $Version + '-win-x64')
if (Test-Path -LiteralPath $payload) { throw "Output already exists: $payload. Choose a new directory; existing releases are preserved." }
[void][IO.Directory]::CreateDirectory($payload)
function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Cli\VrcSetup.Cli.csproj'),'-c','Release','-r','win-x64','--self-contained','true',('-p:Version=' + $Version),'-o',(Join-Path $payload 'app\cli'),'--nologo')
Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Desktop\VrcSetup.Desktop.csproj'),'-c','Release','-p:Platform=x64',('-p:Version=' + $Version),'-o',(Join-Path $payload 'app\desktop'),'--nologo')
foreach ($resource in @('MainWindow.xbf','MainPage.xbf','PackageBrowserView.xbf','VrcSetup.Desktop.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload ('app\desktop\' + $resource)))) { throw "Native XAML publish resource missing: $resource" }
}
foreach ($client in @('cli','desktop')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload ('app\' + $client + '\Resources\dependency-signatures.json')))) { throw "Dependency signature seed missing from published $client." }
}
foreach ($name in @('VRChat Project Setup.bat','Install VRChat Project Setup.bat','Repair VRChat Project Setup.bat','Uninstall VRChat Project Setup.bat')) {
    $text = switch ($name) {
        'VRChat Project Setup.bat' { "@echo off`r`nstart `"`" `"%~dp0app\desktop\VrcSetup.Desktop.exe`"`r`nexit /b`r`n" }
        'Install VRChat Project Setup.bat' { "@echo off`r`n`"%~dp0app\cli\vrcsetup.exe`" install %*`r`nif errorlevel 1 pause`r`nexit /b %errorlevel%`r`n" }
        'Repair VRChat Project Setup.bat' { "@echo off`r`n`"%~dp0app\cli\vrcsetup.exe`" repair %*`r`nif errorlevel 1 pause`r`nexit /b %errorlevel%`r`n" }
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
$archive = Join-Path $OutputDirectory ('VrcSetup-' + $Version + '-win-x64.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$archive,[IO.Compression.CompressionLevel]::SmallestSize,$false)
$installer = $null
$deltaInstaller = $null
if (-not $SkipInstaller) {
    $installerOutput = Join-Path $OutputDirectory 'installer-build'
    Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Installer\VrcSetup.Installer.csproj'),'-c','Release',('-p:Version=' + $Version),('-p:PayloadArchive=' + $archive),'-o',$installerOutput,'--nologo')
    $installer = Join-Path $OutputDirectory ('Install-VrcSetup-' + $Version + '-win-x64.exe')
    Copy-Item -LiteralPath (Join-Path $installerOutput 'VrcSetupInstaller.exe') -Destination $installer
    if ($BaseArchive) {
        $baseZip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($BaseArchive))
        try {
            $entry = $baseZip.GetEntry('.vrcsetup-payload.json')
            if (-not $entry) { throw 'Base archive has no payload manifest.' }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $basis = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $reader.Dispose() }
        } finally { $baseZip.Dispose() }
        if ((Compare-ReleaseVersion $basis.Version $Version) -ge 0) { throw 'Delta base must be older than the target version.' }
        $deltaArchive = Join-Path $OutputDirectory ('delta-' + $basis.Version + '.zip')
        $reuse = [ordered]@{}
        $zip = [IO.Compression.ZipFile]::Open($deltaArchive, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($relative in $hashes.Keys) {
                if ($basis.Files.ContainsKey($relative) -and $basis.Files[$relative] -eq $hashes[$relative]) { $reuse[$relative] = $hashes[$relative]; continue }
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $payload $relative), $relative, [IO.Compression.CompressionLevel]::SmallestSize)
            }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $payload '.vrcsetup-payload.json'), '.vrcsetup-payload.json', [IO.Compression.CompressionLevel]::SmallestSize)
            $entry = $zip.CreateEntry('.vrcsetup-delta.json')
            $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
            try { $writer.Write(([ordered]@{ BaseVersion=$basis.Version; Reuse=$reuse } | ConvertTo-Json -Depth 5)) } finally { $writer.Dispose() }
        } finally { $zip.Dispose() }
        Invoke-Dotnet -Arguments @('publish',(Join-Path $repoRoot 'src\VrcSetup.Installer\VrcSetup.Installer.csproj'),'-c','Release',('-p:Version=' + $Version),('-p:PayloadArchive=' + $deltaArchive),'-o',(Join-Path $OutputDirectory 'delta-installer-build'),'--nologo')
        $deltaInstaller = Join-Path $OutputDirectory ("Update-VrcSetup-$Version-from-$($basis.Version)-win-x64.exe")
        Copy-Item -LiteralPath (Join-Path $OutputDirectory 'delta-installer-build/VrcSetupInstaller.exe') -Destination $deltaInstaller
    }
}
$outputs = @($archive) + @($installer,$deltaInstaller | Where-Object { $_ })
$checksums = @($outputs | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash,[IO.Path]::GetFileName($_) })
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
$report = [ordered]@{Version=$Version;Payload=$payload;Archive=$archive;Installer=$installer;DeltaInstaller=$deltaInstaller;Checksums=(Join-Path $OutputDirectory 'SHA256SUMS.txt');Artifacts=@($outputs|ForEach-Object{[ordered]@{Name=[IO.Path]::GetFileName($_);Bytes=(Get-Item -LiteralPath $_).Length}})}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'release-build.json') -Encoding utf8
[pscustomobject]$report
