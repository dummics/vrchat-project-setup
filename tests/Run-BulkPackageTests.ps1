[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\setup-scripts\commands\wizard.ps1')
function Assert-Bulk { param($Condition,$Message); if (-not $Condition) { throw $Message } }
$config=[pscustomobject]@{}
$root=Join-Path $env:TEMP ('vrcsetup-bulk-test-'+[guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($root)
    $paths=@()
    foreach ($name in @('Avatar PC [test]','Avatar Quest','Busy','Stale','Failure','After failure')) {
        $path=Join-Path $root $name; $paths += $path
        foreach ($folder in @('Assets','Packages','ProjectSettings')) { [void][IO.Directory]::CreateDirectory((Join-Path $path $folder)) }
        $set=Add-RequiredPackagesToSet -Packages ([pscustomobject]@{'com.example.keep'='1.0.0';'com.example.tool'='1.0.0'}) -Config $config
        $deps=[ordered]@{}; foreach ($p in $set.PSObject.Properties) { $deps[$p.Name]=@{version=$p.Value} }
        [IO.File]::WriteAllText((Join-Path $path 'Packages\vpm-manifest.json'),(@{dependencies=$deps;locked=@{}} | ConvertTo-Json -Depth 8))
    }
    function Resolve-VrcSetupSdkPackageSet { param($Packages,$ScriptDir); return $Packages }
    $selection=@([pscustomobject]@{Id='com.example.tool';SelectedVersion='2.3.4'},[pscustomobject]@{Id='com.example.new'})
    $staged=Add-VrcSetupPackageSelections -Packages ([pscustomobject]@{}) -Selections $selection
    Assert-Bulk ($staged.'com.example.tool' -eq '2.3.4' -and $staged.'com.example.new' -eq 'latest') 'Add discarded chosen version or latest default.'
    $plan=@(Get-VrcSetupBulkPlan -ProjectPaths $paths -Packages $staged -Config $config -ScriptDir $scriptDir)
    Assert-Bulk ($plan.Count -eq 6 -and $plan[0].Desired.'com.example.keep' -eq '1.0.0') 'Overlay discarded unrelated package or project.'
    Assert-Bulk ($plan[0].Diff.Updated -contains 'com.example.tool' -and $plan[0].Diff.Added -contains 'com.example.new') 'Per-project review lost add/update intent.'
    Assert-Bulk ((Get-VpmProjectPackageSet $paths[0]).'com.example.tool' -eq '1.0.0') 'Staging modified a manifest.'
    $scoped=@(Get-VrcSetupBulkPlan -ProjectPaths @($paths[0],$paths[1]) -Packages $staged -Config $config -ScriptDir $scriptDir -PackageTargets @{'com.example.tool'=@($paths[0]);'com.example.new'=@($paths[1])})
    Assert-Bulk ($scoped[0].Desired.'com.example.tool' -eq '2.3.4' -and $scoped[0].Desired.PSObject.Properties.Name -notcontains 'com.example.new' -and $scoped[1].Desired.'com.example.tool' -eq '1.0.0' -and $scoped[1].Desired.'com.example.new' -eq 'latest') 'Different package target groups leaked into other selected projects.'
    $exact=@(Get-VrcSetupBulkPlan -ProjectPaths @($paths[0]) -Packages $staged -Replace -Config $config -ScriptDir $scriptDir)
    Assert-Bulk ($exact[0].Diff.Removed -contains 'com.example.keep' -and $exact[0].Desired.PSObject.Properties.Name -contains 'com.vrchat.base') 'Exact synchronization failed or removed required foundation.'
    $removed=@(Get-VrcSetupBulkPlan -ProjectPaths @($paths[0]) -Packages ([pscustomobject]@{}) -Remove @('com.example.keep') -Config $config -ScriptDir $scriptDir)
    Assert-Bulk ($removed[0].Diff.Removed -contains 'com.example.keep' -and $removed[0].Desired.'com.example.tool' -eq '1.0.0') 'Targeted removal affected unrelated packages.'
    $denied=$false; try { Get-VrcSetupBulkDesiredPackages -Current $plan[0].Current -Packages $staged -Remove @('com.vrchat.base') -Config $config | Out-Null } catch { $denied=$true }
    Assert-Bulk $denied 'Required package could be removed.'
    $script:called=@()
    function Test-VrcSetupBulkProjectBusy { param($ProjectPath); return (Split-Path -Leaf $ProjectPath) -eq 'Busy' }
    function Start-Installer { param($projectPath,$PackagesOverride,[switch]$SyncPackages); $script:called += $projectPath; if ((Split-Path -Leaf $projectPath) -eq 'Failure') { return 1 }; return 0 }
    $manifest=Join-Path $paths[3] 'Packages\vpm-manifest.json'
    $content=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $content.dependencies.'com.example.tool'.version='9.0.0'
    [IO.File]::WriteAllText($manifest,($content | ConvertTo-Json -Depth 8))
    $results=@(Invoke-VrcSetupBulkPlan -Plan $plan)
    Assert-Bulk ($results.Count -eq 6 -and $script:called.Count -eq 4) 'Busy or stale project reached the installer.'
    Assert-Bulk ($results[-1].Status -eq 'Saved' -and $results[4].Status -eq 'Skipped or failed') 'A failed project prevented later projects or was reported as success.'
    $script:called=@()
    function Start-Installer { param($projectPath,$PackagesOverride,[switch]$SyncPackages); $script:called += $projectPath; return 2 }
    $results=@(Invoke-VrcSetupBulkPlan -Plan $plan)
    Assert-Bulk ($script:called.Count -eq 1 -and $results.Count -eq 1 -and $results[0].Status -eq 'Cancelled') 'Cancellation continued updating projects.'
    $reportPath=Write-VrcSetupBulkReport -Results $results -ProjectPaths $paths -ScriptDir $root
    $report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Assert-Bulk ($report.Results.Count -eq 1 -and $report.NotProcessed.Count -eq 5) 'Batch report lost interrupted or unprocessed projects.'
    $noop=@(Get-VrcSetupBulkPlan -ProjectPaths @($paths[0]) -Packages ([pscustomobject]@{}) -Config $config -ScriptDir $scriptDir)
    $script:called=@()
    $results=@(Invoke-VrcSetupBulkPlan -Plan $noop)
    Assert-Bulk ($script:called.Count -eq 0 -and $results[0].Status -eq 'Unchanged') 'Unchanged project was sent to the installer.'
    # Selection survives a search; Enter applies only checked projects, including hidden ones.
    $catalog=[pscustomobject]@{Projects=@($paths | ForEach-Object { [pscustomobject]@{Path=$_;RelativePath=(Split-Path -Leaf $_);Kind='Avatar';UnityVersion='2022.3.22f1'} })}
    $state=[pscustomobject]@{Filter='';Index=0;Done=$false;Result=$null}
    Update-VrcSetupLibraryKey $state ([pscustomobject]@{Key='Spacebar'}) $catalog
    $state.Filter='Quest'; $state.Index=0
    Update-VrcSetupLibraryKey $state ([pscustomobject]@{Key='Spacebar'}) $catalog
    Update-VrcSetupLibraryKey $state ([pscustomobject]@{Key='Enter'}) $catalog
    Assert-Bulk ($state.Result.Action -eq 'bulk' -and $state.Result.ProjectPaths.Count -eq 2) 'Search lost a checked project or bulk Enter routing failed.'
    $state.Done=$false; $state.Result=$null
    Update-VrcSetupLibraryKey $state ([pscustomobject]@{Key='Spacebar'}) $catalog
    Assert-Bulk ($state.Selected.Count -eq 1) 'Space did not deselect a project.'
    # Exact IDs and catalog searches must both retain rich version selections.
    function Read-WizardTextInput { return 'com.example.tool' }
    function Test-VpmPackageExists { return $true }
    function Write-VrcSetupSpectreFrame { return $false }
    function Select-VrcSetupPackageResults { param($Items,$Title,[switch]$AllowVersionSelection); Assert-Bulk $AllowVersionSelection 'Version choice unavailable for exact ID.'; $Items[0] | Add-Member -NotePropertyName SelectedVersion -NotePropertyValue '2.3.4' -Force; return $Items[0] }
    $found=@(Find-VrcSetupPackages -WithVersions)
    Assert-Bulk ($found[0].SelectedVersion -eq '2.3.4') 'Exact ID path stripped the selected version.'
    Assert-Bulk ((@(Find-VrcSetupPackages))[0] -eq 'com.example.tool') 'Favorites ID contract regressed.'
    Save-VrcSetupPackagePreset -Config $config -Name 'Creator' -Packages ([pscustomobject]@{'com.example.tool'='2.3.4'})
    $script:editorEntered=$false; $script:applied=$null
    function Show-Menu {
        param($Title,$Options,$Header,$SectionBreaks)
        switch ($Title) {
            'Edit project packages' { if (-not $script:editorEntered) { $script:editorEntered=$true; return [array]::IndexOf($Options,'Apply a creator preset') }; return [array]::IndexOf($Options,'Review changes') }
            'Choose preset' { return 0 }
            'Apply preset' { return 1 }
            'Review project changes' { return 0 }
            'Project updates finished' { return 0 }
            default { throw "Unexpected screen: $Title" }
        }
    }
    function Invoke-VrcSetupSpectreOperation { return $null }
    function Invoke-VrcSetupBulkPlan { param($Plan); $script:applied=@($Plan); return @($Plan | ForEach-Object { [pscustomobject]@{Project=(Split-Path -Leaf $_.Path);Path=$_.Path;Status='Saved';Message='';LogFile=''} }) }
    Invoke-VrcSetupBulkPackageEditor -ProjectPaths @($paths[0],$paths[1]) -Config $config -ScriptDir $root
    Assert-Bulk ($script:applied.Count -eq 2 -and $script:applied[0].Desired.'com.example.tool' -eq '2.3.4' -and $script:applied[0].Diff.Removed -contains 'com.example.keep') 'Editor did not carry exact preset choice through review to execution.'
} finally {
    if ([IO.Path]::GetFullPath($root).StartsWith([IO.Path]::GetFullPath($env:TEMP),[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Host 'PASS: bulk staging, exact sync, targeted removal, busy/stale checks, failure isolation, cancellation and selection routing.'
