param([string]$projectPath, [switch]$Test, [switch]$Wizard)

# Top-level entry point for vrc setup
$scriptDir = $PSScriptRoot
. "${scriptDir}\lib\config.ps1"
. "${scriptDir}\commands\wizard.ps1"
. "${scriptDir}\commands\installer.ps1"

$configPath = Join-Path $scriptDir "config\vrcsetup.json"
$defaultsPath = Join-Path $scriptDir "config\vrcsetup.defaults"
[void](Initialize-ConfigIfMissing -ConfigPath $configPath -DefaultsPath $defaultsPath)

if ($Wizard -or (-not $projectPath)) {
    Start-Wizard
    exit 0
}

$status = Start-Installer -projectPath $projectPath -Test:$Test
exit $status
