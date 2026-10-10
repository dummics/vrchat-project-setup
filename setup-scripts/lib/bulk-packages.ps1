# Bulk editing stages intent first; each project keeps its own current snapshot.
function Add-VrcSetupPackageSelections {
    param($Packages, $Selections)
    $result = Copy-VpmPackageSet -Packages $Packages
    foreach ($selection in @($Selections)) {
        $version = if ($selection.SelectedVersion) { [string]$selection.SelectedVersion } else { 'latest' }
        $result = Set-VrcSetupPackageVersion -Packages $result -PackageName ([string]$selection.Id) -Version $version
    }
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
        $desired = Get-VrcSetupBulkDesiredPackages -Current $current -Packages $scoped -Remove $Remove -Replace:$Replace -Config $Config
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
            if (@($diff.Added).Count + @($diff.Updated).Count + @($diff.Removed).Count -eq 0) { $status='Unchanged' }
            else {
                $code = Start-Installer -projectPath $entry.Path -PackagesOverride $entry.Desired -SyncPackages
                $projectLog = [string]$global:VRCSETUP_LOGFILE
                if ([int]$code -in @(2,130)) { $results += [pscustomobject]@{Project=$name;Path=$entry.Path;Status='Cancelled';Message='Operation interrupted';LogFile=[string]$global:VRCSETUP_LOGFILE}; break }
                if ([int]$code -ne 0) { throw "Package update failed (status $code)." }
                $status='Saved'
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
    param([string[]]$ProjectPaths, $Config, [string]$ConfigPath, [string]$ScriptDir)
    $packages=[pscustomobject]@{}; $remove=@(); $replace=$false; $targets=@{}
    while ($true) {
        $properties=@($packages.PSObject.Properties)
        $options=@($properties | ForEach-Object { "$(Get-VrcSetupFriendlyPackageName -PackageName $_.Name)  ·  $($_.Value)$(if ($targets.ContainsKey($_.Name)) { "  ·  $(@($targets[$_.Name]).Count)/$($ProjectPaths.Count) projects" })" })
        $options += @('Add or change packages','Apply a creator preset','Copy packages from a selected project','Remove packages from projects','Review changes','Cancel')
        $header="$($ProjectPaths.Count) projects  ·  $(if ($replace) { 'Match selected package set; other packages will be removed' } else { 'Keep other packages' })"
        if ($remove.Count) { $header += "`nRemove: $($remove -join ', ')" }
        $choice=Show-Menu -Title 'Edit project packages' -Header $header -Options $options -SectionBreaks @($properties.Count)
        if ($choice -lt 0 -or $options[$choice] -eq 'Cancel') { return }
        if ($choice -lt $properties.Count) {
            $property=$properties[$choice]
            $action=Show-Menu -Title (Get-VrcSetupFriendlyPackageName -PackageName $property.Name) -Options @('Choose version','Choose projects for this package','Undo this change','Back')
            if ($action -eq 0) {
                $version=Select-VpmVersion -PackageName $property.Name -CurrentVersion $property.Value
                if ($version) { $packages=Set-VrcSetupPackageVersion -Packages $packages -PackageName $property.Name -Version $version }
            } elseif ($action -eq 1) {
                if ($replace) { Show-WizardError -Title 'Matching the full set' -Message 'Individual project selection is available when keeping other packages.'; continue }
                $projectItems=@($ProjectPaths | ForEach-Object { [pscustomobject]@{Path=$_;Name=(Split-Path -Leaf $_)} })
                $chosen=@(Show-ChecklistPaged -Title 'Apply this package to' -Items $projectItems -DefaultSelected:$true -ToLabel { param($p,$i) $p.Name })
                if ($chosen.Count) {
                    $linked = if (Test-IsVrcSetupSdkPackage -PackageName $property.Name) { @(Get-VrcSetupSdkPackageNames) } else { @($property.Name) }
                    foreach ($id in $linked) { $targets[$id]=@($chosen.Path) }
                }
            } elseif ($action -eq 2) { $packages.PSObject.Properties.Remove($property.Name); $targets.Remove($property.Name) }
            continue
        }
        switch ($options[$choice]) {
            'Add or change packages' {
                $selections=@(Find-VrcSetupPackages -WithVersions)
                $packages=Add-VrcSetupPackageSelections -Packages $packages -Selections $selections
                $remove=@($remove | Where-Object { $_ -notin @($selections.Id) })
            }
            'Apply a creator preset' {
                $presets=@(Get-VrcSetupPackagePresets -Config $Config)
                if (-not $presets.Count) { Show-WizardError -Title 'No presets yet' -Message 'Create one under Packages and creator presets.'; continue }
                $pick=Show-Menu -Title 'Choose preset' -Options (@($presets.Name)+@('Back'))
                if ($pick -lt 0 -or $pick -ge $presets.Count) { continue }
                $mode=Show-Menu -Title 'Apply preset' -Options @('Add or update preset packages; keep others','Match preset exactly; remove other packages','Back')
                if ($mode -lt 0 -or $mode -eq 2) { continue }
                $preset=Resolve-VrcSetupPackagePreset -Config $Config -Name $presets[$pick].Name
                if ($mode -eq 1) { $packages=$preset; $replace=$true; $remove=@(); $targets.Clear() }
                else { $replace=$false; foreach ($p in $preset.PSObject.Properties) { $packages=Set-VrcSetupPackageVersion -Packages $packages -PackageName $p.Name -Version $p.Value; $targets.Remove($p.Name) }; $remove=@($remove | Where-Object { $_ -notin @($preset.PSObject.Properties.Name) }) }
            }
            'Copy packages from a selected project' {
                $pick=Show-Menu -Title 'Use packages from' -Options (@($ProjectPaths | ForEach-Object { Split-Path -Leaf $_ })+@('Back'))
                if ($pick -lt 0 -or $pick -ge $ProjectPaths.Count) { continue }
                try { $packages=Get-VpmProjectPackageSet -ProjectPath $ProjectPaths[$pick] -IncludeLockedPackages @(Get-RequiredPackages -Config $Config); $replace=$true; $remove=@(); $targets.Clear() }
                catch { Show-WizardError -Title 'Cannot read packages' -Message $_.Exception.Message }
            }
            'Remove packages from projects' {
                try {
                    $ids=@($ProjectPaths | ForEach-Object { (Get-VpmProjectPackageSet -ProjectPath $_).PSObject.Properties.Name } | Select-Object -Unique | Where-Object { -not (Test-IsRequiredPackage -PackageName $_ -Config $Config) })
                    $items=@($ids | ForEach-Object { [pscustomobject]@{Id=$_;DisplayName=(Get-VrcSetupFriendlyPackageName -PackageName $_);LatestVersion='remove';SelectionVerb='Remove'} })
                    $selected=@(Select-VrcSetupPackageResults -Items $items -Title 'Remove from selected projects')
                    foreach ($item in $selected) { $remove += $item.Id; $packages.PSObject.Properties.Remove($item.Id) }
                    $remove=@($remove | Select-Object -Unique)
                } catch { Show-WizardError -Title 'Cannot read packages' -Message $_.Exception.Message }
            }
            'Review changes' {
                try { $plan=@(Get-VrcSetupBulkPlan -ProjectPaths $ProjectPaths -Packages $packages -Remove $remove -Replace:$replace -Config $Config -ScriptDir $ScriptDir -PackageTargets $targets) }
                catch { Show-WizardError -Title 'Cannot prepare changes' -Message $_.Exception.Message; continue }
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
                    Write-Host "Finished: $($results.Count - $failed) succeeded, $failed skipped or failed, $($entries.Count - $results.Count) not processed."
                    $reportPath=Write-VrcSetupBulkReport -Results $results -ProjectPaths @($entries.Path) -ScriptDir $runtime
                    [pscustomobject]@{VrcSetupOperationResult=$true;Status=$(if (@($results | Where-Object Status -eq 'Cancelled').Count) { 2 } elseif ($failed) { 1 } else { 0 });LogFile=$reportPath}
                }
                $outcome=Invoke-VrcSetupSpectreOperation -Title 'Updating selected projects' -Header "$($ProjectPaths.Count) projects" -Operation $operation -ArgumentList @($ScriptDir,$plan) -ScriptDir $ScriptDir
                if ($null -eq $outcome) {
                    $results=@(Invoke-VrcSetupBulkPlan -Plan $plan)
                    $reportPath=Write-VrcSetupBulkReport -Results $results -ProjectPaths $ProjectPaths -ScriptDir $ScriptDir
                    [void](Show-Menu -Title 'Project updates finished' -Header (($results | ForEach-Object { "$($_.Project): $($_.Status) $($_.Message)" }) -join "`n") -Options @('Done — Back to projects'))
                }
                return
            }
        }
    }
}
