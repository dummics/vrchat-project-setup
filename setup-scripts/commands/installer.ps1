param()

$script:VrcSetupStatusSuccess = 0
$script:VrcSetupStatusFailure = 1
$script:VrcSetupStatusCancelled = 2

# commands/installer.ps1 - central installer logic exported as a function
$scriptDir = [System.IO.Directory]::GetParent($PSScriptRoot).FullName
. "$scriptDir\lib\menu.ps1"
. "$scriptDir\lib\utils.ps1"
. "$scriptDir\lib\progress.ps1"
. "$scriptDir\lib\vpm.ps1"
. "$scriptDir\lib\config.ps1"
. "$scriptDir\lib\project-state.ps1"

function Write-VrcSetupLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [int]$RetryCount = 6,
        [int]$RetryDelayMs = 150
    )

    if ([string]::IsNullOrWhiteSpace($global:VRCSETUP_LOGFILE)) { return }

    $line = $Message
    if (-not $line.EndsWith([Environment]::NewLine)) {
        $line += [Environment]::NewLine
    }

    for ($attempt = 0; $attempt -lt $RetryCount; $attempt++) {
        try {
            [System.IO.File]::AppendAllText($global:VRCSETUP_LOGFILE, $line)
            return
        } catch {
            if ($attempt -ge ($RetryCount - 1)) {
                Write-Host ("Warning: failed to write to log file {0}: {1}" -f $global:VRCSETUP_LOGFILE, $_.Exception.Message) -ForegroundColor Yellow
                return
            }
            Start-Sleep -Milliseconds $RetryDelayMs
        }
    }
}

function Write-VrcSetupCommandOutput {
    param(
        $Entries
    )

    foreach ($entry in @($Entries)) {
        $line = [string]$entry
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        Write-Host $line
        Write-VrcSetupLog -Message $line
    }
}

function Get-VpmManifestValidationResult {
    param(
        [string]$ProjectPath,
        $Packages
    )

    $manifestPath = Join-Path $ProjectPath "Packages\manifest.json"
    $vpmManifestPath = Join-Path $ProjectPath "Packages\vpm-manifest.json"
    $result = [pscustomobject]@{
        Valid = $false
        ManifestPath = $manifestPath
        VpmManifestPath = $vpmManifestPath
        MissingPackages = @()
        Message = $null
    }

    if ((-not (Test-Path -LiteralPath $manifestPath)) -and (-not (Test-Path -LiteralPath $vpmManifestPath))) {
        $result.Message = "Neither manifest.json nor vpm-manifest.json was found."
        return $result
    }

    $expectedPackages = @()
    try {
        if ($Packages) {
            $expectedPackages = @($Packages.PSObject.Properties | ForEach-Object { $_.Name })
        }
    } catch {
        $expectedPackages = @()
    }

    if ($expectedPackages.Count -eq 0) {
        $result.Valid = $true
        $result.Message = "No configured VPM packages to validate."
        return $result
    }

    try {
        $dependencyNames = @()

        if (Test-Path -LiteralPath $manifestPath) {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($manifest -and $manifest.dependencies) {
                $dependencyNames += @($manifest.dependencies.PSObject.Properties | ForEach-Object { $_.Name })
            }
        }

        if (Test-Path -LiteralPath $vpmManifestPath) {
            $vpmManifest = Get-Content -LiteralPath $vpmManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($vpmManifest -and $vpmManifest.dependencies) {
                $dependencyNames += @($vpmManifest.dependencies.PSObject.Properties | ForEach-Object { $_.Name })
            }
        }

        $dependencyNames = @($dependencyNames | Sort-Object -Unique)

        $missing = @($expectedPackages | Where-Object { $dependencyNames -notcontains $_ })
        $result.MissingPackages = $missing
        if ($missing.Count -eq 0) {
            $result.Valid = $true
            $result.Message = "All configured VPM packages are present in the project manifests."
        } else {
            $result.Message = ("Missing configured VPM packages in project manifests: {0}" -f ($missing -join ", "))
        }
    } catch {
        $result.Message = ("Failed to read project manifests: {0}" -f $_.Exception.Message)
    }

    return $result
}

function Test-PackagesIncludeEasyLogin {
    param($Packages)
    if (-not $Packages) { return $false }
    return @($Packages.PSObject.Properties.Name) -contains 'dev.foxscore.easy-login'
}

