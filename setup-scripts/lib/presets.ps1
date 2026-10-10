# Named package sets are personal configuration, shared by the CLI and wizard.
function Get-VrcSetupPackagePresets {
    param($Config)
    if ($Config -and $Config.PSObject.Properties.Name -contains 'PackagePresets') {
        return @($Config.PackagePresets.PSObject.Properties | Sort-Object Name)
    }
    return @()
}

function Save-VrcSetupPackagePreset {
    param($Config, [string]$Name, $Packages)
    $Name = $Name.Trim()
    if ([string]::IsNullOrWhiteSpace($Name)) { throw 'A preset name is required.' }
    $copy = Copy-VpmPackageSet -Packages $Packages
    if (@($copy.PSObject.Properties).Count -eq 0) { throw 'A preset must contain at least one package.' }
    foreach ($entry in $copy.PSObject.Properties) {
        if ($entry.Name -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$' -or
            $entry.Value -notmatch '^(latest|[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)$') {
            throw "Invalid package or version: $($entry.Name)@$($entry.Value)"
        }
    }
    if ($Config.PSObject.Properties.Name -notcontains 'PackagePresets' -or -not $Config.PackagePresets) {
        $Config | Add-Member NoteProperty PackagePresets ([pscustomobject]@{}) -Force
    }
    $Config.PackagePresets | Add-Member NoteProperty $Name $copy -Force
}

function Resolve-VrcSetupPackagePreset {
    param($Config, [string]$Name, $BasePackages)
    $preset = @(Get-VrcSetupPackagePresets -Config $Config | Where-Object Name -eq $Name)
    if ($preset.Count -ne 1) { throw "Package preset not found: ${Name}" }
    $result = Copy-VpmPackageSet -Packages $BasePackages
    # Overlay explicitly saved versions without removing unrelated project packages.
    foreach ($entry in $preset[0].Value.PSObject.Properties) {
        $result | Add-Member NoteProperty $entry.Name ([string]$entry.Value) -Force
    }
    return (Add-RequiredPackagesToSet -Packages $result -Config $Config)
}

function Invoke-VrcSetupPresetMenu {
    param($Config, [string]$ConfigPath, $Packages)
    $notice = 'Pins stay fixed. latest follows updates. Other project packages are kept.'
    while ($true) {
        $presets = @(Get-VrcSetupPackagePresets -Config $Config)
        $options = @($presets | ForEach-Object { "$($_.Name)  ($(@($_.Value.PSObject.Properties).Count) packages)" })
        $options += @('Save current set as preset', 'Delete a preset', 'Back')
        $choice = Show-Menu -Title 'Package presets' -Header $notice -Options $options
        if ($choice -lt 0 -or $choice -eq ($presets.Count + 2)) { return $null }
        if ($choice -lt $presets.Count) {
            return [pscustomobject]@{ Name = $presets[$choice].Name; Packages = (Resolve-VrcSetupPackagePreset -Config $Config -Name $presets[$choice].Name -BasePackages $Packages) }
        }
        if ($choice -eq $presets.Count) {
            $name = Read-WizardTextInput -Title 'Save package preset' -Prompt 'Preset name' -BodyLines @('For example: Creator PC, Creator Quest, or a custom setup.') -Hint 'Enter Save  ·  Esc Back'
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if (@($presets | Where-Object Name -eq $name.Trim()).Count -gt 0) {
                if ((Show-Menu -Title 'Replace preset' -Header "Replace '$($name.Trim())' with the current package set?" -Options @('Replace', 'Cancel')) -ne 0) { continue }
            }
            try {
                Save-VrcSetupPackagePreset -Config $Config -Name $name -Packages $Packages
                Save-Config -Config $Config -ConfigPath $ConfigPath
                $notice = "Preset saved: $($name.Trim())"
            } catch { Show-WizardError -Title 'Unable to save preset' -Message $_.Exception.Message }
            continue
        }
        if ($presets.Count -eq 0) { continue }
        $deleteChoice = Show-Menu -Title 'Delete preset' -Options @(@($presets.Name) + 'Back')
        if ($deleteChoice -lt 0 -or $deleteChoice -ge $presets.Count) { continue }
        if ((Show-Menu -Title 'Delete preset' -Header "Delete '$($presets[$deleteChoice].Name)'? Projects are unchanged." -Options @('Delete', 'Cancel')) -eq 0) {
            $Config.PackagePresets.PSObject.Properties.Remove($presets[$deleteChoice].Name)
            Save-Config -Config $Config -ConfigPath $ConfigPath
            $notice = 'Preset deleted.'
        }
    }
}
