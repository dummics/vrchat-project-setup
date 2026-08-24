# VRChat Setup Wizard (commands/wizard.ps1) - modularized
# This file is a drop-in for vrcsetup-wizard.ps1. It contains the full wizard logic.

param()

# === CARICAMENTO CONFIG & HELPERS ===
$cmdDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$scriptDir = (Resolve-Path (Join-Path $cmdDir '..')).Path
. "${scriptDir}\lib\menu.ps1"
. "${scriptDir}\lib\utils.ps1"
. "${scriptDir}\lib\config.ps1"
. "${scriptDir}\lib\vpm.ps1"
. "${scriptDir}\lib\vrcget.ps1"
. "${scriptDir}\lib\project-state.ps1"
. "${scriptDir}\commands\installer.ps1"
$configPath = Join-Path $scriptDir "config\\vrcsetup.json"
$defaultsPath = Join-Path $scriptDir "config\\vrcsetup.defaults"
[void](Initialize-ConfigIfMissing -ConfigPath $configPath -DefaultsPath $defaultsPath)

# Main installer function is provided by commands\installer.ps1 (Start-Installer)

# --- FUNCTIONS ---
# Backend helpers live in lib\vpm.ps1 and lib\vrcget.ps1

function Invoke-FirstRunSetup {
    param([string]$ConfigPath)

    $config = $null
    if (Test-Path $ConfigPath) { $config = Load-Config -ConfigPath $ConfigPath }
    if (-not $config) { return }

    if (Test-ConfigEssentials -Config $config) { return }

    # Helper: redraws the setup screen showing current state of both fields
    function Show-SetupScreen {
        param(
            [string]$EditorValue,
            [string]$ProjectsRootValue,
            [string]$ActiveStep,       # "editor", "projects", "done"
            [string]$StatusMessage
        )
        Clear-Host
        Write-Host "=== First-time setup ===" -ForegroundColor Cyan
        Write-Host ""

        # --- Editor line ---
        if (-not [string]::IsNullOrWhiteSpace($EditorValue)) {
            Write-Host "  Unity Editor:   " -ForegroundColor Gray -NoNewline
            Write-Host $EditorValue -ForegroundColor Green
        }
        elseif ($ActiveStep -eq "editor") {
            Write-Host "  Unity Editor:   " -ForegroundColor Gray -NoNewline
            Write-Host "(selecting...)" -ForegroundColor DarkYellow
        }
        else {
            Write-Host "  Unity Editor:   " -ForegroundColor Gray -NoNewline
            Write-Host "(skipped)" -ForegroundColor DarkGray
        }

        # --- Projects Root line ---
        if (-not [string]::IsNullOrWhiteSpace($ProjectsRootValue)) {
            Write-Host "  Projects root:  " -ForegroundColor Gray -NoNewline
            Write-Host $ProjectsRootValue -ForegroundColor Green
        }
        elseif ($ActiveStep -eq "projects") {
            Write-Host "  Projects root:  " -ForegroundColor Gray -NoNewline
            Write-Host "(waiting for input...)" -ForegroundColor DarkYellow
        }
        else {
            Write-Host "  Projects root:  " -ForegroundColor Gray -NoNewline
            Write-Host "(not yet)" -ForegroundColor DarkGray
        }

        Write-Host ""
        if ($StatusMessage) {
            Write-Host $StatusMessage -ForegroundColor Yellow
            Write-Host ""
        }
    }

    $editorPath = [string]$config.UnityEditorPath
    $projectsRoot = [string]$config.UnityProjectsRoot
    $needEditor = [string]::IsNullOrWhiteSpace($editorPath) -or -not (Test-Path $editorPath)
    $needProjects = [string]::IsNullOrWhiteSpace($projectsRoot)

    # ===== STEP 1: Unity Editor Path =====
    if ($needEditor) {
        $editorPath = ""
        $found = @((Find-UnityEditorPaths))

        if ($found.Count -gt 0) {
            $editorOptions = @()
            foreach ($f in $found) {
                $editorOptions += "$($f.Version) - $($f.Path)"
            }
            $editorOptions += "Enter path manually"
            $editorOptions += "Skip for now"

            $pick = Show-Menu -Title "First-time setup" -Header "Step 1/2: Select your Unity Editor" -Options $editorOptions
            if ($pick -ge 0 -and $pick -lt $found.Count) {
                $editorPath = $found[$pick].Path
            }
            elseif ($pick -eq $found.Count) {
                Show-SetupScreen -EditorValue "" -ProjectsRootValue "" -ActiveStep "editor" -StatusMessage "Drag Unity.exe here or paste the full path:"
                $manualPath = Normalize-UserPath (Read-Host "  Unity.exe path")
                $manualCheck = Test-UnityEditorPath -Path $manualPath
                if ($manualCheck.Valid) {
                    $editorPath = $manualPath
                } else {
                    Show-SetupScreen -EditorValue "" -ProjectsRootValue "" -ActiveStep "editor" -StatusMessage $manualCheck.Message
                    Start-Sleep -Seconds 2
                }
            }
            # else: skip
        }
        else {
            Show-SetupScreen -EditorValue "" -ProjectsRootValue "" -ActiveStep "editor" -StatusMessage "No Unity installations found. Drag Unity.exe here or paste the path (ENTER to skip):"
            $manualPath = Normalize-UserPath (Read-Host "  Unity.exe path")
            if (-not [string]::IsNullOrWhiteSpace($manualPath)) {
                $manualCheck = Test-UnityEditorPath -Path $manualPath
                if ($manualCheck.Valid) {
                    $editorPath = $manualPath
                } else {
                    Show-SetupScreen -EditorValue "" -ProjectsRootValue "" -ActiveStep "editor" -StatusMessage $manualCheck.Message
                    Start-Sleep -Seconds 2
                }
            }
        }

        # Save editor if valid
        $editorValidation = Test-UnityEditorPath -Path $editorPath
        if ($editorValidation.Valid) {
            $config | Add-Member -MemberType NoteProperty -Name "UnityEditorPath" -Value $editorPath -Force
            Save-Config -Config $config -ConfigPath $ConfigPath
        } else {
            $editorPath = ""
        }
    }

    # ===== STEP 2: Projects Root =====
    if ($needProjects) {
        $projectsRoot = ""
        Show-SetupScreen -EditorValue $editorPath -ProjectsRootValue "" -ActiveStep "projects" -StatusMessage "Step 2/2: Where should new projects be created?`nDrag a folder here or paste the path (ENTER to skip):"
        $inputRoot = Normalize-UserPath (Read-Host "  Projects folder")

        if (-not [string]::IsNullOrWhiteSpace($inputRoot)) {
            if (-not (Test-Path $inputRoot)) {
                $mkChoice = Show-Menu -Title "Folder not found" -Header "Create folder?`n${inputRoot}" -Options @("Create it", "Skip")
                if ($mkChoice -eq 0) {
                    try {
                        New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null
                    } catch {
                        Show-SetupScreen -EditorValue $editorPath -ProjectsRootValue "" -ActiveStep "projects" -StatusMessage "Failed to create folder."
                        Start-Sleep -Seconds 2
                        $inputRoot = ""
                    }
                    if (-not [string]::IsNullOrWhiteSpace($inputRoot) -and -not (Test-Path $inputRoot)) {
                        $inputRoot = ""
                    }
                }
                else {
                    $inputRoot = ""
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($inputRoot)) {
                $projectsRoot = $inputRoot
                $config | Add-Member -MemberType NoteProperty -Name "UnityProjectsRoot" -Value $projectsRoot -Force
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
        }
    }

    # ===== FINAL SCREEN =====
    Show-SetupScreen -EditorValue $editorPath -ProjectsRootValue $projectsRoot -ActiveStep "done"
    if (Test-ConfigEssentials -Config $config) {
        Write-Host "Setup complete! You're ready to create projects." -ForegroundColor Green
    }
    else {
        Write-Host "Some settings are missing. You can configure them in 'Advanced settings'." -ForegroundColor DarkGray
    }
    Write-Host ""
    Read-Host "Press ENTER to continue"
}

function Get-LastTextLines {
    param(
        [string]$Text,
        [int]$MaxLines = 20
    )
    if ([string]::IsNullOrWhiteSpace($Text)) { return "" }
    $lines = $Text -split "`r?`n"
    return ($lines | Select-Object -Last $MaxLines) -join "`n"
}

function Show-WizardError {
    param(
        [string]$Title,
        [string]$Message,
        [string]$Details
    )
    Clear-Host
    Write-Host $Title -ForegroundColor Red
    Write-Host "" 
    if ($Message) { Write-Host $Message -ForegroundColor Yellow }
    if ($Details) {
        Write-Host "" 
        Write-Host "Details (last lines):" -ForegroundColor DarkGray
        Write-Host $Details -ForegroundColor Gray
    }
    Write-Host "" 
    Read-Host "Press ENTER to continue" | Out-Null
}

function Normalize-UserPath {
    param([string]$Path)
    if ($null -eq $Path) { return $null }
    $p = $Path.Trim()
    $p = $p.Trim('"')
    $p = $p.Trim("'")
    # PowerShell drag&drop can escape spaces and shell metacharacters
    # (for example "` " or "`&") in some hosts.
    # Remove those escape markers so Test-Path sees the real filesystem path.
    $p = $p -replace '`(?=[\s&()\[\]{}$;,])', ''
    return $p
}

function Read-WizardPathInput {
    param(
        [string]$Title,
        [string[]]$BodyLines = @(),
        [string]$Prompt = "Path",
        [ConsoleColor]$TitleColor = [ConsoleColor]::Cyan,
        [string]$Hint = "Drag & drop works. Press ENTER to go back."
    )

    Clear-Host
    if (-not [string]::IsNullOrWhiteSpace($Title)) {
        Write-Host $Title -ForegroundColor $TitleColor
        Write-Host ""
    }

    foreach ($line in @($BodyLines)) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            Write-Host $line -ForegroundColor Gray
        } else {
            Write-Host ""
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($Hint)) {
        Write-Host ""
        Write-Host $Hint -ForegroundColor DarkGray
    }

    return Normalize-UserPath (Read-Host $Prompt)
}

function Add-ConfirmHint {
    param(
        [string]$Header,
        [string]$Hint = "Use arrows + Enter. ESC is disabled on this screen."
    )

    if ([string]::IsNullOrWhiteSpace($Header)) { return $Hint }
    return "${Header}`n`n${Hint}"
}

function Ensure-ConfigDefaults {
    param($Config)
    if (-not $Config) { return $null }

    if (-not $Config.Naming) {
        $Config | Add-Member -MemberType NoteProperty -Name "Naming" -Value ([pscustomobject]@{}) -Force
    }

    if ($null -eq $Config.Naming.DefaultPrefix) { $Config.Naming | Add-Member -MemberType NoteProperty -Name "DefaultPrefix" -Value "" -Force }
    if ($null -eq $Config.Naming.DefaultSuffix) { $Config.Naming | Add-Member -MemberType NoteProperty -Name "DefaultSuffix" -Value "" -Force }
    if ($null -eq $Config.Naming.RegexRemovePatterns) { $Config.Naming | Add-Member -MemberType NoteProperty -Name "RegexRemovePatterns" -Value @() -Force }
    if ($null -eq $Config.Naming.RememberUnityPackageNames) { $Config.Naming | Add-Member -MemberType NoteProperty -Name "RememberUnityPackageNames" -Value $true -Force }

    if (-not $Config.SavedProjectNames) {
        $Config | Add-Member -MemberType NoteProperty -Name "SavedProjectNames" -Value ([pscustomobject]@{}) -Force
    }

    if ($null -eq $Config.UnityPackagesFolder) {
        # Optional: when set, installer imports all *.unitypackage found in that folder (in addition to the selected one).
        # When not set, extra-imports are disabled.
        $Config | Add-Member -MemberType NoteProperty -Name "UnityPackagesFolder" -Value $null -Force
    }

    # Ensure DefaultPackages list exists (for protected packages)
    $Config | Add-Member -MemberType NoteProperty -Name "DefaultPackages" -Value @(Get-DefaultPackages -Config $Config) -Force

    # Ensure VpmPackages contains all default packages
    if ($Config.VpmPackages) {
        $defaults = Get-DefaultPackages -Config $Config
        foreach ($dpkg in $defaults) {
            if ($Config.VpmPackages.PSObject.Properties.Name -notcontains $dpkg) {
                $Config.VpmPackages | Add-Member -MemberType NoteProperty -Name $dpkg -Value "latest" -Force
            }
        }
    }

    return $Config
}

function Apply-ProjectNamingRules {
    param(
        [string]$BaseName,
        $Config
    )
    if ([string]::IsNullOrWhiteSpace($BaseName)) { return $BaseName }
    if (-not $Config) { return $BaseName }

    $name = $BaseName
    $cfg = Ensure-ConfigDefaults -Config $Config
    $patterns = @($cfg.Naming.RegexRemovePatterns)
    foreach ($pat in $patterns) {
        if ([string]::IsNullOrWhiteSpace($pat)) { continue }
        try {
            $name = ($name -replace $pat, "")
        }
        catch {
            # ignore invalid regex
        }
    }

    $name = $name.Trim()
    $name = ($cfg.Naming.DefaultPrefix + $name + $cfg.Naming.DefaultSuffix).Trim()
    return $name
}

function Advanced-NamingSettings {
    param(
        [string]$ConfigPath
    )

    if (-not (Test-Path $ConfigPath)) {
        Write-Host "Config not found. Run setup first." -ForegroundColor Red
        Read-Host "Press ENTER to continue"
        return
    }

    $config = Load-Config -ConfigPath $ConfigPath
    $config = Ensure-ConfigDefaults -Config $config

    while ($true) {
        $patternsCount = @($config.Naming.RegexRemovePatterns).Count
        $remember = if ($config.Naming.RememberUnityPackageNames) { "ON" } else { "OFF" }
        $workspaceRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
        $commonPackagesStatus = 'DISABLED'
        if ($config -and -not [string]::IsNullOrWhiteSpace([string]$config.UnityPackagesFolder)) {
            $cfgCommon = ([string]$config.UnityPackagesFolder).Trim().Trim('"').Trim("'")
            $resolvedCommon = $cfgCommon
            if (-not [System.IO.Path]::IsPathRooted($cfgCommon)) {
                $resolvedCommon = Join-Path $workspaceRoot $cfgCommon
            }
            $commonPackagesStatus = Get-PathStatus -Path $resolvedCommon
        }

        $editorStatus = Get-PathStatus -Path ([string]$config.UnityEditorPath)
        # Extra check: if editor path exists but isn't Unity.exe, flag it
        if (-not [string]::IsNullOrWhiteSpace([string]$config.UnityEditorPath) -and (Test-Path ([string]$config.UnityEditorPath))) {
            $editorValidation = Test-UnityEditorPath -Path ([string]$config.UnityEditorPath)
            if (-not $editorValidation.Valid) { $editorStatus = "INVALID: $($editorValidation.Message)" }
        }
        $projectsRootStatus = Get-PathStatus -Path ([string]$config.UnityProjectsRoot)

        $optEditor = "Unity Editor path: ${editorStatus}"
        $optProjectsRoot = "Projects root: ${projectsRootStatus}"
        $optPrefix = "Prefix: '$($config.Naming.DefaultPrefix)'"
        $optSuffix = "Suffix: '$($config.Naming.DefaultSuffix)'"
        $optRegex = "Regex remove patterns: ${patternsCount}"
        $optRemember = "Remember unitypackage names: ${remember}"
        $optUnityPackages = "UnityPackages folder (extra imports): ${commonPackagesStatus}"

        $sel = Show-Menu -Title "Advanced settings" -Header "Edit your defaults. Enter to select." -Options @(
            $optEditor,
            $optProjectsRoot,
            $optPrefix,
            $optSuffix,
            $optRegex,
            $optRemember,
            $optUnityPackages,
            "Back"
        )

        if ($sel -eq -1 -or $sel -eq 7) { Save-Config -Config $config -ConfigPath $ConfigPath; return }

        switch ($sel) {
            0 {
                # Unity Editor path
                $found = @((Find-UnityEditorPaths))
                $editorOptions = @()
                foreach ($f in $found) {
                    $editorOptions += "$($f.Version) - $($f.Path)"
                }
                $editorOptions += "Enter path manually"
                $editorOptions += "Back"

                $pick = Show-Menu -Title "Unity Editor path" -Header "Current: ${editorStatus}" -Options $editorOptions
                if ($pick -ge 0 -and $pick -lt $found.Count) {
                    $config | Add-Member -MemberType NoteProperty -Name "UnityEditorPath" -Value $found[$pick].Path -Force
                    Save-Config -Config $config -ConfigPath $ConfigPath
                }
                elseif ($pick -eq $found.Count) {
                    Clear-Host
                    Write-Host "Drag Unity.exe here or paste the full path:" -ForegroundColor Yellow
                    $newPath = Normalize-UserPath (Read-Host "Unity.exe path")
                    if (-not [string]::IsNullOrWhiteSpace($newPath)) {
                        $validation = Test-UnityEditorPath -Path $newPath
                        if ($validation.Valid) {
                            $config | Add-Member -MemberType NoteProperty -Name "UnityEditorPath" -Value $newPath -Force
                            Save-Config -Config $config -ConfigPath $ConfigPath
                            Write-Host "Unity Editor set: ${newPath}" -ForegroundColor Green
                        } else {
                            Write-Host $validation.Message -ForegroundColor Red
                            Write-Host "Path was NOT saved." -ForegroundColor DarkGray
                            Read-Host "Press ENTER to continue" | Out-Null
                        }
                    }
                }
            }
            1 {
                # Projects root
                Clear-Host
                Write-Host "Current projects root: ${projectsRootStatus}" -ForegroundColor Gray
                Write-Host "Drag a folder here or paste the path:" -ForegroundColor Yellow
                $newRoot = Normalize-UserPath (Read-Host "Projects folder")
                if (-not [string]::IsNullOrWhiteSpace($newRoot)) {
                    if (-not (Test-Path $newRoot)) {
                        $mkChoice = Show-Menu -Title "Folder not found" -Header "Create folder?`n${newRoot}" -Options @("Create it", "Cancel")
                        if ($mkChoice -eq 0) {
                            try {
                                New-Item -ItemType Directory -Path $newRoot -Force | Out-Null
                            } catch {
                                Write-Host "Failed to create folder: ${_}" -ForegroundColor Red
                                Read-Host "Press ENTER to continue" | Out-Null
                                continue
                            }
                            if (-not (Test-Path $newRoot)) {
                                Write-Host "Folder still doesn't exist after creation attempt." -ForegroundColor Red
                                Read-Host "Press ENTER to continue" | Out-Null
                                continue
                            }
                        }
                        else { continue }
                    }
                    $config | Add-Member -MemberType NoteProperty -Name "UnityProjectsRoot" -Value $newRoot -Force
                    Save-Config -Config $config -ConfigPath $ConfigPath
                    Write-Host "Projects root set: ${newRoot}" -ForegroundColor Green
                }
            }
            2 {
                $p = Read-Host "Default prefix (blank to clear)"
                $config.Naming.DefaultPrefix = if ($p) { $p } else { "" }
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            3 {
                $s = Read-Host "Default suffix (blank to clear)"
                $config.Naming.DefaultSuffix = if ($s) { $s } else { "" }
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            4 {
                while ($true) {
                    $patterns = @($config.Naming.RegexRemovePatterns)
                    $opts = @()
                    foreach ($pat in $patterns) { $opts += $pat }
                    $opts += @("Add pattern", "Remove pattern", "Back")

                    $pSel = Show-Menu -Title "Regex remove patterns" -Header "These patterns will be removed from the suggested project name." -Options $opts
                    if ($pSel -eq -1 -or $opts[$pSel] -eq "Back") { break }

                    if ($opts[$pSel] -eq "Add pattern") {
                        $newPat = Read-Host "Regex pattern to remove"
                        if ([string]::IsNullOrWhiteSpace($newPat)) { continue }
                        try {
                            [void][regex]::new($newPat)
                        }
                        catch {
                            Write-Host "Invalid regex." -ForegroundColor Red
                            Read-Host "Press ENTER"
                            continue
                        }
                        $config.Naming.RegexRemovePatterns += @($newPat)
                        Save-Config -Config $config -ConfigPath $ConfigPath
                        continue
                    }

                    if ($opts[$pSel] -eq "Remove pattern") {
                        if ($patterns.Count -eq 0) { continue }
                        $idx = Show-Menu -Title "Remove which pattern?" -Options $patterns
                        if ($idx -eq -1) { continue }
                        $toRemove = $patterns[$idx]
                        $config.Naming.RegexRemovePatterns = @($patterns | Where-Object { $_ -ne $toRemove })
                        Save-Config -Config $config -ConfigPath $ConfigPath
                        continue
                    }
                }
            }
            5 {
                $config.Naming.RememberUnityPackageNames = -not $config.Naming.RememberUnityPackageNames
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            6 {
                $inputPath = Read-WizardPathInput -Title "UnityPackages folder (extra imports)" -Prompt "Folder path" -BodyLines @(
                    "When you create a project from a UnityPackage, the installer can also import all *.unitypackage found in this folder.",
                    "",
                    "Current: ${commonPackagesStatus}",
                    "",
                    "Type 'disable' (or 'none' / 'clear') to turn it off."
                )
                if ([string]::IsNullOrWhiteSpace($inputPath)) { continue }

                if ($inputPath -ieq 'disable' -or $inputPath -ieq 'none' -or $inputPath -ieq 'clear') {
                    $config.UnityPackagesFolder = $null
                    Save-Config -Config $config -ConfigPath $ConfigPath
                    continue
                }

                $resolved = $inputPath
                if (-not [System.IO.Path]::IsPathRooted($resolved)) {
                    $resolved = Join-Path $workspaceRoot $resolved
                }

                if (-not (Test-Path $resolved)) {
                    $create = Show-Menu -Title "Folder not found" -Header (Add-ConfirmHint -Header "Create folder?`n${resolved}") -Options @("Create", "Cancel") -AllowCancel $false
                    if ($create -ne 0) { continue }
                    try {
                        New-Item -Path $resolved -ItemType Directory -Force | Out-Null
                    }
                    catch {
                        Write-Host "Failed to create folder: ${_}" -ForegroundColor Red
                        Read-Host "Press ENTER" | Out-Null
                        continue
                    }
                }

                # Persist the raw value the user typed (absolute or relative). Installer resolves relative paths from workspace root.
                $config.UnityPackagesFolder = $inputPath
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
        }
    }
}

function Select-VpmVersion {
    param(
        [string]$PackageName,
        [string]$CurrentVersion
    )

    $available = @()
    $sourceLabel = $null

    $vrcGetVersions = Get-VrcGetAvailableVersions -PackageName $PackageName -ScriptDir $scriptDir
    if ($vrcGetVersions.Count -gt 0) {
        $available = $vrcGetVersions
        $sourceLabel = "vrc-get"
    }
    else {
        $available = Get-VpmAvailableVersions -PackageName $PackageName
        $sourceLabel = "VCC repos"
    }

    $pageSize = 20
    $page = 0
    $filterPattern = $null

    while ($true) {
        $filtered = @($available)
        if (-not [string]::IsNullOrWhiteSpace($filterPattern)) {
            $filtered = @($available | Where-Object { Test-VersionMatchesPattern -Version $_ -Pattern $filterPattern })
        }

        $total = $filtered.Count

        if ($total -le 0) {
            $page = 0
        }
        else {
            $maxPage = [Math]::Max(0, [Math]::Floor(($total - 1) / $pageSize))
            if ($page -gt $maxPage) { $page = $maxPage }
            if ($page -lt 0) { $page = 0 }
        }

        $startIdx = if ($total -gt 0) { $page * $pageSize } else { 0 }
        $endIdx = if ($total -gt 0) { [Math]::Min($startIdx + $pageSize - 1, $total - 1) } else { -1 }

        $pageItems = @()
        if ($total -gt 0) {
            if ($startIdx -eq $endIdx) {
                $pageItems = @($filtered[$startIdx])
            }
            else {
                $pageItems = @($filtered[$startIdx..$endIdx])
            }
        }

        $header = "Package: ${PackageName}`nCurrent: ${CurrentVersion}`n"
        if ($available.Count -gt 0) {
            $header += "Versions found: ${($available.Count)} (from ${sourceLabel})`n"
        }
        else {
            $header += "No versions found. You can still enter manually.`n"
        }

        $fLabel = if ([string]::IsNullOrWhiteSpace($filterPattern)) { "(none)" } else { $filterPattern }
        $header += "Filter: ${fLabel}`n"
        if ($total -gt 0) {
            $header += ("Showing: {0}-{1} of {2} (page {3})`n" -f ($startIdx + 1), ($endIdx + 1), $total, ($page + 1))
        }
        else {
            $header += "No matches for current filter.`n"
        }

        if ($total -gt $pageSize) {
            $header += "Tip: use Left/Right to change page faster.`n"
        }

        $optLatest = "latest"
        $optPrev = "< Prev page"
        $optNext = "Next page >"
        $optJump = "Jump to range"
        $optSetFilter = "Set filter"
        $optClearFilter = "Clear filter"
        $optEnter = "Enter manually"
        $optBack = "Back"

        $options = @($optLatest)
        $options += @($pageItems)
        if ($total -gt $pageSize) { $options += $optJump }
        if ($page -gt 0) { $options += $optPrev }
        if (($page + 1) * $pageSize -lt $total) { $options += $optNext }
        $options += $optSetFilter
        if (-not [string]::IsNullOrWhiteSpace($filterPattern)) { $options += $optClearFilter }
        $options += @($optEnter, $optBack)

        $sel = Show-Menu -Title "Select version" -Header $header -Options $options -EnableHorizontalNav ($total -gt $pageSize)
        if ($sel -eq -1) { return $null }
        if ($sel -eq -2) { $page--; continue }
        if ($sel -eq -3) { $page++; continue }

        $picked = $options[$sel]
        if ($picked -eq $optBack) { return $null }

        if ($picked -eq $optPrev) { $page--; continue }
        if ($picked -eq $optNext) { $page++; continue }

        if ($picked -eq $optSetFilter) {
            Write-Host "Filter pattern examples:" -ForegroundColor DarkGray
            Write-Host "  *.9        (ends with .9)" -ForegroundColor DarkGray
            Write-Host "  X.X.1190   (digits.digits.1190)" -ForegroundColor DarkGray
            Write-Host "  re:1190$   (regex mode)" -ForegroundColor DarkGray
            $newFilter = Read-Host "Filter (*, ?, X) or re:<regex> (blank = cancel)"
            if (-not [string]::IsNullOrWhiteSpace($newFilter)) {
                $filterPattern = $newFilter.Trim()
                $page = 0
            }
            continue
        }

        if ($picked -eq $optClearFilter) {
            $filterPattern = $null
            $page = 0
            continue
        }

        if ($picked -eq $optJump) {
            if ($total -le 0) { continue }
            $in = Read-Host "Range start-end (e.g. 41-60) or start (e.g. 81). Blank = cancel"
            if ([string]::IsNullOrWhiteSpace($in)) { continue }
            $txt = $in.Trim()
            $start = $null
            if ($txt -match '^(?<a>\d+)\s*-\s*(?<b>\d+)$') {
                $start = [int]$Matches['a']
            }
            elseif ($txt -match '^(?<a>\d+)$') {
                $start = [int]$Matches['a']
            }
            if ($null -eq $start -or $start -lt 1) { continue }
            $idx = $start - 1
            if ($idx -ge $total) { $idx = $total - 1 }
            $page = [Math]::Floor($idx / $pageSize)
            continue
        }

        if ($picked -eq $optEnter) {
            $manual = Read-Host "Version (or 'latest')"
            if ([string]::IsNullOrWhiteSpace($manual)) { return $null }
            return $manual.Trim()
        }

        # Selected a version or "latest"
        return $picked
    }
}

function Resolve-VpmPackageFromSearch {
    param(
        [string]$InitialQuery,
        [string]$SearchOption,
        [string]$ManualOption
    )

    $query = $InitialQuery
    while ($true) {
        if ([string]::IsNullOrWhiteSpace($query)) {
            $query = Read-Host "Search query"
        }
        $query = $query.Trim()
        if ([string]::IsNullOrWhiteSpace($query)) { return $null }

        $found = Search-VrcGetPackages -Query $query -ScriptDir $ScriptDir
        if ($found.Count -eq 0) {
            $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
            Show-WizardError -Title "No matches" -Message "No packages matched: ${query}" -Details $tail

            $retryChoice = Show-Menu `
                -Title "Package search" `
                -Header "No results for '${query}'. Try a different search, enter an exact package id manually, or go back." `
                -Options @("Search packages", "Enter package name manually", "Back") `
                -AllowCancel $false

            if ($retryChoice -eq 0) {
                $query = $null
                continue
            }
            if ($retryChoice -eq 1) {
                $manual = Read-Host "Package name"
                if ($null -eq $manual) { return $null }
                return $manual.Trim()
            }
            return $null
        }

        $displayOptions = @()
        foreach ($x in $found) {
            if ($x.DisplayName) {
                $displayOptions += ("{0} ({1})" -f $x.DisplayName, $x.Id)
            }
            else {
                $displayOptions += $x.Id
            }
        }

        $searchParams = @{
            Title                     = "Search results"
            Header                    = "Results for '${query}'"
            Options                   = $displayOptions
            PinnedOptions             = @($SearchOption, $ManualOption)
            Placeholder               = "type to narrow results"
            ShowListMarkers           = $true
            ReturnSelectionWithFilter = $true
        }
        $pickedResult = Show-MenuFilter @searchParams

        if ($null -eq $pickedResult) { return $null }
        $pickStr = [string]$pickedResult.Selection
        $typedQuery = [string]$pickedResult.Filter
        if ($pickStr -eq $SearchOption) {
            $query = if ([string]::IsNullOrWhiteSpace($typedQuery)) { $null } else { $typedQuery.Trim() }
            continue
        }
        if ($pickStr -eq $ManualOption) {
            $manual = Read-Host "Package name"
            if ($null -eq $manual) { return $null }
            return $manual.Trim()
        }

        $idx = [array]::IndexOf($displayOptions, $pickStr)
        if ($idx -ge 0) { return $found[$idx].Id }

        if (-not [string]::IsNullOrWhiteSpace($pickStr)) {
            $query = $pickStr.Trim()
            continue
        }
    }
}

function Edit-VpmPackages {
    param(
        [string]$ConfigPath,
        [string]$ScriptDir
    )

    if (-not (Test-Path $ConfigPath)) {
        Write-Host "Config not found. Run setup first." -ForegroundColor Red
        Read-Host "Press ENTER to continue"
        return
    }

    $config = Load-Config -ConfigPath $ConfigPath
    if (-not $config) {
        Write-Host "Unable to load config." -ForegroundColor Red
        Read-Host "Press ENTER to continue"
        return
    }

    if ($config.VpmPackages -is [System.Array]) {
        $newPackages = @{}
        foreach ($pkg in $config.VpmPackages) { $newPackages[$pkg] = "latest" }
        $config.VpmPackages = [pscustomobject]$newPackages
    }
    if (-not $config.VpmPackages) {
        $config | Add-Member -MemberType NoteProperty -Name "VpmPackages" -Value ([pscustomobject]@{ "com.vrchat.base" = "latest" }) -Force
    }

    while ($true) {
        $packagesList = @($config.VpmPackages.PSObject.Properties) | Sort-Object Name
        $pkgOptions = @()
        foreach ($pkg in $packagesList) {
            $isDefault = Test-IsDefaultPackage -PackageName $pkg.Name -Config $config
            $lockIcon = if ($isDefault) { " [default]" } else { "" }
            $pkgOptions += ("{0}  [{1}]{2}" -f $pkg.Name, $pkg.Value, $lockIcon)
        }
        $pkgOptions += @("Add package", "Back")

        $header = "Select a package, then choose an action.`nPackages marked [default] cannot be removed."
        $selected = Show-Menu -Title "VPM Packages" -Header $header -Options $pkgOptions
        if ($selected -eq -1) { return }

        $picked = $pkgOptions[$selected]
        if ($picked -eq "Back") { return }

        if ($picked -eq "Add package") {
            $manualOption = "Enter package name manually"
            $searchOption = "SEARCH PACKAGES"
            $hasVrcGet = -not [string]::IsNullOrWhiteSpace((Get-VrcGetExecutablePath -ScriptDir $ScriptDir))

            $opts = @($searchOption, $manualOption)
            $pinned = @($searchOption, $manualOption)

            $hint = if ($hasVrcGet) {
                "Type a package name, then choose SEARCH PACKAGES."
            } else {
                "vrc-get not found. Enter an exact package id manually."
            }
            $filterParams = @{
                Title                     = "Add package"
                Header                    = $hint
                Options                   = $opts
                PinnedOptions             = $pinned
                Placeholder               = "package name"
                ReturnSelectionWithFilter = $true
            }
            $pickedResult = Show-MenuFilter @filterParams
            if ($null -eq $pickedResult) { continue }
            $pickedName = [string]$pickedResult.Selection
            $typedQuery = [string]$pickedResult.Filter

            $newPackage = $null
            if ($pickedName -eq $searchOption) {
                if (-not $hasVrcGet) {
                    Show-WizardError -Title "vrc-get not found" -Message "Remote package search needs vrc-get under setup-scripts/lib/vrc-get/."
                    continue
                }
                $newPackage = Resolve-VpmPackageFromSearch -InitialQuery $typedQuery -SearchOption $searchOption -ManualOption $manualOption
            }
            elseif ($pickedName -eq $manualOption) {
                $newPackage = if ([string]::IsNullOrWhiteSpace($typedQuery)) { Read-Host "Package name" } else { $typedQuery }
                $newPackage = $newPackage.Trim()
            }
            else {
                $newPackage = $pickedName
            }

            if ($null -eq $newPackage) { continue }

            # If user pressed Enter with zero matches, Show-MenuFilter returns the typed filter string.
            $newPackage = $newPackage.Trim()

            if ([string]::IsNullOrWhiteSpace($newPackage)) { continue }

            # Make intent clear for the next step
            Write-Host "Selected package: ${newPackage}" -ForegroundColor Cyan

            if ($config.VpmPackages.PSObject.Properties.Name -contains $newPackage) {
                Write-Host "Package already present." -ForegroundColor Yellow
                Start-Sleep -Seconds 1
                continue
            }

            if (-not (Test-VpmPackageExists -PackageName $newPackage -ScriptDir $ScriptDir)) {
                $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                Show-WizardError -Title "Package not found" -Message "Package not found / not resolvable: ${newPackage}" -Details $tail
                continue
            }

            $version = Select-VpmVersion -PackageName $newPackage -CurrentVersion "(new)"
            if ($null -eq $version) { continue }

            $validation = Test-VpmPackageVersion -PackageName $newPackage -Version $version -ScriptDir $ScriptDir
            if (-not $validation.Valid) {
                $details = $validation.Details
                if ([string]::IsNullOrWhiteSpace($details)) {
                    $details = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                }
                Show-WizardError -Title "Validation failed" -Message $validation.Message -Details $details
                continue
            }

            $config.VpmPackages | Add-Member -MemberType NoteProperty -Name $newPackage -Value $version -Force
            Save-Config -Config $config -ConfigPath $ConfigPath
            continue
        }

        # A real package selected
        $pkgProp = $packagesList[$selected]
        $pkgName = $pkgProp.Name
        $pkgVersion = $pkgProp.Value
        $isDefaultPkg = Test-IsDefaultPackage -PackageName $pkgName -Config $config

        if ($isDefaultPkg) {
            $action = Show-Menu -Title "Package: ${pkgName} [default]" -Header "Current: ${pkgVersion}`nThis is a default package and cannot be removed." -Options @("Change version", "Back")
            if ($action -eq -1 -or $action -eq 1) { continue }

            if ($action -eq 0) {
                if (-not (Test-VpmPackageExists -PackageName $pkgName -ScriptDir $ScriptDir)) {
                    $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                    Show-WizardError -Title "Package not found" -Message "Package not found / not resolvable: ${pkgName}" -Details $tail
                    continue
                }

                $newVersion = Select-VpmVersion -PackageName $pkgName -CurrentVersion $pkgVersion
                if ($null -eq $newVersion) { continue }

                $validation = Test-VpmPackageVersion -PackageName $pkgName -Version $newVersion -ScriptDir $ScriptDir
                if (-not $validation.Valid) {
                    $details = $validation.Details
                    if ([string]::IsNullOrWhiteSpace($details)) {
                        $details = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                    }
                    Show-WizardError -Title "Validation failed" -Message $validation.Message -Details $details
                    continue
                }

                $config.VpmPackages.($pkgName) = $newVersion
                Save-Config -Config $config -ConfigPath $ConfigPath
                continue
            }
        } else {
            $action = Show-Menu -Title "Package: ${pkgName}" -Header "Current: ${pkgVersion}" -Options @("Change version", "Remove package", "Back")
            if ($action -eq -1 -or $action -eq 2) { continue }
        }

        if ($action -eq 0) {
            if (-not (Test-VpmPackageExists -PackageName $pkgName -ScriptDir $ScriptDir)) {
                $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                Show-WizardError -Title "Package not found" -Message "Package not found / not resolvable: ${pkgName}" -Details $tail
                continue
            }

            $newVersion = Select-VpmVersion -PackageName $pkgName -CurrentVersion $pkgVersion
            if ($null -eq $newVersion) { continue }

            $validation = Test-VpmPackageVersion -PackageName $pkgName -Version $newVersion -ScriptDir $ScriptDir
            if (-not $validation.Valid) {
                $details = $validation.Details
                if ([string]::IsNullOrWhiteSpace($details)) {
                    $details = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                }
                Show-WizardError -Title "Validation failed" -Message $validation.Message -Details $details
                continue
            }

            $config.VpmPackages.($pkgName) = $newVersion
            Save-Config -Config $config -ConfigPath $ConfigPath
            continue
        }

        if ($action -eq 1) {
            $confirm = Show-Menu -Title "Remove package" -Header (Add-ConfirmHint -Header "Remove ${pkgName}?") -Options @("Yes, remove", "Cancel") -AllowCancel $false
            if ($confirm -eq 0) {
                $config.VpmPackages.PSObject.Properties.Remove($pkgName)
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            continue
        }
    }
}

function Setup-ProjectFlow {
    param([string]$ConfigPath)

    function Cleanup-IncompleteProjectsFlow {
        param($Config)

        if (-not $Config -or [string]::IsNullOrWhiteSpace([string]$Config.UnityProjectsRoot)) {
            Clear-Host
            Write-Host "UnityProjectsRoot is missing in config." -ForegroundColor Red
            Read-Host "Press ENTER to continue" | Out-Null
            return
        }

        $root = [string]$Config.UnityProjectsRoot
        $projects = Get-VrcSetupIncompleteProjects -UnityProjectsRoot $root
        if (-not $projects -or $projects.Count -eq 0) {
            Clear-Host
            Write-Host "No incomplete projects found." -ForegroundColor Green
            Read-Host "Press ENTER to continue" | Out-Null
            return
        }

        $header = "Found incomplete projects under:`n${root}`n`nDefault: all selected (for delete)."
        $picked = Show-ChecklistPaged -Title "Cleanup incomplete projects" -Header $header -Items $projects -DefaultSelected $true -MaxVisible 12 -ToLabel {
            param($p, $i)
            $name = if (-not [string]::IsNullOrWhiteSpace([string]$p.ProjectName)) { [string]$p.ProjectName } else { Split-Path -Leaf ([string]$p.ProjectPath) }
            $pending = if (-not [string]::IsNullOrWhiteSpace([string]$p.PendingStep)) { "pending=${p.PendingStep}" } else { "incomplete" }
            $updated = if (-not [string]::IsNullOrWhiteSpace([string]$p.LastUpdatedAt)) { $p.LastUpdatedAt } else { "" }
            return "${name}  |  ${pending}  |  ${updated}"
        }

        if ($null -eq $picked) { return }
        if ($picked.Count -eq 0) {
            Clear-Host
            Write-Host "Nothing selected." -ForegroundColor Yellow
            Read-Host "Press ENTER to continue" | Out-Null
            return
        }

        $confirm = Show-Menu -Title "Confirm delete" -Header (Add-ConfirmHint -Header ("Delete {0} selected project(s)?`nThis deletes the entire project folders." -f $picked.Count)) -Options @("Delete", "Cancel") -AllowCancel $false
        if ($confirm -ne 0) { return }

        Clear-Host
        foreach ($p in $picked) {
            $pp = [string]$p.ProjectPath
            if ([string]::IsNullOrWhiteSpace($pp)) { continue }
            Write-Host "Deleting: ${pp}" -ForegroundColor Yellow
            $deleteResult = Remove-ProjectFolderWithRecovery -ProjectPath $pp -FailurePrefix ("Failed: ${pp}") -AllowSkip:$true -SkipLabel "Skip this project"
            if ($deleteResult.Skipped) {
                Write-Host "Skipped: ${pp}" -ForegroundColor DarkYellow
            }
        }
        Write-Host "Cleanup done." -ForegroundColor Green
        Read-Host "Press ENTER to continue" | Out-Null
    }

    function Get-ConfiguredExtraUnityPackagesInfo {
        param(
            $Config,
            [string]$PackagePath
        )

        $result = [pscustomobject]@{
            ExtrasCount = 0
            StatusLabel = "Disabled"
        }

        if (-not $Config) { return $result }
        if (-not ($Config.PSObject.Properties.Name -contains 'UnityPackagesFolder')) { return $result }

        $cfgCommon = [string]$Config.UnityPackagesFolder
        if ([string]::IsNullOrWhiteSpace($cfgCommon)) { return $result }

        $cfgCommon = $cfgCommon.Trim().Trim('"').Trim("'")
        $workspaceRoot = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
        $resolvedCommon = if ([System.IO.Path]::IsPathRooted($cfgCommon)) { $cfgCommon } else { Join-Path $workspaceRoot $cfgCommon }

        if (-not (Test-Path $resolvedCommon)) {
            $result.StatusLabel = "Configured but folder missing"
            return $result
        }

        $mainResolved = $PackagePath
        try { $mainResolved = (Resolve-Path $PackagePath -ErrorAction Stop).Path } catch { }

        $extraPackages = @()
        $commonPackages = Get-ChildItem -Path $resolvedCommon -Filter "*.unitypackage" -ErrorAction SilentlyContinue
        foreach ($pkg in $commonPackages) {
            $pkgResolved = $pkg.FullName
            try { $pkgResolved = (Resolve-Path $pkg.FullName -ErrorAction Stop).Path } catch { }
            if ($pkgResolved -ne $mainResolved) {
                $extraPackages += $pkg.FullName
            }
        }

        $result.ExtrasCount = @($extraPackages).Count
        $result.StatusLabel = if ($result.ExtrasCount -gt 0) { "{0} package(s) found" -f $result.ExtrasCount } else { "0 package(s) found" }
        return $result
    }

    function New-UnityPackageFlowState {
        return [pscustomobject]@{
            PackagePath = $null
            SuggestedProjectName = $null
            SavedProjectName = $null
            ProjectName = $null
            TargetProjectPath = $null
            ExistingAction = 'create-new'
            ActionLabel = 'Create new project'
            ExtrasCount = 0
            ExtrasStatus = 'Disabled'
        }
    }

    function Get-UnityPackageActionLabel {
        param([string]$Action)

        switch ($Action) {
            'overwrite'           { return "Delete existing and recreate" }
            'use-existing-vpm'    { return "Use existing (VPM only)" }
            'use-existing-extras' { return "Use existing (VPM + import extras)" }
            default               { return "Create new project" }
        }
    }

    function Update-UnityPackageFlowState {
        param(
            $State,
            $Config
        )

        if (-not $State) { return }

        $State.SuggestedProjectName = $null
        $State.SavedProjectName = $null
        $State.TargetProjectPath = $null
        $State.ExtrasCount = 0
        $State.ExtrasStatus = "Disabled"

        if (-not [string]::IsNullOrWhiteSpace([string]$State.PackagePath)) {
            $rawDefault = [System.IO.Path]::GetFileNameWithoutExtension([string]$State.PackagePath)
            $State.SuggestedProjectName = Apply-ProjectNamingRules -BaseName $rawDefault -Config $Config

            if ($Config -and $Config.SavedProjectNames -and $Config.SavedProjectNames.PSObject.Properties.Name -contains $State.PackagePath) {
                $State.SavedProjectName = $Config.SavedProjectNames.($State.PackagePath)
            }

            $extrasInfo = Get-ConfiguredExtraUnityPackagesInfo -Config $Config -PackagePath $State.PackagePath
            $State.ExtrasCount = $extrasInfo.ExtrasCount
            $State.ExtrasStatus = $extrasInfo.StatusLabel
        }

        $defaultPromptName = if (-not [string]::IsNullOrWhiteSpace([string]$State.SavedProjectName)) {
            [string]$State.SavedProjectName
        } else {
            [string]$State.SuggestedProjectName
        }

        if ([string]::IsNullOrWhiteSpace([string]$State.ProjectName)) {
            $State.ProjectName = $defaultPromptName
        } else {
            $State.ProjectName = ([string]$State.ProjectName).Trim()
        }

        if ($Config -and -not [string]::IsNullOrWhiteSpace([string]$Config.UnityProjectsRoot) -and -not [string]::IsNullOrWhiteSpace([string]$State.ProjectName)) {
            try {
                $State.TargetProjectPath = Join-Path ([string]$Config.UnityProjectsRoot) ([string]$State.ProjectName)
            } catch {
                $State.TargetProjectPath = $null
            }
        }

        if ([string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath) -or -not (Test-Path ([string]$State.TargetProjectPath))) {
            $State.ExistingAction = 'create-new'
        }

        $State.ActionLabel = Get-UnityPackageActionLabel -Action $State.ExistingAction
    }

    function Prompt-UnityPackagePackageStep {
        param($State)

        while ($true) {
            $packagePath = Read-WizardPathInput -Title "UnityPackage setup - Step 1/4: Select package" -Prompt "UnityPackage path" -BodyLines @(
                "Choose the .unitypackage file to import.",
                "The wizard will keep you inside this flow until the input is valid or you cancel."
            )

            if ([string]::IsNullOrWhiteSpace($packagePath)) { return $false }

            if (-not (Test-Path $packagePath)) {
                Show-WizardError -Title "Path not found" -Message $packagePath
                continue
            }

            if ($packagePath -notlike "*.unitypackage") {
                Show-WizardError -Title "Invalid file type" -Message "The selected path must point to a .unitypackage file."
                continue
            }

            $State.PackagePath = $packagePath
            $State.ProjectName = $null
            $State.ExistingAction = 'create-new'
            return $true
        }
    }

    function Prompt-UnityPackageIdentityStep {
        param(
            $State,
            $Config
        )

        Update-UnityPackageFlowState -State $State -Config $Config

        $projectsRootValue = if ($Config -and -not [string]::IsNullOrWhiteSpace([string]$Config.UnityProjectsRoot)) {
            [string]$Config.UnityProjectsRoot
        } else {
            "(missing)"
        }

        $targetPreview = if (-not [string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath)) {
            [string]$State.TargetProjectPath
        } else {
            "(unavailable)"
        }

        $defaultPromptName = if (-not [string]::IsNullOrWhiteSpace([string]$State.SavedProjectName)) {
            [string]$State.SavedProjectName
        } else {
            [string]$State.SuggestedProjectName
        }

        Clear-Host
        Write-Host "UnityPackage setup - Step 2/4: Project identity" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "UnityPackage:" -ForegroundColor Gray
        Write-Host "  $($State.PackagePath)" -ForegroundColor White
        Write-Host ""
        Write-Host "Suggested project name:" -ForegroundColor Gray
        Write-Host "  $($State.SuggestedProjectName)" -ForegroundColor White
        if ($State.SavedProjectName) {
            Write-Host "Saved name for this UnityPackage:" -ForegroundColor DarkGray
            Write-Host "  $($State.SavedProjectName)" -ForegroundColor Gray
        }
        Write-Host ""
        Write-Host "Projects root:" -ForegroundColor Gray
        Write-Host "  ${projectsRootValue}" -ForegroundColor White
        Write-Host ""
        Write-Host "Target preview:" -ForegroundColor Gray
        Write-Host "  ${targetPreview}" -ForegroundColor White
        Write-Host ""
        Write-Host ("Press ENTER to use: {0}" -f $defaultPromptName) -ForegroundColor DarkGray

        $projectName = Read-Host "Project name"
        if ([string]::IsNullOrWhiteSpace($projectName)) {
            $State.ProjectName = $defaultPromptName
        } else {
            $State.ProjectName = $projectName.Trim()
        }

        Update-UnityPackageFlowState -State $State -Config $Config
        return $true
    }

    function Prompt-UnityPackageExistingActionStep {
        param($State)

        Update-UnityPackageFlowState -State $State -Config $config

        if ([string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath) -or -not (Test-Path ([string]$State.TargetProjectPath))) {
            $State.ExistingAction = 'create-new'
            $State.ActionLabel = Get-UnityPackageActionLabel -Action $State.ExistingAction
            return $true
        }

        $extrasLabel = if ($State.ExtrasCount -gt 0) {
            "Use existing: setup VPM + import extra UnityPackages ({0} found)" -f $State.ExtrasCount
        } else {
            "Use existing: setup VPM + import extra UnityPackages (0 found)"
        }

        $options = @(
            "Delete existing and recreate (from UnityPackage)",
            "Use existing: setup VPM only",
            $extrasLabel,
            "Back"
        )

        $header = @(
            "Step 3/4: Existing target decision",
            "",
            "Target already exists:",
            $State.TargetProjectPath,
            "",
            "Choose how to continue with this existing project."
        ) -join "`n"

        $choice = Show-Menu -Title "UnityPackage setup" -Header (Add-ConfirmHint -Header $header -Hint "Choose an action for the existing target. Use Back to return to project identity.") -Options $options -AllowCancel $false
        if ($choice -lt 0 -or $options[$choice] -eq "Back") { return $false }

        switch ($choice) {
            0 { $State.ExistingAction = 'overwrite' }
            1 { $State.ExistingAction = 'use-existing-vpm' }
            2 { $State.ExistingAction = 'use-existing-extras' }
            default { $State.ExistingAction = 'create-new' }
        }
        $State.ActionLabel = Get-UnityPackageActionLabel -Action $State.ExistingAction
        return $true
    }

    function Get-UnityPackageReviewHeader {
        param($State)

        $targetState = if (-not [string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath) -and (Test-Path ([string]$State.TargetProjectPath))) {
            "Already exists"
        } else {
            "Will be created"
        }

        return @(
            "Step 4/4: Review setup plan",
            "",
            "UnityPackage: $($State.PackagePath)",
            "Project name: $($State.ProjectName)",
            "Target: $($State.TargetProjectPath)",
            "Target status: ${targetState}",
            "Action: $($State.ActionLabel)",
            "Extra UnityPackages: $($State.ExtrasStatus)",
            "",
            "Review the plan before starting setup."
        ) -join "`n"
    }

    function Show-SetupOutcomeSummary {
        param(
            [int]$Status,
            [string]$ActionLabel,
            [string]$TargetPath,
            [string]$PackagePath,
            [bool]$CanLeavePartialProject = $false
        )

        Write-Host ""

        if ($Status -eq 0) {
            Write-Host "Setup completed successfully" -ForegroundColor Green
        } elseif ($Status -eq 2) {
            Write-Host "Setup cancelled" -ForegroundColor Yellow
        } else {
            Write-Host "Setup failed" -ForegroundColor Red
        }

        if (-not [string]::IsNullOrWhiteSpace($ActionLabel)) {
            Write-Host ("Action: {0}" -f $ActionLabel) -ForegroundColor Gray
        }
        if (-not [string]::IsNullOrWhiteSpace($TargetPath)) {
            Write-Host ("Target: {0}" -f $TargetPath) -ForegroundColor Gray
        }
        if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
            Write-Host ("Source package: {0}" -f $PackagePath) -ForegroundColor DarkGray
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$global:VRCSETUP_LOGFILE)) {
            Write-Host ("Session log: {0}" -f $global:VRCSETUP_LOGFILE) -ForegroundColor DarkGray
        }

        Write-Host ""
        if ($Status -eq 0) {
            if ($CanLeavePartialProject) {
                Write-Host "Next step: open the created project in Unity and do a quick sanity check." -ForegroundColor Cyan
            } else {
                Write-Host "Next step: open the target project in Unity and verify packages/assets." -ForegroundColor Cyan
            }
        } elseif ($Status -eq 2) {
            if ($CanLeavePartialProject) {
                Write-Host "If a partial project remains on disk, use 'Setup project -> Cleanup incomplete projects' before retrying." -ForegroundColor Yellow
            } else {
                Write-Host "The target project was left in place. You can retry when ready." -ForegroundColor Yellow
            }
        } else {
            Write-Host "Review the messages above and the session log, then retry." -ForegroundColor Yellow
            if ($CanLeavePartialProject) {
                Write-Host "If the run stopped mid-creation, use 'Setup project -> Cleanup incomplete projects' before retrying." -ForegroundColor Yellow
            }
        }
    }

    function Invoke-UnityPackageExecution {
        param(
            $State,
            $Config,
            [string]$ConfigPath
        )

        if ($Config) {
            $Config | Add-Member -MemberType NoteProperty -Name "LastUnityPackagePath" -Value $State.PackagePath -Force
            if ($Config.Naming.RememberUnityPackageNames) {
                $Config.SavedProjectNames | Add-Member -MemberType NoteProperty -Name $State.PackagePath -Value $State.ProjectName -Force
            }
            Save-Config -Config $Config -ConfigPath $ConfigPath
        }

        Clear-Host

        $status = 1
        $canLeavePartialProject = ($State.ExistingAction -eq 'create-new' -or $State.ExistingAction -eq 'overwrite')
        switch ($State.ExistingAction) {
            'overwrite' {
                $confirmDel = Show-Menu -Title "Confirm delete" -Header (Add-ConfirmHint -Header ("This will DELETE the existing folder:`n{0}`n`nContinue?" -f $State.TargetProjectPath)) -Options @("Delete and recreate", "Cancel") -AllowCancel $false
                if ($confirmDel -ne 0) { return [pscustomobject]@{ Executed = $false; Status = $null } }
                Clear-Host
                $status = Start-Installer -projectPath $State.PackagePath -NewProjectName $State.ProjectName -OverwriteExistingProject
            }
            'use-existing-vpm' {
                $status = Start-Installer -projectPath $State.TargetProjectPath
            }
            'use-existing-extras' {
                $status = Start-Installer -projectPath $State.TargetProjectPath -ImportExtras -ExcludeUnityPackagePath $State.PackagePath
            }
            default {
                $status = Start-Installer -projectPath $State.PackagePath -NewProjectName $State.ProjectName
            }
        }

        Show-SetupOutcomeSummary -Status $status -ActionLabel $State.ActionLabel -TargetPath $State.TargetProjectPath -PackagePath $State.PackagePath -CanLeavePartialProject:$canLeavePartialProject
        Read-Host "Press ENTER to return" | Out-Null
        return [pscustomobject]@{ Executed = $true; Status = $status }
    }

    function Invoke-UnityPackageSetupFlow {
        param(
            $Config,
            [string]$ConfigPath
        )

        $state = New-UnityPackageFlowState

        while ($true) {
            if (-not $state.PackagePath) {
                if (-not (Prompt-UnityPackagePackageStep -State $state)) { return }
            }

            [void](Prompt-UnityPackageIdentityStep -State $state -Config $Config)
            if (-not (Prompt-UnityPackageExistingActionStep -State $state)) {
                continue
            }

            while ($true) {
                Update-UnityPackageFlowState -State $state -Config $Config

                $options = @("Start setup", "Change project name")
                if (-not [string]::IsNullOrWhiteSpace([string]$state.TargetProjectPath) -and (Test-Path ([string]$state.TargetProjectPath))) {
                    $options += "Change existing target action"
                }
                $options += @("Choose another UnityPackage", "Cancel")

                $choice = Show-Menu -Title "UnityPackage setup" -Header (Get-UnityPackageReviewHeader -State $state) -Options $options
                if ($choice -eq -1) { return }

                $picked = $options[$choice]
                if ($picked -eq "Start setup") {
                    $executionResult = Invoke-UnityPackageExecution -State $state -Config $Config -ConfigPath $ConfigPath
                    if (-not $executionResult.Executed) {
                        continue
                    }
                    return
                }
                if ($picked -eq "Change project name") {
                    break
                }
                if ($picked -eq "Change existing target action") {
                    if (-not (Prompt-UnityPackageExistingActionStep -State $state)) {
                        break
                    }
                    continue
                }
                if ($picked -eq "Choose another UnityPackage") {
                    $state = New-UnityPackageFlowState
                    break
                }
                return
            }
        }
    }

    $setupChoice = Show-Menu -Title "Setup project" -Header "Choose what you're starting from:" -Options @(
        "UnityPackage (.unitypackage) -> create new project",
        "Existing Unity project folder",
        "Cleanup incomplete projects",
        "Back"
    )

    if ($setupChoice -eq -1 -or $setupChoice -eq 3) { return }

    $config = $null
    if (Test-Path $ConfigPath) { $config = Load-Config -ConfigPath $ConfigPath }
    if ($config) { $config = Ensure-ConfigDefaults -Config $config }

    if ($setupChoice -eq 2) {
        Cleanup-IncompleteProjectsFlow -Config $config
        return
    }

    # Gate: check essential paths exist before proceeding with project creation
    $essentials = Test-ConfigEssentialsExist -Config $config
    if (-not $essentials.Ready) {
        # Try auto-fix: if Unity Editor is missing but can be auto-detected, set it now
        $editorVal = [string]$config.UnityEditorPath
        if ([string]::IsNullOrWhiteSpace($editorVal) -or -not (Test-Path $editorVal)) {
            $autoFound = @((Find-UnityEditorPaths))
            if ($autoFound.Count -eq 1) {
                # Single install found: auto-set it
                $config | Add-Member -MemberType NoteProperty -Name "UnityEditorPath" -Value $autoFound[0].Path -Force
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            elseif ($autoFound.Count -gt 1) {
                # Multiple installs: let user pick quickly
                $editorOptions = @()
                foreach ($f in $autoFound) { $editorOptions += "$($f.Version) - $($f.Path)" }
                $editorOptions += "Cancel"
                $pick = Show-Menu -Title "Unity Editor required" -Header "Select your Unity Editor to continue:" -Options $editorOptions
                if ($pick -ge 0 -and $pick -lt $autoFound.Count) {
                    $config | Add-Member -MemberType NoteProperty -Name "UnityEditorPath" -Value $autoFound[$pick].Path -Force
                    Save-Config -Config $config -ConfigPath $ConfigPath
                } else {
                    return
                }
            }
            # Re-check after auto-fix attempt
            $essentials = Test-ConfigEssentialsExist -Config $config
        }
    }

    if (-not $essentials.Ready) {
        Clear-Host
        Write-Host "Cannot proceed - configuration issues found:" -ForegroundColor Red
        Write-Host ""
        foreach ($msg in $essentials.Missing) {
            Write-Host "  - ${msg}" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "Go to 'Advanced settings' from the main menu to fix these." -ForegroundColor Gray
        Read-Host "Press ENTER to continue" | Out-Null
        return
    }

    if ($setupChoice -eq 0) {
        Invoke-UnityPackageSetupFlow -Config $config -ConfigPath $ConfigPath
        return
    }

    if ($setupChoice -eq 1) {
        $projectPath = Read-WizardPathInput -Title "Existing Unity project" -TitleColor ([ConsoleColor]::Yellow) -Prompt "Project path" -BodyLines @(
            "Choose the Unity project folder you want to configure."
        )
        if ([string]::IsNullOrWhiteSpace($projectPath)) { return }
        if (-not (Test-Path $projectPath)) { Write-Host "Path not found: ${projectPath}" -ForegroundColor Red; Read-Host "Press ENTER"; return }

        $assetsPath = Join-Path $projectPath "Assets"
        if (-not (Test-Path $assetsPath)) { Write-Host "Not a Unity project (missing Assets)." -ForegroundColor Red; Read-Host "Press ENTER"; return }

        $confirmHeader = "Project folder: ${projectPath}`n\nProceed?"
        $confirm = Show-Menu -Title "Confirm" -Header (Add-ConfirmHint -Header $confirmHeader) -Options @("Proceed", "Cancel") -AllowCancel $false
        if ($confirm -ne 0) { return }

        # Avoid leftover TUI lines before starting installer output
        Clear-Host
        $status = Start-Installer -projectPath $projectPath
        Show-SetupOutcomeSummary -Status $status -ActionLabel "Configure existing project" -TargetPath $projectPath -PackagePath $null -CanLeavePartialProject:$false
        Read-Host "Press ENTER to return"
        return
    }
}

# --- Main launcher for interactive wizard ---
function Start-Wizard {
    # First-run: prompt for essential config (Unity editor path, projects root)
    Invoke-FirstRunSetup -ConfigPath $configPath

    while ($true) {
        # Check config state for warnings
        $menuConfig = $null
        if (Test-Path $configPath) { $menuConfig = Load-Config -ConfigPath $configPath }
        $essentials = Test-ConfigEssentialsExist -Config $menuConfig
        $setupLabel = "Setup project (UnityPackage or existing)"
        $header = "Use arrows + Enter. ESC goes back in submenus. Select Exit here to close."
        if (-not $essentials.Ready) {
            $setupLabel = "Setup project (UnityPackage or existing)  [!]"
            $warnings = ($essentials.Missing | ForEach-Object { "  - $_" }) -join "`n"
            $header = "WARNING: Some paths are missing or invalid:`n${warnings}`n  Go to 'Advanced settings' to fix.`n`n${header}"
        }

        $choice = Show-Menu -Title "VRChat Project Setup Wizard" -Header $header -Options @(
            $setupLabel,
            "Configure VPM packages",
            "Advanced settings",
            "Reset configuration",
            "Exit"
        )

        if ($choice -eq -1) { continue }

        switch ($choice) {
            0 {
                Setup-ProjectFlow -ConfigPath $configPath
            }
            1 {
                Edit-VpmPackages -ConfigPath $configPath -ScriptDir $scriptDir
            }
            2 {
                Advanced-NamingSettings -ConfigPath $configPath
            }
            3 {
                $confirm = Show-Menu -Title "Reset configuration" -Header (Add-ConfirmHint -Header "Reset config file?") -Options @("Yes, reset", "Cancel") -AllowCancel $false
                if ($confirm -eq 0) {
                    Clear-Host
                    Start-Installer -projectPath "-reset"
                    Read-Host "Press ENTER to continue"
                }
            }
            4 {
                Write-Host " Goodbye!" -ForegroundColor Cyan
                return
            }
        }
    }
}

