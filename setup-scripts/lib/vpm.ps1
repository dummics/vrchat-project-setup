# VPM helpers (backend utilities)
# NOTE: This script is dot-sourced by the wizard.

# Cache: package id -> versions array
$script:VpmVersionsCache = @{}
$script:VpmMutexName = 'Local\VrcSetup.Vpm.Commands'

function Test-IsFileLockMessage {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    return ($Text -match 'being used by another process' -or $Text -match 'cannot access the file')
}

function Read-VccRepoJsonSafe {
    param(
        [string]$Path,
        [int]$RetryCount = 5,
        [int]$RetryDelayMs = 250
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }

    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            return (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop)
        } catch {
            $message = $_.Exception.Message
            if (($attempt -lt $RetryCount) -and (Test-IsFileLockMessage -Text $message)) {
                Start-Sleep -Milliseconds $RetryDelayMs
                continue
            }
            return $null
        }
    }

    return $null
}

function Get-VrcSetupLastToolOutput {
    return [string]$global:VRCSETUP_LAST_TOOL_OUTPUT
}

function Invoke-VpmCapture {
    param(
        [string[]]$Arguments,
        [int]$RetryCount = 4,
        [int]$InitialDelayMs = 500,
        [int]$MutexTimeoutSec = 900
    )

    $output = ""
    $outputLines = @()
    $exitCode = 1
    $mutex = $null
    $hasHandle = $false

    try {
        $mutex = New-Object System.Threading.Mutex($false, $script:VpmMutexName)
        try {
            $hasHandle = $mutex.WaitOne([TimeSpan]::FromSeconds($MutexTimeoutSec))
        } catch [System.Threading.AbandonedMutexException] {
            $hasHandle = $true
        }

        if (-not $hasHandle) {
            $output = "Timed out waiting for the global VPM lock."
            $outputLines = @($output)
            $exitCode = 1
        } else {
            $delayMs = $InitialDelayMs
            for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
                $rawOutput = @()
                try {
                    $rawOutput = @(& vpm @Arguments 2>&1)
                    $exitCode = $LASTEXITCODE
                } catch {
                    $rawOutput = @(${_})
                    $exitCode = if ($LASTEXITCODE -ne 0) { $LASTEXITCODE } else { 1 }
                }

                $outputLines = @($rawOutput | ForEach-Object { [string]$_ })
                $output = ($outputLines | Out-String)

                $shouldRetry = ($exitCode -ne 0) -and (Test-IsFileLockMessage -Text $output)
                if (-not $shouldRetry -or $attempt -ge $RetryCount) {
                    break
                }

                Start-Sleep -Milliseconds $delayMs
                $delayMs = [Math]::Min(($delayMs * 2), 4000)
            }
        }
    } catch {
        $output = (${_} | Out-String)
        $outputLines = @($output)
        $exitCode = if ($LASTEXITCODE -ne 0) { $LASTEXITCODE } else { 1 }
    } finally {
        if ($hasHandle -and $mutex) {
            try { $mutex.ReleaseMutex() } catch { }
        }
        if ($mutex) {
            try { $mutex.Dispose() } catch { }
        }
    }

    $global:VRCSETUP_LAST_TOOL_OUTPUT = $output
    return @{ ExitCode = $exitCode; Output = $output; OutputLines = $outputLines }
}

function Test-VpmCheckOutputIsSuccess {
    param(
        $Result
    )

    if ($null -eq $Result) { return $false }
    if ($Result.ExitCode -ne 0) { return $false }

    $out = [string]$Result.Output
    if ([string]::IsNullOrWhiteSpace($out)) { return $false }

    # VPM can return ExitCode 0 even when it prints a warning like:
    # "[WRN] No directory found at <id>"
    if ($out -match "\[.*ERR.*\]" -or $out -match "\[.*WRN.*\]" -or $out -match "No directory found") {
        return $false
    }

    # For check/show, a successful lookup usually prints INF fields.
    if ($out -match "\[.*INF.*\]" -and $out -match "\bname:\s*") {
        return $true
    }

    return $false
}

