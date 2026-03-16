param()

$script:VrcSetupStatusSuccess = 0
$script:VrcSetupStatusFailure = 1
$script:VrcSetupStatusCancelled = 2

# commands/installer.ps1 - central installer logic exported as a function
$cmdDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$scriptDir = (Resolve-Path (Join-Path $cmdDir '..')).Path
. "$scriptDir\lib\menu.ps1"
. "$scriptDir\lib\utils.ps1"
. "$scriptDir\lib\progress.ps1"
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

function Install-PackagesInProject {
    param(
        [string]$ProjectPath,
        $Packages,
        [switch]$Test
    )

    Push-Location $ProjectPath
    try {

    # Backup manifest once before any changes (keeps logs in a sane order)
    $manifestPath = Join-Path $ProjectPath "Packages\manifest.json"
    if ((-not $Test) -and (Test-Path $manifestPath)) {
        try {
            $backupPath = "${manifestPath}.bak.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Copy-Item $manifestPath -Destination $backupPath -Force
            Write-Host "Backup manifest created: ${backupPath}" -ForegroundColor Gray
        } catch {
            Write-Host "Failed to create manifest backup: ${_}" -ForegroundColor Yellow
        }
    }

    foreach ($pkg in $Packages.PSObject.Properties) {
        $packageName = $pkg.Name
        $packageVersion = $pkg.Value

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
                $cmdOutput = @(vpm add package "${packageName}" 2>&1)
            } else {
                Write-Host "Adding package: ${packageName} @ ${packageVersion}" -ForegroundColor Cyan
                $cmdOutput = @(vpm add package "${packageName}@${packageVersion}" 2>&1)
            }
            Write-VrcSetupCommandOutput -Entries $cmdOutput
            if ($LASTEXITCODE -ne 0) { Write-Host "vpm reported exit code ${LASTEXITCODE} for ${packageName}" -ForegroundColor Yellow }
        } catch {
            Write-Host "Failed to add ${packageName}: ${_}" -ForegroundColor Red
            Write-VrcSetupLog -Message "ERROR: Failed to add ${packageName} : ${_}"
        }
    }

    if ($Test) {
        Write-Host "[TEST] Would resolve VPM project: ${ProjectPath}" -ForegroundColor DarkGray
        Write-VrcSetupLog -Message "[TEST] Would resolve VPM project: ${ProjectPath}"
        return $script:VrcSetupStatusSuccess
    }

    # Resolve packages
    $manifestPath = Join-Path ${ProjectPath} "Packages\manifest.json"
    $resolveOutput = @(vpm resolve project ${ProjectPath} 2>&1)
    Write-VrcSetupCommandOutput -Entries $resolveOutput

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
        if (Test-Path $ProjectPath) {
            $normalizedPath = (Resolve-Path $ProjectPath -ErrorAction Stop).Path
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
            Remove-Item -Path $ProjectPath -Recurse -Force -ErrorAction Stop
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
        [string]$ExcludeUnityPackagePath
    )

    # prepare environment
    $logDir = Join-Path $scriptDir 'logs'
    if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }
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
        if (Test-Path $configPath) { Remove-Item $configPath -Force; Write-Host "Configuration reset" -ForegroundColor Green; return $script:VrcSetupStatusSuccess }
        Write-Host "No configuration to reset" -ForegroundColor Yellow
        return $script:VrcSetupStatusSuccess
    }

    # Validate projectPath exists
    if (-not $projectPath) { Write-Host "Error: project path required" -ForegroundColor Red; return $script:VrcSetupStatusFailure }
    if (-not (Test-Path $projectPath)) { Write-Host "Error: path not found: ${projectPath}" -ForegroundColor Red; return $script:VrcSetupStatusFailure }

    # Load config
    $config = Load-Config -ConfigPath $configPath
    if ($config) {
        $UNITY_PROJECTS_ROOT = $config.UnityProjectsRoot
        $UNITY_EDITOR_PATH = $config.UnityEditorPath
        $VPM_PACKAGES = $config.VpmPackages
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
        if (-not (Test-Path $commonPackagesPath)) { return @() }

        $excludeResolved = $null
        if (-not [string]::IsNullOrWhiteSpace($ExcludeUnityPackagePath)) {
            try { $excludeResolved = (Resolve-Path $ExcludeUnityPackagePath -ErrorAction Stop).Path } catch { $excludeResolved = $ExcludeUnityPackagePath }
        }

        $extra = @()
        $commonPackages = Get-ChildItem -Path $commonPackagesPath -Filter "*.unitypackage" -ErrorAction SilentlyContinue
        foreach ($pkg in $commonPackages) {
            $pkgResolved = $pkg.FullName
            try { $pkgResolved = (Resolve-Path $pkg.FullName -ErrorAction Stop).Path } catch { }

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
        if (-not $UnityEditorPath -or (-not (Test-Path $UnityEditorPath))) {
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

        if ($overallProgressEnabled) {
            $overallProgressActivity = "[Setup] ${projectName}"
            try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status ("UnityPackage: {0}" -f ([System.IO.Path]::GetFileName($projectPath))) } catch { }
        }
        if (-not $UNITY_PROJECTS_ROOT) {
            Write-Host "Error: UnityProjectsRoot is missing in config." -ForegroundColor Red
            return $script:VrcSetupStatusFailure
        }

        $newProjectPath = Join-Path $UNITY_PROJECTS_ROOT $projectName
        if (Test-Path $newProjectPath) {
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

        if (-not $UNITY_EDITOR_PATH -or (-not (Test-Path $UNITY_EDITOR_PATH))) {
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
                    if (Test-Path $newProjectPath) {
                        Write-Host "Cancelling: deleting created project folder..." -ForegroundColor Yellow
                        Remove-Item -Path $newProjectPath -Recurse -Force -ErrorAction SilentlyContinue
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

            if (-not (Test-Path (Join-Path $newProjectPath "Assets"))) {
                Write-Host "Error: project was not created correctly." -ForegroundColor Red
                if (Test-Path $createLogFile) {
                    Write-Host "Last log lines:" -ForegroundColor Yellow
                    Get-Content $createLogFile -Tail 20
                }
                Remove-Item -Path $newProjectPath -Recurse -Force -ErrorAction SilentlyContinue
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
            Install-PackagesInProject -ProjectPath $newProjectPath -Packages $VPM_PACKAGES -Test:$Test | Out-Null
            try { Set-VrcSetupProjectStep -ProjectPath $newProjectPath -Step 'vpm' -Done $true } catch { }

            $packagesToImport = @($projectPath)

            $mainPackageResolved = $projectPath
            try { $mainPackageResolved = (Resolve-Path $projectPath -ErrorAction Stop).Path } catch { }

            $workspaceRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
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

            if (${commonPackagesPath} -and (Test-Path ${commonPackagesPath})) {
                $commonPackages = Get-ChildItem -Path ${commonPackagesPath} -Filter "*.unitypackage" -ErrorAction SilentlyContinue
                foreach ($pkg in $commonPackages) {
                    $pkgResolved = $pkg.FullName
                    try { $pkgResolved = (Resolve-Path $pkg.FullName -ErrorAction Stop).Path } catch { }
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
                if (-not (Test-Path $editorDir)) { New-Item -Path $editorDir -ItemType Directory -Force | Out-Null }

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
'@ | Set-Content -Path $postImportScriptPath -Encoding UTF8

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
    if ((Test-Path $assetsPath) -or (Test-Path $packagesPath)) {
        if ($overallProgressEnabled) {
            $leaf = $null
            try { $leaf = Split-Path -Leaf $projectPath } catch { $leaf = $null }
            if (-not [string]::IsNullOrWhiteSpace($leaf)) {
                $overallProgressActivity = "[Setup] ${leaf}"
                try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Preparing..." } catch { }
            }
        }
        if ($overallProgressEnabled) { try { Write-Progress -Id 1 -Activity $overallProgressActivity -Status "Installing VPM packages..." } catch { } }
        Install-PackagesInProject -ProjectPath $projectPath -Packages $VPM_PACKAGES -Test:$Test | Out-Null

        if ($ImportExtras) {
            $workspaceRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
            $extraPkgs = Resolve-ExtraUnityPackagesFromConfig -Config $config -WorkspaceRoot $workspaceRoot -ExcludeUnityPackagePath $ExcludeUnityPackagePath
            if (-not $extraPkgs -or $extraPkgs.Count -eq 0) {
                Write-Host "No extra UnityPackages configured/found to import." -ForegroundColor Yellow
            } else {
                Write-Host ("Importing extra UnityPackages from config... count={0}" -f $extraPkgs.Count) -ForegroundColor Cyan
                $impRes = Import-UnityPackagesSequential -ProjectPath $projectPath -UnityPackagePaths $extraPkgs -UnityEditorPath $UNITY_EDITOR_PATH -OverallProgressActivity $overallProgressActivity -OverallProgressEnabled $overallProgressEnabled
                if ($impRes -ne $script:VrcSetupStatusSuccess) { return $impRes }
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


