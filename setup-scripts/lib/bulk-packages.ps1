# Bulk editing stages intent first; each project keeps its own current snapshot.
function Add-VrcSetupPackageSelections {
    param($Packages, $Selections)
    $result = Copy-VpmPackageSet -Packages $Packages
    foreach ($selection in @($Selections)) {
        $version = if ($selection.SelectedVersion) { [string]$selection.SelectedVersion } else { 'latest' }
        $result = Set-VrcSetupPackageVersion -Packages $result -PackageName ([string]$selection.Id) -Version $version
    }
    if (Get-Command Save-VrcSetupKnownPackages -ErrorAction SilentlyContinue) { try { Save-VrcSetupKnownPackages -Selections $Selections -ScriptDir $scriptDir } catch { Write-Warning "Could not save local package definitions: $_" } }
    return $result
}

function Get-VrcSetupBulkDesiredPackages {
    param($Current, $Packages, [string[]]$Remove = @(), [switch]$Replace, $Config)
    $desired = if ($Replace) { Copy-VpmPackageSet -Packages $Packages } else { Copy-VpmPackageSet -Packages $Current }
    if (-not $Replace) {
        foreach ($property in $Packages.PSObject.Properties) {
            $desired = Set-VrcSetupPackageVersion -Packages $desired -PackageName $property.Name -Version ([string]$property.Value)
        }
    }
    foreach ($id in $Remove) {
        if (Test-IsRequiredPackage -PackageName $id -Config $Config) { throw "Cannot remove required package: $id" }
        $desired.PSObject.Properties.Remove($id)
    }
    return Add-RequiredPackagesToSet -Packages $desired -Config $Config
}

function Get-VrcSetupBulkPlan {
    param([string[]]$ProjectPaths, $Packages, [string[]]$Remove = @(), [switch]$Replace, $Config, [string]$ScriptDir, [hashtable]$PackageTargets = @{})
    foreach ($path in @($ProjectPaths | Select-Object -Unique)) {
        if (-not (Test-VrcSetupUnityProject -Path $path)) { throw "Not a Unity project: $path" }
        $current = Get-VpmProjectPackageSet -ProjectPath $path -IncludeLockedPackages @(Get-RequiredPackages -Config $Config)
        $scoped=Copy-VpmPackageSet -Packages $Packages
        if (-not $Replace) {
            foreach ($property in @($scoped.PSObject.Properties)) {
                if ($PackageTargets.ContainsKey($property.Name) -and $path -notin @($PackageTargets[$property.Name])) { $scoped.PSObject.Properties.Remove($property.Name) }
            }
        }
        $scopedRemove = @($Remove | Where-Object { -not $PackageTargets.ContainsKey($_) -or $path -in @($PackageTargets[$_]) })
        $desired = Get-VrcSetupBulkDesiredPackages -Current $current -Packages $scoped -Remove $scopedRemove -Replace:$Replace -Config $Config
        $desired = Resolve-VrcSetupSdkPackageSet -Packages $desired -ScriptDir $ScriptDir
        $diff = Compare-VpmPackageSets -CurrentPackages $current -DesiredPackages $desired
        [pscustomobject]@{ Path=$path; Current=$current; Desired=$desired; Diff=$diff; ReadNames=@(Get-RequiredPackages -Config $Config) }
    }
}

function Test-VrcSetupBulkProjectBusy {
    param([string]$ProjectPath)
    if (@(Get-UnityProcessesUsingProjectPath -ProjectPath $ProjectPath).Count -gt 0) { return $true }
    $lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lock) {
        try { $handle=[IO.File]::Open($lock,'Open','ReadWrite','None'); $handle.Dispose() }
        catch { return $true }
    }
    return $false
}

