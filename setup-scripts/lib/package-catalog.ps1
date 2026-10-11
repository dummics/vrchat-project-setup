# Local package definitions and project provenance; no network calls while browsing.
$script:VrcSetupPackageDisplayNames=@{}
function Get-VrcSetupFriendlyPackageName {
    param([Parameter(Mandatory)][string]$PackageName)

    $knownNames = @{
        'com.vrchat.base' = 'VRChat SDK Base'
        'com.vrchat.avatars' = 'VRChat SDK Avatars'
        'com.vrchat.worlds' = 'VRChat SDK Worlds'
        'com.vrchat.core.vpm-resolver' = 'VRChat Package Resolver'
        'com.vrcfury.vrcfury' = 'VRCFury'
        'com.poiyomi.toon' = 'Poiyomi Toon Shader'
        'adjerry91.vrcft.templates' = 'VRCFury Templates'
        'dev.foxscore.easy-login' = 'Easy Login'
        'gogoloco' = 'GoGo Loco'
        'nadena.dev.ndmf' = 'NDMF (framework)'
        'nadena.dev.modular-avatar' = 'Modular Avatar'
    }
    if ($knownNames.ContainsKey($PackageName)) { return [string]$knownNames[$PackageName] }
    if ($script:VrcSetupPackageDisplayNames.ContainsKey($PackageName)) { return [string]$script:VrcSetupPackageDisplayNames[$PackageName] }

    $leaf = @($PackageName -split '\.')[-1]
    if ([string]::IsNullOrWhiteSpace($leaf)) { return $PackageName }
    return [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase(($leaf -replace '[-_]+', ' '))
}

function Get-VrcSetupKnownPackages {
    param($Config, $PackageSets = @(), [string]$ScriptDir)
    Complete-VrcSetupPackageCatalogRefresh -ScriptDir $ScriptDir
    $known = @{}
    $file = Join-Path $ScriptDir 'cache\known-packages.json'
    if (Test-Path -LiteralPath $file) { try { $decoded=Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json; foreach ($item in $decoded) { $known[[string]$item.Id] = $item; if ($item.DisplayName) { $script:VrcSetupPackageDisplayNames[$item.Id]=$item.DisplayName } } } catch { } }
    $sets = @($Config.VpmPackages) + @($PackageSets) + @(Get-VrcSetupPackagePresets -Config $Config | ForEach-Object Value)
    foreach ($set in $sets) {
        foreach ($entry in $set.PSObject.Properties) {
            $cached=$known[$entry.Name]
            $known[$entry.Name] = [pscustomobject]@{ Id=$entry.Name; DisplayName=(Get-VrcSetupFriendlyPackageName $entry.Name); LatestVersion=$(if ($cached.LatestVersion -and $cached.LatestVersion -ne 'latest') { $cached.LatestVersion } else { [string]$entry.Value }); SelectedVersion=[string]$entry.Value; Versions=@($cached.Versions); UpdatedUtc=$cached.UpdatedUtc }
        }
    }
    foreach ($id in @(Get-VrcSetupFavoritePackages -Config $Config)) {
        if (-not $known.ContainsKey($id)) { $known[$id]=[pscustomobject]@{Id=$id;DisplayName=(Get-VrcSetupFriendlyPackageName $id);LatestVersion='latest';SelectedVersion='latest'} }
    }
    return @($known.Values | Sort-Object DisplayName,Id)
}

function Save-VrcSetupKnownPackages {
    param($Selections, [string]$ScriptDir)
    $file = Join-Path $ScriptDir 'cache\known-packages.json'
    $known = @{}
    if (Test-Path -LiteralPath $file) { try { $decoded=Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json; foreach ($item in $decoded) { $known[[string]$item.Id]=$item } } catch { } }
    foreach ($item in @($Selections)) { if ($item.Id) {
        if ($item.DisplayName) { $script:VrcSetupPackageDisplayNames[$item.Id]=$item.DisplayName }
        $old=$known[$item.Id]
        $known[$item.Id]=[pscustomobject]@{Id=[string]$item.Id;DisplayName=[string]$item.DisplayName;LatestVersion=[string]$item.LatestVersion;SelectedVersion=$(if ($item.SelectedVersion) { [string]$item.SelectedVersion } else { 'latest' });Versions=$(if (@($item.Versions).Count) { @($item.Versions) } else { @($old.Versions) });UpdatedUtc=$(if ($item.UpdatedUtc) { $item.UpdatedUtc } else { $old.UpdatedUtc })}
    } }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $file))
    [IO.File]::WriteAllText($file,(ConvertTo-Json -InputObject @($known.Values | Sort-Object Id) -Depth 5))
}

