param()

$scriptDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. "${scriptDir}\commands\wizard.ps1"
. "${scriptDir}\commands\installer.ps1"
. "${scriptDir}\lib\config.ps1"
Start-Wizard
exit 0