function Invoke-VrcSetupBulkPlan {
    param($Plan)
    $results = @()
    foreach ($entry in @($Plan)) {
        $name = Split-Path -Leaf $entry.Path
        $projectLog = ''
        Write-Host "Project: $name"
        try {
            if (Test-VrcSetupBulkProjectBusy -ProjectPath $entry.Path) { throw 'Open in Unity; skipped.' }
            $fresh = Get-VpmProjectPackageSet -ProjectPath $entry.Path -IncludeLockedPackages $entry.ReadNames
            $changed = Compare-VpmPackageSets -CurrentPackages $entry.Current -DesiredPackages $fresh
            if (@($changed.Added).Count + @($changed.Updated).Count + @($changed.Removed).Count -gt 0) { throw 'Packages changed after review; skipped. Review again.' }
            $diff = $entry.Diff
            if (@($diff.Added).Count + @($diff.Updated).Count + @($diff.Removed).Count -eq 0 -and -not $entry.ImportExtras) { $status='Unchanged' }
            else {
                $parameters = @{projectPath=$entry.Path;PackagesOverride=$entry.Desired;SyncPackages=$true}
                if ($entry.ImportExtras) { $parameters.ImportExtras=$true }
                $code = Start-Installer @parameters
                $projectLog = [string]$global:VRCSETUP_LOGFILE
                if ([int]$code -in @(2,130)) { $results += [pscustomobject]@{Project=$name;Path=$entry.Path;Status='Cancelled';Message='Operation interrupted';LogFile=[string]$global:VRCSETUP_LOGFILE}; break }
                if ([int]$code -ne 0) { throw "Package update failed (status $code)." }
                $status='Saved'
            }
            if ($entry.PresetName) {
                try { $installed=Get-VpmProjectPackageSet -ProjectPath $entry.Path -IncludeLockedPackages $entry.ReadNames; Save-VrcSetupProjectPackageProfile -ProjectPath $entry.Path -PresetName $entry.PresetName -Policy $entry.Desired -Installed $installed -PresetDefinition $entry.PresetDefinition }
                catch { Write-Warning "Packages saved, but profile metadata could not be written: $_" }
            }
            $results += [pscustomobject]@{Project=$name;Path=$entry.Path;Status=$status;Message='';LogFile=$projectLog}
        } catch {
            $results += [pscustomobject]@{Project=$name;Path=$entry.Path;Status='Skipped or failed';Message=$_.Exception.Message;LogFile=$projectLog}
        }
        Write-Host "$name : $($results[-1].Status) $($results[-1].Message)"
    }
    return $results
}

