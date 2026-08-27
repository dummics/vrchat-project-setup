# lib/config.ps1 - simple config helpers
function Initialize-ConfigIfMissing {
    param(
        [string]$ConfigPath,
        [string]$DefaultsPath
    )

    if (-not $ConfigPath) { throw 'ConfigPath is required' }
    if (Test-Path -LiteralPath $ConfigPath) { return $false }
    $hasTemplate = ($DefaultsPath -and (Test-Path -LiteralPath $DefaultsPath))

    $configDir = Split-Path -Parent $ConfigPath
    if ($configDir -and (-not (Test-Path -LiteralPath $configDir))) {
        New-Item -Path $configDir -ItemType Directory -Force | Out-Null
    }

    if ($hasTemplate) {
        Copy-Item -LiteralPath $DefaultsPath -Destination $ConfigPath -Force
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
            'dev.foxscore.easy-login' = 'latest'
        }
        DefaultPackages = @(
            'com.vrchat.base',
            'com.vrchat.avatars',
            'com.vrchat.core.vpm-resolver',
            'com.vrcfury.vrcfury',
            'gogoloco',
            'adjerry91.vrcft.templates',
            'com.poiyomi.toon',
            'dev.foxscore.easy-login'
        )
        RequiredPackages = @(
            'com.vrchat.base',
            'com.vrchat.avatars',
            'com.vrchat.core.vpm-resolver'
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

    $skeleton | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
    return $true
}

function Load-Config {
    param([string]$ConfigPath)
    if (-not $ConfigPath) { throw 'ConfigPath is required' }
    if (-not (Test-Path -LiteralPath $ConfigPath)) { return $null }
    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

function Save-Config {
    param($Config, [string]$ConfigPath)
    if (-not $ConfigPath) { throw 'ConfigPath required' }
    $Config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Get-DefaultPackages {
    param($Config)
    $builtIn = @(
        'com.vrchat.base',
        'com.vrchat.avatars',
        'com.vrchat.core.vpm-resolver',
        'com.vrcfury.vrcfury',
        'gogoloco',
        'adjerry91.vrcft.templates',
        'com.poiyomi.toon',
        'dev.foxscore.easy-login'
    )

    if ($Config -and $Config.PSObject.Properties.Name -contains 'DefaultPackages' -and $Config.DefaultPackages) {
        return @($builtIn + @($Config.DefaultPackages) | Select-Object -Unique)
    }
    return $builtIn
}

function Test-IsDefaultPackage {
    param(
        [string]$PackageName,
        $Config
    )
    $defaults = Get-DefaultPackages -Config $Config
    return ($defaults -contains $PackageName)
}

function Get-RequiredPackages {
    param($Config)

    # These packages form the minimum VRChat avatar/VPM foundation. Everything
    # else in DefaultPackages is a removable starter choice, not a lock.
    $builtIn = @(
        'com.vrchat.base',
        'com.vrchat.avatars',
        'com.vrchat.core.vpm-resolver'
    )

    if ($Config -and $Config.PSObject.Properties.Name -contains 'RequiredPackages' -and $Config.RequiredPackages) {
        return @($builtIn + @($Config.RequiredPackages) | Select-Object -Unique)
    }
    return $builtIn
}

function Test-IsRequiredPackage {
    param(
        [string]$PackageName,
        $Config
    )

    return ((Get-RequiredPackages -Config $Config) -contains $PackageName)
}

function Copy-VpmPackageSet {
    param($Packages)

    $copy = [ordered]@{}
    if ($Packages -is [System.Array]) {
        foreach ($packageName in @($Packages)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$packageName)) {
                $copy[[string]$packageName] = 'latest'
            }
        }
    } elseif ($Packages) {
        foreach ($package in @($Packages.PSObject.Properties)) {
            $copy[$package.Name] = [string]$package.Value
        }
    }
    return [pscustomobject]$copy
}

function Add-RequiredPackagesToSet {
    param(
        $Packages,
        $Config
    )

    $result = Copy-VpmPackageSet -Packages $Packages
    foreach ($packageName in Get-RequiredPackages -Config $Config) {
        if ($result.PSObject.Properties.Name -notcontains $packageName) {
            $result | Add-Member -MemberType NoteProperty -Name $packageName -Value 'latest' -Force
        }
    }
    return $result
}

function Find-UnityEditorPaths {
    # Search common Unity Hub installation locations for Unity.exe
    $candidates = @()

    # Unity Hub default location
    $hubEditors = Join-Path $env:ProgramFiles "Unity\Hub\Editor"
    if (Test-Path -LiteralPath $hubEditors) {
        Get-ChildItem -LiteralPath $hubEditors -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $exe = Join-Path $_.FullName "Editor\Unity.exe"
            if (Test-Path -LiteralPath $exe) {
                $candidates += [pscustomobject]@{ Version = $_.Name; Path = $exe }
            }
        }
    }

    # Secondary location (x86)
    $hubEditors86 = Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor"
    if (Test-Path -LiteralPath $hubEditors86) {
        Get-ChildItem -LiteralPath $hubEditors86 -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $exe = Join-Path $_.FullName "Editor\Unity.exe"
            if (Test-Path -LiteralPath $exe) {
                $candidates += [pscustomobject]@{ Version = $_.Name; Path = $exe }
            }
        }
    }

    return ,$candidates
}