function Initialize-VpmTestProject {
    param(
        [string]$ScriptDir
    )

    $testProjectPath = Join-Path $ScriptDir ".vpm-validation-cache"
    $assetsPath = Join-Path $testProjectPath "Assets"
    $packagesPath = Join-Path $testProjectPath "Packages"
    $settingsPath = Join-Path $testProjectPath "ProjectSettings"

    # VPM requires at minimum an Assets folder to recognize a Unity project.
    # Re-create missing structure even if the cache directory already exists.
    $needsInit = -not (Test-Path -LiteralPath $assetsPath) -or -not (Test-Path -LiteralPath $packagesPath)

    if (-not $needsInit) {
        return $testProjectPath
    }

    Write-Host "Initializing VPM validation cache..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $testProjectPath -Force | Out-Null
    New-Item -ItemType Directory -Path $assetsPath -Force | Out-Null
    New-Item -ItemType Directory -Path $packagesPath -Force | Out-Null
    New-Item -ItemType Directory -Path $settingsPath -Force | Out-Null

    $manifest = @{ dependencies = @{ } }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $packagesPath "manifest.json") -Encoding UTF8

    $vpmManifest = @{ dependencies = @{ }; locked = @{ } }
    $vpmManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $packagesPath "vpm-manifest.json") -Encoding UTF8

    # Minimal ProjectVersion.txt so VPM and other tools can identify the Unity version
    Set-Content -LiteralPath (Join-Path $settingsPath "ProjectVersion.txt") -Value "m_EditorVersion: 2022.3.22f1" -Encoding UTF8

    Write-Host "Cache created at: ${testProjectPath}" -ForegroundColor Green
    return $testProjectPath
}

function Get-VpmReposPath {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        return $null
    }
    try {
        return (Join-Path $env:LOCALAPPDATA "VRChatCreatorCompanion\Repos")
    } catch {
        return $null
    }
}

function Get-VpmProjectPackageSet {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [string[]]$IncludeLockedPackages = @()
    )

    $packages = [ordered]@{}
    $manifestPath = Join-Path $ProjectPath 'Packages\vpm-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return [pscustomobject]$packages
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($manifest -and $manifest.dependencies) {
            foreach ($dependency in @($manifest.dependencies.PSObject.Properties)) {
                $rawValue = $dependency.Value
                $version = if ($rawValue -and $rawValue.PSObject.Properties.Name -contains 'version') {
                    [string]$rawValue.version
                } else {
                    [string]$rawValue
                }
                if (-not [string]::IsNullOrWhiteSpace($version)) {
                    $packages[$dependency.Name] = $version
                }
            }
        }
        if ($manifest -and $manifest.locked -and $IncludeLockedPackages.Count -gt 0) {
            foreach ($dependency in @($manifest.locked.PSObject.Properties)) {
                if ($IncludeLockedPackages -notcontains $dependency.Name -or $packages.Contains($dependency.Name)) { continue }
                $rawValue = $dependency.Value
                $version = if ($rawValue -and $rawValue.PSObject.Properties.Name -contains 'version') {
                    [string]$rawValue.version
                } else {
                    [string]$rawValue
                }
                if (-not [string]::IsNullOrWhiteSpace($version)) {
                    $packages[$dependency.Name] = $version
                }
            }
        }
    } catch {
        throw "Unable to read VPM packages from '${manifestPath}': $($_.Exception.Message)"
    }

    return [pscustomobject]$packages
}

function Compare-VpmPackageSets {
    param(
        $CurrentPackages,
        $DesiredPackages
    )

    $current = Copy-VpmPackageSet -Packages $CurrentPackages
    $desired = Copy-VpmPackageSet -Packages $DesiredPackages
    $added = @()
    $updated = @()
    $removed = @()
    $unchanged = @()

    foreach ($package in @($desired.PSObject.Properties)) {
        if ($current.PSObject.Properties.Name -notcontains $package.Name) {
            $added += $package.Name
        } elseif ([string]$current.($package.Name) -ne [string]$package.Value) {
            $updated += $package.Name
        } else {
            $unchanged += $package.Name
        }
    }
    foreach ($package in @($current.PSObject.Properties)) {
        if ($desired.PSObject.Properties.Name -notcontains $package.Name) {
            $removed += $package.Name
        }
    }

    return [pscustomobject]@{
        Added = @($added | Sort-Object)
        Updated = @($updated | Sort-Object)
        Removed = @($removed | Sort-Object)
        Unchanged = @($unchanged | Sort-Object)
    }
}

