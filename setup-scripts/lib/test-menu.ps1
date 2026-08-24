Write-Host "Quick test harness for Show-Menu"
$scriptDir = $PSScriptRoot
. "$scriptDir/menu.ps1"
$options = @("Option 1","Option 2","Option 3")
$sel = Show-Menu -Title "Test menu" -Options $options
Write-Host "Selected index: ${sel} -> ${($options[$sel])}"
