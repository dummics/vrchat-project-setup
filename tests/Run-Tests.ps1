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

    $syncOutput = @(& { Start-Installer -projectPath $projectRoot -PackagesOverride $desiredPackages -SyncPackages -Test } *>&1)
    $syncStatus = [int]$syncOutput[-1]
    $syncText = ($syncOutput | ForEach-Object { [string]$_ }) -join "`n"
    Assert-True ($syncStatus -eq 0) "AIO test-mode synchronization returned ${syncStatus}."
    Assert-True ($syncText -match 'Would remove package: gogoloco') 'AIO synchronization did not issue the GoGoLoco removal.'
    Assert-True ($syncText -notmatch 'Would remove package: com\.example\.keep') 'AIO synchronization tried to remove a selected package.'
    Assert-True ($syncText -notmatch 'Would add package: com\.vrchat\.base') 'AIO synchronization reprocessed an unchanged package.'
    Assert-True ($syncText -notmatch 'Would add package: com\.example\.keep') 'AIO synchronization reprocessed another unchanged package.'

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
    [System.IO.File]::SetLastWriteTimeUtc((Join-Path $avatarProject 'Packages\vpm-manifest.json'), [DateTime]'2099-01-02T00:00:00Z')
    [System.IO.File]::SetLastWriteTimeUtc((Join-Path $worldProject 'Packages\vpm-manifest.json'), [DateTime]'2099-01-01T00:00:00Z')
    $catalogCache = Join-Path $testRoot 'cache\projects.json'
    $firstCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache -ForceRefresh
    Assert-True ($firstCatalog.ProjectCount -eq 2) 'Initial scan did not find exactly the two Unity projects.'
    Assert-True ($firstCatalog.Refreshed -eq 2 -and $firstCatalog.CacheHits -eq 0) 'Initial scan incorrectly reused cached metadata.'
    Assert-True (($firstCatalog.Projects | Where-Object Path -eq $avatarProject).Kind -eq 'Avatar') 'Avatar project type was not detected from VPM dependencies.'
    Assert-True (($firstCatalog.Projects | Where-Object Path -eq $worldProject).Kind -eq 'World') 'Nested world project was not detected.'
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
    Assert-True ($secondCatalog.CacheHits -eq 2 -and $secondCatalog.Refreshed -eq 0) 'Unchanged projects were not reused from the incremental cache.'
    [System.IO.File]::WriteAllText(
        (Join-Path $avatarProject 'Packages\vpm-manifest.json'),
        '{"dependencies":{"com.vrchat.base":"3.8.0","com.vrchat.avatars":"3.8.0","gogoloco":"1.8.6"},"locked":{}}'
    )
    $thirdCatalog = Get-VrcSetupProjectCatalog -RootPath $libraryRoot -CachePath $catalogCache
    $updatedAvatar = $thirdCatalog.Projects | Where-Object Path -eq $avatarProject
    Assert-True ($thirdCatalog.CacheHits -eq 1 -and $thirdCatalog.Refreshed -eq 1) 'A changed project did not refresh independently.'
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
        $mainShortcutPath = Join-Path $startMenuFolder 'VRChat Project Setup.lnk'
        $shortcutProcess = Start-Process -FilePath $mainShortcutPath -PassThru -Wait
        Assert-True ($shortcutProcess.ExitCode -eq 0) "Start Menu shortcut exited with $($shortcutProcess.ExitCode)."
        Assert-True (Test-Path -LiteralPath $routeProbe) 'Start Menu shortcut did not launch the installed copy.'
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
    & (Join-Path $repoRoot 'Repair VRChat Project Setup.bat') --no-pause *> $null
    $repairExit = $LASTEXITCODE
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