function Start-VrcSetupPackageCatalogRefresh {
    param($Items,[string]$ScriptDir,[scriptblock]$RefreshProvider)
    Complete-VrcSetupPackageCatalogRefresh -ScriptDir $ScriptDir
    if ($script:VrcSetupCatalogRefresh -and -not $script:VrcSetupCatalogRefresh.Handle.IsCompleted) { return $false }
    $progress=[hashtable]::Synchronized(@{Done=0;Total=@($Items).Count;Failed=0})
    $worker=[powershell]::Create()
    [void]$worker.AddScript({
        param($runtime,$definitions,$progress,$provider)
        . (Join-Path $runtime 'lib\utils.ps1')
        . (Join-Path $runtime 'lib\vrcget.ps1')
        foreach ($item in $definitions) {
            try {
                $versions = if ($provider) { @(& ([scriptblock]::Create($provider)) $item.Id) } else { @(Get-VrcGetAvailableVersions -PackageName $item.Id -ScriptDir $runtime) }
                if ($versions.Count) { $item | Add-Member -NotePropertyName Versions -NotePropertyValue $versions -Force; $item.LatestVersion=$versions[0]; $item | Add-Member -NotePropertyName UpdatedUtc -NotePropertyValue ([datetime]::UtcNow.ToString('o')) -Force; $item }
                else { $progress.Failed++ }
            } catch { $progress.Failed++ }
            $progress.Done++
            Start-Sleep -Milliseconds 250
        }
    }).AddArgument($ScriptDir).AddArgument(@($Items | ForEach-Object { $_ | Select-Object * })).AddArgument($progress).AddArgument($(if ($RefreshProvider) { $RefreshProvider.ToString() } else { $null }))
    $script:VrcSetupCatalogRefresh=[pscustomobject]@{Worker=$worker;Handle=$worker.BeginInvoke();Progress=$progress;ScriptDir=$ScriptDir}
    return $true
}

function Complete-VrcSetupPackageCatalogRefresh {
    param([string]$ScriptDir)
    $job=$script:VrcSetupCatalogRefresh
    if (-not $job -or -not $job.Handle.IsCompleted) { return }
    try {
        $items=@($job.Worker.EndInvoke($job.Handle))
        $file=Join-Path $job.ScriptDir 'cache\known-packages.json'
        if (Test-Path -LiteralPath $file) {
            $current=Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($item in $items) { $saved=$current | Where-Object Id -eq $item.Id | Select-Object -First 1; if ($saved) { $item.SelectedVersion=$saved.SelectedVersion } }
        }
        if ($items.Count) { Save-VrcSetupKnownPackages -Selections $items -ScriptDir $job.ScriptDir }
        foreach ($item in $items) { $script:VrcGetVersionsCache[$item.Id]=@($item.Versions) }
        $script:VrcSetupCatalogRefreshNotice="Updated $($job.Progress.Done - $job.Progress.Failed)/$($job.Progress.Total) packages$(if ($job.Progress.Failed) { '; unavailable entries kept' })."
    } catch { $script:VrcSetupCatalogRefreshNotice='Refresh stopped; saved definitions kept.' }
    finally { $job.Worker.Dispose(); $script:VrcSetupCatalogRefresh=$null }
}

