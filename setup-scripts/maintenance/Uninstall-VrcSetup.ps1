[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\VrcSetup'),
    [switch]$KeepConfig,
    [switch]$SkipPathUpdate,
    [switch]$DeferredCleanup
)

$ErrorActionPreference = 'Stop'
$installRootFull = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($InstallRoot))
$shellIntegrationScript = Join-Path $PSScriptRoot 'VrcSetup-ShellIntegration.ps1'

if (-not (Test-Path -LiteralPath $shellIntegrationScript)) {
    throw "The selected folder is not a complete VRChat Project Setup installation: ${installRootFull}"
}
. $shellIntegrationScript
if (-not (Test-VrcSetupInstalledCopy -InstallRoot $installRootFull)) {
    throw "Refusing to remove a folder that is not marked as an installed copy: ${installRootFull}"
}

$skipPath = $SkipPathUpdate -or $env:VRCSETUP_SKIP_PATH_UPDATE -eq '1'
if ($PSCmdlet.ShouldProcess('Windows user integration', 'Remove PATH entry and Start Menu shortcuts')) {
    Remove-VrcSetupShellIntegration -InstallRoot $installRootFull -SkipPathUpdate:$skipPath
}

if ($KeepConfig) {
    $configPath = Join-Path $installRootFull 'setup-scripts\config\vrcsetup.json'
    if (Test-Path -LiteralPath $configPath) {
        $savedConfig = Get-Content -LiteralPath $configPath -Raw
        $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
        $savedConfigPath = Join-Path $desktop 'vrcsetup-config.json'
        if (Test-Path -LiteralPath $savedConfigPath) {
            $savedConfigPath = Join-Path $desktop ("vrcsetup-config-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        }
        if ($PSCmdlet.ShouldProcess($savedConfigPath, 'Save configuration backup')) {
            Set-Content -LiteralPath $savedConfigPath -Value $savedConfig -Encoding UTF8
            Write-Host "Configuration saved to: ${savedConfigPath}" -ForegroundColor Gray
        }
    }
}

if (Test-Path -LiteralPath $installRootFull) {
    if ($DeferredCleanup) {
        if ($PSCmdlet.ShouldProcess($installRootFull, 'Schedule removal after the launcher exits')) {
            $cleanupScript = Join-Path $env:TEMP ("vrcsetup-uninstall-{0}.ps1" -f [guid]::NewGuid().ToString('N'))
            $cleanupContent = @'
param([string]$InstallRoot, [string]$SelfPath)
Start-Sleep -Milliseconds 750
try {
    if (Test-Path -LiteralPath $InstallRoot) {
        Remove-Item -LiteralPath $InstallRoot -Recurse -Force
    }
} finally {
    [System.IO.File]::Delete($SelfPath)
}
'@
            [System.IO.File]::WriteAllText($cleanupScript, $cleanupContent, [System.Text.UTF8Encoding]::new($false))
            $shell = Get-Command pwsh -ErrorAction SilentlyContinue
            if (-not $shell) { $shell = Get-Command powershell -ErrorAction Stop }
            $arguments = @(
                '-NoProfile',
                '-ExecutionPolicy', 'Bypass',
                '-File', "`"${cleanupScript}`"",
                '-InstallRoot', "`"${installRootFull}`"",
                '-SelfPath', "`"${cleanupScript}`""
            )
            Start-Process -FilePath $shell.Source -ArgumentList $arguments -WindowStyle Hidden | Out-Null
        }
    } elseif ($PSCmdlet.ShouldProcess($installRootFull, 'Remove installed VRChat Project Setup files')) {
        Remove-Item -LiteralPath $installRootFull -Recurse -Force
    }
}

Write-Host 'VRChat Project Setup uninstalled. Open a new terminal to refresh PATH.' -ForegroundColor Green