function Test-VpmRepositoryConfigured {
    param([Parameter(Mandatory)][string]$Url)

    $reposPath = Get-VpmReposPath
    if ([string]::IsNullOrWhiteSpace($reposPath) -or -not (Test-Path -LiteralPath $reposPath)) { return $false }

    foreach ($repoFile in Get-ChildItem -LiteralPath $reposPath -Filter '*.json' -File -ErrorAction SilentlyContinue) {
        $repoData = Read-VccRepoJsonSafe -Path $repoFile.FullName
        if (-not $repoData) { continue }
        $configuredUrl = if ($repoData.repo -and $repoData.repo.url) {
            [string]$repoData.repo.url
        } elseif ($repoData.url) {
            [string]$repoData.url
        } else {
            $null
        }
        if ($configuredUrl -and $configuredUrl.TrimEnd('/') -ieq $Url.TrimEnd('/')) { return $true }
    }
    return $false
}

function Ensure-VpmRepository {
    param([Parameter(Mandatory)][string]$Url)

    if (Test-VpmRepositoryConfigured -Url $Url) {
        return @{ Success = $true; Added = $false; Message = 'Repository already configured.' }
    }

    $result = Invoke-VpmCapture -Arguments @('add', 'repo', $Url)
    if ($result.ExitCode -ne 0 -or -not (Test-VpmRepositoryConfigured -Url $Url)) {
        return @{ Success = $false; Added = $false; Message = "Unable to add VPM repository: ${Url}"; Result = $result }
    }
    return @{ Success = $true; Added = $true; Message = 'Repository configured.'; Result = $result }
}

function Get-AllVpmPackageNames {
    $reposPath = Get-VpmReposPath
    $names = @()
    if (-not [string]::IsNullOrWhiteSpace($reposPath) -and (Test-Path -LiteralPath $reposPath)) {
        Get-ChildItem -LiteralPath $reposPath -Filter "*.json" -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $repoData = Read-VccRepoJsonSafe -Path $_.FullName
                if ($repoData.packages) {
                    $names += $repoData.packages.PSObject.Properties.Name
                }
            } catch { }
        }
    }
    return ($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique | Sort-Object)
}

function Get-VpmAvailableVersions {
    param(
        [string]$PackageName
    )

    if ([string]::IsNullOrWhiteSpace($PackageName)) { return @() }

    if ($script:VpmVersionsCache.ContainsKey($PackageName)) {
        return $script:VpmVersionsCache[$PackageName]
    }

    $reposPath = Get-VpmReposPath
    $available = @()
    if (-not [string]::IsNullOrWhiteSpace($reposPath) -and (Test-Path -LiteralPath $reposPath)) {
        Get-ChildItem -LiteralPath $reposPath -Filter "*.json" -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $repoData = Read-VccRepoJsonSafe -Path $_.FullName
                if (${repoData.packages}.${PackageName}) {
                    $versions = $repoData.packages.$PackageName.versions.PSObject.Properties.Name
                    if ($versions) {
                        $available += $versions
                    }
                }
            } catch { }
        }
    }

    $available = $available | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $available = Sort-SemVerDescending -Versions $available

    $script:VpmVersionsCache[$PackageName] = @($available)
    return $script:VpmVersionsCache[$PackageName]
}