function Invoke-VrcSetupPackageCatalog {
    param([string]$ConfigPath,[string]$ScriptDir)
    while ($true) {
        $config=Load-Config $ConfigPath
        $items=@(Get-VrcSetupKnownPackages -Config $config -ScriptDir $ScriptDir)
        $job=$script:VrcSetupCatalogRefresh
        $notice=if ($job) { "Refreshing in background: $($job.Progress.Done)/$($job.Progress.Total)" } elseif ($script:VrcSetupCatalogRefreshNotice) { $script:VrcSetupCatalogRefreshNotice } else { "$($items.Count) saved package definitions" }
        $choice=Show-Menu -Title 'Saved package registry' -Header $notice -Options @('Browse and use in a preset','Find and remember a package','Refresh in background','Stop background refresh','Back') -SectionBreaks @(2,4)
        if ($choice -lt 0 -or $choice -eq 4) { return }
        if ($choice -eq 2) { [void](Start-VrcSetupPackageCatalogRefresh -Items $items -ScriptDir $ScriptDir); continue }
        if ($choice -eq 3) { if ($job) { [void]$job.Worker.BeginStop($null,$null) }; continue }
        $selected = if ($choice -eq 1) { @(Find-VrcSetupPackages -WithVersions -SkipLocal) } else { @(Select-VrcSetupPackageResults -Items $items -Title 'Saved package registry' -Header 'Select packages to use in a preset.' -AllowVersionSelection) }
        if (-not $selected.Count) { continue }
        Save-VrcSetupKnownPackages -Selections $selected -ScriptDir $ScriptDir
        $presets=@([pscustomobject]@{Name='My default packages';Value=$config.VpmPackages})+@(Get-VrcSetupPackagePresets $config)
        $pick=Show-Menu -Title 'Use selected packages in' -Options (@($presets.Name)+@('Create a preset','Keep in registry only'))
        if ($pick -lt 0 -or $pick -gt $presets.Count) { continue }
        if ($pick -eq $presets.Count) {
            $name=Read-WizardTextInput -Title 'Create preset' -Prompt 'Preset name' -Hint 'Enter Save  |  Esc Cancel'
            if (-not $name) { continue }
            $set=Add-VrcSetupPackageSelections -Packages ([pscustomobject]@{}) -Selections $selected
            Save-VrcSetupPackagePreset -Config $config -Name $name -Packages $set
        } elseif ($pick -eq 0) { $config.VpmPackages=Add-VrcSetupPackageSelections -Packages $config.VpmPackages -Selections $selected }
        else { $set=Add-VrcSetupPackageSelections -Packages $presets[$pick].Value -Selections $selected; Save-VrcSetupPackagePreset -Config $config -Name $presets[$pick].Name -Packages $set }
        Save-Config -Config $config -ConfigPath $ConfigPath
    }
}

function Save-VrcSetupProjectPackageProfile {
    param([string]$ProjectPath,[string]$PresetName,$Policy,$Installed,$PresetDefinition)
    $file = Join-Path (Get-VrcSetupStateDir $ProjectPath) 'package-profile.json'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $file))
    [IO.File]::WriteAllText($file,([pscustomobject]@{Schema=1;Preset=$PresetName;PresetDefinition=$PresetDefinition;Policy=$Policy;Installed=$Installed;UpdatedUtc=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 8))
}

