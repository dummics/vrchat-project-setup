param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\setup-scripts\commands\wizard.ps1')
function Assert-Unified { param($Condition,$Message); if (-not $Condition) { throw $Message } }
$root=Join-Path $env:TEMP ('vrcsetup-unified-qa-'+[guid]::NewGuid().ToString('N'))
try {
    $config=[pscustomobject]@{VpmPackages=[pscustomobject]@{'com.example.default'='latest'};PackagePresets=[pscustomobject]@{Creator=[pscustomobject]@{'com.example.saved'='2.3.4'}}}
    $snapshots=@()
    foreach ($version in @('1.0.0','2.0.0')) {
        $path=Join-Path $root $version
        foreach ($dir in @('Assets','Packages')) { [void][IO.Directory]::CreateDirectory((Join-Path $path $dir)) }
        $packages=[pscustomobject]@{'com.example.tool'=$version;'com.example.keep'='1.0.0'}
        [IO.File]::WriteAllText((Join-Path $path 'Packages\vpm-manifest.json'),(@{dependencies=@{'com.example.tool'=@{version=$version};'com.example.keep'=@{version='1.0.0'}};locked=@{}} | ConvertTo-Json -Depth 8))
        [IO.File]::WriteAllText((Join-Path $path 'Packages\manifest.json'),'{"dependencies":{}}')
        $snapshots += [pscustomobject]@{Path=$path;Packages=$packages}
    }
    $known=@(Get-VrcSetupKnownPackages -Config $config -PackageSets @($snapshots.Packages) -ScriptDir $root)
    Assert-Unified ($known.Id -contains 'com.example.saved' -and $known.Id -contains 'com.example.default') 'Preset definitions absent from local catalog.'
    $items=@(Get-VrcSetupBulkWorkspaceItems -Snapshots $snapshots -Packages ([pscustomobject]@{}) -Known $known -Config $config)
    Assert-Unified (($items | Where-Object Name -eq 'com.example.tool').Status -eq 'Mixed') 'Mixed project versions hidden.'
    Assert-Unified (($items | Where-Object Name -eq 'com.example.saved').Status -eq 'Available') 'Saved preset package staged without explicit selection.'
    $staged=[pscustomobject]@{'com.example.saved'='2.3.4'}
    $items=@(Get-VrcSetupBulkWorkspaceItems -Snapshots $snapshots -Packages $staged -Remove @('com.example.keep') -Known $known -Config $config)
    Assert-Unified (($items | Where-Object Name -eq 'com.example.saved').Status -eq 'Add') 'Add toggle state missing.'
    Assert-Unified (($items | Where-Object Name -eq 'com.example.keep').Status -eq 'Remove') 'Remove toggle state missing.'
    $scope=@{'com.example.keep'=@($snapshots[0].Path)}
    $plan=@(Get-VrcSetupBulkPlan -ProjectPaths $snapshots.Path -Packages $staged -Remove @('com.example.keep') -PackageTargets $scope -Config $config -ScriptDir $scriptDir)
    Assert-Unified ($plan[0].Diff.Removed -contains 'com.example.keep' -and $plan[1].Diff.Removed -notcontains 'com.example.keep') 'Scoped removal leaked into other projects.'
    Save-VrcSetupProjectPackageProfile -ProjectPath $snapshots[0].Path -PresetName 'Creator' -Policy $staged -Installed $snapshots[0].Packages -PresetDefinition $config.PackagePresets.Creator
    $profile=Get-VrcSetupProjectPackageProfile -ProjectPath $snapshots[0].Path -Current $snapshots[0].Packages -Config $config
    Assert-Unified ($profile.Preset -eq 'Creator' -and -not $profile.Modified) 'Profile recognition failed.'
    $profile=Get-VrcSetupProjectPackageProfile -ProjectPath $snapshots[0].Path -Current $snapshots[1].Packages -Config $config
    Assert-Unified $profile.Modified 'External manifest change was not detected.'
    $config.PackagePresets.Creator.'com.example.saved'='2.3.5'
    $profile=Get-VrcSetupProjectPackageProfile -ProjectPath $snapshots[0].Path -Current $snapshots[0].Packages -Config $config
    Assert-Unified $profile.PresetChanged 'Updated preset definition was not detected.'
    $config.PackagePresets.Creator.'com.example.saved'='2.3.4'
    Save-VrcSetupKnownPackages -Selections @([pscustomobject]@{Id='com.example.remembered';DisplayName='Remembered';LatestVersion='3.0.0';SelectedVersion='2.0.0'}) -ScriptDir $root
    Assert-Unified ((Get-VrcSetupKnownPackages -Config $config -ScriptDir $root | Where-Object Id -eq 'com.example.remembered').SelectedVersion -eq '2.0.0') 'Selected version was lost from local registry.'
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $started=Start-VrcSetupPackageCatalogRefresh -Items @([pscustomobject]@{Id='com.example.remembered';DisplayName='Remembered';LatestVersion='3.0.0';SelectedVersion='2.0.0'}) -ScriptDir $scriptDir -RefreshProvider { param($id); Start-Sleep -Milliseconds 1000; @('4.0.0','3.0.0') }
    Assert-Unified ($started -and $clock.Elapsed.TotalMilliseconds -lt 900) 'Background refresh blocked the caller.'
    # Redirect the test result cache to the disposable directory, retaining the real runtime for worker code.
    $script:VrcSetupCatalogRefresh.ScriptDir=$root
    Save-VrcSetupKnownPackages -Selections @([pscustomobject]@{Id='com.example.remembered';DisplayName='Remembered';LatestVersion='3.0.0';SelectedVersion='1.0.0'}) -ScriptDir $root
    while ($script:VrcSetupCatalogRefresh -and -not $script:VrcSetupCatalogRefresh.Handle.IsCompleted) { Start-Sleep -Milliseconds 100 }
    Complete-VrcSetupPackageCatalogRefresh -ScriptDir $root
    $refreshed=Get-VrcSetupKnownPackages -Config $config -ScriptDir $root | Where-Object Id -eq 'com.example.remembered'
    Assert-Unified ($refreshed.LatestVersion -eq '4.0.0' -and $refreshed.SelectedVersion -eq '1.0.0') 'Background metadata refresh overwrote a foreground version choice.'
    [void](Start-VrcSetupPackageCatalogRefresh -Items @($refreshed) -ScriptDir $scriptDir -RefreshProvider { param($id); Start-Sleep -Milliseconds 5000; '5.0.0' })
    $script:VrcSetupCatalogRefresh.ScriptDir=$root
    [void]$script:VrcSetupCatalogRefresh.Worker.BeginStop($null,$null)
    $stopWatch=[Diagnostics.Stopwatch]::StartNew()
    while (-not $script:VrcSetupCatalogRefresh.Handle.IsCompleted -and $stopWatch.Elapsed.TotalSeconds -lt 3) { Start-Sleep -Milliseconds 100 }
    Assert-Unified $script:VrcSetupCatalogRefresh.Handle.IsCompleted 'Cancellation did not stop the background worker.'
    Complete-VrcSetupPackageCatalogRefresh -ScriptDir $root
    Assert-Unified ((Get-VrcSetupKnownPackages -Config $config -ScriptDir $root | Where-Object Id -eq 'com.example.remembered').LatestVersion -eq '4.0.0') 'Stopping refresh damaged saved metadata.'
    function Load-Config { return $config }
    function Select-VrcSetupPackageResults { param($Items,$Title,$Header,[switch]$AllowVersionSelection); return @($Items | Where-Object Id -eq 'com.example.saved') }
    function Search-VrcGetPackages { throw 'Local browsing queried repositories.' }
    function Test-VpmPackageExists { throw 'Local browsing validated through network catalog.' }
    $found=@(Find-VrcSetupPackages -KnownPackages $known -WithVersions)
    Assert-Unified ($found.Count -eq 1 -and $found[0].SelectedVersion -eq '2.3.4') 'Local package selection did not preserve preset version.'
    Assert-Unified ((Get-VrcSetupFriendlyPackageName 'nadena.dev.ndmf') -eq 'NDMF (framework)') 'Framework name is misleading.'
    if ($PSVersionTable.PSVersion.Major -ge 7 -and (Initialize-VrcSetupSpectre -ScriptDir $scriptDir)) {
        [Spectre.Console.AnsiConsole]::Record()
        foreach ($width in @(50,80,120)) {
            [Spectre.Console.AnsiConsole]::Profile.Width=$width
            [Spectre.Console.AnsiConsole]::Write((New-VrcSetupPackageWorkspaceRenderable -Items $items -SelectedIndex 0 -PendingCount 2))
        }
        $html=[Spectre.Console.AnsiConsole]::ExportHtml()
        $artifact=Join-Path $PSScriptRoot '..\.work\artifacts\unified-packages-preview.html'
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $artifact))
        [IO.File]::WriteAllText($artifact,$html)
        Assert-Unified ($html -match 'Change preset' -and $html -match 'Projects') 'Workspace preview lacks primary controls or project presence.'
    }
    Write-Host 'PASS: saved definitions, mixed versions, toggle states, scoped removal, profile validation and local search without repository calls.'
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if ($resolved.StartsWith(([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