function Move-LegacyEasyLoginPackage {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        $Packages,
        [switch]$Test
    )

    $result = [pscustomobject]@{ Success = $true; Changed = $false; BackupPath = $null; Message = $null }
    if (-not (Test-PackagesIncludeEasyLogin -Packages $Packages)) { return $result }

    $resolvedProject = (Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop).Path.TrimEnd('\')
    $legacyPath = Join-Path $resolvedProject 'Assets\EASY LOGIN'
    if (-not (Test-Path -LiteralPath $legacyPath)) { return $result }

    $packageJson = Join-Path $legacyPath 'package.json'
    try {
        $legacyPackage = Get-Content -LiteralPath $packageJson -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    } catch {
        $result.Success = $false
        $result.Message = "Legacy Easy Login folder exists but package.json is unreadable: ${legacyPath}"
        return $result
    }
    if ([string]$legacyPackage.name -ne 'dev.foxscore.easy-login') {
        $result.Success = $false
        $result.Message = "Refusing to move an unexpected Assets\\EASY LOGIN folder: ${legacyPath}"
        return $result
    }

    $resolvedLegacy = (Resolve-Path -LiteralPath $legacyPath -ErrorAction Stop).Path
    if (-not $resolvedLegacy.StartsWith($resolvedProject + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        $result.Success = $false
        $result.Message = "Legacy package resolved outside the Unity project: ${resolvedLegacy}"
        return $result
    }

    $backupPath = Join-Path $resolvedProject ('.vrcsetup\backups\easy-login-assets-{0}' -f (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
    $result.Changed = $true
    $result.BackupPath = $backupPath
    if ($Test) {
        $result.Message = "Would move legacy Easy Login to ${backupPath}"
        return $result
    }

    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
        Move-Item -LiteralPath $legacyPath -Destination $backupPath
        $legacyMeta = "${legacyPath}.meta"
        if (Test-Path -LiteralPath $legacyMeta) {
            Move-Item -LiteralPath $legacyMeta -Destination "${backupPath}.meta"
        }
        $result.Message = "Legacy Easy Login moved outside Assets to ${backupPath}"
    } catch {
        $result.Success = $false
        $result.Message = "Failed to back up legacy Easy Login: $($_.Exception.Message)"
    }
    return $result
}

function Ensure-VpmPackagesPresentAfterImport {
    param(
        [string]$ProjectPath,
        $Packages,
        [switch]$Test,
        [string]$PhaseLabel = "after import"
    )

    if ($Test) { return $script:VrcSetupStatusSuccess }

    $legacyMigration = Move-LegacyEasyLoginPackage -ProjectPath $ProjectPath -Packages $Packages
    if (-not $legacyMigration.Success) {
        Write-Host $legacyMigration.Message -ForegroundColor Red
        Write-VrcSetupLog -Message ("ERROR: {0}" -f $legacyMigration.Message)
        return $script:VrcSetupStatusFailure
    }
    if ($legacyMigration.Changed) {
        Write-Host $legacyMigration.Message -ForegroundColor Yellow
        Write-VrcSetupLog -Message ("INFO: {0}" -f $legacyMigration.Message)
    }

    $validation = Get-VpmManifestValidationResult -ProjectPath $ProjectPath -Packages $Packages
    if ($validation.Valid -and -not $legacyMigration.Changed) {
        return $script:VrcSetupStatusSuccess
    }

    Write-Host ("Configured VPM packages need repair {0}." -f $PhaseLabel) -ForegroundColor Yellow
    Write-Host $validation.Message -ForegroundColor Yellow
    Write-VrcSetupLog -Message ("WARN: VPM manifest validation failed {0}: {1}" -f $PhaseLabel, $validation.Message)

    $repairStatus = Install-PackagesInProject -ProjectPath $ProjectPath -Packages $Packages -Test:$Test
    if ($repairStatus -ne $script:VrcSetupStatusSuccess) {
        return $repairStatus
    }

    $revalidation = Get-VpmManifestValidationResult -ProjectPath $ProjectPath -Packages $Packages
    if (-not $revalidation.Valid) {
        Write-Host ("Error: configured VPM packages are still missing {0}." -f $PhaseLabel) -ForegroundColor Red
        Write-Host $revalidation.Message -ForegroundColor Red
        Write-VrcSetupLog -Message ("ERROR: VPM manifest validation still failing {0}: {1}" -f $PhaseLabel, $revalidation.Message)
        return $script:VrcSetupStatusFailure
    }

    Write-Host ("Configured VPM packages restored successfully {0}." -f $PhaseLabel) -ForegroundColor Green
    return $script:VrcSetupStatusSuccess
}

function Install-PackagesInProject {
    param(
        [string]$ProjectPath,
        $Packages,
        [switch]$Test,
        [switch]$SyncPackages
    )

    if ((-not $Test) -and (Test-PackagesIncludeEasyLogin -Packages $Packages)) {
        $repoResult = Ensure-VpmRepository -Url 'https://foxscore.dev/vpm/index.json'
        if (-not $repoResult.Success) {
            Write-Host $repoResult.Message -ForegroundColor Red
            Write-VrcSetupLog -Message ("ERROR: {0}" -f $repoResult.Message)
            return $script:VrcSetupStatusFailure
        }
    }

    $legacyMigration = Move-LegacyEasyLoginPackage -ProjectPath $ProjectPath -Packages $Packages -Test:$Test
    if (-not $legacyMigration.Success) {
        Write-Host $legacyMigration.Message -ForegroundColor Red
        Write-VrcSetupLog -Message ("ERROR: {0}" -f $legacyMigration.Message)
        return $script:VrcSetupStatusFailure
    }
    if ($legacyMigration.Changed) {
        Write-Host $legacyMigration.Message -ForegroundColor Yellow
        Write-VrcSetupLog -Message ("INFO: {0}" -f $legacyMigration.Message)
    }

    Push-Location -LiteralPath $ProjectPath
    try {
    $hadFailures = $false
    $syncPlan = $null

    # Back up both package manifests once before a batch change.
    foreach ($manifestPath in @(
        (Join-Path $ProjectPath 'Packages\manifest.json'),
        (Join-Path $ProjectPath 'Packages\vpm-manifest.json')
    )) {
        if ((-not $Test) -and (Test-Path -LiteralPath $manifestPath)) {
            try {
                $backupPath = "${manifestPath}.bak.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
                Copy-Item -LiteralPath $manifestPath -Destination $backupPath -Force
                Write-Host "Backup manifest created: ${backupPath}" -ForegroundColor Gray
            } catch {
                Write-Host "Failed to create manifest backup: ${_}" -ForegroundColor Yellow
            }
        }
    }

    if ($SyncPackages) {
        $currentPackages = Get-VpmProjectPackageSet -ProjectPath $ProjectPath
        $syncPlan = Compare-VpmPackageSets -CurrentPackages $currentPackages -DesiredPackages $Packages
        $desiredNames = @($Packages.PSObject.Properties.Name)
        $packagesToRemove = @($currentPackages.PSObject.Properties.Name | Where-Object { $desiredNames -notcontains $_ })
        foreach ($packageName in $packagesToRemove) {
            if ($Test) {
                Write-Host "[TEST] Would remove package: ${packageName}" -ForegroundColor DarkGray
                Write-VrcSetupLog -Message "[TEST] Would remove package: ${packageName}"
                continue
            }

            Write-Host "Removing package: ${packageName}" -ForegroundColor Cyan
            $removeResult = Invoke-VpmCapture -Arguments @('remove', 'package', $packageName, '-p', $ProjectPath)
            Write-VrcSetupCommandOutput -Entries $removeResult.OutputLines
            if ($removeResult.ExitCode -ne 0) {
                Write-Host "vpm reported exit code $($removeResult.ExitCode) while removing ${packageName}" -ForegroundColor Yellow
                Write-VrcSetupLog -Message "ERROR: vpm remove failed for ${packageName} with exit code $($removeResult.ExitCode)"
                $hadFailures = $true
            }
        }
    }

    foreach ($pkg in $Packages.PSObject.Properties) {
        $packageName = $pkg.Name
        $packageVersion = $pkg.Value

        if ($SyncPackages -and $syncPlan -and
            $syncPlan.Added -notcontains $packageName -and
            $syncPlan.Updated -notcontains $packageName) {
            Write-Host "Keeping unchanged package: ${packageName} @ ${packageVersion}" -ForegroundColor DarkGray
            continue
        }

        Write-Host "Processing package: ${packageName} : ${packageVersion}" -ForegroundColor Cyan

        if ($Test) {
            Write-Host "[TEST] Would add package: ${packageName}@${packageVersion}" -ForegroundColor DarkGray
            Write-VrcSetupLog -Message "[TEST] Would add package: ${packageName}@${packageVersion}"
            continue
        }

        try {
            $cmdOutput = @()
            if ($packageVersion -eq "latest") {
                Write-Host "Adding package: ${packageName} (latest)" -ForegroundColor Cyan
                $cmdResult = Invoke-VpmCapture -Arguments @('add', 'package', "${packageName}")
            } else {
                Write-Host "Adding package: ${packageName} @ ${packageVersion}" -ForegroundColor Cyan
                $cmdResult = Invoke-VpmCapture -Arguments @('add', 'package', "${packageName}@${packageVersion}")
            }
            Write-VrcSetupCommandOutput -Entries $cmdResult.OutputLines
            if ($cmdResult.ExitCode -ne 0) {
                Write-Host "vpm reported exit code $($cmdResult.ExitCode) for ${packageName}" -ForegroundColor Yellow
                Write-VrcSetupLog -Message "ERROR: vpm add failed for ${packageName} with exit code $($cmdResult.ExitCode)"
                $hadFailures = $true
            }
        } catch {
            Write-Host "Failed to add ${packageName}: ${_}" -ForegroundColor Red
            Write-VrcSetupLog -Message "ERROR: Failed to add ${packageName} : ${_}"
            $hadFailures = $true
        }
    }

    if ($Test) {
        Write-Host "[TEST] Would resolve VPM project: ${ProjectPath}" -ForegroundColor DarkGray
        Write-VrcSetupLog -Message "[TEST] Would resolve VPM project: ${ProjectPath}"
        return $script:VrcSetupStatusSuccess
    }

    # Resolve packages
    $manifestPath = Join-Path ${ProjectPath} "Packages\manifest.json"
    $resolveResult = Invoke-VpmCapture -Arguments @('resolve', 'project', "${ProjectPath}")
    Write-VrcSetupCommandOutput -Entries $resolveResult.OutputLines
    if ($resolveResult.ExitCode -ne 0) {
        Write-Host "vpm resolve reported exit code $($resolveResult.ExitCode)" -ForegroundColor Red
        Write-VrcSetupLog -Message "ERROR: vpm resolve failed for ${ProjectPath} with exit code $($resolveResult.ExitCode)"
        return $script:VrcSetupStatusFailure
    }

    $validation = Get-VpmManifestValidationResult -ProjectPath $ProjectPath -Packages $Packages
    if (-not $validation.Valid) {
        Write-Host $validation.Message -ForegroundColor Red
        Write-VrcSetupLog -Message ("ERROR: {0}" -f $validation.Message)
        return $script:VrcSetupStatusFailure
    }

    if ($SyncPackages) {
        $remainingPackages = Get-VpmProjectPackageSet -ProjectPath $ProjectPath
        $desiredNames = @($Packages.PSObject.Properties.Name)
        $unexpectedPackages = @($remainingPackages.PSObject.Properties.Name | Where-Object { $desiredNames -notcontains $_ })
        if ($unexpectedPackages.Count -gt 0) {
            $message = "Packages still present after synchronization: $($unexpectedPackages -join ', ')"
            Write-Host $message -ForegroundColor Red
            Write-VrcSetupLog -Message "ERROR: ${message}"
            return $script:VrcSetupStatusFailure
        }
    }

    if ($hadFailures) {
        Write-Host "One or more VPM package operations failed." -ForegroundColor Red
        return $script:VrcSetupStatusFailure
    }

    return $script:VrcSetupStatusSuccess

    } finally {
        Pop-Location
    }
}

function Get-UnityProcessesUsingProjectPath {
    param(
        [string]$ProjectPath
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) { return @() }

    $normalizedPath = $ProjectPath
    try {
        if (Test-Path -LiteralPath $ProjectPath) {
            $normalizedPath = (Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop).Path
        } else {
            $normalizedPath = [System.IO.Path]::GetFullPath($ProjectPath)
        }
    } catch {
        $normalizedPath = $ProjectPath
    }

    $matches = @()
    try {
        $candidates = Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            $_.Name -eq 'Unity.exe' -and
            -not [string]::IsNullOrWhiteSpace($_.CommandLine)
        }

        foreach ($proc in $candidates) {
            if ($proc.CommandLine -match [regex]::Escape($normalizedPath)) {
                $matches += [pscustomobject]@{
                    ProcessId   = [int]$proc.ProcessId
                    Name        = [string]$proc.Name
                    CommandLine = [string]$proc.CommandLine
                }
            }
        }
    } catch { }

    return @($matches | Sort-Object ProcessId -Unique)
}

function Remove-ProjectFolderWithRecovery {
    param(
        [string]$ProjectPath,
        [string]$FailurePrefix = "Failed to delete project folder",
        [bool]$AllowSkip = $false,
        [string]$SkipLabel = "Cancel"
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        return [pscustomobject]@{ Removed = $false; Skipped = $AllowSkip; Cancelled = (-not $AllowSkip) }
    }

    while ($true) {
        try {
            Remove-Item -LiteralPath $ProjectPath -Recurse -Force -ErrorAction Stop
            return [pscustomobject]@{ Removed = $true; Skipped = $false; Cancelled = $false }
        } catch {
            $deleteMessage = $_.Exception.Message
            $unityLocks = @(Get-UnityProcessesUsingProjectPath -ProjectPath $ProjectPath)

            if ($unityLocks.Count -eq 0) {
                Write-Host "${FailurePrefix}: ${deleteMessage}" -ForegroundColor Red
                if ($AllowSkip) {
                    return [pscustomobject]@{ Removed = $false; Skipped = $true; Cancelled = $false }
                }
                return [pscustomobject]@{ Removed = $false; Skipped = $false; Cancelled = $true }
            }

            $options = @()
            foreach ($proc in $unityLocks) {
                $options += ("Close Unity PID {0}" -f $proc.ProcessId)
            }
            $options += "Retry delete"
            $options += $SkipLabel

            $header = @(
                "Could not delete:",
                $ProjectPath,
                "",
                "Reason:",
                $deleteMessage,
                "",
                "Unity appears to be using this folder. Choose the PID to close, then retry."
            ) -join "`n"

            $choice = Show-Menu -Title "Project folder is in use" -Header $header -Options $options -AllowCancel $false
            if ($choice -lt 0) {
                return [pscustomobject]@{ Removed = $false; Skipped = $AllowSkip; Cancelled = (-not $AllowSkip) }
            }

            if ($choice -lt $unityLocks.Count) {
                $selectedProc = $unityLocks[$choice]
                $confirmHeader = @(
                    "Close this Unity process?",
                    "",
                    ("PID: {0}" -f $selectedProc.ProcessId),
                    ("Command: {0}" -f $selectedProc.CommandLine)
                ) -join "`n"
                $confirm = Show-Menu -Title "Confirm process close" -Header $confirmHeader -Options @("Close PID", "Back") -AllowCancel $false
                if ($confirm -eq 0) {
                    try {
                        Stop-Process -Id $selectedProc.ProcessId -Force -ErrorAction Stop
                        Write-Host ("Closed Unity PID {0}." -f $selectedProc.ProcessId) -ForegroundColor Yellow
                    } catch {
                        Write-Host ("Failed to close Unity PID {0}: {1}" -f $selectedProc.ProcessId, $_.Exception.Message) -ForegroundColor Red
                        Read-Host "Press ENTER to continue" | Out-Null
                    }
                }
                continue
            }

            if ($choice -eq $unityLocks.Count) {
                continue
            }

            if ($AllowSkip) {
                return [pscustomobject]@{ Removed = $false; Skipped = $true; Cancelled = $false }
            }

            return [pscustomobject]@{ Removed = $false; Skipped = $false; Cancelled = $true }
        }
    }
}

function Start-Installer {
    param(
        [string]$projectPath,
        [switch]$Test,
        [string]$NewProjectName,
        [switch]$OverwriteExistingProject,
        [switch]$ImportExtras,
        [string]$ExcludeUnityPackagePath,
        $PackagesOverride,
        [switch]$SyncPackages
    )

    # prepare environment
    $logDir = Join-Path $scriptDir 'logs'
    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }
    $global:VRCSETUP_LOGFILE = Join-Path $logDir "vrcsetup-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
    $configPath = Join-Path $scriptDir "config\\vrcsetup.json"
    $defaultsPath = Join-Path $scriptDir "config\\vrcsetup.defaults"
    [void](Initialize-ConfigIfMissing -ConfigPath $configPath -DefaultsPath $defaultsPath)

    # Normalize path input (drag&drop often wraps in quotes)
    if ($null -ne $projectPath) {
        $projectPath = $projectPath.Trim()
        $projectPath = $projectPath.Trim('"')
        $projectPath = $projectPath.Trim("'")
        $projectPath = $projectPath -replace '`(?=[\s&()\[\]{}$;,])', ''
    }

    # If reset requested
    if ($projectPath -eq "-reset") {
        if (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath -Force; Write-Host "Configuration reset" -ForegroundColor Green; return $script:VrcSetupStatusSuccess }
        Write-Host "No configuration to reset" -ForegroundColor Yellow
        return $script:VrcSetupStatusSuccess
    }

    # Validate projectPath exists
    if (-not $projectPath) { Write-Host "Error: project path required" -ForegroundColor Red; return $script:VrcSetupStatusFailure }
    if (-not (Test-Path -LiteralPath $projectPath)) { Write-Host "Error: path not found: ${projectPath}" -ForegroundColor Red; return $script:VrcSetupStatusFailure }

    # Load config
    $config = Load-Config -ConfigPath $configPath
    if ($config) {
        $UNITY_PROJECTS_ROOT = $config.UnityProjectsRoot
        $UNITY_EDITOR_PATH = $config.UnityEditorPath
        $VPM_PACKAGES = if ($null -ne $PackagesOverride) { $PackagesOverride } else { $config.VpmPackages }
    } else {
        Write-Host "Config missing (create via the wizard)." -ForegroundColor Red
        return $script:VrcSetupStatusFailure
    }

    # Normalize legacy formats (array -> object with versions)
    if ($VPM_PACKAGES -is [System.Array]) {
        $normalized = [ordered]@{}
        foreach ($pkg in $VPM_PACKAGES) {
            if (-not [string]::IsNullOrWhiteSpace($pkg)) {
                $normalized[$pkg] = "latest"
            }
        }
        $VPM_PACKAGES = [pscustomobject]$normalized
    }

    if (-not $VPM_PACKAGES) {
        Write-Host "Error: VpmPackages missing in config." -ForegroundColor Red
        return $script:VrcSetupStatusFailure
    }
    $VPM_PACKAGES = Add-RequiredPackagesToSet -Packages $VPM_PACKAGES -Config $config

    # Sticky overall progress (shows immediately; logs scroll below)
    $overallProgressEnabled = $true
    try {
        if ($env:VRCSETUP_PROGRESS_PLAIN -eq '1') { $overallProgressEnabled = $false }
        if ($null -eq $Host -or $null -eq $Host.UI -or $null -eq $Host.UI.RawUI) { $overallProgressEnabled = $false }
    } catch { $overallProgressEnabled = $false }

    $overallProgressActivity = "[Setup]"
    if ($overallProgressEnabled) {
        try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Starting..." } catch { $overallProgressEnabled = $false }
    }

    function Resolve-ExtraUnityPackagesFromConfig {
        param(
            $Config,
            [string]$WorkspaceRoot,
            [string]$ExcludeUnityPackagePath
        )

        $commonPackagesPath = $null
        if ($Config -and ($Config.PSObject.Properties.Name -contains 'UnityPackagesFolder')) {
            $cfgCommon = [string]$Config.UnityPackagesFolder
            if (-not [string]::IsNullOrWhiteSpace($cfgCommon)) {
                $cfgCommon = $cfgCommon.Trim().Trim('"').Trim("'")
                if ([System.IO.Path]::IsPathRooted($cfgCommon)) {
                    $commonPackagesPath = $cfgCommon
                } else {
                    $commonPackagesPath = Join-Path $WorkspaceRoot $cfgCommon
                }
            }
        }

        if (-not $commonPackagesPath) { return @() }
        if (-not (Test-Path -LiteralPath $commonPackagesPath)) { return @() }

        $excludeResolved = $null
        if (-not [string]::IsNullOrWhiteSpace($ExcludeUnityPackagePath)) {
            try { $excludeResolved = (Resolve-Path -LiteralPath $ExcludeUnityPackagePath -ErrorAction Stop).Path } catch { $excludeResolved = $ExcludeUnityPackagePath }
        }

        $extra = @()
        $commonPackages = Get-ChildItem -LiteralPath $commonPackagesPath -Filter "*.unitypackage" -ErrorAction SilentlyContinue
        foreach ($pkg in $commonPackages) {
            $pkgResolved = $pkg.FullName
            try { $pkgResolved = (Resolve-Path -LiteralPath $pkg.FullName -ErrorAction Stop).Path } catch { }

            if ($excludeResolved -and ($pkgResolved -eq $excludeResolved)) { continue }
            $extra += $pkg.FullName
        }
        return $extra
    }

    function Import-UnityPackagesSequential {
        param(
            [string]$ProjectPath,
            [string[]]$UnityPackagePaths,
            [string]$UnityEditorPath,
            [string]$OverallProgressActivity,
            [bool]$OverallProgressEnabled
        )

        if (-not $UnityPackagePaths -or $UnityPackagePaths.Count -eq 0) { return $script:VrcSetupStatusSuccess }
        if (-not $UnityEditorPath -or (-not (Test-Path -LiteralPath $UnityEditorPath))) {
            Write-Host "Error: Unity Editor not found at: ${UnityEditorPath}" -ForegroundColor Red
            return $script:VrcSetupStatusFailure
        }

        $idx = 0
        foreach ($pkg in $UnityPackagePaths) {
            $idx++
            $log = Join-Path $env:TEMP ("unity-import-extra{0:00}-{1}.log" -f $idx, (Get-Date -Format 'yyyyMMdd-HHmmss'))
            $args = @(
                "-projectPath", "`"${ProjectPath}`"",
                "-buildTarget", "StandaloneWindows64",
                "-importPackage", "`"${pkg}`"",
                "-quit",
                "-batchmode",
                "-logFile", "`"${log}`""
            )
            $p = Start-Process -FilePath $UnityEditorPath -ArgumentList $args -NoNewWindow -PassThru
            if ($OverallProgressEnabled) { try { Write-Progress -Id 1 -Activity $OverallProgressActivity -Status ("Importing UnityPackage extra ({0}/{1})..." -f $idx, $UnityPackagePaths.Count) } catch { } }
            $res = Show-ProcessProgress -Process $p -LogFile $log -Prefix ("[Import:extra {0}/{1}]" -f $idx, $UnityPackagePaths.Count) -AllowCancel -CancelPrompt "Cancel this import step only? (y/N)" -ProgressId 2 -ParentProgressId 1
            if ($res -and $res.Cancelled) { return $script:VrcSetupStatusCancelled }
        }
        return $script:VrcSetupStatusSuccess
    }

    # UnityPackage mode: create a new project, import package(s), then continue install on the new project
    if ($projectPath -like "*.unitypackage") {
        Write-Host "Detected UnityPackage: creating new project..." -ForegroundColor Cyan

        $packageName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
        $projectName = if (-not [string]::IsNullOrWhiteSpace($NewProjectName)) { $NewProjectName } else { $packageName }

        $projectNameCheck = Test-VrcSetupProjectName -Name $projectName
        if (-not $projectNameCheck.Valid) {
            Write-Host ("Error: invalid project name. {0}" -f $projectNameCheck.Message) -ForegroundColor Red
            return $script:VrcSetupStatusFailure
        }

        if ($overallProgressEnabled) {
            $overallProgressActivity = "[Setup] ${projectName}"
            try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status ("UnityPackage: {0}" -f ([System.IO.Path]::GetFileName($projectPath))) } catch { }
        }
        if (-not $UNITY_PROJECTS_ROOT) {
            Write-Host "Error: UnityProjectsRoot is missing in config." -ForegroundColor Red
            return $script:VrcSetupStatusFailure
        }

        $newProjectPath = Join-Path $UNITY_PROJECTS_ROOT $projectName
        if (Test-Path -LiteralPath $newProjectPath) {
            if ($OverwriteExistingProject) {
                Write-Host "Project already exists, deleting (overwrite enabled): ${newProjectPath}" -ForegroundColor Yellow
                $deleteResult = Remove-ProjectFolderWithRecovery -ProjectPath $newProjectPath -FailurePrefix "Error: failed to delete existing project" -AllowSkip:$false -SkipLabel "Cancel setup"
                if (-not $deleteResult.Removed) {
                    return $(if ($deleteResult.Cancelled) { $script:VrcSetupStatusCancelled } else { $script:VrcSetupStatusFailure })
                }
            } else {
                Write-Host "Error: project already exists at: ${newProjectPath}" -ForegroundColor Red
                return $script:VrcSetupStatusFailure
            }
        }

        if (-not $UNITY_EDITOR_PATH -or (-not (Test-Path -LiteralPath $UNITY_EDITOR_PATH))) {
            Write-Host "Error: Unity Editor not found at: ${UNITY_EDITOR_PATH}" -ForegroundColor Red
            return $script:VrcSetupStatusFailure
        }

        if ($Test) {
            Write-Host "[TEST] Would create Unity project: ${newProjectPath}" -ForegroundColor DarkGray
            Write-Host "[TEST] Would import UnityPackage: ${projectPath}" -ForegroundColor DarkGray
            Write-Host "[TEST] Would then install configured VPM packages into: ${newProjectPath}" -ForegroundColor DarkGray
            return $script:VrcSetupStatusSuccess
        } else {
            Write-Host "Creating project: ${projectName}" -ForegroundColor Green
            Write-Host "Path: ${newProjectPath}" -ForegroundColor Gray

            $onCancelDeleteProject = {
                try {
                    if (Test-Path -LiteralPath $newProjectPath) {
                        Write-Host "Cancelling: deleting created project folder..." -ForegroundColor Yellow
                        Remove-Item -LiteralPath $newProjectPath -Recurse -Force -ErrorAction SilentlyContinue
                        Write-Host "Deleted: ${newProjectPath}" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Host "Warning: failed to delete project folder: ${_}" -ForegroundColor Yellow
                }
            }

            $createLogFile = Join-Path $env:TEMP "unity-create-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
            $createArgs = "-createProject `"${newProjectPath}`" -buildTarget StandaloneWindows64 -quit -batchmode -logFile `"${createLogFile}`""
            $createProcess = Start-Process -FilePath $UNITY_EDITOR_PATH -ArgumentList $createArgs -NoNewWindow -PassThru
            if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Creating Unity project..." } catch { } }
            $createRes = Show-ProcessProgress -Process $createProcess -LogFile $createLogFile -Prefix "[Unity]" -AllowCancel -OnCancel $onCancelDeleteProject -CancelPrompt "Cancel and delete the created project folder? (y/N)" -ProgressId 2 -ParentProgressId 1
            if ($createRes -and $createRes.Cancelled) { return $script:VrcSetupStatusCancelled }

            if (-not (Test-Path -LiteralPath (Join-Path $newProjectPath "Assets"))) {
                Write-Host "Error: project was not created correctly." -ForegroundColor Red
                if (Test-Path -LiteralPath $createLogFile) {
                    Write-Host "Last log lines:" -ForegroundColor Yellow
                    Get-Content -LiteralPath $createLogFile -Tail 20
                }
                Remove-Item -LiteralPath $newProjectPath -Recurse -Force -ErrorAction SilentlyContinue
                return $script:VrcSetupStatusFailure
            }

            # Create state marker for cleanup (incomplete projects)
            try {
                Initialize-VrcSetupProjectState -ProjectPath $newProjectPath -UnityPackagePath $projectPath -ProjectName $projectName | Out-Null
            } catch { }

            # 1) Ensure Unity Test Framework + any required manifest tweaks BEFORE importing the big UnityPackage.
            if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Applying Unity Test Framework (NUnit)..." } catch { } }
            Install-NUnitPackage -ProjectPath $newProjectPath -Test:$Test
            try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'nunit' -Done $true } catch { }

            # 2) Install configured VPM packages BEFORE importing the UnityPackage(s).
            # This usually reduces re-import work when the GUI opens (SDK + dependencies already present).
            if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Installing VPM packages..." } catch { } }
            $vpmStatus = Install-PackagesInProject -ProjectPath $newProjectPath -Packages $VPM_PACKAGES -Test:$Test
            if ($vpmStatus -ne $script:VrcSetupStatusSuccess) {
                try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'vpm' -Done $false } catch { }
                return $vpmStatus
            }
            try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'vpm' -Done $true } catch { }

            $packagesToImport = @($projectPath)

            $mainPackageResolved = $projectPath
            try { $mainPackageResolved = (Resolve-Path -LiteralPath $projectPath -ErrorAction Stop).Path } catch { }

            $workspaceRoot = [System.IO.Directory]::GetParent($scriptDir).FullName
            ${commonPackagesPath} = $null
            if ($config -and ($config.PSObject.Properties.Name -contains 'UnityPackagesFolder')) {
                $cfgCommon = [string]$config.UnityPackagesFolder
                if (-not [string]::IsNullOrWhiteSpace($cfgCommon)) {
                    $cfgCommon = $cfgCommon.Trim().Trim('"').Trim("'")
                    if ([System.IO.Path]::IsPathRooted($cfgCommon)) {
                        ${commonPackagesPath} = $cfgCommon
                    } else {
                        ${commonPackagesPath} = Join-Path $workspaceRoot $cfgCommon
                    }
                }
            }

            if (${commonPackagesPath} -and (Test-Path -LiteralPath ${commonPackagesPath})) {
                $commonPackages = Get-ChildItem -LiteralPath ${commonPackagesPath} -Filter "*.unitypackage" -ErrorAction SilentlyContinue
                foreach ($pkg in $commonPackages) {
                    $pkgResolved = $pkg.FullName
                    try { $pkgResolved = (Resolve-Path -LiteralPath $pkg.FullName -ErrorAction Stop).Path } catch { }
                    if ($pkgResolved -ne $mainPackageResolved) {
                        $packagesToImport += $pkg.FullName
                    }
                }
            }

            # 3) Import UnityPackage(s) at the end.
            # Importing multiple packages in one Unity invocation can be flaky; do it sequentially to guarantee order.
            $extraPackages = @()
            if ($packagesToImport.Count -gt 1) {
                $extraPackages = @($packagesToImport | Select-Object -Skip 1)
            }

            Write-Host ("Importing UnityPackage(s)... (main=1, extra={0})" -f $extraPackages.Count) -ForegroundColor Cyan

            # Main package first
            $importLogFile = Join-Path $env:TEMP "unity-import-main-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
            $importArgs = @(
                "-projectPath", "`"${newProjectPath}`"",
                "-buildTarget", "StandaloneWindows64",
                "-importPackage", "`"$($packagesToImport[0])`"",
                "-quit",
                "-batchmode",
                "-logFile", "`"${importLogFile}`""
            )
            $importProcess = Start-Process -FilePath $UNITY_EDITOR_PATH -ArgumentList $importArgs -NoNewWindow -PassThru
            if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Importing UnityPackage (main)..." } catch { } }
            $importRes = Show-ProcessProgress -Process $importProcess -LogFile $importLogFile -Prefix "[Import:main]" -AllowCancel -OnCancel $onCancelDeleteProject -CancelPrompt "Cancel and delete the created project folder? (y/N)" -ProgressId 2 -ParentProgressId 1
            if ($importRes -and $importRes.Cancelled) { return $script:VrcSetupStatusCancelled }
            try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'importMain' -Done $true } catch { }

            # Extra packages (if any) AFTER main
            $idx = 0
            foreach ($pkg in $extraPackages) {
                $idx++
                $extraLog = Join-Path $env:TEMP ("unity-import-extra{0:00}-{1}.log" -f $idx, (Get-Date -Format 'yyyyMMdd-HHmmss'))
                $extraArgs = @(
                    "-projectPath", "`"${newProjectPath}`"",
                    "-buildTarget", "StandaloneWindows64",
                    "-importPackage", "`"${pkg}`"",
                    "-quit",
                    "-batchmode",
                    "-logFile", "`"${extraLog}`""
                )
                $p = Start-Process -FilePath $UNITY_EDITOR_PATH -ArgumentList $extraArgs -NoNewWindow -PassThru
                if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status ("Importing UnityPackage (extra {0}/{1})..." -f $idx, $extraPackages.Count) } catch { } }
                $extraRes = Show-ProcessProgress -Process $p -LogFile $extraLog -Prefix ("[Import:extra {0}/{1}]" -f $idx, $extraPackages.Count) -AllowCancel -OnCancel $onCancelDeleteProject -CancelPrompt "Cancel and delete the created project folder? (y/N)" -ProgressId 2 -ParentProgressId 1
                if ($extraRes -and $extraRes.Cancelled) { return $script:VrcSetupStatusCancelled }
            }

            try {
                # If there are no extras, consider this step done.
                Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'importExtras' -Done $true
            } catch { }

            # 4) Post-import settle pass (bounded) to let Unity finish asset pipeline work.
            # This helps avoid a full re-import/crunch pass when opening the GUI right after.
            try {
                $editorDir = Join-Path $newProjectPath "Assets\\Editor"
                if (-not (Test-Path -LiteralPath $editorDir)) { New-Item -Path $editorDir -ItemType Directory -Force | Out-Null }

                $postImportScriptPath = Join-Path $editorDir "VrcSetupPostImport.cs"
                @'
using System;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public static class VrcSetupPostImport
{
    private static bool IsCompilationPipelineCompiling()
    {
        try
        {
            var t = typeof(CompilationPipeline);
            var p = t.GetProperty("isCompiling") ?? t.GetProperty("IsCompiling");
            if (p != null && p.PropertyType == typeof(bool))
            {
                return (bool)p.GetValue(null, null);
            }
            var f = t.GetField("isCompiling") ?? t.GetField("IsCompiling");
            if (f != null && f.FieldType == typeof(bool))
            {
                return (bool)f.GetValue(null);
            }
        }
        catch { }
        return false;
    }

    // Called via -executeMethod VrcSetupPostImport.Run
    public static void Run()
    {
        var start = DateTime.UtcNow;
        var timeout = TimeSpan.FromMinutes(10);

        Debug.Log("[vrc-setup] Post-import settle started...");

        // Force a synchronous import pass so the first UI open is less likely to trigger a second big import.
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch
        {
            AssetDatabase.Refresh();
        }

        // Block until Unity is stable (batchmode + -executeMethod can exit early if we rely on update callbacks).
        while (EditorApplication.isCompiling || EditorApplication.isUpdating || IsCompilationPipelineCompiling())
        {
            Thread.Sleep(200);
            if (DateTime.UtcNow - start > timeout)
            {
                Debug.LogWarning("[vrc-setup] Post-import settle TIMEOUT, continuing anyway.");
                break;
            }
        }

        // Second synchronous refresh to consolidate any queued imports.
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        catch
        {
            AssetDatabase.Refresh();
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[vrc-setup] Post-import settle complete, quitting.");
        EditorApplication.Exit(0);
    }
}
'@ | Set-Content -LiteralPath $postImportScriptPath -Encoding UTF8

                $settleLogFile = Join-Path $env:TEMP "unity-postimport-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
                $settleArgs = @(
                    "-projectPath", "`"${newProjectPath}`"",
                    "-buildTarget", "StandaloneWindows64",
                    "-executeMethod", "VrcSetupPostImport.Run",
                    "-batchmode",
                    "-logFile", "`"${settleLogFile}`""
                )

                Write-Host "Finalizing import (post-import settle)..." -ForegroundColor Cyan
                $settleProcess = Start-Process -FilePath $UNITY_EDITOR_PATH -ArgumentList $settleArgs -NoNewWindow -PassThru
                if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Finalizing (settle/flush)..." } catch { } }
                $settleRes = Show-ProcessProgress -Process $settleProcess -LogFile $settleLogFile -Prefix "[Finalize]" -AllowCancel -OnCancel $onCancelDeleteProject -CancelPrompt "Cancel and delete the created project folder? (y/N)" -ProgressId 2 -ParentProgressId 1
                if ($settleRes -and $settleRes.Cancelled) { return $script:VrcSetupStatusCancelled }
                try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'finalize' -Done $true } catch { }
            } catch {
                Write-Host "Warning: post-import finalize step failed: ${_}" -ForegroundColor Yellow
            }

            $postImportVpmStatus = Ensure-VpmPackagesPresentAfterImport -ProjectPath $newProjectPath -Packages $VPM_PACKAGES -Test:$Test -PhaseLabel "after UnityPackage import"
            if ($postImportVpmStatus -ne $script:VrcSetupStatusSuccess) {
                try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'vpm' -Done $false } catch { }
                return $postImportVpmStatus
            }
            try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'vpm' -Done $true } catch { }

            # Mark completed only if steps are all done (avoids false positives).
            try { Complete-VrcSetupProjectState -ProjectPath $newProjectPath } catch { }

            if ($overallProgressEnabled) {
                try { Write-Progress -Id 1 -Activity $overallProgressActivity -Completed } catch { }
            }

            $projectPath = $newProjectPath

            # UnityPackage flow already:
            # - ensured test framework
            # - installed configured VPM packages
            # - imported main + extra unitypackages
            # - ran post-import finalize
            # Don't run the generic "install packages in existing project" step again.
            return $script:VrcSetupStatusSuccess
        }
    }

    # If not a Unity package, assume existing project and install packages
    $assetsPath = Join-Path $projectPath "Assets"
    $packagesPath = Join-Path $projectPath "Packages"
    if ((Test-Path -LiteralPath $assetsPath) -or (Test-Path -LiteralPath $packagesPath)) {
        if ($overallProgressEnabled) {
            $leaf = $null
            try { $leaf = Split-Path -Leaf $projectPath } catch { $leaf = $null }
            if (-not [string]::IsNullOrWhiteSpace($leaf)) {
                $overallProgressActivity = "[Setup] ${leaf}"
                try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Preparing..." } catch { }
            }
        }
        if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Installing VPM packages..." } catch { } }
        $vpmStatus = Install-PackagesInProject -ProjectPath $projectPath -Packages $VPM_PACKAGES -Test:$Test -SyncPackages:$SyncPackages
        if ($vpmStatus -ne $script:VrcSetupStatusSuccess) { return $vpmStatus }

        if ($ImportExtras) {
            $workspaceRoot = [System.IO.Directory]::GetParent($scriptDir).FullName
            $extraPkgs = Resolve-ExtraUnityPackagesFromConfig -Config $config -WorkspaceRoot $workspaceRoot -ExcludeUnityPackagePath $ExcludeUnityPackagePath
            if (-not $extraPkgs -or $extraPkgs.Count -eq 0) {
                Write-Host "No extra UnityPackages configured/found to import." -ForegroundColor Yellow
            } else {
                Write-Host ("Importing extra UnityPackages from config... count={0}" -f $extraPkgs.Count) -ForegroundColor Cyan
                $impRes = Import-UnityPackagesSequential -ProjectPath $projectPath -UnityPackagePaths $extraPkgs -UnityEditorPath $UNITY_EDITOR_PATH -OverallProgressActivity $overallProgressActivity -OverallProgressEnabled $overallProgressEnabled
                if ($impRes -ne $script:VrcSetupStatusSuccess) { return $impRes }

                $postImportVpmStatus = Ensure-VpmPackagesPresentAfterImport -ProjectPath $projectPath -Packages $VPM_PACKAGES -Test:$Test -PhaseLabel "after extra UnityPackage import"
                if ($postImportVpmStatus -ne $script:VrcSetupStatusSuccess) { return $postImportVpmStatus }
            }
        }

        if ($overallProgressEnabled) {
            try { Write-Progress -Id 1 -Activity $overallProgressActivity -Completed } catch { }
        }
        return $script:VrcSetupStatusSuccess
    }

    Write-Host "Error: path is not a Unity project (missing Assets/Packages): ${projectPath}" -ForegroundColor Red
    return $script:VrcSetupStatusFailure

    # If we reach here nothing else to do
    return $script:VrcSetupStatusSuccess
}


