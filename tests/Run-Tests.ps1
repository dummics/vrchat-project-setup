[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Directory]::GetParent($PSScriptRoot).FullName
$testRoot = Join-Path $env:TEMP ('vrcsetup-tests-' + [guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $testRoot 'Install [portable] & café'
$projectRoot = Join-Path $testRoot 'Project [avatar] & café'
$startMenuRoot = Join-Path $testRoot 'Start Menu [user] & café'
$previousInstallRoot = $env:VRCSETUP_INSTALL_ROOT
$previousStartMenuRoot = $env:VRCSETUP_START_MENU_ROOT
$previousSkipPath = $env:VRCSETUP_SKIP_PATH_UPDATE

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $projectRoot 'Assets')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $projectRoot 'Packages')) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $projectRoot 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.base":{"version":"3.8.0"},"gogoloco":{"version":"1.8.6"},"com.example.keep":"1.0.0"},"locked":{}}'
    )
    [System.IO.File]::WriteAllText(
        (Join-Path $projectRoot 'Packages\manifest.json'),
        '{"dependencies":{"com.vrchat.base":"3.8.0","gogoloco":"1.8.6","com.example.keep":"1.0.0"}}'
    )
    $env:VRCSETUP_INSTALL_ROOT = $installRoot
    $env:VRCSETUP_START_MENU_ROOT = $startMenuRoot
    $env:VRCSETUP_SKIP_PATH_UPDATE = '1'

    Write-Host '[1/11] Parsing PowerShell files...'
    foreach ($file in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.ps1') {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        Assert-True ($errors.Count -eq 0) "Parse failure in $($file.FullName): $($errors[0].Message)"
    }

    Write-Host '[2/11] Checking runtime for machine-specific owner paths...'
    $runtimeFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File | Where-Object {
        $_.Extension -in @('.ps1', '.bat', '.cmd') -and $_.FullName -notlike "$PSScriptRoot*"
    }
    foreach ($file in $runtimeFiles) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        Assert-True ($text -notmatch '(?i)C:\\Users\\domix|\.scriptsdum') "Machine-specific path found in $($file.FullName)"
    }
    $expectedTopLevelLaunchers = @(
        'Install VRChat Project Setup.bat',
        'Repair VRChat Project Setup.bat',
        'Uninstall VRChat Project Setup.bat',
        'VRChat Project Setup.bat'
    )
    $actualTopLevelLaunchers = @(Get-ChildItem -LiteralPath $repoRoot -File -Filter '*.bat' | Select-Object -ExpandProperty Name | Sort-Object)
    Assert-True (($actualTopLevelLaunchers -join '|') -ceq (($expectedTopLevelLaunchers | Sort-Object) -join '|')) 'The source root does not expose exactly the four friendly BAT launchers.'
    $uninstallLauncherText = Get-Content -LiteralPath (Join-Path $repoRoot 'Uninstall VRChat Project Setup.bat') -Raw
    $aliasLauncherText = Get-Content -LiteralPath (Join-Path $repoRoot 'setup-scripts\bin\vrcsetup.cmd') -Raw
    Assert-True ($uninstallLauncherText -match 'CONFIRM_UNINSTALL=1' -and $uninstallLauncherText -match '"--yes"') 'Uninstall no longer requires an explicit --yes flag for unattended removal.'
    Assert-True ($aliasLauncherText -match '(?s)"uninstall".*?shift.*?--no-pause %\*') 'The alias does not forward explicit uninstall flags safely.'

    Write-Host '[3/11] Testing removable presets and AIO package synchronization...'
    $scriptDir = Join-Path $repoRoot 'setup-scripts'
    . (Join-Path $scriptDir 'lib\config.ps1')
    . (Join-Path $scriptDir 'lib\vpm.ps1')
    . (Join-Path $scriptDir 'commands\installer.ps1')
    . (Join-Path $scriptDir 'commands\wizard.ps1')
    . (Join-Path $scriptDir 'commands\cli.ps1')

    Assert-True ($null -eq (Normalize-UserPath -Path '')) 'Blank path input no longer returns to the previous screen.'
    $fileInsteadOfProjectRoot = Join-Path $projectRoot 'Packages\manifest.json'
    $invalidRootStatus = Test-ConfigEssentialsExist -Config ([pscustomobject]@{
        UnityEditorPath = $null
        UnityProjectsRoot = $fileInsteadOfProjectRoot
    })
    Assert-True ($invalidRootStatus.Missing -contains "Projects root not found: ${fileInsteadOfProjectRoot}") 'A file path was accepted as the projects root.'

    $legacyConfig = [pscustomobject]@{
        VpmPackages = [pscustomobject]@{ 'com.vrchat.base' = 'latest' }
        DefaultPackages = @('com.vrchat.base', 'gogoloco', 'com.vrcfury.vrcfury')
    }
    $migratedConfig = Ensure-ConfigDefaults -Config $legacyConfig
    Assert-True ($migratedConfig.VpmPackages.PSObject.Properties.Name -notcontains 'gogoloco') 'Legacy config migration restored removed GoGoLoco.'
    Assert-True ($migratedConfig.VpmPackages.PSObject.Properties.Name -notcontains 'com.vrcfury.vrcfury') 'Legacy config migration restored another removed optional package.'
    Assert-True ($migratedConfig.ProjectLibrarySort -eq 'recent') 'Legacy config migration did not default the project library to recently updated.'

    $projectPackages = Get-VpmProjectPackageSet -ProjectPath $projectRoot
    Assert-True ($projectPackages.PSObject.Properties.Name -contains 'gogoloco') 'Test project did not expose GoGoLoco from vpm-manifest.json.'
    Assert-True ($projectPackages.'com.vrchat.base' -eq '3.8.0') 'Object-shaped VPM dependency version was not parsed.'
    $lockedProject = Join-Path $testRoot 'Locked dependency project'
    [System.IO.Directory]::CreateDirectory((Join-Path $lockedProject 'Packages')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $lockedProject 'Packages\manifest.json'), '{"dependencies":{}}')
    [System.IO.File]::WriteAllText(
        (Join-Path $lockedProject 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.avatars":{"version":"3.10.4"}},"locked":{"com.vrchat.base":{"version":"3.10.4"},"com.example.transitive":{"version":"1.0.0"}}}'
    )
    $lockedPackages = Get-VpmProjectPackageSet -ProjectPath $lockedProject -IncludeLockedPackages @('com.vrchat.base')
    Assert-True ($lockedPackages.'com.vrchat.base' -eq '3.10.4') 'A required package present in the VPM locked set was reported as missing.'
    Assert-True ($lockedPackages.PSObject.Properties.Name -notcontains 'com.example.transitive') 'Unrequested transitive packages leaked into the editable package set.'
    [System.IO.File]::WriteAllText(
        (Join-Path $lockedProject 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.avatars":{"version":"3.10.5-beta.1"}},"locked":{"com.vrchat.base":{"version":"3.10.5-beta.2"},"com.vrchat.avatars":{"version":"3.10.5-beta.2"}}}'
    )
    $resolvedLockedPackages = Get-VpmProjectPackageSet -ProjectPath $lockedProject -IncludeLockedPackages @('com.vrchat.base', 'com.vrchat.avatars')
    Assert-True ($resolvedLockedPackages.'com.vrchat.base' -eq '3.10.5-beta.2' -and $resolvedLockedPackages.'com.vrchat.avatars' -eq '3.10.5-beta.2') 'The editable package set preferred a stale direct dependency over the resolved locked SDK version.'
    $lockedValidation = Get-VpmManifestValidationResult -ProjectPath $lockedProject -Packages ([pscustomobject]@{ 'com.vrchat.base' = 'latest' })
    Assert-True ($lockedValidation.Valid) 'Manifest validation ignored an installed package recorded by VPM in the locked set.'
    $lockedMismatch = Get-VpmManifestValidationResult -ProjectPath $lockedProject -Packages ([pscustomobject]@{ 'com.vrchat.base' = '3.10.5-beta.1' })
    Assert-True (-not $lockedMismatch.Valid -and $lockedMismatch.VersionMismatches.Count -eq 1) 'Manifest validation accepted an SDK package that stayed on the wrong version.'
    Assert-True ($projectPackages.'com.example.keep' -eq '1.0.0') 'String-shaped VPM dependency version was not parsed.'
    $desiredPackages = Copy-VpmPackageSet -Packages $projectPackages
    $desiredPackages.PSObject.Properties.Remove('gogoloco')
    $desiredPackages = Add-RequiredPackagesToSet -Packages $desiredPackages -Config $null
    Assert-True (-not (Test-IsRequiredPackage -PackageName 'gogoloco' -Config $null)) 'GoGoLoco is still classified as required.'
    Assert-True ($desiredPackages.PSObject.Properties.Name -notcontains 'gogoloco') 'GoGoLoco was automatically restored after removal.'
    foreach ($requiredPackage in Get-RequiredPackages -Config $null) {
        Assert-True ($desiredPackages.PSObject.Properties.Name -contains $requiredPackage) "Required package was not restored: ${requiredPackage}"
    }
    $changePlan = Compare-VpmPackageSets -CurrentPackages $projectPackages -DesiredPackages $desiredPackages
    Assert-True ($changePlan.Removed -contains 'gogoloco') 'AIO plan did not schedule GoGoLoco for removal.'
    Assert-True ($changePlan.Removed -notcontains 'com.example.keep') 'AIO plan removed a package that stayed selected.'
    $emptyProjectPlan = Compare-VpmPackageSets -CurrentPackages ([pscustomobject]@{}) -DesiredPackages ([pscustomobject]@{ 'com.vrchat.base' = '3.10.4' })
    Assert-True ($emptyProjectPlan.Added.Count -eq 1 -and $emptyProjectPlan.Added[0] -eq 'com.vrchat.base') 'An empty project cannot stage its first package safely.'

    $toggleResult = Set-VrcSetupOptionalPackageSelection -Packages $projectPackages -SelectedPackageNames @('com.example.keep') -Config $null
    Assert-True ($toggleResult.PSObject.Properties.Name -notcontains 'gogoloco') 'Toggling an optional package off did not stage its removal.'
    Assert-True ($toggleResult.PSObject.Properties.Name -contains 'com.example.keep') 'Toggling an optional package on did not keep it selected.'
    Assert-True ($toggleResult.PSObject.Properties.Name -contains 'com.vrchat.base') 'Toggling optional packages removed a required package.'
    $bulkAdded = Add-VrcSetupPackagesAtLatest -Packages $toggleResult -PackageNames @('com.example.one', 'com.example.two')
    Assert-True ($bulkAdded.'com.example.one' -eq 'latest' -and $bulkAdded.'com.example.two' -eq 'latest') 'Bulk package add did not stage every package at latest.'
    $bulkUpdated = Set-VrcSetupPackagesToLatest -Packages $projectPackages -PackageNames @('gogoloco', 'com.example.keep')
    Assert-True ($bulkUpdated.gogoloco -eq 'latest' -and $bulkUpdated.'com.example.keep' -eq 'latest') 'Bulk package update did not stage every selected package.'
    $sdkPackages = [pscustomobject]@{
        'com.vrchat.base' = '3.10.5-beta.1'
        'com.vrchat.avatars' = '3.10.5-beta.1'
        'gogoloco' = '1.8.6'
    }
    $alignedSdkPackages = Set-VrcSetupPackageVersion -Packages $sdkPackages -PackageName 'com.vrchat.avatars' -Version '3.10.5-beta.2'
    Assert-True ($alignedSdkPackages.'com.vrchat.base' -eq '3.10.5-beta.2' -and $alignedSdkPackages.'com.vrchat.avatars' -eq '3.10.5-beta.2') 'Changing an Avatar SDK version did not keep Base aligned.'
    Assert-True ($alignedSdkPackages.gogoloco -eq '1.8.6' -and $alignedSdkPackages.PSObject.Properties.Name -notcontains 'com.vrchat.worlds') 'SDK alignment changed an unrelated package or added the other project type.'
    $worldSdkPackages = Set-VrcSetupPackageVersion -Packages $alignedSdkPackages -PackageName 'com.vrchat.worlds' -Version '3.10.4'
    Assert-True ($worldSdkPackages.'com.vrchat.base' -eq '3.10.4' -and $worldSdkPackages.'com.vrchat.avatars' -eq '3.10.4' -and $worldSdkPackages.'com.vrchat.worlds' -eq '3.10.4') 'Adding Worlds did not add it and align every SDK component already selected.'
    Assert-True ((Get-VrcSetupVrcGetPackageAction -CurrentVersion '' -TargetVersion '3.10.5-beta.2') -eq 'install') 'A new prerelease package is not routed to vrc-get install.'
    Assert-True ((Get-VrcSetupVrcGetPackageAction -CurrentVersion '3.10.5-beta.1' -TargetVersion '3.10.5-beta.2') -eq 'upgrade') 'A newer prerelease package is not routed to vrc-get upgrade.'
    Assert-True ((Get-VrcSetupVrcGetPackageAction -CurrentVersion '3.10.5-beta.2' -TargetVersion '3.10.5-beta.1') -eq 'downgrade') 'An older prerelease package is not routed to vrc-get downgrade.'
    Assert-True (Test-VrcSetupPackageCommandFailed -Result ([pscustomobject]@{ ExitCode = 0; Output = '[ERR] Could not get match for com.vrchat.base' })) 'A package command that printed a resolver error was accepted as successful.'
    $script:VrcGetVersionsCache['com.example.sdk-a'] = @('3.10.5-beta.2', '3.10.5-beta.1', '3.10.4')
    $script:VrcGetVersionsCache['com.example.sdk-b'] = @('3.10.5-beta.2', '3.10.4')
    $commonVersions = @(Get-VrcSetupCommonPackageVersions -PackageNames @('com.example.sdk-a', 'com.example.sdk-b') -ScriptDir $scriptDir)
    Assert-True (($commonVersions -join ',') -eq '3.10.5-beta.2,3.10.4') 'The SDK version picker did not keep only versions shared by linked packages.'
    $syncOutput = @(& { Start-Installer -projectPath $projectRoot -PackagesOverride $desiredPackages -SyncPackages -Test } *>&1)
    $syncStatus = [int]$syncOutput[-1]
    $syncText = ($syncOutput | ForEach-Object { [string]$_ }) -join "`n"
    Assert-True ($syncStatus -eq 0) "AIO test-mode synchronization returned ${syncStatus}."
    Assert-True ($syncText -match 'Would remove package: gogoloco') 'AIO synchronization did not issue the GoGoLoco removal.'
    Assert-True ($syncText -notmatch 'Would remove package: com\.example\.keep') 'AIO synchronization tried to remove a selected package.'
    Assert-True ($syncText -match 'Would add package: com\.vrchat\.base') 'AIO synchronization did not align the installed Base package with the linked SDK target.'
    Assert-True ($syncText -notmatch 'Would add package: com\.example\.keep') 'AIO synchronization reprocessed another unchanged package.'
    $script:VrcGetVersionsCache['com.vrchat.base'] = @('3.10.5-beta.2', '3.10.4', '3.10.3')
    $script:VrcGetVersionsCache['com.vrchat.avatars'] = @('3.10.5-beta.2', '3.10.4', '3.10.2')
    $resolvedLatestSdk = Resolve-VrcSetupSdkPackageSet -Packages ([pscustomobject]@{
        'com.vrchat.base' = 'latest'
        'com.vrchat.avatars' = 'latest'
    }) -ScriptDir $scriptDir
    Assert-True ($resolvedLatestSdk.'com.vrchat.base' -eq '3.10.4' -and $resolvedLatestSdk.'com.vrchat.avatars' -eq '3.10.4') 'Newest-compatible SDK selection did not resolve to one shared stable version.'

    $cliListOutput = @(Invoke-VrcSetupCli -Command 'packages' -Arguments @('list', $projectRoot) -ScriptDir $scriptDir -ConfigPath (Join-Path $scriptDir 'config\vrcsetup.json') -Json)
    $cliListStatus = [int]$cliListOutput[-1]
    $cliListJson = (($cliListOutput[0..($cliListOutput.Count - 2)] | ForEach-Object { [string]$_ }) -join "`n") | ConvertFrom-Json
    Assert-True ($cliListStatus -eq 0) 'CLI package list returned a failure status.'
    Assert-True (($cliListJson | Where-Object Package -eq 'gogoloco').Version -eq '1.8.6') 'CLI package list did not return the parsed GoGoLoco version.'

    $cliRemoveOutput = @(& { Invoke-VrcSetupCli -Command 'packages' -Arguments @('remove', $projectRoot, 'gogoloco') -ScriptDir $scriptDir -ConfigPath (Join-Path $scriptDir 'config\vrcsetup.json') -DryRun } *>&1)
    $cliRemoveStatus = [int]$cliRemoveOutput[-1]
    $cliRemoveText = ($cliRemoveOutput | ForEach-Object { [string]$_ }) -join "`n"
    Assert-True ($cliRemoveStatus -eq 0 -and $cliRemoveText -match 'Remove:\s+gogoloco') 'CLI dry-run removal did not produce the expected AIO plan.'

    $requiredRemoveOutput = @(& { Invoke-VrcSetupCli -Command 'packages' -Arguments @('remove', $projectRoot, 'com.vrchat.base') -ScriptDir $scriptDir -ConfigPath (Join-Path $scriptDir 'config\vrcsetup.json') -DryRun } *>&1)
    Assert-True ([int]$requiredRemoveOutput[-1] -eq 1) 'CLI allowed removal of a required package.'

    $packageOrderConfig = [pscustomobject]@{
        VpmPackages = [pscustomobject]@{
            'gogoloco' = 'latest'
            'com.vrchat.core.vpm-resolver' = 'latest'
            'com.vrcfury.vrcfury' = 'latest'
            'com.vrchat.base' = 'latest'
            'com.vrchat.avatars' = 'latest'
        }
    }
    $orderedDefaultPackages = @(Get-OrderedVpmPackageProperties -Packages $packageOrderConfig.VpmPackages -Config $packageOrderConfig | ForEach-Object Name)
    Assert-True (($orderedDefaultPackages[0..2] -join ',') -eq 'com.vrchat.base,com.vrchat.avatars,com.vrchat.core.vpm-resolver') 'Required VPM packages were not kept first in their canonical order.'
    Assert-True ((($orderedDefaultPackages | Select-Object -Skip 3) -join ',') -eq (($orderedDefaultPackages | Select-Object -Skip 3 | Sort-Object) -join ',')) 'Optional VPM packages were not sorted after required packages.'
    $packageRow = Format-VrcSetupPackageRow -Role 'Required' -PackageName 'com.vrchat.avatars' -Version '3.10.1'
    Assert-True ($packageRow -match '^Required\s+VRChat SDK Avatars\s+com\.vrchat\.avatars\s+3\.10\.1$') 'Package rows are not aligned into role, friendly name, package ID, and version columns.'
    $searchPackageRow = Format-VrcSetupPackageRow -DisplayName 'Avatar Tools' -PackageName 'com.example.avatar-tools' -Version '1.2.3'
    Assert-True ($searchPackageRow -match '^Avatar Tools\s+com\.example\.avatar-tools\s+1\.2\.3$') 'Package checklist rows retained an empty role column.'
    $workspaceDesired = Copy-VpmPackageSet -Packages $projectPackages
    $workspaceDesired.PSObject.Properties.Remove('gogoloco')
    $workspaceDesired.'com.example.keep' = 'latest'
    $workspaceDesired = Add-VrcSetupPackagesAtLatest -Packages $workspaceDesired -PackageNames @('com.example.new')
    $workspaceItems = @(Get-VrcSetupPackageWorkspaceItems -CurrentPackages $projectPackages -DesiredPackages $workspaceDesired -Config $null)
    $workspaceNames = @($workspaceItems | ForEach-Object Name)
    Assert-True ($workspaceNames[0] -eq 'com.vrchat.base') 'The project package table no longer begins with the VRChat SDK foundation.'
    Assert-True (($workspaceItems | Where-Object Name -eq 'gogoloco').Status -eq 'Remove') 'The package workspace did not keep a staged removal visible.'
    Assert-True (($workspaceItems | Where-Object Name -eq 'com.example.keep').Status -eq 'Update') 'The package workspace did not expose a staged version change.'
    Assert-True (($workspaceItems | Where-Object Name -eq 'com.example.new').Status -eq 'Add') 'The package workspace did not expose a staged addition.'
    $workspaceRow = Format-VrcSetupPackageWorkspaceRow -Item ($workspaceItems | Where-Object Name -eq 'com.example.keep')
    Assert-True ($workspaceRow -match '^Keep\s+1\.0\.0 -> Newest\s+Will change$') 'The package workspace row is not aligned into one readable version and outcome.'
    $olderVersion = Get-VrcSetupAdjacentPackageVersion -AvailableVersions @('3.10.4', '3.10.3', '3.10.2') -CurrentVersion '3.10.3' -Direction Older
    $newerVersion = Get-VrcSetupAdjacentPackageVersion -AvailableVersions @('3.10.4', '3.10.3', '3.10.2') -CurrentVersion '3.10.3' -Direction Newer
    $olderFromLatest = Get-VrcSetupAdjacentPackageVersion -AvailableVersions @('3.10.4', '3.10.3', '3.10.2') -CurrentVersion 'latest' -Direction Older
    $newerFromLatest = Get-VrcSetupAdjacentPackageVersion -AvailableVersions @('3.10.4', '3.10.3', '3.10.2') -CurrentVersion 'latest' -Direction Newer
    $newerFromNewestFixed = Get-VrcSetupAdjacentPackageVersion -AvailableVersions @('3.10.4', '3.10.3', '3.10.2') -CurrentVersion '3.10.4' -Direction Newer
    Assert-True ($olderVersion.Version -eq '3.10.2' -and $newerVersion.Version -eq '3.10.4') 'Inline version arrows do not move predictably between cached versions.'
    Assert-True ($olderFromLatest.Version -eq '3.10.4') 'Moving older from newest did not select the newest concrete version.'
    Assert-True ($newerFromLatest.Version -eq 'latest' -and $newerFromLatest.AtLimit) 'Moving newer from newest changed the version policy unexpectedly.'
    Assert-True ($newerFromNewestFixed.Version -eq 'latest') 'Moving newer from the newest fixed version did not restore the newest-compatible policy.'
    $sdkWorkspaceItems = @(Get-VrcSetupPackageWorkspaceItems -CurrentPackages $sdkPackages -DesiredPackages $sdkPackages -Config $null)
    $linkedWorkspaceChanges = @(Set-VrcSetupWorkspaceItemVersion -Items $sdkWorkspaceItems -PackageName 'com.vrchat.base' -Version '3.10.5-beta.2')
    Assert-True ($linkedWorkspaceChanges.Count -eq 2 -and @($sdkWorkspaceItems | Where-Object DesiredVersion -eq '3.10.5-beta.2').Count -eq 2) 'Inline SDK version changes did not update both visible rows together.'
    Assert-True ((Get-VrcSetupWorkspacePendingCount -Items $sdkWorkspaceItems) -eq 1) 'One linked SDK update is still counted as multiple user changes.'
    $mergedDefaults = Merge-VrcSetupPackageSets -BasePackages $projectPackages -PackagesToAdd ([pscustomobject]@{ 'gogoloco' = 'latest'; 'com.example.default' = '2.0.0' })
    Assert-True ($mergedDefaults.'com.example.keep' -eq '1.0.0') 'Using the default package set removed an existing non-default package.'
    Assert-True ($mergedDefaults.gogoloco -eq 'latest' -and $mergedDefaults.'com.example.default' -eq '2.0.0') 'Using the default package set did not stage its package versions.'
    $wizardText = Get-Content -LiteralPath (Join-Path $scriptDir 'commands\wizard.ps1') -Raw
    Assert-True ($wizardText -notmatch "'Toggle installed packages'|'Update selected packages'|'Review and apply'") 'The old procedural package-manager selectors are still present.'
    Assert-True ($wizardText -match 'Show-VrcSetupSpectrePackageWorkspace') 'The project package manager is not using the direct interactive workspace.'
    Assert-True ($wizardText -match "'Save changes'" -and $wizardText -notmatch "'Apply changes'") 'The package workspace does not expose one UI-like Save changes action.'
    $menuText = Get-Content -LiteralPath (Join-Path $scriptDir 'lib\menu.ps1') -Raw
    $spectreText = Get-Content -LiteralPath (Join-Path $scriptDir 'lib\spectre.ps1') -Raw
    $progressText = Get-Content -LiteralPath (Join-Path $scriptDir 'lib\progress.ps1') -Raw
    $installerText = Get-Content -LiteralPath (Join-Path $scriptDir 'commands\installer.ps1') -Raw
    $cliText = Get-Content -LiteralPath (Join-Path $scriptDir 'commands\cli.ps1') -Raw
    Assert-True ($menuText -match '\[int\[\]\]\$SectionBreaks' -and $spectreText -match '\[int\[\]\]\$SectionBreaks') 'Menu section spacing is not available in both renderers.'
    Assert-True ($menuText -notmatch "\[string\]\$PromptTitle = 'Choose an action'" -and $spectreText -notmatch "\[string\]\$PromptTitle = 'Choose an action'") 'Menus still default to technical action instructions.'
    Assert-True ($spectreText -match 'function New-VrcSetupPackageWorkspaceRenderable' -and $spectreText -match 'Left/Right Change version' -and $spectreText -match 'Space Include/remove') 'The direct package-table controls are missing.'
    Assert-True ($spectreText -match "Label = 'Version'" -and $spectreText -match "Label = 'After saving'" -and $spectreText -notmatch "Label = 'Installed'|Label = 'State'") 'The package workspace returned to technical duplicate columns.'
    Assert-True ($spectreText -match 'function Show-VrcSetupSpectreSaveReview' -and $spectreText -match 'function Invoke-VrcSetupSpectreOperation') 'The polished save review or embedded progress surface is missing.'
    Assert-True ($wizardText -match 'Invoke-VrcSetupInstallerWithProgress' -and $wizardText -match 'Show-VrcSetupSpectreSaveReview') 'The interactive wizard bypasses the embedded review/progress flow.'
    Assert-True ($wizardText -match 'VRCSETUP_EMBEDDED_PROGRESS' -and $progressText -match 'VRCSETUP_EMBEDDED_PROGRESS') 'Embedded progress does not isolate the viewport keyboard from process cancellation input.'
    Assert-True ($spectreText -match '\$versionWidth = if \(\$isNarrow\).*36' -and $spectreText -match 'Viewing messages') 'The package/version table or progress message counter regressed to the cramped or ambiguous layout.'
    Assert-True ($installerText -match "'--prerelease'" -and $installerText -match 'Invoke-VrcGetCapture') 'Prerelease packages are still routed only through the VPM CLI that cannot resolve them.'
    Assert-True ($installerText -match 'IncludeLockedPackages \$configuredPackageNames' -and $installerText -match 'Restore-VrcSetupPackageBatch') 'Locked SDK packages or failed package operations still bypass the safe installer contract.'
    Assert-True ($cliText -match 'Set-VrcSetupPackageVersion' -and $cliText -match 'Test-VrcSetupCliSdkVersionSelection') 'CLI package/create flows bypass SDK version alignment or compatibility validation.'
    if ($PSVersionTable.PSVersion.Major -ge 7 -and (Initialize-VrcSetupSpectre -ScriptDir $scriptDir)) {
        $workspaceRenderable = New-VrcSetupPackageWorkspaceRenderable -Items $workspaceItems -SelectedIndex 0 -PendingCount 3
        Assert-True ($workspaceRenderable -is [Spectre.Console.Rows]) 'The package workspace did not build a Spectre renderable.'
        $reviewRenderable = New-VrcSetupSaveReviewRenderable -Added @('Easy Login') -Updated @('VRCFury') -Removed @() -SelectedIndex 0
        Assert-True ($reviewRenderable -is [Spectre.Console.Rows]) 'The save review did not build a Spectre renderable.'
        $progressRenderable = New-VrcSetupProgressRenderable -Lines @('Preparing project', 'Adding Easy Login') -ScrollIndex 0 -Status Running -Follow:$true -Elapsed ([TimeSpan]::FromSeconds(2))
        Assert-True ($progressRenderable -is [Spectre.Console.Rows]) 'The embedded progress viewport did not build a Spectre renderable.'
    }

    Write-Host '[4/11] Scanning and incrementally refreshing the project library...'
    $libraryRoot = Join-Path $testRoot 'Unity Projects [shared] & café'
    $avatarProject = Join-Path $libraryRoot 'Z Avatar [daily] & café'
    $worldProject = Join-Path $libraryRoot 'Grouped\World Portal'
    foreach ($path in @($avatarProject, $worldProject)) {
        [System.IO.Directory]::CreateDirectory((Join-Path $path 'Assets')) | Out-Null
        [System.IO.Directory]::CreateDirectory((Join-Path $path 'Packages')) | Out-Null
        [System.IO.Directory]::CreateDirectory((Join-Path $path 'ProjectSettings')) | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $path 'ProjectSettings\ProjectVersion.txt'), 'm_EditorVersion: 2022.3.22f1')
    }
    [System.IO.Directory]::CreateDirectory((Join-Path $libraryRoot 'Notes only')) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $avatarProject 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.base":"3.8.0","com.vrchat.avatars":"3.8.0"},"locked":{}}'
    )
    [System.IO.File]::WriteAllText(
        (Join-Path $worldProject 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.base":"3.8.0","com.vrchat.worlds":"3.8.0"},"locked":{}}'
    )
    $recoveredProject = Join-Path $libraryRoot 'ZZ Recovered Avatar'
    [System.IO.Directory]::CreateDirectory((Join-Path $recoveredProject 'Assets')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $recoveredProject 'Packages\com.vrchat.avatars')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $recoveredProject 'Packages\com.vrchat.base')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $recoveredProject 'Packages\com.vrchat.core.vpm-resolver')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $recoveredProject 'ProjectSettings')) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $recoveredProject 'Assembly-CSharp.csproj'),
        '<Project><PropertyGroup><UnityVersion>2022.3.22f1</UnityVersion></PropertyGroup></Project>'
    )
    [System.IO.Directory]::SetLastWriteTimeUtc($recoveredProject, [DateTime]'2000-01-01T00:00:00Z')
    [System.IO.File]::SetLastWriteTimeUtc((Join-Path $avatarProject 'Packages\vpm-manifest.json'), [DateTime]'2099-01-02T00:00:00Z')
    [System.IO.File]::SetLastWriteTimeUtc((Join-Path $worldProject 'Packages\vpm-manifest.json'), [DateTime]'2099-01-01T00:00:00Z')
    $catalogCache = Join-Path $testRoot 'cache\projects.json'
    $firstCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache -ForceRefresh
    Assert-True ($firstCatalog.ProjectCount -eq 3) 'Initial scan did not find exactly the three Unity projects.'
    Assert-True ($firstCatalog.Refreshed -eq 3 -and $firstCatalog.CacheHits -eq 0) 'Initial scan incorrectly reused cached metadata.'
    Assert-True (($firstCatalog.Projects | Where-Object Path -eq $avatarProject).Kind -eq 'Avatar') 'Avatar project type was not detected from VPM dependencies.'
    Assert-True (($firstCatalog.Projects | Where-Object Path -eq $worldProject).Kind -eq 'World') 'Nested world project was not detected.'
    $recoveredMetadata = $firstCatalog.Projects | Where-Object Path -eq $recoveredProject
    Assert-True ($recoveredMetadata.Kind -eq 'Avatar' -and $recoveredMetadata.PackageCount -eq 3) 'Embedded package folders were not used when the VPM manifest was missing.'
    Assert-True ($recoveredMetadata.UnityVersion -eq '2022.3.22f1' -and $recoveredMetadata.UnityVersionSource -eq 'GeneratedProject') 'Generated project metadata was not used as the Unity version fallback.'
    $recoveredRow = Format-VrcSetupProjectCatalogRow -Project $recoveredMetadata -Index 1
    Assert-True ($recoveredRow -match '2022\.3\.22f1\*' -and $recoveredRow -match '3 packages\*') 'Recovered project metadata is not marked in the project table.'
    $libraryRow = Format-VrcSetupProjectCatalogRow -Project $firstCatalog.Projects[0] -Index 1
    Assert-True ($libraryRow -match 'Z Avatar' -and $libraryRow -match 'Avatar' -and $libraryRow -match '2 packages') 'Project library rows did not include the project, type, and package count in one selectable table row.'
    Assert-True (Test-Path -LiteralPath $catalogCache) 'Project library cache was not written.'
    Assert-True ($firstCatalog.Projects[0].Path -eq $avatarProject) 'Project library did not list the most recently updated project first.'
    $alphabeticalCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache -SortOrder name
    Assert-True ($alphabeticalCatalog.Projects[0].Path -eq $worldProject) 'Project library name ordering did not list projects alphabetically.'
    $cliCatalogConfigPath = Join-Path $testRoot 'config\projects.json'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $cliCatalogConfigPath)) | Out-Null
    [pscustomobject]@{
        UnityProjectsRoot = $libraryRoot
        VpmPackages = [pscustomobject]@{ 'com.vrchat.base' = 'latest' }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $cliCatalogConfigPath -Encoding UTF8
    $cliProjectsOutput = @(& { Invoke-VrcSetupCli -Command 'projects' -ScriptDir $scriptDir -ConfigPath $cliCatalogConfigPath -Json -SortOrder name } *>&1)
    $cliProjectsStatus = [int]$cliProjectsOutput[-1]
    $cliProjects = (($cliProjectsOutput[0..($cliProjectsOutput.Count - 2)] | ForEach-Object { [string]$_ }) -join "`n") | ConvertFrom-Json
    Assert-True ($cliProjectsStatus -eq 0 -and $cliProjects[0].Name -eq 'World Portal') 'CLI project sorting did not honor -Sort name.'

    $secondCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache
    Assert-True ($secondCatalog.CacheHits -eq 3 -and $secondCatalog.Refreshed -eq 0) 'Unchanged projects were not reused from the incremental cache.'
    $cachedLibraryRow = Format-VrcSetupProjectCatalogRow -Project $secondCatalog.Projects[0] -Index 1
    Assert-True ($cachedLibraryRow -match '02 Jan 2099' -and $cachedLibraryRow -notmatch 'Unknown') 'Project library rows did not retain the last-updated time after reading the scan cache.'
    [System.IO.Directory]::SetLastWriteTimeUtc($worldProject, [DateTime]'2099-01-03T00:00:00Z')
    $folderUpdatedCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache
    Assert-True ($folderUpdatedCatalog.CacheHits -eq 2 -and $folderUpdatedCatalog.Refreshed -eq 1) 'A changed project folder timestamp did not refresh the cached project metadata.'
    Assert-True ($folderUpdatedCatalog.Projects[0].Path -eq $worldProject) 'Project library did not use the project folder timestamp for recent ordering.'
    [System.IO.File]::WriteAllText(
        (Join-Path $avatarProject 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.base":"3.8.0","com.vrchat.avatars":"3.8.0","gogoloco":"1.8.6"},"locked":{}}'
    )
    $thirdCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache
    $updatedAvatar = $thirdCatalog.Projects | Where-Object Path -eq $avatarProject
    Assert-True ($thirdCatalog.CacheHits -eq 2 -and $thirdCatalog.Refreshed -eq 1) 'A changed project did not refresh independently.'
    Assert-True ($updatedAvatar.PackageCount -eq 3 -and $updatedAvatar.PackageNames -contains 'gogoloco') 'Refreshed metadata did not include the changed VPM package set.'

    Write-Host '[5/11] Running the portable click launcher before installation...'
    $portableLauncher = Join-Path $repoRoot 'VRChat Project Setup.bat'
    & $portableLauncher -projectPath $projectRoot -Test *> $null
    $portableExit = $LASTEXITCODE
    Assert-True ($portableExit -eq 0) "Portable click launcher exited with ${portableExit}."

    Write-Host '[6/11] Installing through the clickable BAT to a special-character path...'
    [System.IO.Directory]::CreateDirectory($installRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $installRoot 'bin')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $installRoot 'bin\vrcsetup.cmd'), '@echo obsolete')
    [System.IO.File]::WriteAllText((Join-Path $installRoot 'Install-VrcSetup.ps1'), '# obsolete installed copy')
    Push-Location -LiteralPath $env:TEMP
    try {
        & (Join-Path $repoRoot 'Install VRChat Project Setup.bat') --no-pause --no-launch | Out-Host
        $installExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    Assert-True ($installExit -eq 0) "Installer exited with ${installExit}."
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'setup-scripts\bin\vrcsetup.cmd')) 'Alias was not installed.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot '.vrcsetup-installed')) 'Installation marker was not created.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'VRChat Project Setup.bat')) 'Smart click launcher was not installed.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'setup-scripts\lib\projects.ps1')) 'The project scanner was not installed.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'setup-scripts\lib\spectre\Spectre.Console.dll')) 'The optional Spectre.Console runtime was not installed.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot 'setup-scripts\cache'))) 'Generated project cache was copied into the installation.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot 'setup-scripts\maintenance\Install-VrcSetup.ps1'))) 'The source-only installer was copied into the installed runtime.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot 'bin'))) 'The obsolete root-level alias folder was not removed.'

    $startMenuFolder = Join-Path $startMenuRoot 'VRChat Project Setup'
    $shortcutNames = @(
        'VRChat Project Setup.lnk',
        'Repair VRChat Project Setup.lnk',
        'Uninstall VRChat Project Setup.lnk'
    )
    $shortcutShell = New-Object -ComObject WScript.Shell
    foreach ($shortcutName in $shortcutNames) {
        $shortcutPath = Join-Path $startMenuFolder $shortcutName
        Assert-True (Test-Path -LiteralPath $shortcutPath) "Start Menu shortcut was not created: ${shortcutName}"
        $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
        Assert-True ($shortcut.TargetPath -ieq (Join-Path $env:SystemRoot 'System32\cmd.exe')) "Unexpected shortcut target for ${shortcutName}."
        Assert-True ($shortcut.Arguments.Contains($installRoot)) "Shortcut does not point at the installed copy: ${shortcutName}"
        Assert-True ($shortcut.WorkingDirectory -ieq $installRoot) "Shortcut working directory is incorrect: ${shortcutName}"
    }
    $sourceConfig = Join-Path $repoRoot 'setup-scripts\config\vrcsetup.json'
    $installedConfig = Join-Path $installRoot 'setup-scripts\config\vrcsetup.json'
    if (Test-Path -LiteralPath $sourceConfig) {
        Assert-True (Test-Path -LiteralPath $installedConfig) 'Installer did not migrate the existing local config.'
        Assert-True ((Get-FileHash -LiteralPath $sourceConfig).Hash -eq (Get-FileHash -LiteralPath $installedConfig).Hash) 'Migrated config differs from the source.'
    } else {
        Assert-True (-not (Test-Path -LiteralPath $installedConfig)) 'Installer created a machine-local config when none existed in the source.'
    }

    Write-Host '[7/11] Verifying the downloaded smart launcher prefers the installed copy...'
    $installedLauncher = Join-Path $installRoot 'setup-scripts\setup.bat'
    $installedLauncherContent = [System.IO.File]::ReadAllText($installedLauncher)
    $routeProbe = Join-Path $testRoot 'installed-route.txt'
    try {
        [System.IO.File]::WriteAllLines($installedLauncher, @(
            '@echo off',
            "> `"$routeProbe`" echo installed"
        ), [System.Text.Encoding]::ASCII)
        & $portableLauncher *> $null
        $smartExit = $LASTEXITCODE
        Assert-True ($smartExit -eq 0) "Smart launcher exited with ${smartExit}."
        Assert-True (Test-Path -LiteralPath $routeProbe) 'Downloaded launcher did not route to the installed copy.'

        Remove-Item -LiteralPath $routeProbe -Force
        $hiddenRunner = Join-Path $PSScriptRoot 'Run-Hidden.vbs'
        $hiddenArguments = '"{0}" "{1}"' -f $hiddenRunner, $installedLauncher
        $shortcutProcess = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\wscript.exe') -ArgumentList $hiddenArguments -PassThru -Wait -WindowStyle Hidden
        Assert-True ($shortcutProcess.ExitCode -eq 0) "Hidden installed launcher exited with $($shortcutProcess.ExitCode)."
        Assert-True (Test-Path -LiteralPath $routeProbe) 'Hidden launcher test did not reach the installed copy.'
    } finally {
        [System.IO.File]::WriteAllText($installedLauncher, $installedLauncherContent)
    }

    Write-Host '[8/11] Running installed CLI alias against a special-character project path...'
    $aliasPath = Join-Path $installRoot 'setup-scripts\bin\vrcsetup.cmd'
    Push-Location -LiteralPath $env:TEMP
    try {
        & $aliasPath -projectPath $projectRoot -Test *> $null
        $aliasExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    Assert-True ($aliasExit -eq 0) "Installed alias exited with ${aliasExit}."
    [pscustomobject]@{
        UnityProjectsRoot = $libraryRoot
        VpmPackages = [pscustomobject]@{ 'com.vrchat.base' = 'latest' }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $installedConfig -Encoding UTF8
    $installedSortOutput = @(& $aliasPath projects -Sort name -Json)
    $installedSortExit = $LASTEXITCODE
    $installedSortedProjects = (($installedSortOutput | ForEach-Object { [string]$_ }) -join "`n") | ConvertFrom-Json
    Assert-True ($installedSortExit -eq 0 -and $installedSortedProjects[0].Name -eq 'World Portal') 'Installed CLI alias did not forward projects -Sort name.'
    $installedDefaultSortOutput = @(& $aliasPath projects -Json)
    $installedDefaultSortExit = $LASTEXITCODE
    $installedDefaultSortedProjects = (($installedDefaultSortOutput | ForEach-Object { [string]$_ }) -join "`n") | ConvertFrom-Json
    Assert-True ($installedDefaultSortExit -eq 0 -and $installedDefaultSortedProjects[0].Name -eq 'World Portal') 'Installed CLI alias did not use the default recent ordering when -Sort was omitted.'

    Write-Host '[9/11] Verifying the runtime launcher preserves caller working directory and arguments...'
    $cwdOutput = Join-Path $testRoot 'caller-cwd.txt'
    $batchPath = Join-Path $testRoot 'check-launcher.cmd'
    $launcherPath = Join-Path $installRoot 'VRChat Project Setup.bat'
    $batchLines = @(
        '@echo off',
        'chcp 65001 >nul',
        "cd /d `"$env:TEMP`"",
        "call `"$launcherPath`" -projectPath `"$projectRoot`" -Test >nul 2>&1",
        'if errorlevel 1 exit /b %errorlevel%',
        "cd > `"$cwdOutput`""
    )
    [System.IO.File]::WriteAllLines($batchPath, $batchLines, [System.Text.UTF8Encoding]::new($false))
    & cmd.exe /d /c $batchPath
    Assert-True ($LASTEXITCODE -eq 0) "Top-level launcher exited with ${LASTEXITCODE}."
    $reportedCwd = (Get-Content -LiteralPath $cwdOutput -Raw).Trim().TrimEnd('\')
    Assert-True ($reportedCwd -ieq $env:TEMP.TrimEnd('\')) "Launcher changed caller directory to ${reportedCwd}."

    Write-Host '[10/11] Repairing the installed copy from the downloaded BAT...'
    Remove-Item -LiteralPath (Join-Path $startMenuFolder 'Repair VRChat Project Setup.lnk') -Force
    $lockedSpectreDll = Join-Path $installRoot 'setup-scripts\lib\spectre\Spectre.Console.Ansi.dll'
    $lockJob = Start-Job -ScriptBlock {
        param($dllPath)
        Add-Type -LiteralPath $dllPath -ErrorAction Stop
        'ready'
        Start-Sleep -Seconds 20
    } -ArgumentList $lockedSpectreDll
    try {
        $readyDeadline = [DateTime]::UtcNow.AddSeconds(5)
        $lockerReady = $false
        while ([DateTime]::UtcNow -lt $readyDeadline) {
            if (@(Receive-Job -Job $lockJob -Keep -ErrorAction SilentlyContinue) -contains 'ready') {
                $lockerReady = $true
                break
            }
            Start-Sleep -Milliseconds 100
        }
        Assert-True $lockerReady 'Test process did not lock the installed Spectre runtime.'
        & (Join-Path $repoRoot 'Repair VRChat Project Setup.bat') --no-pause *> $null
        $repairExit = $LASTEXITCODE
    } finally {
        Remove-Job -Job $lockJob -Force -ErrorAction SilentlyContinue
    }
    Assert-True ($repairExit -eq 0) "Source repair launcher exited with ${repairExit}."
    Assert-True (Test-Path -LiteralPath (Join-Path $startMenuFolder 'Repair VRChat Project Setup.lnk')) 'Repair did not restore the missing Start Menu shortcut.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.vrcsetup-installed'))) 'Repair incorrectly marked the downloaded folder as installed.'

    Write-Host '[11/11] Uninstalling through the terminal alias without touching its source folder...'
    & $aliasPath uninstall --yes *> $null
    $uninstallExit = $LASTEXITCODE
    Assert-True ($uninstallExit -eq 0) "Source uninstall launcher exited with ${uninstallExit}."
    $uninstallDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path -LiteralPath $installRoot) -and [DateTime]::UtcNow -lt $uninstallDeadline) {
        Start-Sleep -Milliseconds 100
    }
    Assert-True (-not (Test-Path -LiteralPath $installRoot)) 'Uninstall left the install folder behind.'
    Assert-True (-not (Test-Path -LiteralPath $startMenuFolder)) 'Uninstall left Start Menu shortcuts behind.'
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot 'Install VRChat Project Setup.bat')) 'Uninstall damaged the downloaded source folder.'

    & (Join-Path $repoRoot 'Uninstall VRChat Project Setup.bat') --no-pause *> $null
    Assert-True ($LASTEXITCODE -eq 2) 'Uninstall without an installed copy did not return the safe no-op result.'
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot 'setup-scripts')) 'Safe no-op uninstall damaged the downloaded source folder.'

    $directUninstallRefused = $false
    try {
        & (Join-Path $repoRoot 'setup-scripts\maintenance\Uninstall-VrcSetup.ps1') -InstallRoot $repoRoot *> $null
    } catch {
        $directUninstallRefused = $true
    }
    Assert-True $directUninstallRefused 'Direct PowerShell uninstall accepted the downloaded source folder.'
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot 'README.md')) 'Direct uninstall safety check damaged the source folder.'

    Write-Host 'PASS: all VRChat Project Setup tests completed.' -ForegroundColor Green
    exit 0
} catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    $env:VRCSETUP_INSTALL_ROOT = $previousInstallRoot
    $env:VRCSETUP_START_MENU_ROOT = $previousStartMenuRoot
    $env:VRCSETUP_SKIP_PATH_UPDATE = $previousSkipPath
    if (Test-Path -LiteralPath $testRoot) {
        try { [System.IO.Directory]::Delete($testRoot, $true) } catch { }
    }
}
