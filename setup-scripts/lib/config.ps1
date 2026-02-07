# lib/config.ps1 - simple config helpers
function Initialize-ConfigIfMissing {
    param(
        [string]$ConfigPath,
        [string]$DefaultsPath
    )

    if (-not $ConfigPath) { throw 'ConfigPath is required' }
    if (Test-Path $ConfigPath) { return $false }
    $hasTemplate = ($DefaultsPath -and (Test-Path $DefaultsPath))

    $configDir = Split-Path -Parent $ConfigPath
    if ($configDir -and (-not (Test-Path $configDir))) {
        New-Item -Path $configDir -ItemType Directory -Force | Out-Null
    }

    if ($hasTemplate) {
        Copy-Item -Path $DefaultsPath -Destination $ConfigPath -Force
        return $true
    }

    # Worst-case fallback: generate a minimal skeleton.
    $skeleton = [pscustomobject]@{
        VpmPackages = [pscustomobject]@{
            'com.vrchat.base' = 'latest'
            'com.vrchat.avatars' = 'latest'
            'com.vrchat.core.vpm-resolver' = 'latest'
            'com.vrcfury.vrcfury' = 'latest'
            'gogoloco' = 'latest'
            'adjerry91.vrcft.templates' = 'latest'
            'com.poiyomi.toon' = 'latest'
        }
        DefaultPackages = @(
            'com.vrchat.base',
            'com.vrchat.avatars',
            'com.vrchat.core.vpm-resolver',
            'com.vrcfury.vrcfury',
            'gogoloco',
            'adjerry91.vrcft.templates',
            'com.poiyomi.toon'
        )
        UnityEditorPath = ''
        UnityProjectsRoot = ''
        Naming = [pscustomobject]@{
            DefaultPrefix = ''
            DefaultSuffix = ''
            RegexRemovePatterns = @()
            RememberUnityPackageNames = $true
        }
        SavedProjectNames = [pscustomobject]@{}
        LastProjectName = ''
        LastUnityPackagePath = ''
        UnityPackagesFolder = $null
    }

    $skeleton | ConvertTo-Json -Depth 10 | Set-Content $ConfigPath -Encoding UTF8
    return $true
}

function Load-Config {
    param([string]$ConfigPath)
    if (-not $ConfigPath) { throw 'ConfigPath is required' }
    if (-not (Test-Path $ConfigPath)) { return $null }
    return Get-Content $ConfigPath -Raw | ConvertFrom-Json
}

function Save-Config {
    param($Config, [string]$ConfigPath)
    if (-not $ConfigPath) { throw 'ConfigPath required' }
    $Config | ConvertTo-Json -Depth 10 | Set-Content $ConfigPath -Encoding UTF8
}

function Get-DefaultPackages {
    param($Config)
    if (-not $Config) { return @() }
    if ($Config.PSObject.Properties.Name -contains 'DefaultPackages' -and $Config.DefaultPackages) {
        return @($Config.DefaultPackages)
    }
    # Fallback: VRChat core packages are always protected
    return @(
        'com.vrchat.base',
        'com.vrchat.avatars',
        'com.vrchat.core.vpm-resolver',
        'com.vrcfury.vrcfury',
        'gogoloco',
        'adjerry91.vrcft.templates',
        'com.poiyomi.toon'
    )
}

function Test-IsDefaultPackage {
    param(
        [string]$PackageName,
        $Config
    )
    $defaults = Get-DefaultPackages -Config $Config
    return ($defaults -contains $PackageName)
}

function Find-UnityEditorPaths {
    # Search common Unity Hub installation locations for Unity.exe
    $candidates = @()

    # Unity Hub default location
    $hubEditors = Join-Path $env:ProgramFiles "Unity\Hub\Editor"
    if (Test-Path $hubEditors) {
        Get-ChildItem $hubEditors -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $exe = Join-Path $_.FullName "Editor\Unity.exe"
            if (Test-Path $exe) {
                $candidates += @{ Version = $_.Name; Path = $exe }
            }
        }
    }

    # Secondary location (x86)
    $hubEditors86 = Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor"
    if (Test-Path $hubEditors86) {
        Get-ChildItem $hubEditors86 -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $exe = Join-Path $_.FullName "Editor\Unity.exe"
            if (Test-Path $exe) {
                $candidates += @{ Version = $_.Name; Path = $exe }
            }
        }
    }

    return $candidates
}

function Test-ConfigEssentials {
    # Returns $true if UnityEditorPath and UnityProjectsRoot are configured and valid
    param($Config)
    if (-not $Config) { return $false }
    $editor = [string]$Config.UnityEditorPath
    $root = [string]$Config.UnityProjectsRoot
    if ([string]::IsNullOrWhiteSpace($editor) -or [string]::IsNullOrWhiteSpace($root)) { return $false }
    return $true
}