function Test-VpmPackageExists {
    param(
        [string]$PackageName,
        [string]$ScriptDir
    )

    if ([string]::IsNullOrWhiteSpace($PackageName)) { return $false }

    # Prefer vrc-get if available (it can list versions reliably).
    try {
        $cmd = Get-Command Get-VrcGetAvailableVersions -ErrorAction SilentlyContinue
        if ($cmd) {
            $vrcGetVersions = Get-VrcGetAvailableVersions -PackageName $PackageName -ScriptDir $ScriptDir
            if ($vrcGetVersions.Count -gt 0) { return $true }
        }
    } catch { }

    # Source of truth fallback: VPM itself.
    $res = Invoke-VpmCapture -Arguments @('check', 'package', $PackageName)
    if (Test-VpmCheckOutputIsSuccess -Result $res) { return $true }

    # Secondary: VPM show (some installs support show better than check).
    $res2 = Invoke-VpmCapture -Arguments @('show', 'package', $PackageName)
    if (Test-VpmCheckOutputIsSuccess -Result $res2) { return $true }

    # Fallback: local VCC repos cache (useful for version listing).
    $versions = Get-VpmAvailableVersions -PackageName $PackageName
    if ($versions.Count -gt 0) { return $true }

    return $false
}

function Test-VpmPackageVersion {
    param(
        [string]$PackageName,
        [string]$Version,
        [string]$ScriptDir
    )

    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        return @{ Valid = $false; Message = "Package name is empty" }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        return @{ Valid = $false; Message = "Version is empty" }
    }

    # Validate existence even for 'latest'
    if ($Version -eq "latest") {
        try {
            $cmd = Get-Command Get-VrcGetAvailableVersions -ErrorAction SilentlyContinue
            if ($cmd) {
                $vrcGetVersions = Get-VrcGetAvailableVersions -PackageName $PackageName -ScriptDir $ScriptDir
                if ($vrcGetVersions.Count -gt 0) {
                    return @{ Valid = $true; Message = "Validated with vrc-get (latest)" }
                }
            }
        } catch { }

        $res = Invoke-VpmCapture -Arguments @('check', 'package', $PackageName)
        if (Test-VpmCheckOutputIsSuccess -Result $res) {
            return @{ Valid = $true; Message = "Validated with VPM (latest)" }
        }

        return @{ Valid = $false; Message = "Package not found or not resolvable (latest)" }
    }

    $testProject = Initialize-VpmTestProject -ScriptDir $ScriptDir
    try {
        $packageSpec = "${PackageName}@${Version}"
        $res = Invoke-VpmCapture -Arguments @('add', 'package', $packageSpec, '-p', $testProject)
        $output = [string]$res.Output
        $global:VRCSETUP_LAST_TOOL_OUTPUT = $output

        if ($res.ExitCode -ne 0 -or $output -match "ERR.*Could not get match" -or $output -match "ERR.*not found" -or $output -match "ERR.*Could not find project") {
            $reposPath = Get-VpmReposPath
            $availableVersions = @()
            if (Test-Path -LiteralPath $reposPath) {
                Get-ChildItem -LiteralPath $reposPath -Filter "*.json" -ErrorAction SilentlyContinue | ForEach-Object {
                    try {
                        $repoData = Read-VccRepoJsonSafe -Path $_.FullName
                        if (${repoData.packages}.${PackageName}) {
                            $versions = $repoData.packages.$PackageName.versions.PSObject.Properties.Name
                            if ($versions) { $availableVersions += $versions }
                        }
                    } catch { }
                }
            }

            if ($availableVersions.Count -gt 0) {
                $sortedVersions = $availableVersions | Sort-Object -Descending | Select-Object -First 5
                $versionList = $sortedVersions -join ", "
                return @{ Valid = $false; Message = "Version ${Version} not found. Recent versions: ${versionList}" }
            }

            return @{ Valid = $false; Message = "Version ${Version} not available" }
        }

        [void](Invoke-VpmCapture -Arguments @('remove', 'package', $PackageName, '-p', $testProject))
        return @{ Valid = $true; Message = "Version verified with VPM" }
    } catch {
        $global:VRCSETUP_LAST_TOOL_OUTPUT = (${_} | Out-String)
        return @{ Valid = $false; Message = "VPM validation error" }
    }
}
