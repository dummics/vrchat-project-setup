[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\VrcSetup'),
    [switch]$SkipPathUpdate
)

$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$installRootFull = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($InstallRoot))
$sourceRootFull = [System.IO.Path]::GetFullPath($sourceRoot)

if ($installRootFull.TrimEnd('\').Equals($sourceRootFull.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallRoot must be different from the source folder.'
}

function Copy-VrcSetupTree {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [string]$Relative = ''
    )

    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        $childRelative = if ($Relative) { Join-Path $Relative $item.Name } else { $item.Name }
        $normalizedRelative = $childRelative.Replace('/', '\')
        if ($normalizedRelative -ieq 'setup-scripts\config\vrcsetup.json' -or
            $normalizedRelative -like 'setup-scripts\logs\*' -or
            $normalizedRelative -like '*\.vpm-validation-cache\*') {
            continue
        }

        $target = Join-Path $Destination $item.Name
        if ($item.PSIsContainer) {
            Copy-VrcSetupTree -Source $item.FullName -Destination $target -Relative $childRelative
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination $target -Force
        }
    }
}

[System.IO.Directory]::CreateDirectory($installRootFull) | Out-Null
foreach ($runtimeItem in @(
    'setup-scripts',
    'bin',
    'vrcsetupfull.bat',
    'Install-VrcSetup.ps1',
    'Repair-VrcSetup.ps1',
    'Uninstall-VrcSetup.ps1',
    'REPAIR.bat',
    'UNINSTALL.bat'
)) {
    $source = Join-Path $sourceRoot $runtimeItem
    if (-not (Test-Path -LiteralPath $source)) { throw "Required runtime item is missing: ${source}" }
    $destination = Join-Path $installRootFull $runtimeItem
    if ((Get-Item -LiteralPath $source).PSIsContainer) {
        Copy-VrcSetupTree -Source $source -Destination $destination -Relative $runtimeItem
    } else {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

$binPath = Join-Path $installRootFull 'bin'
if (-not $SkipPathUpdate) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $alreadyPresent = @($parts | Where-Object { $_.TrimEnd('\') -ieq $binPath.TrimEnd('\') }).Count -gt 0
    if (-not $alreadyPresent) {
        $newUserPath = (@($parts) + $binPath) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
    }
}

Write-Host 'VRChat Project Setup installed successfully.' -ForegroundColor Green
Write-Host "Install folder: ${installRootFull}" -ForegroundColor Gray
if ($SkipPathUpdate) {
    Write-Host "Alias path (not added to PATH): ${binPath}" -ForegroundColor Yellow
} else {
    Write-Host 'Open a new terminal, then run: vrcsetup' -ForegroundColor Cyan
}