function Write-VrcSetupBulkReport {
    param($Results, [string[]]$ProjectPaths, [string]$ScriptDir)
    $folder=Join-Path $ScriptDir 'logs'
    [void][IO.Directory]::CreateDirectory($folder)
    $path=Join-Path $folder ("bulk-{0}-{1}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'),[guid]::NewGuid().ToString('N').Substring(0,8))
    $report=[pscustomobject]@{CompletedUtc=[datetime]::UtcNow.ToString('o');Results=@($Results);NotProcessed=@($ProjectPaths | Where-Object { $_ -notin @($Results.Path) })}
    [IO.File]::WriteAllText($path,($report | ConvertTo-Json -Depth 8))
    return $path
}

function Invoke-VrcSetupBulkPackageEditor {
    param([string[]]$ProjectPaths, $Config, [string]$ConfigPath, [string]$ScriptDir, $ExtrasInfo)
    $packages=[pscustomobject]@{}; $remove=@(); $replace=$false; $targets=@{}
    $snapshots = @()
    try {
        [void](Write-VrcSetupSpectreFrame -Title 'Project packages' -Header 'Reading selected project packages...' -ScriptDir $ScriptDir)
        $snapshots = @($ProjectPaths | ForEach-Object { [pscustomobject]@{Path=$_;Packages=(Get-VpmProjectPackageSet -ProjectPath $_ -IncludeLockedPackages @(Get-RequiredPackages -Config $Config))} })
    } catch { Show-WizardError -Title 'Cannot read projects' -Message $_.Exception.Message; return }
    $profiles = @($snapshots | ForEach-Object { Get-VrcSetupProjectPackageProfile -ProjectPath $_.Path -Current $_.Packages -Config $Config })
    Save-VrcSetupKnownPackages -Selections @(Get-VrcSetupKnownPackages -Config $Config -PackageSets @($snapshots.Packages) -ScriptDir $ScriptDir) -ScriptDir $ScriptDir
    $profileNames = @($profiles.Preset | Select-Object -Unique)
    $presetName = if ($profileNames.Count -eq 1) { $profileNames[0] } else { 'Different presets' }
    if (@($profiles | Where-Object Modified).Count) { $presetName += ' (changed outside VrcSetup)' }
    if (@($profiles | Where-Object PresetChanged).Count) { $presetName += ' (preset updated)' }
    $originalPresetName = $presetName
    $index = 0; $includeExtras = $false
    while ($true) {
        $known = @(Get-VrcSetupKnownPackages -Config $Config -PackageSets @($snapshots.Packages) -ScriptDir $ScriptDir)
        $items = @(Get-VrcSetupBulkWorkspaceItems -Snapshots $snapshots -Packages $packages -Remove $remove -Replace:$replace -Known $known -Config $Config -PackageTargets $targets | Sort-Object @{Expression={ if ($_.Required) { 0 } else { 1 } }}, FriendlyName)
        $pending = @($items | Where-Object { $_.Status -in @('Add','Update','Remove') }).Count
        $header = "$(if ($ProjectPaths.Count -eq 1) { Split-Path -Leaf $ProjectPaths[0] } else { "$($ProjectPaths.Count) selected projects" })`nPreset: $presetName$(if ($replace) { ' | Match package set' } else { ' | Keep other packages' })"
        $selected = Show-VrcSetupSpectrePackageWorkspace -Items $items -ProjectName $header -PendingCount $pending -InitialSelectedIndex $index -IncludeExtras:$includeExtras -ExtrasCount $(if ($ExtrasInfo) { $ExtrasInfo.ExtrasCount } else { 0 }) -ScriptDir $ScriptDir
        if ($null -eq $selected) {
            $labels = @($items | ForEach-Object { "$(if ($_.DesiredVersion) { '[x]' } else { '[ ]' }) $($_.FriendlyName) | $($_.DesiredVersion) | $($_.Presence)" }) + @('Apply changes','Add package','Change preset','Copy project','Options','Back')
            $choice = Show-Menu -Title 'Project packages' -Header $header -Options $labels
            if ($choice -lt 0 -or $choice -eq $labels.Count-1) { return }
            if ($choice -lt $items.Count) {
                $rowAction=Show-Menu -Title $items[$choice].FriendlyName -Options @('Choose version','Include or remove','Back')
                if ($rowAction -lt 0 -or $rowAction -eq 2) { continue }
                $selected=[pscustomobject]@{Action=$(if ($rowAction -eq 0) { 'version' } else { 'toggle' });PackageName=$items[$choice].Name;SelectedIndex=$choice}
            } else { $selected=[pscustomobject]@{Action=@('save','add','presets','copy','options')[$choice-$items.Count];SelectedIndex=$choice} }
        }
        $index = $selected.SelectedIndex
        if ($selected.Action -eq 'back') { return }
        foreach ($change in @($selected.VersionChanges.PSObject.Properties)) { $packages=Set-VrcSetupPackageVersion -Packages $packages -PackageName $change.Name -Version $change.Value; $remove=@($remove | Where-Object { $_ -ne $change.Name }) }
        if ($selected.Action -in @('toggle','version')) {
            $item = $items | Where-Object Name -eq $selected.PackageName | Select-Object -First 1
            if (-not $item) { continue }
            if ($selected.Action -eq 'toggle') {
                if ($item.Required) { continue }
                if ($item.DesiredVersion -and $item.Status -ne 'Mixed') {
                    $packages.PSObject.Properties.Remove($item.Name)
                    if ($item.CurrentVersion) { $remove=@(@($remove)+@($item.Name) | Select-Object -Unique) }
                } else {
                    $version = if ($item.CurrentVersion -and $item.CurrentVersion -ne 'Mixed') { $item.CurrentVersion } else { $item.SuggestedVersion }
                    $packages=Set-VrcSetupPackageVersion -Packages $packages -PackageName $item.Name -Version $version
                    $remove=@($remove | Where-Object { $_ -ne $item.Name })
                }
            } else {
                $version=Select-VpmVersion -PackageName $item.Name -CurrentVersion $(if ($item.DesiredVersion -and $item.DesiredVersion -ne 'Mixed') { $item.DesiredVersion } else { $item.SuggestedVersion })
                if ($version) { $packages=Set-VrcSetupPackageVersion -Packages $packages -PackageName $item.Name -Version $version; $remove=@($remove | Where-Object { $_ -ne $item.Name }) }
            }
            continue
        }
        $command = switch ($selected.Action) { 'save' { 'Apply changes' } 'add' { 'Add package' } 'presets' { 'Change preset' } 'copy' { 'Copy project' } 'options' { 'Options' } }
        switch ($command) {
            'Add package' {
                $selections=@(Find-VrcSetupPackages -KnownPackages $known -WithVersions)
                $packages=Add-VrcSetupPackageSelections -Packages $packages -Selections $selections
                $remove=@($remove | Where-Object { $_ -notin @($selections.Id) })
            }
            'Change preset' {
                $presets=@([pscustomobject]@{Name='My default packages';Value=$Config.VpmPackages})+@(Get-VrcSetupPackagePresets -Config $Config)
                $pick=Show-Menu -Title 'Choose preset' -Options (@($presets.Name)+@('Back'))
                if ($pick -lt 0 -or $pick -ge $presets.Count) { continue }
                $mode=Show-Menu -Title 'Use preset' -Options @('Use preset; keep custom packages','Match preset exactly','Back')
                if ($mode -lt 0 -or $mode -eq 2) { continue }
                $preset=Add-RequiredPackagesToSet -Packages (Copy-VpmPackageSet $presets[$pick].Value) -Config $Config
                $packages=$preset; $replace=($mode -eq 1); $remove=@(); $targets.Clear(); $presetName=$presets[$pick].Name; $index=0
            }
            'Copy project' {
                try {
                    [void](Write-VrcSetupSpectreFrame -Title 'Copy packages' -Header 'Loading project library...' -ScriptDir $ScriptDir)
                    $catalog=Get-VrcSetupProjectCatalog -RootPath $Config.UnityProjectsRoot -CachePath (Join-Path $ScriptDir 'cache\projects.json')
                    $source=Show-VrcSetupProjectCatalogSpectre -Catalog $catalog -ScriptDir $ScriptDir -State ([pscustomobject]@{Filter='';Index=0;Done=$false;Result=$null;SingleSelection=$true})
                    if ($null -eq $source) {
                        $options=@($catalog.Projects | ForEach-Object Name)+@('Back')
                        $pick=Show-Menu -Title 'Copy packages from' -Options $options
                        if ($pick -lt 0 -or $pick -ge $catalog.Projects.Count) { continue }
                        $path=$catalog.Projects[$pick].Path
                    } elseif ($source.Action -eq 'project') { $path=$source.ProjectPath } else { continue }
                    [void](Write-VrcSetupSpectreFrame -Title 'Copy packages' -Header 'Reading source project packages...' -ScriptDir $ScriptDir)
                    $copied=Get-VpmProjectPackageSet -ProjectPath $path -IncludeLockedPackages @(Get-RequiredPackages -Config $Config)
                    Save-VrcSetupKnownPackages -Selections @(Get-VrcSetupKnownPackages -Config $Config -PackageSets @($copied) -ScriptDir $ScriptDir) -ScriptDir $ScriptDir
                    $sourceProfile=Get-VrcSetupProjectPackageProfile -ProjectPath $path -Current $copied -Config $Config
                    if ($sourceProfile.Preset -ne 'Custom packages') {
                        $sourceMode=Show-Menu -Title 'Source package selection' -Header ("Preset: " + $sourceProfile.Preset + $(if ($sourceProfile.Modified) { ' (project changed)' })) -Options @('Copy installed packages','Use saved preset selection','Back')
                        if ($sourceMode -lt 0 -or $sourceMode -eq 2) { continue }
                        if ($sourceMode -eq 1) { $copied=Copy-VpmPackageSet $sourceProfile.Policy }
                    }
                    $mode=Show-Menu -Title 'Use source packages' -Header (Split-Path -Leaf $path) -Options @('Copy packages; keep others','Match source exactly','Back')
                    if ($mode -lt 0 -or $mode -eq 2) { continue }
                    $packages=$copied; $replace=($mode -eq 1); $remove=@(); $targets.Clear(); $presetName='From '+(Split-Path -Leaf $path)
                } catch { Show-WizardError -Title 'Cannot read source project' -Message $_.Exception.Message }
            }
            'Options' {
                $option=Show-Menu -Title 'Package options' -Options @('Choose projects for highlighted package','Additional imports','Undo all changes','Back')
                if ($option -eq 0 -and $index -lt $items.Count) {
                    if ($replace) { Show-WizardError -Title 'Matching full set' -Message 'Use keep custom packages to choose individual project groups.'; continue }
                    $item=$items[$index]
                    $projectItems=@($ProjectPaths | ForEach-Object { [pscustomobject]@{Path=$_;Name=(Split-Path -Leaf $_)} })
                    $chosen=@(Show-ChecklistPaged -Title 'Apply this package to' -Items $projectItems -DefaultSelected:$true -ToLabel { param($p,$i) $p.Name })
                    if ($chosen.Count) {
                        $version=if ($item.DesiredVersion -and $item.DesiredVersion -ne 'Mixed') { $item.DesiredVersion } else { $item.SuggestedVersion }
                        if ($remove -notcontains $item.Name) { $packages=Set-VrcSetupPackageVersion -Packages $packages -PackageName $item.Name -Version $version }
                        foreach ($id in $(if (Test-IsVrcSetupSdkPackage $item.Name) { @(Get-VrcSetupSdkPackageNames) } else { @($item.Name) })) { $targets[$id]=@($chosen.Path) }
                    }
                } elseif ($option -eq 1 -and $ExtrasInfo -and $ExtrasInfo.ExtrasCount -gt 0) { $includeExtras=-not $includeExtras }
                elseif ($option -eq 2) { $packages=[pscustomobject]@{}; $remove=@(); $replace=$false; $targets.Clear(); $includeExtras=$false; $presetName=$originalPresetName }
            }
            'Apply changes' {
                try { $plan=@(Get-VrcSetupBulkPlan -ProjectPaths $ProjectPaths -Packages $packages -Remove $remove -Replace:$replace -Config $Config -ScriptDir $ScriptDir -PackageTargets $targets) }
                catch { Show-WizardError -Title 'Cannot prepare changes' -Message $_.Exception.Message; continue }
                $definition=if ($presetName -in @('My default packages','My package set')) { $Config.VpmPackages } else { @(Get-VrcSetupPackagePresets $Config | Where-Object Name -eq $presetName | Select-Object -First 1).Value }
                foreach ($entry in $plan) { $entry | Add-Member -NotePropertyName PresetName -NotePropertyValue $presetName -Force; $entry | Add-Member -NotePropertyName PresetDefinition -NotePropertyValue $definition -Force; $entry | Add-Member -NotePropertyName ImportExtras -NotePropertyValue $includeExtras -Force }
                $lines=@($plan | ForEach-Object { "$(Split-Path -Leaf $_.Path): +$(@($_.Diff.Added).Count) add, $(@($_.Diff.Updated).Count) update, -$(@($_.Diff.Removed).Count) remove" })
                $visibleCount=[Math]::Max(2,[Math]::Min(6,(Get-VrcSetupViewport).Height - 20))
                $summary=@($lines | Select-Object -First $visibleCount)
                if ($lines.Count -gt $visibleCount) { $summary += "+$($lines.Count - $visibleCount) more projects — View package details" }
                $pick=Show-Menu -Title 'Review project changes' -Header ($summary -join "`n") -Options @('Apply to selected projects','View package details','Back')
                if ($pick -eq 1) {
                    $projectChoice=Show-Menu -Title 'Choose project to review' -Options ($lines+@('Back'))
                    if ($projectChoice -lt 0 -or $projectChoice -ge $plan.Count) { continue }
                    $entry=$plan[$projectChoice]
                    $details=@(
                        foreach ($id in @($entry.Diff.Added)+@($entry.Diff.Updated)) { "$(Get-VrcSetupFriendlyPackageName -PackageName $id): $($entry.Current.($id)) -> $($entry.Desired.($id))" }
                        foreach ($id in @($entry.Diff.Removed)) { "Remove $(Get-VrcSetupFriendlyPackageName -PackageName $id)" }
                    )
                    if (-not $details.Count) { $details=@('No changes') }
                    $page=0
                    do {
                        $pageCount=[int][Math]::Ceiling($details.Count / $visibleCount)
                        $detailHeader=@("$(Split-Path -Leaf $entry.Path)  ·  $($page+1)/$pageCount")+@($details | Select-Object -Skip ($page*$visibleCount) -First $visibleCount)
                        $nav=Show-Menu -Title 'Package changes' -Header ($detailHeader -join "`n") -Options @('Back to review','Next page','Previous page')
                        if ($nav -eq 1) { $page=[Math]::Min($pageCount-1,$page+1) }
                        elseif ($nav -eq 2) { $page=[Math]::Max(0,$page-1) }
                    } while ($nav -gt 0)
                    continue
                }
                if ($pick -ne 0) { continue }
                $operation={
                    param($runtime,$entries)
                    $env:VRCSETUP_PROGRESS_PLAIN='1'; $env:VRCSETUP_EMBEDDED_PROGRESS='1'
                    . (Join-Path $runtime 'commands\installer.ps1')
                    . (Join-Path $runtime 'lib\bulk-packages.ps1')
                    $results=@(Invoke-VrcSetupBulkPlan -Plan $entries)
                    foreach ($result in $results) { Write-Host "$($result.Project): $($result.Status) $($result.Message)" }
                    $failed=@($results | Where-Object { $_.Status -notin @('Saved','Unchanged') }).Count
                    if ($failed -eq 0 -and $results.Count -eq $entries.Count) {
                        Write-Host "Completed: $($results.Count) projects saved or already up to date."
                    } else {
                        Write-Host "Finished: $($results.Count - $failed) succeeded, $failed skipped or failed, $($entries.Count - $results.Count) not processed."
                    }
                    $reportPath=Write-VrcSetupBulkReport -Results $results -ProjectPaths @($entries.Path) -ScriptDir $runtime
                    [pscustomobject]@{VrcSetupOperationResult=$true;Status=$(if (@($results | Where-Object Status -eq 'Cancelled').Count) { 2 } elseif ($failed) { 1 } else { 0 });LogFile=$reportPath}
                }
                $outcome=Invoke-VrcSetupSpectreOperation -Title 'Updating selected projects' -Header "$($ProjectPaths.Count) projects" -Operation $operation -ArgumentList @($ScriptDir,$plan) -ScriptDir $ScriptDir
                if ($null -eq $outcome) {
                    $results=@(Invoke-VrcSetupBulkPlan -Plan $plan)
                    $reportPath=Write-VrcSetupBulkReport -Results $results -ProjectPaths $ProjectPaths -ScriptDir $ScriptDir
                    [void](Show-Menu -Title 'Project updates finished' -Header (($results | ForEach-Object { "$($_.Project): $($_.Status) $($_.Message)" }) -join "`n") -Options @('Done — Back to projects'))
                    $operationStatus=if (@($results | Where-Object { $_.Status -notin @('Saved','Unchanged') }).Count) { 1 } else { 0 }
                } else { $operationStatus=[int]$outcome.Status }
                if ($operationStatus -eq 1) {
                    $recovery=Show-Menu -Title 'Some changes were not applied' -Options @('Continue editing or retry','Back to projects')
                    if ($recovery -eq 0) {
                        try { $snapshots=@($ProjectPaths | ForEach-Object { [pscustomobject]@{Path=$_;Packages=(Get-VpmProjectPackageSet -ProjectPath $_ -IncludeLockedPackages @(Get-RequiredPackages -Config $Config))} }); continue }
                        catch { Show-WizardError -Title 'Cannot reread projects' -Message $_.Exception.Message }
                    }
                }
                return
            }
        }
    }
}
