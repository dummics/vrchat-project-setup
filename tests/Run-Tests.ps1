[CmdletBinding()]
param([switch]$BuildDesktop)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet test (Join-Path $repoRoot 'tests\VrcSetup.Core.Tests') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($BuildDesktop) {
    & dotnet build (Join-Path $repoRoot 'src\VrcSetup.Desktop') '-p:Platform=x64' --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet build (Join-Path $repoRoot 'src\VrcSetup.Installer') --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
