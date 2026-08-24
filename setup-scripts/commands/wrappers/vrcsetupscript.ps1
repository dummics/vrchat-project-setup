param([string]$projectPath, [switch]$Test)

$scriptDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. "${scriptDir}\commands\installer.ps1"
. "${scriptDir}\lib\config.ps1"

# Delegate to Start-Installer implemented in commands/installer.ps1
$status = Start-Installer -projectPath $projectPath -Test:$Test
exit $status
