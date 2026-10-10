$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\setup-scripts\commands\wizard.ps1')
function Assert-CreatorPreset { param($Condition, $Message); if (-not $Condition) { throw $Message } }
# Exercise the actual menu routing without an interactive console or personal config.
function Show-Menu {
    param($Title, $Header, $Options)
    $label = $script:Choices.Dequeue()
    $index = [array]::IndexOf($Options, $label)
    if ($index -lt 0) { throw "Missing menu action: $label" }
    return $index
}
function Save-Config { param($Config, $ConfigPath); $script:Saved++ }
function Edit-VpmPackages {
    param($ConfigPath, $ScriptDir, $ConfigObject, $PresetConfig, [switch]$WorkingCopy, $EditingPreset)
    Assert-CreatorPreset ($WorkingCopy -and $EditingPreset -eq 'Creator') 'Preset editor used the default editor.'
    $ConfigObject.VpmPackages.PSObject.Properties.Remove('gogoloco')
    $ConfigObject.VpmPackages | Add-Member NoteProperty 'com.vrchat.base' '3.10.2' -Force
    $ConfigObject.VpmPackages | Add-Member NoteProperty 'com.vrchat.avatars' '3.10.2' -Force
    if ($script:CancelEdit) { return $null }
    return $ConfigObject.VpmPackages
}
function Set-MenuChoices { param([string[]]$Labels); $script:Choices = [System.Collections.Generic.Queue[string]]::new(); foreach ($label in $Labels) { $script:Choices.Enqueue($label) } }
$config = [pscustomobject]@{ VpmPackages = [pscustomobject]@{ gogoloco='latest'; 'com.vrchat.base'='latest' } }
Save-VrcSetupPackagePreset -Config $config -Name Creator -Packages $config.VpmPackages
$before = $config.VpmPackages | ConvertTo-Json -Compress
$script:Saved = 0
Set-MenuChoices @('Creator  (2 packages)', 'Back')
$result = Invoke-VrcSetupPresetMenu -Config $config -Packages $config.VpmPackages -ManageOnly
Assert-CreatorPreset ($null -eq $result -and $script:Saved -eq 1) 'Manage-only applied a preset to the default set.'
Assert-CreatorPreset (($config.VpmPackages | ConvertTo-Json -Compress) -eq $before) 'Editing a creator changed the defaults.'
Set-MenuChoices @("Creator  ($(@($config.PackagePresets.Creator.PSObject.Properties).Count) packages)")
$result = Invoke-VrcSetupPresetMenu -Config $config -Packages $config.VpmPackages -NewProject
Assert-CreatorPreset ($result.Packages.PSObject.Properties.Name -notcontains 'gogoloco') 'New project inherited excluded GoGoLoco.'
Assert-CreatorPreset ($result.Packages.'com.vrchat.base' -eq '3.10.2' -and $result.Packages.'com.vrchat.avatars' -eq '3.10.2') 'Creator SDK version was lost.'
Set-MenuChoices @('Use my defaults')
$result = Invoke-VrcSetupPresetMenu -Config $config -NewProject
Assert-CreatorPreset ($result.Packages.gogoloco -eq 'latest' -and $result.Name -eq 'My package set') 'Returning to defaults failed.'
$script:CancelEdit = $true
$before = $config.PackagePresets | ConvertTo-Json -Depth 10 -Compress
Set-MenuChoices @('Edit a preset', 'Creator', 'Back')
Invoke-VrcSetupPresetMenu -Config $config -ManageOnly | Out-Null
Assert-CreatorPreset (($config.PackagePresets | ConvertTo-Json -Depth 10 -Compress) -eq $before -and $script:Saved -eq 1) 'Cancelled preset edit was saved.'
Write-Host 'PASS: creator presets remain independent from defaults; selection and cancellation verified.'
