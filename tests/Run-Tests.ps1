[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Directory]::GetParent($PSScriptRoot).FullName
$testRoot = Join-Path $env:TEMP ('vrcsetup-tests-' + [guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $testRoot 'Install [portable] & café'
$projectRoot = Join-Path $testRoot 'Project [avatar] & café'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Invoke-PowerShellFile {
    param([string]$Path, [object[]]$Arguments = @())
    $shell = Get-Command pwsh -ErrorAction SilentlyContinue
    if (-not $shell) { $shell = Get-Command powershell -ErrorAction Stop }
    & $shell.Source -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments | Out-Host
    $exitCode = $LASTEXITCODE
    return [int]$exitCode
}

try {
    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null

    Write-Host '[1/6] Parsing PowerShell files...'
    foreach ($file in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.ps1') {
        $tokens = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        Assert-True ($errors.Count -eq 0) "Parse failure in $($file.FullName): $($errors[0].Message)"
    }

    Write-Host '[2/6] Checking runtime for machine-specific owner paths...'
    $runtimeFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File | Where-Object {
        $_.Extension -in @('.ps1', '.bat', '.cmd') -and $_.FullName -notlike "$PSScriptRoot*"
    }
    foreach ($file in $runtimeFiles) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        Assert-True ($text -notmatch '(?i)C:\\Users\\domix|\.scriptsdum') "Machine-specific path found in $($file.FullName)"
    }

    Write-Host '[3/6] Installing to a path with spaces, Unicode and wildcard characters...'
    $installExit = Invoke-PowerShellFile -Path (Join-Path $repoRoot 'Install-VrcSetup.ps1') -Arguments @('-InstallRoot', $installRoot, '-SkipPathUpdate')
    Assert-True ($installExit -eq 0) "Installer exited with ${installExit}."
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'bin\vrcsetup.cmd')) 'Alias was not installed.'
    $sourceConfig = Join-Path $repoRoot 'setup-scripts\config\vrcsetup.json'
    $installedConfig = Join-Path $installRoot 'setup-scripts\config\vrcsetup.json'
    if (Test-Path -LiteralPath $sourceConfig) {
        Assert-True (Test-Path -LiteralPath $installedConfig) 'Installer did not migrate the existing local config.'
        Assert-True ((Get-FileHash -LiteralPath $sourceConfig).Hash -eq (Get-FileHash -LiteralPath $installedConfig).Hash) 'Migrated config differs from the source.'
    } else {
        Assert-True (-not (Test-Path -LiteralPath $installedConfig)) 'Installer created a machine-local config when none existed in the source.'
    }

    Write-Host '[4/6] Running installed CLI alias against a special-character project path...'
    [System.IO.Directory]::CreateDirectory((Join-Path $projectRoot 'Assets')) | Out-Null
    $aliasPath = Join-Path $installRoot 'bin\vrcsetup.cmd'
    Push-Location -LiteralPath $env:TEMP
    try {
        & $aliasPath -projectPath $projectRoot -Test *> $null
        $aliasExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    Assert-True ($aliasExit -eq 0) "Installed alias exited with ${aliasExit}."

    Write-Host '[5/6] Verifying top-level launcher preserves caller working directory and forwards CLI arguments...'
    $cwdOutput = Join-Path $testRoot 'caller-cwd.txt'
    $batchPath = Join-Path $testRoot 'check-launcher.cmd'
    $launcherPath = Join-Path $installRoot 'vrcsetupfull.bat'
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

    Write-Host '[6/6] Repairing, then uninstalling the isolated installation...'
    $previousSkipPath = $env:VRCSETUP_SKIP_PATH_UPDATE
    $env:VRCSETUP_SKIP_PATH_UPDATE = '1'
    try {
        & $aliasPath repair *> $null
        $repairExit = $LASTEXITCODE
        Assert-True ($repairExit -eq 0) "Repair alias exited with ${repairExit}."
        & $aliasPath uninstall *> $null
        $uninstallExit = $LASTEXITCODE
        Assert-True ($uninstallExit -eq 0) "Uninstall alias exited with ${uninstallExit}."
    } finally {
        $env:VRCSETUP_SKIP_PATH_UPDATE = $previousSkipPath
    }
    $uninstallDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path -LiteralPath $installRoot) -and [DateTime]::UtcNow -lt $uninstallDeadline) {
        Start-Sleep -Milliseconds 100
    }
    Assert-True (-not (Test-Path -LiteralPath $installRoot)) 'Uninstall left the install folder behind.'

    Write-Host 'PASS: all VRChat Project Setup tests completed.' -ForegroundColor Green
    exit 0
} catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        try { [System.IO.Directory]::Delete($testRoot, $true) } catch { }
    }
}
