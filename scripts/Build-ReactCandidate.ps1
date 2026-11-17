[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$desktop = Join-Path $repo 'src/VrcSetup.ReactDesktop'
$payload = Join-Path $output 'payload'
$evidence = Join-Path $output 'evidence'
$clock = [Diagnostics.Stopwatch]::StartNew()

& npm.cmd --prefix (Join-Path $desktop 'ui') ci --no-audit --no-fund
if ($LASTEXITCODE) { throw 'npm ci failed.' }
& npm.cmd --prefix (Join-Path $desktop 'ui') run build
if ($LASTEXITCODE) { throw 'React build failed.' }
$frontendSeconds = $clock.Elapsed.TotalSeconds
& dotnet publish (Join-Path $desktop 'VrcSetup.ReactDesktop.csproj') -c Release -p:Version=0.4.0-alpha.1 -p:DebugType=None -o $payload
if ($LASTEXITCODE) { throw 'Desktop publish failed.' }
$publishSeconds = $clock.Elapsed.TotalSeconds - $frontendSeconds

$launch = [Diagnostics.ProcessStartInfo]::new((Join-Path $payload 'VrcSetup.ReactDesktop.exe'))
$launch.UseShellExecute = $false
$launch.ArgumentList.Add('--smoke')
$launch.ArgumentList.Add($evidence)
$process = [Diagnostics.Process]::Start($launch)
if (-not $process.WaitForExit(45000)) {
    $process.Kill($true)
    throw 'React candidate smoke timed out.'
}
if ($process.ExitCode) {
    Get-Content -LiteralPath (Join-Path $evidence 'failure.txt')
    throw 'React candidate smoke failed.'
}

$archive = Join-Path $output 'VrcSetup-React-0.4.0-alpha.1-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive, [IO.Compression.CompressionLevel]::SmallestSize, $false)
$webArchive = Join-Path $output 'web-size.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $payload 'web'), $webArchive, [IO.Compression.CompressionLevel]::SmallestSize, $false)
$smoke = Get-Content -LiteralPath (Join-Path $evidence 'result.json') -Raw | ConvertFrom-Json
$report = [ordered]@{
    version = '0.4.0-alpha.1'
    frontendZipBytes = (Get-Item -LiteralPath $webArchive).Length
    desktopZipBytes = (Get-Item -LiteralPath $archive).Length
    desktopExtractedBytes = (Get-ChildItem -LiteralPath $payload -Recurse -File | Measure-Object Length -Sum).Sum
    frontendSeconds = [math]::Round($frontendSeconds, 2)
    desktopPublishSeconds = [math]::Round($publishSeconds, 2)
    sha256 = (Get-FileHash -LiteralPath $archive).Hash
    smoke = $smoke
}
Remove-Item -LiteralPath $webArchive
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'candidate-build.json') -Encoding utf8
$report | ConvertTo-Json -Depth 6