function Test-UnityEditorPath {
    # Validates a Unity Editor path: must exist, must be a file named Unity.exe
    # Returns @{ Valid = $bool; Message = $string }
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return @{ Valid = $false; Message = "Path is empty" }
    }
    if (-not (Test-Path -LiteralPath $Path)) {
        return @{ Valid = $false; Message = "Path not found: ${Path}" }
    }
    if ((Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue).PSIsContainer) {
        return @{ Valid = $false; Message = "Path is a folder, not Unity.exe: ${Path}" }
    }
    $fileName = [System.IO.Path]::GetFileName($Path)
    if ($fileName -ine 'Unity.exe') {
        return @{ Valid = $false; Message = "File is '${fileName}', expected 'Unity.exe'" }
    }
    return @{ Valid = $true; Message = $null }
}

function Get-PathStatus {
    # Returns a display string for a configured path:
    #   "(not set)"            - when value is empty/null
    #   "NOT FOUND: <path>"    - when set but doesn't exist on disk
    #   "<path>"               - when set and exists
    param(
        [string]$Path,
        [string]$NotSetLabel = "(not set)"
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { return $NotSetLabel }
    if (-not (Test-Path -LiteralPath $Path)) { return "NOT FOUND: ${Path}" }
    return $Path
}

function Test-PathExists {
    # Quick boolean: is the configured path non-empty AND exists on disk?
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return (Test-Path -LiteralPath $Path)
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

function Test-ConfigEssentialsExist {
    # Returns @{ Ready = $bool; Missing = @(...) }
    # Checks both that values are configured AND that paths actually exist on disk
    param($Config)
    $missing = @()
    if (-not $Config) { return @{ Ready = $false; Missing = @("Config not loaded") } }

    $editor = [string]$Config.UnityEditorPath
    $root = [string]$Config.UnityProjectsRoot

    if ([string]::IsNullOrWhiteSpace($editor)) {
        $missing += "Unity Editor path is not set"
    } elseif (-not (Test-Path -LiteralPath $editor)) {
        $missing += "Unity Editor not found: ${editor}"
    } else {
        $editorCheck = Test-UnityEditorPath -Path $editor
        if (-not $editorCheck.Valid) { $missing += $editorCheck.Message }
    }

    if ([string]::IsNullOrWhiteSpace($root)) {
        $missing += "Projects root is not set"
    } elseif (-not (Test-Path -LiteralPath $root)) {
        $missing += "Projects root not found: ${root}"
    }

    return @{ Ready = ($missing.Count -eq 0); Missing = $missing }
}