function Get-VrcSetupProjectPackageProfile {
    param([string]$ProjectPath,$Current,$Config)
    $file = Join-Path (Get-VrcSetupStateDir $ProjectPath) 'package-profile.json'
    if (Test-Path -LiteralPath $file) {
        try {
            $profile = Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
            $diff = Compare-VpmPackageSets -CurrentPackages $profile.Installed -DesiredPackages $Current
            $profile | Add-Member -NotePropertyName Modified -NotePropertyValue ((@($diff.Added)+@($diff.Updated)+@($diff.Removed)).Count -gt 0) -Force
            $definition=if ($profile.Preset -in @('My default packages','My package set')) { $Config.VpmPackages } else { @(Get-VrcSetupPackagePresets $Config | Where-Object Name -eq $profile.Preset | Select-Object -First 1).Value }
            $presetChanged=$false
            if ($profile.PresetDefinition -and $definition) { $delta=Compare-VpmPackageSets -CurrentPackages $profile.PresetDefinition -DesiredPackages $definition; $presetChanged=(@($delta.Added)+@($delta.Updated)+@($delta.Removed)).Count -gt 0 }
            $profile | Add-Member -NotePropertyName PresetChanged -NotePropertyValue $presetChanged -Force
            return $profile
        } catch { }
    }
    foreach ($preset in @([pscustomobject]@{Name='My default packages';Value=$Config.VpmPackages}) + @(Get-VrcSetupPackagePresets $Config)) {
        if (-not $preset.Value) { continue }
        $wanted = Add-RequiredPackagesToSet -Packages $preset.Value -Config $Config
        $diff = Compare-VpmPackageSets -CurrentPackages $Current -DesiredPackages $wanted
        if ((@($diff.Added)+@($diff.Updated)+@($diff.Removed)).Count -eq 0) { return [pscustomobject]@{Preset=$preset.Name;Policy=$wanted;Installed=$Current;Modified=$false} }
    }
    return [pscustomobject]@{Preset='Custom packages';Policy=$Current;Installed=$Current;Modified=$false}
}

function Get-VrcSetupBulkWorkspaceItems {
    param($Snapshots,$Packages,[string[]]$Remove=@(),[switch]$Replace,$Known,$Config,[hashtable]$PackageTargets=@{})
    $ids = @(@($Snapshots | ForEach-Object { $_.Packages.PSObject.Properties.Name }) + @($Packages.PSObject.Properties.Name) + @($Known.Id) | Select-Object -Unique)
    foreach ($id in $ids) {
        $present = @($Snapshots | Where-Object { $_.Packages.PSObject.Properties.Name -contains $id })
        $versions = @($present | ForEach-Object { [string]$_.Packages.($id) } | Select-Object -Unique)
        $current = if ($versions.Count -gt 1) { 'Mixed' } elseif ($versions.Count) { $versions[0] } else { '' }
        $staged = $Packages.PSObject.Properties.Name -contains $id
        $desired = if ($Remove -contains $id -or ($Replace -and -not $staged)) { '' } elseif ($staged) { [string]$Packages.($id) } else { $current }
        $status = if ($desired -and (-not $present.Count -or ($staged -and $present.Count -lt $Snapshots.Count))) { 'Add' } elseif (-not $desired -and $present.Count) { 'Remove' } elseif ($staged -and $current -ne $desired) { 'Update' } elseif (-not $desired) { 'Available' } elseif ($versions.Count -gt 1 -or $present.Count -lt $Snapshots.Count) { 'Mixed' } else { 'Installed' }
        $required = Test-IsRequiredPackage -PackageName $id -Config $Config
        $definition = @($Known | Where-Object Id -eq $id | Select-Object -First 1)
        [pscustomobject]@{Name=$id;FriendlyName=(Get-VrcSetupFriendlyPackageName $id);CurrentVersion=$current;DesiredVersion=$desired;Required=$required;Status=$status;Presence="$($present.Count)/$($Snapshots.Count)";SuggestedVersion=$(if ($definition.Count) { $definition[0].SelectedVersion } elseif ($versions.Count -eq 1) { $versions[0] } else { 'latest' });Scope=$(if ($PackageTargets.ContainsKey($id)) { "$(@($PackageTargets[$id]).Count) projects" } else { '' })}
    }
}
