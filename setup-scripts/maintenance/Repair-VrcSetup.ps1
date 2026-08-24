[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\VrcSetup'),
    [switch]$SkipPathUpdate
)

$ErrorActionPreference = 'Stop'
$installRootFull = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($InstallRoot))
$setupScript = Join-Path $installRootFull 'setup-scripts\vrc-setup-script.ps1'
$defaultsPath = Join-Path $installRootFull 'setup-scripts\config\vrcsetup.defaults'
$configPath = Join-Path $installRootFull 'setup-scripts\config\vrcsetup.json'
$binPath = Join-Path $installRootFull 'setup-scripts\bin'
$aliasPath = Join-Path $binPath 'vrcsetup.cmd'
$shellIntegrationScript = Join-Path $PSScriptRoot 'VrcSetup-ShellIntegration.ps1'

if (-not (Test-Path -LiteralPath $shellIntegrationScript)) {
    throw 'This installation is incomplete. Run Install VRChat Project Setup.bat again from the original package.'
}
. $shellIntegrationScript
if (-not (Test-VrcSetupInstalledCopy -InstallRoot $installRootFull)) {
    throw "The selected folder is not an installed copy: ${installRootFull}"
}

$required = @(
    $setupScript,
    (Join-Path $installRootFull 'setup-scripts\commands\installer.ps1'),
    (Join-Path $installRootFull 'setup-scripts\commands\wizard.ps1'),
    $defaultsPath,
    (Join-Path $installRootFull 'VRChat Project Setup.bat'),
    $shellIntegrationScript,
    (Join-Path $installRootFull 'setup-scripts\maintenance\Repair-VrcSetup.ps1'),
    (Join-Path $installRootFull 'setup-scripts\maintenance\Uninstall-VrcSetup.ps1'),
    (Join-Path $installRootFull 'Repair VRChat Project Setup.bat'),
    (Join-Path $installRootFull 'Uninstall VRChat Project Setup.bat')
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) {
    throw "Core files are missing. Run Install VRChat Project Setup.bat again from the original package. Missing: $($missing -join ', ')"
}

[System.IO.Directory]::CreateDirectory($binPath) | Out-Null
$aliasContent = @'
@echo off
if /i "%~1"=="repair" (
	call "%~dp0..\..\Repair VRChat Project Setup.bat" --no-pause
	exit /b
)
if /i "%~1"=="uninstall" (
	call "%~dp0..\..\Uninstall VRChat Project Setup.bat" --no-pause
	exit /b
)
call "%~dp0..\setup.bat" %*
exit /b
'@
$normalizedAliasContent = $aliasContent -replace "`r?`n", "`r`n"
[System.IO.File]::WriteAllText($aliasPath, $normalizedAliasContent, [System.Text.Encoding]::ASCII)

if (-not (Test-Path -LiteralPath $configPath)) {
    Copy-Item -LiteralPath $defaultsPath -Destination $configPath
}

$skipPath = $SkipPathUpdate -or $env:VRCSETUP_SKIP_PATH_UPDATE -eq '1'
$startMenuFolder = Install-VrcSetupShellIntegration -InstallRoot $installRootFull -SkipPathUpdate:$skipPath

$parseErrors = @()
foreach ($file in Get-ChildItem -LiteralPath $installRootFull -Recurse -File -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { $parseErrors += @($errors | ForEach-Object { "$($file.FullName): $($_.Message)" }) }
}
if ($parseErrors.Count -gt 0) { throw "PowerShell validation failed: $($parseErrors -join '; ')" }

$smokeRoot = Join-Path $env:TEMP ('vrcsetup-repair-' + [guid]::NewGuid().ToString('N'))
try {
    [System.IO.Directory]::CreateDirectory((Join-Path $smokeRoot 'Assets')) | Out-Null
    $shell = Get-Command pwsh -ErrorAction SilentlyContinue
    if (-not $shell) { $shell = Get-Command powershell -ErrorAction Stop }
    & $shell.Source -NoProfile -ExecutionPolicy Bypass -File $setupScript -projectPath $smokeRoot -Test *> $null
    if ($LASTEXITCODE -ne 0) { throw "CLI smoke test failed with exit code ${LASTEXITCODE}." }
} finally {
    if (Test-Path -LiteralPath $smokeRoot) { [System.IO.Directory]::Delete($smokeRoot, $true) }
}

Write-Host 'VRChat Project Setup repair completed successfully.' -ForegroundColor Green
Write-Host "Windows Search shortcuts: ${startMenuFolder}" -ForegroundColor Gray
Write-Host 'Open a new terminal if the alias was previously unavailable.' -ForegroundColor Cyan
