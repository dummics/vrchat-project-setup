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
    $env:VRCSETUP_INSTALL_ROOT = $installRoot
    $env:VRCSETUP_START_MENU_ROOT = $startMenuRoot
    $env:VRCSETUP_SKIP_PATH_UPDATE = '1'

    Write-Host '[1/9] Parsing PowerShell files...'
    foreach ($file in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.ps1') {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        Assert-True ($errors.Count -eq 0) "Parse failure in $($file.FullName): $($errors[0].Message)"
    }

    Write-Host '[2/9] Checking runtime for machine-specific owner paths...'
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

    Write-Host '[3/9] Running the portable click launcher before installation...'
    $portableLauncher = Join-Path $repoRoot 'VRChat Project Setup.bat'
    & $portableLauncher -projectPath $projectRoot -Test *> $null
    $portableExit = $LASTEXITCODE
    Assert-True ($portableExit -eq 0) "Portable click launcher exited with ${portableExit}."

    Write-Host '[4/9] Installing through the clickable BAT to a special-character path...'
    [System.IO.Directory]::CreateDirectory($installRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $installRoot 'bin')) | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $installRoot 'bin\vrcsetup.cmd'), '@echo obsolete')
    [System.IO.File]::WriteAllText((Join-Path $installRoot 'Install-VrcSetup.ps1'), '# obsolete installed copy')
    & (Join-Path $repoRoot 'Install VRChat Project Setup.bat') --no-pause --no-launch | Out-Host
    $installExit = $LASTEXITCODE
    Assert-True ($installExit -eq 0) "Installer exited with ${installExit}."
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'setup-scripts\bin\vrcsetup.cmd')) 'Alias was not installed.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot '.vrcsetup-installed')) 'Installation marker was not created.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'VRChat Project Setup.bat')) 'Smart click launcher was not installed.'
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

    Write-Host '[5/9] Verifying the downloaded smart launcher prefers the installed copy...'
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

    Write-Host '[6/9] Running installed CLI alias against a special-character project path...'
    $aliasPath = Join-Path $installRoot 'setup-scripts\bin\vrcsetup.cmd'
    Push-Location -LiteralPath $env:TEMP
    try {
        & $aliasPath -projectPath $projectRoot -Test *> $null
        $aliasExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    Assert-True ($aliasExit -eq 0) "Installed alias exited with ${aliasExit}."

    Write-Host '[7/9] Verifying the runtime launcher preserves caller working directory and arguments...'
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

    Write-Host '[8/9] Repairing the installed copy from the downloaded BAT...'
    Remove-Item -LiteralPath (Join-Path $startMenuFolder 'Repair VRChat Project Setup.lnk') -Force
    & (Join-Path $repoRoot 'Repair VRChat Project Setup.bat') --no-pause *> $null
    $repairExit = $LASTEXITCODE
    Assert-True ($repairExit -eq 0) "Source repair launcher exited with ${repairExit}."
    Assert-True (Test-Path -LiteralPath (Join-Path $startMenuFolder 'Repair VRChat Project Setup.lnk')) 'Repair did not restore the missing Start Menu shortcut.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $repoRoot '.vrcsetup-installed'))) 'Repair incorrectly marked the downloaded folder as installed.'

    Write-Host '[9/9] Uninstalling from the downloaded BAT without touching its folder...'
    & (Join-Path $repoRoot 'Uninstall VRChat Project Setup.bat') --no-pause *> $null
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
