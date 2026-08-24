Set-StrictMode -Version 2.0

$script:VrcSetupMarkerName = '.vrcsetup-installed'
$script:VrcSetupStartMenuFolderName = 'VRChat Project Setup'
$script:VrcSetupShortcutNames = @(
    'VRChat Project Setup.lnk',
    'Repair VRChat Project Setup.lnk',
    'Uninstall VRChat Project Setup.lnk'
)

function Get-VrcSetupInstallMarkerPath {
    param([Parameter(Mandatory)][string]$InstallRoot)

    return Join-Path $InstallRoot $script:VrcSetupMarkerName
}

function Test-VrcSetupInstalledCopy {
    param([Parameter(Mandatory)][string]$InstallRoot)

    return Test-Path -LiteralPath (Get-VrcSetupInstallMarkerPath -InstallRoot $InstallRoot) -PathType Leaf
}

function Write-VrcSetupInstallMarker {
    param([Parameter(Mandatory)][string]$InstallRoot)

    $markerPath = Get-VrcSetupInstallMarkerPath -InstallRoot $InstallRoot
    $content = "VRChat Project Setup user installation`r`n"
    [System.IO.File]::WriteAllText($markerPath, $content, [System.Text.Encoding]::ASCII)
}

function Get-VrcSetupStartMenuFolder {
    $programsRoot = $env:VRCSETUP_START_MENU_ROOT
    if ([string]::IsNullOrWhiteSpace($programsRoot)) {
        $programsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    }
    if ([string]::IsNullOrWhiteSpace($programsRoot)) {
        throw 'The current user Start Menu folder could not be resolved.'
    }

    return Join-Path ([Environment]::ExpandEnvironmentVariables($programsRoot)) $script:VrcSetupStartMenuFolderName
}

function Add-VrcSetupToUserPath {
    param([Parameter(Mandatory)][string]$BinPath)

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if (@($parts | Where-Object { $_.TrimEnd('\') -ieq $BinPath.TrimEnd('\') }).Count -eq 0) {
        [Environment]::SetEnvironmentVariable('Path', ((@($parts) + $BinPath) -join ';'), 'User')
    }
}

function Remove-VrcSetupFromUserPath {
    param([Parameter(Mandatory)][string]$BinPath)

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_.TrimEnd('\') -ine $BinPath.TrimEnd('\')
    })
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
}

function New-VrcSetupShortcut {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$BatchPath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Description
    )

    $commandProcessor = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $commandProcessor
    $shortcut.Arguments = '/d /s /c ""{0}""' -f $BatchPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.IconLocation = "$commandProcessor,0"
    $shortcut.Save()
}

function Install-VrcSetupStartMenuShortcuts {
    param([Parameter(Mandatory)][string]$InstallRoot)

    $startMenuFolder = Get-VrcSetupStartMenuFolder
    [System.IO.Directory]::CreateDirectory($startMenuFolder) | Out-Null

    $shortcuts = @(
        @{
            Name = 'VRChat Project Setup.lnk'
            Batch = 'VRChat Project Setup.bat'
            Description = 'Open VRChat Project Setup'
        },
        @{
            Name = 'Repair VRChat Project Setup.lnk'
            Batch = 'Repair VRChat Project Setup.bat'
            Description = 'Repair VRChat Project Setup'
        },
        @{
            Name = 'Uninstall VRChat Project Setup.lnk'
            Batch = 'Uninstall VRChat Project Setup.bat'
            Description = 'Uninstall VRChat Project Setup'
        }
    )

    foreach ($definition in $shortcuts) {
        New-VrcSetupShortcut `
            -ShortcutPath (Join-Path $startMenuFolder $definition.Name) `
            -BatchPath (Join-Path $InstallRoot $definition.Batch) `
            -WorkingDirectory $InstallRoot `
            -Description $definition.Description
    }

    return $startMenuFolder
}

function Remove-VrcSetupStartMenuShortcuts {
    $startMenuFolder = Get-VrcSetupStartMenuFolder
    foreach ($shortcutName in $script:VrcSetupShortcutNames) {
        $shortcutPath = Join-Path $startMenuFolder $shortcutName
        if (Test-Path -LiteralPath $shortcutPath) {
            Remove-Item -LiteralPath $shortcutPath -Force
        }
    }

    if ((Test-Path -LiteralPath $startMenuFolder) -and
        @(Get-ChildItem -LiteralPath $startMenuFolder -Force).Count -eq 0) {
        Remove-Item -LiteralPath $startMenuFolder -Force
    }
}

function Install-VrcSetupShellIntegration {
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [switch]$SkipPathUpdate
    )

    Write-VrcSetupInstallMarker -InstallRoot $InstallRoot
    if (-not $SkipPathUpdate) {
        Add-VrcSetupToUserPath -BinPath (Join-Path $InstallRoot 'setup-scripts\bin')
    }
    return Install-VrcSetupStartMenuShortcuts -InstallRoot $InstallRoot
}

function Remove-VrcSetupShellIntegration {
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [switch]$SkipPathUpdate
    )

    if (-not $SkipPathUpdate) {
        Remove-VrcSetupFromUserPath -BinPath (Join-Path $InstallRoot 'setup-scripts\bin')
    }
    Remove-VrcSetupStartMenuShortcuts
}
