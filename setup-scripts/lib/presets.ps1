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

function Invoke-VrcSetupPackageHome {
    param([string]$ConfigPath, [string]$ScriptDir)
    while ($true) {
        $choice = Show-Menu -Title 'Packages and presets' -SectionBreaks @(1,4) -Options @('My default packages','Creator presets','Favorite packages','Saved package registry','Back')
        if ($choice -lt 0 -or $choice -eq 4) { return }
        $config = Load-Config -ConfigPath $ConfigPath
        switch ($choice) {
            0 { Edit-VpmPackages -ConfigPath $ConfigPath -ScriptDir $ScriptDir }
            1 { Invoke-VrcSetupPresetMenu -Config $config -ConfigPath $ConfigPath -Packages $config.VpmPackages -ManageOnly -ScriptDir $ScriptDir | Out-Null }
            2 { Manage-VrcSetupFavoritePackages -Config $config -ConfigPath $ConfigPath }
            3 { Invoke-VrcSetupPackageCatalog -ConfigPath $ConfigPath -ScriptDir $ScriptDir }
        }
    }
}

function Invoke-VrcSetupPresetMenu {
    param($Config, [string]$ConfigPath, $Packages, [switch]$ManageOnly, [switch]$NewProject, [string]$ScriptDir)
    $notice = if ($ManageOnly) { 'Choose a preset to edit, or create one from your defaults.' } else { 'Choose the package set for this project. Your defaults stay unchanged.' }
    while ($true) {
        $presets = @(Get-VrcSetupPackagePresets -Config $Config)
        $options = @($presets | ForEach-Object { "$($_.Name)  ($(@($_.Value.PSObject.Properties).Count) packages)" })
        $createLabel = if ($ManageOnly) { 'Create a preset from my defaults' } else { 'Save current set as preset' }
        $options += @($createLabel, 'Edit a preset', 'Delete a preset')
        if ($NewProject) { $options += 'Use my defaults' }
        $options += 'Back'
        $choice = Show-Menu -Title 'Creator presets' -Header $notice -SectionBreaks @($presets.Count, ($presets.Count + 2), ($options.Count - 1)) -Options $options
        if ($choice -lt 0 -or $options[$choice] -eq 'Back') { return $null }
        if ($options[$choice] -eq 'Use my defaults') {
            return [pscustomobject]@{ Name = 'My package set'; Packages = Copy-VpmPackageSet -Packages $Config.VpmPackages }
        }
        if ($choice -lt $presets.Count) {
            if (-not $ManageOnly) {
                $base = if ($NewProject) { $null } else { $Packages }
                return [pscustomobject]@{ Name = $presets[$choice].Name; Packages = (Resolve-VrcSetupPackagePreset -Config $Config -Name $presets[$choice].Name -BasePackages $base) }
            }
            $editChoice = $choice
        } elseif ($options[$choice] -eq 'Edit a preset') {
            if ($presets.Count -eq 0) { continue }
            $editChoice = Show-Menu -Title 'Edit preset' -Options @(@($presets.Name) + 'Back')
            if ($editChoice -lt 0 -or $editChoice -ge $presets.Count) { continue }
        } else { $editChoice = -1 }
        if ($editChoice -ge 0) {
            $name = $presets[$editChoice].Name
            $localConfig = [pscustomobject]@{
                VpmPackages = Resolve-VrcSetupPackagePreset -Config $Config -Name $name
                RequiredPackages = @(Get-RequiredPackages -Config $Config)
            }
            $edited = Edit-VpmPackages -ConfigPath $ConfigPath -ScriptDir $ScriptDir -ConfigObject $localConfig -PresetConfig $Config -WorkingCopy -EditingPreset $name
            if ($edited) {
                Save-VrcSetupPackagePreset -Config $Config -Name $name -Packages $edited
                Save-Config -Config $Config -ConfigPath $ConfigPath
                $notice = "Preset saved: ${name}. Your defaults are unchanged."
            }
            continue
        }
        if ($choice -eq $presets.Count) {
            $name = Read-WizardTextInput -Title 'Save package preset' -Prompt 'Preset name' -BodyLines @('For example: Creator PC, Creator Quest, or a custom setup.') -Hint 'Enter Save  ·  Esc Back'
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if (@($presets | Where-Object Name -eq $name.Trim()).Count -gt 0) {
                if ((Show-Menu -Title 'Replace preset' -Header "Replace '$($name.Trim())' with the current package set?" -Options @('Replace', 'Cancel')) -ne 0) { continue }
            }
            try {
                $toSave = $Packages
                if ($ManageOnly) {
                    $localConfig = [pscustomobject]@{
                        VpmPackages = Add-RequiredPackagesToSet -Packages (Copy-VpmPackageSet -Packages $Config.VpmPackages) -Config $Config
                        RequiredPackages = @(Get-RequiredPackages -Config $Config)
                    }
                    $toSave = Edit-VpmPackages -ConfigPath $ConfigPath -ScriptDir $ScriptDir -ConfigObject $localConfig -PresetConfig $Config -WorkingCopy -EditingPreset $name.Trim()
                    if (-not $toSave) { continue }
                }
                Save-VrcSetupPackagePreset -Config $Config -Name $name -Packages $toSave
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
