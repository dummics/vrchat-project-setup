[CmdletBinding()]
param([ValidateSet('gui','install','repair','uninstall')][string]$Action = 'gui', [Parameter(ValueFromRemainingArguments=$true)][string[]]$ToolArguments)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installed = Join-Path $env:LOCALAPPDATA 'Programs\VrcSetup'
if ($Action -in @('repair','uninstall')) {
    $cli = Join-Path $installed 'app\cli\vrcsetup.exe'
    if (-not (Test-Path -LiteralPath $cli)) { throw 'Install the new .NET package first. Legacy installations are managed by their existing installer.' }
    & $cli $Action @ToolArguments
    exit $LASTEXITCODE
}
if ($Action -eq 'install') {
    $version = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    $installer = Join-Path $repoRoot ('.work\artifacts\release\Install-VrcSetup-' + $version + '-win-x64.exe')
    if (-not (Test-Path -LiteralPath $installer)) {
        & (Join-Path $PSScriptRoot 'Publish-Windows.ps1')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Start-Process -FilePath $installer
    exit 0
}
if ($ToolArguments.Count -gt 0) {
    & dotnet run --project (Join-Path $repoRoot 'src\VrcSetup.Cli') -- @ToolArguments
    exit $LASTEXITCODE
}
& dotnet build (Join-Path $repoRoot 'src\VrcSetup.Desktop') '-p:Platform=x64' --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$desktop = Join-Path $repoRoot 'src\VrcSetup.Desktop\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\VrcSetup.Desktop.exe'
Start-Process -FilePath $desktop -WindowStyle Hidden
