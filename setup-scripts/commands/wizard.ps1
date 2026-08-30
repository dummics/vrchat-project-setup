# VRChat Setup Wizard (commands/wizard.ps1) - modularized
# This file is a drop-in for vrcsetup-wizard.ps1. It contains the full wizard logic.

param()

# === CARICAMENTO CONFIG & HELPERS ===
$scriptDir = [System.IO.Directory]::GetParent($PSScriptRoot).FullName
. "${scriptDir}\lib\menu.ps1"
. "${scriptDir}\lib\utils.ps1"
. "${scriptDir}\lib\config.ps1"
. "${scriptDir}\lib\vpm.ps1"
. "${scriptDir}\lib\vrcget.ps1"
. "${scriptDir}\lib\project-state.ps1"
. "${scriptDir}\lib\projects.ps1"
. "${scriptDir}\lib\spectre.ps1"
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
    if (Test-Path -LiteralPath $ConfigPath) { $config = Load-Config -ConfigPath $ConfigPath }
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
    $needEditor = [string]::IsNullOrWhiteSpace($editorPath) -or -not (Test-Path -LiteralPath $editorPath)
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
                $manualPath = Read-WizardPathInput -Title 'Unity Editor' -Prompt 'Unity.exe path' -BodyLines @('Drag Unity.exe here or paste its full path.')
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
            $manualPath = Read-WizardPathInput -Title 'Unity Editor' -Prompt 'Unity.exe path' -BodyLines @('No Unity installations were found automatically.', 'Drag Unity.exe here or paste its full path.')
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
        $inputRoot = Read-WizardPathInput -Title 'Projects folder' -Prompt 'Folder path' -BodyLines @('Step 2/2: Where should new projects be created?', 'Drag a folder here or paste its path.')

        if (-not [string]::IsNullOrWhiteSpace($inputRoot)) {
            if (-not (Test-Path -LiteralPath $inputRoot -PathType Container)) {
                if (Test-Path -LiteralPath $inputRoot -PathType Leaf) {
                    Show-SetupScreen -EditorValue $editorPath -ProjectsRootValue "" -ActiveStep "projects" -StatusMessage "That path is a file. Choose a folder for new projects."
                    Start-Sleep -Seconds 2
                    $inputRoot = ""
                }
                elseif (-not (Test-Path -LiteralPath $inputRoot)) {
                    $mkChoice = Show-Menu -Title "Folder not found" -Header "Create folder?`n${inputRoot}" -Options @("Create it", "Skip")
                    if ($mkChoice -eq 0) {
                        try {
                            New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null
                        } catch {
                            Show-SetupScreen -EditorValue $editorPath -ProjectsRootValue "" -ActiveStep "projects" -StatusMessage "Failed to create folder."
                            Start-Sleep -Seconds 2
                            $inputRoot = ""
                        }
                        if (-not [string]::IsNullOrWhiteSpace($inputRoot) -and -not (Test-Path -LiteralPath $inputRoot -PathType Container)) {
                            $inputRoot = ""
                        }
                    }
                    else {
                        $inputRoot = ""
                    }
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
    $spectreNotice = Get-Command -Name 'Show-VrcSetupSpectreNotice' -ErrorAction SilentlyContinue
    if ($spectreNotice) {
        $noticeDetails = if ($Details) { [string]$Details } else { '' }
        if (Show-VrcSetupSpectreNotice -Title $Title -Message $Message -Details $noticeDetails -Kind Error -ScriptDir $scriptDir) { return }
    }
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
    param(
        [string]$Path,
        [switch]$PreserveRelative
    )
    if ($null -eq $Path) { return $null }
    $p = $Path.Trim()
    $p = $p.Trim('"')
    $p = $p.Trim("'")
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }
    # PowerShell drag&drop can escape spaces and shell metacharacters
    # (for example "` " or "`&") in some hosts.
    # Remove those escape markers so Test-Path sees the real filesystem path.
    $p = $p -replace '`(?=[\s&()\[\]{}$;,])', ''
    $p = [Environment]::ExpandEnvironmentVariables($p)

    if ($p -eq '~') {
        $p = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    } elseif ($p.StartsWith('~\') -or $p.StartsWith('~/')) {
        $p = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) $p.Substring(2)
    }

    if ((-not $PreserveRelative) -and (-not [System.IO.Path]::IsPathRooted($p))) {
        $repoRoot = [System.IO.Directory]::GetParent($scriptDir).FullName
        $p = Join-Path $repoRoot $p
    }

    try { return [System.IO.Path]::GetFullPath($p) } catch { return $p }
}

function Read-WizardPathInput {
    param(
        [string]$Title,
        [string[]]$BodyLines = @(),
        [string]$Prompt = "Path",
        [ConsoleColor]$TitleColor = [ConsoleColor]::Cyan,
        [switch]$PreserveRelative,
        [string]$Hint = "Paste or drag the path here. Press ENTER to go back."
    )

    $spectreTextInput = Get-Command -Name 'Read-VrcSetupSpectreTextInput' -ErrorAction SilentlyContinue
    if ($spectreTextInput) {
        $body = @($BodyLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"
        $value = Read-VrcSetupSpectreTextInput -Title $Title -Header $body -Prompt $Prompt -Hint $Hint
        return Normalize-UserPath $value -PreserveRelative:$PreserveRelative
    }

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

    return Normalize-UserPath (Read-Host $Prompt) -PreserveRelative:$PreserveRelative
}

function Read-WizardTextInput {
    param(
        [string]$Title,
        [string]$Prompt,
        [string[]]$BodyLines = @(),
        [string]$Hint = 'Leave blank to clear or go back.'
    )

    $spectreTextInput = Get-Command -Name 'Read-VrcSetupSpectreTextInput' -ErrorAction SilentlyContinue
    if ($spectreTextInput) {
        $body = @($BodyLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"
        return Read-VrcSetupSpectreTextInput -Title $Title -Header $body -Prompt $Prompt -Hint $Hint
    }

    Clear-Host
    if ($Title) { Write-Host $Title -ForegroundColor Cyan; Write-Host '' }
    foreach ($line in $BodyLines) { Write-Host $line -ForegroundColor Gray }
    return Read-Host $Prompt
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

    if ($Config.PSObject.Properties.Name -notcontains 'Naming' -or -not $Config.Naming) {
        $Config | Add-Member -MemberType NoteProperty -Name "Naming" -Value ([pscustomobject]@{}) -Force
    }

    $namingProperties = @($Config.Naming.PSObject.Properties | ForEach-Object { $_.Name })
    if ($namingProperties -notcontains 'DefaultPrefix') { $Config.Naming | Add-Member -MemberType NoteProperty -Name "DefaultPrefix" -Value "" -Force }
    if ($namingProperties -notcontains 'DefaultSuffix') { $Config.Naming | Add-Member -MemberType NoteProperty -Name "DefaultSuffix" -Value "" -Force }
    if ($namingProperties -notcontains 'RegexRemovePatterns') { $Config.Naming | Add-Member -MemberType NoteProperty -Name "RegexRemovePatterns" -Value @() -Force }
    if ($namingProperties -notcontains 'RememberUnityPackageNames') { $Config.Naming | Add-Member -MemberType NoteProperty -Name "RememberUnityPackageNames" -Value $true -Force }

    if ($Config.PSObject.Properties.Name -notcontains 'SavedProjectNames' -or -not $Config.SavedProjectNames) {
        $Config | Add-Member -MemberType NoteProperty -Name "SavedProjectNames" -Value ([pscustomobject]@{}) -Force
    }

    if ($Config.PSObject.Properties.Name -notcontains 'UnityPackagesFolder') {
        # Optional: when set, installer imports all *.unitypackage found in that folder (in addition to the selected one).
        # When not set, extra-imports are disabled.
        $Config | Add-Member -MemberType NoteProperty -Name "UnityPackagesFolder" -Value $null -Force
    }

    if ($Config.PSObject.Properties.Name -notcontains 'ProjectLibrarySort' -or [string]$Config.ProjectLibrarySort -notin @('recent', 'name')) {
        $Config | Add-Member -MemberType NoteProperty -Name "ProjectLibrarySort" -Value 'recent' -Force
    }

    # DefaultPackages is the starter preset. It does not make packages immutable.
    $Config | Add-Member -MemberType NoteProperty -Name "DefaultPackages" -Value @(Get-DefaultPackages -Config $Config) -Force

    # Only the VRChat foundation is locked and restored when an older config is loaded.
    $Config | Add-Member -MemberType NoteProperty -Name "RequiredPackages" -Value @(Get-RequiredPackages -Config $Config) -Force
    $requiredPackageSet = Add-RequiredPackagesToSet -Packages $Config.VpmPackages -Config $Config
    $Config | Add-Member -MemberType NoteProperty -Name "VpmPackages" -Value $requiredPackageSet -Force

    if ($Config.PSObject.Properties.Name -notcontains 'FavoritePackages') {
        $Config | Add-Member -MemberType NoteProperty -Name 'FavoritePackages' -Value @(Get-VrcSetupFavoritePackages -Config $null) -Force
    } else {
        $Config.FavoritePackages = @(Get-VrcSetupFavoritePackages -Config $Config)
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

function Edit-ProjectNamingSettings {
    param(
        $Config,
        [string]$ConfigPath
    )

    while ($true) {
        $patterns = @($Config.Naming.RegexRemovePatterns)
        $remember = if ($Config.Naming.RememberUnityPackageNames) { 'On' } else { 'Off' }
        $choice = Show-Menu -Title 'Project names' -Header 'These preferences only affect new projects created from a UnityPackage.' -Options @(
            "Prefix: '$($Config.Naming.DefaultPrefix)'",
            "Suffix: '$($Config.Naming.DefaultSuffix)'",
            "Remember a chosen name: ${remember}",
            "Name cleanup rules: $($patterns.Count)",
            'Back'
        )
        if ($choice -lt 0 -or $choice -eq 4) { return }

        switch ($choice) {
            0 {
                $value = Read-WizardTextInput -Title 'Project name prefix' -Prompt 'Prefix' -BodyLines @('Optional text placed before the suggested project name.')
                $Config.Naming.DefaultPrefix = if ($value) { $value } else { '' }
                Save-Config -Config $Config -ConfigPath $ConfigPath
            }
            1 {
                $value = Read-WizardTextInput -Title 'Project name suffix' -Prompt 'Suffix' -BodyLines @('Optional text placed after the suggested project name.')
                $Config.Naming.DefaultSuffix = if ($value) { $value } else { '' }
                Save-Config -Config $Config -ConfigPath $ConfigPath
            }
            2 {
                $Config.Naming.RememberUnityPackageNames = -not $Config.Naming.RememberUnityPackageNames
                Save-Config -Config $Config -ConfigPath $ConfigPath
            }
            3 {
                while ($true) {
                    $patterns = @($Config.Naming.RegexRemovePatterns)
                    $options = @($patterns) + @('Add cleanup rule', 'Remove cleanup rule', 'Back')
                    $ruleChoice = Show-Menu -Title 'Name cleanup rules' -Header 'Optional rules remove text from an automatically suggested project name.' -Options $options
                    if ($ruleChoice -lt 0 -or $options[$ruleChoice] -eq 'Back') { break }

                    if ($options[$ruleChoice] -eq 'Add cleanup rule') {
                        $newPattern = Read-WizardTextInput -Title 'Add cleanup rule' -Prompt 'Regex rule' -BodyLines @('Only use this if you already know the text pattern you want removed.') -Hint 'Leave blank to return.'
                        if ([string]::IsNullOrWhiteSpace($newPattern)) { continue }
                        try { [void][regex]::new($newPattern) }
                        catch {
                            Show-WizardError -Title 'Invalid cleanup rule' -Message 'That regular expression cannot be used. Nothing was saved.'
                            continue
                        }
                        $Config.Naming.RegexRemovePatterns += @($newPattern)
                        Save-Config -Config $Config -ConfigPath $ConfigPath
                        continue
                    }

                    if ($options[$ruleChoice] -eq 'Remove cleanup rule') {
                        if ($patterns.Count -eq 0) { continue }
                        $removeIndex = Show-Menu -Title 'Remove cleanup rule' -Header 'Choose the rule to remove.' -Options ($patterns + @('Back'))
                        if ($removeIndex -ge 0 -and $removeIndex -lt $patterns.Count) {
                            $Config.Naming.RegexRemovePatterns = @($patterns | Where-Object { $_ -ne $patterns[$removeIndex] })
                            Save-Config -Config $Config -ConfigPath $ConfigPath
                        }
                    }
                }
            }
        }
    }
}

function Get-SettingsPathSummary {
    param(
        [string]$Path,
        [switch]$Invalid
    )

    if ($Invalid) { return 'Invalid' }
    if ([string]::IsNullOrWhiteSpace($Path)) { return 'Not set' }
    if (-not (Test-Path -LiteralPath $Path)) { return 'Not found' }
    return 'Ready'
}

function Advanced-NamingSettings {
    param(
        [string]$ConfigPath
    )

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Write-Host "Config not found. Run setup first." -ForegroundColor Red
        Read-Host "Press ENTER to continue"
        return
    }

    $config = Load-Config -ConfigPath $ConfigPath
    $config = Ensure-ConfigDefaults -Config $config

    while ($true) {
        $workspaceRoot = [System.IO.Directory]::GetParent($scriptDir).FullName
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
        $editorInvalid = $false
        # Extra check: if editor path exists but isn't Unity.exe, flag it
        if (-not [string]::IsNullOrWhiteSpace([string]$config.UnityEditorPath) -and (Test-Path -LiteralPath ([string]$config.UnityEditorPath))) {
            $editorValidation = Test-UnityEditorPath -Path ([string]$config.UnityEditorPath)
            if (-not $editorValidation.Valid) {
                $editorStatus = "INVALID: $($editorValidation.Message)"
                $editorInvalid = $true
            }
        }
        $projectsRootStatus = Get-PathStatus -Path ([string]$config.UnityProjectsRoot)
        $editorSummary = Get-SettingsPathSummary -Path ([string]$config.UnityEditorPath) -Invalid:$editorInvalid
        $projectsRootSummary = Get-SettingsPathSummary -Path ([string]$config.UnityProjectsRoot)
        $commonPackagesSummary = if ($commonPackagesStatus -eq 'DISABLED') {
            'Disabled'
        } else {
            Get-SettingsPathSummary -Path $resolvedCommon
        }

        $sel = Show-Menu -Title 'Settings' -Header 'Unity and project folders are used everywhere. The other choices affect new projects.' -SectionBreaks @(2) -Options @(
            "Unity Editor  ·  ${editorSummary}",
            "Project folder  ·  ${projectsRootSummary}",
            'Project names',
            "Extra UnityPackages  ·  ${commonPackagesSummary}",
            'Reset all settings',
            'Back'
        )

        if ($sel -eq -1 -or $sel -eq 5) { Save-Config -Config $config -ConfigPath $ConfigPath; return }

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
                    $newPath = Read-WizardPathInput -Title 'Unity Editor' -Prompt 'Unity.exe path' -BodyLines @('Paste or drag the Unity.exe file here.')
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
                $newRoot = Read-WizardPathInput -Title 'Project folder' -Prompt 'Projects folder' -BodyLines @("Current: ${projectsRootStatus}", 'Paste or drag the folder where you keep VRChat Unity projects.')
                if (-not [string]::IsNullOrWhiteSpace($newRoot)) {
                    if (-not (Test-Path -LiteralPath $newRoot -PathType Container)) {
                        if (Test-Path -LiteralPath $newRoot -PathType Leaf) {
                            Show-WizardError -Title 'A folder is required' -Message 'This path is a file. Choose the folder where you keep VRChat Unity projects.'
                            continue
                        }
                        $mkChoice = Show-Menu -Title "Folder not found" -Header "Create folder?`n${newRoot}" -Options @("Create it", "Cancel")
                        if ($mkChoice -eq 0) {
                            try {
                                New-Item -ItemType Directory -Path $newRoot -Force | Out-Null
                            } catch {
                                Write-Host "Failed to create folder: ${_}" -ForegroundColor Red
                                Read-Host "Press ENTER to continue" | Out-Null
                                continue
                            }
                            if (-not (Test-Path -LiteralPath $newRoot -PathType Container)) {
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
                Edit-ProjectNamingSettings -Config $config -ConfigPath $ConfigPath
            }
            3 {
                $inputPath = Read-WizardPathInput -Title "UnityPackages folder (extra imports)" -Prompt "Folder path" -PreserveRelative -BodyLines @(
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

                if (-not (Test-Path -LiteralPath $resolved)) {
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
            4 {
                $confirm = Show-Menu -Title 'Reset all settings' -Header (Add-ConfirmHint -Header 'This restores the initial configuration and clears saved paths.') -Options @('Reset settings', 'Cancel') -AllowCancel $false
                if ($confirm -eq 0) {
                    Clear-Host
                    Start-Installer -projectPath "-reset" | Out-Null
                    [void](Initialize-ConfigIfMissing -ConfigPath $ConfigPath -DefaultsPath $defaultsPath)
                    Invoke-FirstRunSetup -ConfigPath $ConfigPath
                    return
                }
            }
        }
    }
}

function Select-VpmVersion {
    param(
        [string]$PackageName,
        [string]$CurrentVersion,
        [object]$AvailableVersions
    )

    $available = if ($PSBoundParameters.ContainsKey('AvailableVersions')) {
        @($AvailableVersions)
    } else {
        $vrcGetVersions = @(Get-VrcGetAvailableVersions -PackageName $PackageName -ScriptDir $scriptDir)
        if ($vrcGetVersions.Count -gt 0) { @($vrcGetVersions) } else { @(Get-VpmAvailableVersions -PackageName $PackageName) }
    }

    while ($true) {
        $friendlyName = Get-VrcSetupFriendlyPackageName -PackageName $PackageName
        $currentChoice = if ($CurrentVersion -eq 'latest') { 'Newest compatible version' } elseif ($CurrentVersion) { $CurrentVersion } else { 'Not included' }
        $header = "Package: ${friendlyName}`nCurrent choice: ${currentChoice}`n"
        if ($available.Count -gt 0) {
            $header += "Versions available: $($available.Count)`nScroll the list or start typing to jump to a version."
        }
        else {
            $header += 'No versions were found. You can still enter one manually.'
        }

        $latestOption = 'Use the newest compatible version'
        $manualOption = 'Enter a version manually'
        $backOption = 'Back'
        $options = @($latestOption, $manualOption) + @($available) + @($backOption)
        $sel = Show-Menu -Title 'Choose version' -Header $header -PromptTitle 'Version' -Options $options -MaxVisible 18
        if ($sel -eq -1) { return $null }

        $picked = $options[$sel]
        if ($picked -eq $backOption) { return $null }
        if ($picked -eq $latestOption) { return 'latest' }
        if ($picked -eq $manualOption) {
            $manual = Read-WizardTextInput -Title 'Enter package version' -Prompt 'Version' -BodyLines @("Package: ${friendlyName}", "Current choice: ${currentChoice}") -Hint 'Enter Confirm  ·  Esc Back'
            if ([string]::IsNullOrWhiteSpace($manual)) { return $null }
            $manual = $manual.Trim()
            if ($PSBoundParameters.ContainsKey('AvailableVersions') -and $manual -ne 'latest' -and $available -notcontains $manual) {
                Show-WizardError -Title 'Version not available for this SDK' -Message 'Choose a version shared by every VRChat SDK component in this project.' -Details "Requested: ${manual}"
                continue
            }
            return $manual
        }

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
            $query = Read-WizardTextInput -Title 'Find a package' -Prompt 'Search or package ID' -BodyLines @('Search by name or paste an exact package ID.') -Hint 'Enter Search  ·  Esc Back'
        }
        $query = $query.Trim()
        if ([string]::IsNullOrWhiteSpace($query)) { return $null }

        $found = @(Search-VrcGetPackages -Query $query -ScriptDir $ScriptDir)
        if ($found.Count -eq 0) {
            Show-WizardError -Title "No matches" -Message "No packages matched '${query}'. Try another name or paste the exact package ID."

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
                $manual = Read-WizardTextInput -Title 'Enter package ID' -Prompt 'Package ID' -BodyLines @('Paste the exact package ID from its repository.') -Hint 'Enter Confirm  ·  Esc Back'
                if ($null -eq $manual) { return $null }
                return $manual.Trim()
            }
            return $null
        }

        $picked = @(Select-VrcSetupPackageResults -Items $found -Title 'Search results' -Header "Results for '${query}'. Choose one package." -SingleSelection:$true)
        if ($picked.Count -gt 0) { return [string]$picked[0].Id }

        $nextChoice = Show-Menu -Title 'Package search' -Header 'Search again, paste an exact package ID, or return.' -Options @($SearchOption, $ManualOption, 'Back')
        if ($nextChoice -eq 0) { $query = $null; continue }
        if ($nextChoice -eq 1) {
            $manual = Read-WizardTextInput -Title 'Enter package ID' -Prompt 'Package ID' -BodyLines @('Paste the exact package ID from its repository.') -Hint 'Enter Confirm  ·  Esc Back'
            if ($null -eq $manual) { return $null }
            return $manual.Trim()
        }
        return $null
    }
}

function Format-VrcSetupPackageCell {
    param(
        [AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][int]$Width
    )

    $value = ([string]$Text -replace '[\r\n]+', ' ').Trim()
    if ($value.Length -gt $Width) {
        $value = $value.Substring(0, [Math]::Max(0, $Width - 3)) + '...'
    }
    return $value.PadRight($Width)
}

function Get-VrcSetupFriendlyPackageName {
    param([Parameter(Mandatory)][string]$PackageName)

    $knownNames = @{
        'com.vrchat.base' = 'VRChat SDK Base'
        'com.vrchat.avatars' = 'VRChat SDK Avatars'
        'com.vrchat.worlds' = 'VRChat SDK Worlds'
        'com.vrchat.core.vpm-resolver' = 'VRChat Package Resolver'
        'com.vrcfury.vrcfury' = 'VRCFury'
        'com.poiyomi.toon' = 'Poiyomi Toon Shader'
        'adjerry91.vrcft.templates' = 'VRCFury Templates'
        'dev.foxscore.easy-login' = 'Easy Login'
        'gogoloco' = 'GoGo Loco'
    }
    if ($knownNames.ContainsKey($PackageName)) { return [string]$knownNames[$PackageName] }

    $leaf = @($PackageName -split '\.')[-1]
    if ([string]::IsNullOrWhiteSpace($leaf)) { return $PackageName }
    return [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase(($leaf -replace '[-_]+', ' '))
}

function Format-VrcSetupPackageRow {
    param(
        [string]$Role = '',
        [AllowEmptyString()][string]$DisplayName = '',
        [Parameter(Mandatory)][string]$PackageName,
        [AllowEmptyString()][string]$Version = ''
    )

    $friendlyName = if ([string]::IsNullOrWhiteSpace($DisplayName)) {
        Get-VrcSetupFriendlyPackageName -PackageName $PackageName
    } else {
        $DisplayName
    }
    if ([string]::IsNullOrWhiteSpace($Role)) {
        return ('{0}  {1}  {2}' -f
            (Format-VrcSetupPackageCell -Text $friendlyName -Width 28),
            (Format-VrcSetupPackageCell -Text $PackageName -Width 38),
            (Format-VrcSetupPackageCell -Text $Version -Width 14)).TrimEnd()
    }
    return ('{0}  {1}  {2}  {3}' -f
        (Format-VrcSetupPackageCell -Text $Role -Width 9),
        (Format-VrcSetupPackageCell -Text $friendlyName -Width 28),
        (Format-VrcSetupPackageCell -Text $PackageName -Width 38),
        (Format-VrcSetupPackageCell -Text $Version -Width 14)).TrimEnd()
}

function Select-VrcSetupPackageResults {
    param(
        [Parameter(Mandatory)]$Items,
        [string]$Title = 'Choose packages',
        [string]$Header = 'Select everything you want to add. New packages use their latest version.',
        [bool]$DefaultSelected = $false,
        [bool]$SingleSelection = $false
    )

    $itemsArray = @($Items)
    if ($itemsArray.Count -eq 0) { return @() }
    if (Get-Command -Name 'Show-VrcSetupSpectrePackageChecklist' -ErrorAction SilentlyContinue) {
        return @(Show-VrcSetupSpectrePackageChecklist -Items $itemsArray -Title $Title -Header $Header -DefaultSelected:$DefaultSelected -SingleSelection:$SingleSelection -MaxVisible 14 -ScriptDir $scriptDir)
    }
    return @(Show-ChecklistPaged -Title $Title -PromptTitle 'Select packages' -Header $Header -Items $itemsArray -DefaultSelected:$DefaultSelected -MaxVisible 14 -ToLabel {
        param($match, $index)
        Format-VrcSetupPackageRow -DisplayName ([string]$match.DisplayName) -PackageName ([string]$match.Id) -Version ([string]$match.LatestVersion)
    })
}

function Find-VrcSetupPackages {
    param(
        [string[]]$ExcludePackageNames = @(),
        [string]$Title = 'Find a package',
        [string]$Header = 'Search by name or paste one or more exact package IDs separated by commas.'
    )

    $query = Read-WizardTextInput -Title $Title -Prompt 'Search or package ID' -BodyLines @($Header) -Hint 'Enter Search  ·  Esc Back'
    if ([string]::IsNullOrWhiteSpace($query)) { return @() }
    $query = $query.Trim()
    $enteredIds = @($query -split '[,;\r\n]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
    $exactIds = @()
    if ($enteredIds.Count -gt 1) {
        $exactIds = $enteredIds
    } elseif ($enteredIds.Count -eq 1 -and (Test-VpmPackageExists -PackageName $enteredIds[0] -ScriptDir $scriptDir)) {
        $exactIds = $enteredIds
    }

    if ($exactIds.Count -gt 0) {
        $newIds = @($exactIds | Where-Object { $ExcludePackageNames -notcontains $_ })
        if ($newIds.Count -eq 0) {
            Show-WizardError -Title 'Already included' -Message 'Every package you entered is already in this list.'
            return @()
        }
        $invalid = @($newIds | Where-Object { -not (Test-VpmPackageExists -PackageName $_ -ScriptDir $scriptDir) })
        if ($invalid.Count -gt 0) {
            Show-WizardError -Title 'Package not found' -Message ("Could not find: {0}" -f ($invalid -join ', '))
            return @()
        }
        return $newIds
    }

    if ([string]::IsNullOrWhiteSpace((Get-VrcGetExecutablePath -ScriptDir $scriptDir))) {
        Show-WizardError -Title 'Package search unavailable' -Message 'Paste the exact package ID instead.'
        return @()
    }
    $allMatches = @(Search-VrcGetPackages -Query $query -ScriptDir $scriptDir)
    if ($allMatches.Count -eq 0) {
        Show-WizardError -Title 'No packages found' -Message "No results for '${query}'. Try another name or paste the exact package ID."
        return @()
    }
    $availableMatches = @($allMatches | Where-Object { $ExcludePackageNames -notcontains [string]$_.Id })
    if ($availableMatches.Count -eq 0) {
        $matchedNames = @($allMatches | ForEach-Object { if ($_.DisplayName) { [string]$_.DisplayName } else { Get-VrcSetupFriendlyPackageName -PackageName ([string]$_.Id) } } | Select-Object -Unique)
        Show-WizardError -Title 'Already included' -Message "The matching package is already available here." -Details ($matchedNames -join ', ')
        return @()
    }
    $selected = @(Select-VrcSetupPackageResults -Items $availableMatches -Title 'Search results' -Header "Results for '${query}'. Select everything you want to add.")
    return @($selected | ForEach-Object { [string]$_.Id } | Select-Object -Unique)
}

function Get-VrcSetupFavoritePackageResults {
    param($Config)

    $results = @()
    foreach ($packageId in @(Get-VrcSetupFavoritePackages -Config $Config)) {
        $results += [pscustomobject]@{
            Id = [string]$packageId
            DisplayName = Get-VrcSetupFriendlyPackageName -PackageName ([string]$packageId)
            LatestVersion = 'Newest'
            Description = ''
        }
    }
    return @($results)
}

function Manage-VrcSetupFavoritePackages {
    param($Config, [string]$ConfigPath)

    while ($true) {
        $favorites = @(Get-VrcSetupFavoritePackages -Config $Config)
        $choice = Show-Menu -Title 'Favorite packages' -Header "Keep frequently used packages one shortcut away.`nSaved favorites: $($favorites.Count)" -Options @('Add favorites', 'Remove favorites', 'Back')
        if ($choice -lt 0 -or $choice -eq 2) { return }
        if ($choice -eq 0) {
            $newIds = @(Find-VrcSetupPackages -ExcludePackageNames $favorites -Title 'Add favorites' -Header 'Find packages you want available from every project.')
            if ($newIds.Count -eq 0) { continue }
            $Config.FavoritePackages = @($favorites + $newIds | Select-Object -Unique)
            Save-Config -Config $Config -ConfigPath $ConfigPath
            continue
        }
        if ($favorites.Count -eq 0) {
            Show-WizardError -Title 'No favorites yet' -Message 'Add a favorite first.'
            continue
        }
        $favoriteResults = @(Get-VrcSetupFavoritePackageResults -Config $Config)
        $toRemove = @(Select-VrcSetupPackageResults -Items $favoriteResults -Title 'Remove favorites' -Header 'Select the favorites you no longer need.')
        if ($toRemove.Count -eq 0) { continue }
        $removeIds = @($toRemove | ForEach-Object { [string]$_.Id })
        $Config.FavoritePackages = @($favorites | Where-Object { $removeIds -notcontains $_ })
        Save-Config -Config $Config -ConfigPath $ConfigPath
    }
}

function Select-VrcSetupFavoritePackagesForProject {
    param($Config, [string]$ConfigPath, $CurrentPackages)

    $choice = Show-Menu -Title 'Favorite packages' -Header 'Quickly add your usual packages, or change this list.' -Options @('Add favorites to this project', 'Manage favorites', 'Back')
    if ($choice -eq 1) { Manage-VrcSetupFavoritePackages -Config $Config -ConfigPath $ConfigPath; return @() }
    if ($choice -ne 0) { return @() }
    $currentNames = @($CurrentPackages.PSObject.Properties | ForEach-Object { $_.Name })
    $available = @(Get-VrcSetupFavoritePackageResults -Config $Config | Where-Object { $currentNames -notcontains [string]$_.Id })
    if ($available.Count -eq 0) {
        Show-WizardError -Title 'Favorites already included' -Message 'Every favorite package is already part of this project.'
        return @()
    }
    $selected = @(Select-VrcSetupPackageResults -Items $available -Title 'Favorite packages' -Header 'Select the favorites to add to this project.')
    return @($selected | ForEach-Object { [string]$_.Id } | Select-Object -Unique)
}

function Edit-VpmPackages {
    param(
        [string]$ConfigPath,
        [string]$ScriptDir,
        $ConfigObject,
        [switch]$WorkingCopy
    )

    if ((-not $ConfigObject) -and (-not (Test-Path -LiteralPath $ConfigPath))) {
        Write-Host "Config not found. Run setup first." -ForegroundColor Red
        Read-Host "Press ENTER to continue"
        return
    }

    $config = if ($ConfigObject) { $ConfigObject } else { Load-Config -ConfigPath $ConfigPath }
    if (-not $config) {
        Write-Host "Unable to load config." -ForegroundColor Red
        Read-Host "Press ENTER to continue"
        return
    }
    $config = Ensure-ConfigDefaults -Config $config

    if ($config.VpmPackages -is [System.Array]) {
        $newPackages = @{}
        foreach ($pkg in $config.VpmPackages) { $newPackages[$pkg] = "latest" }
        $config.VpmPackages = [pscustomobject]$newPackages
    }
    if (-not $config.VpmPackages) {
        $config | Add-Member -MemberType NoteProperty -Name "VpmPackages" -Value ([pscustomobject]@{ "com.vrchat.base" = "latest" }) -Force
    }

    while ($true) {
        # Keep the locked VRChat foundation together and first.  Optional tools
        # remain alphabetical immediately after it, so the daily package view is
        # predictable without making removable packages look locked.
        $packagesList = @(Get-OrderedVpmPackageProperties -Packages $config.VpmPackages -Config $config)
        $pkgOptions = @()
        foreach ($pkg in $packagesList) {
            $isRequired = Test-IsRequiredPackage -PackageName $pkg.Name -Config $config
            $kind = if ($isRequired) { 'Required' } else { 'Optional' }
            $pkgOptions += (Format-VrcSetupPackageRow -Role $kind -PackageName $pkg.Name -Version ([string]$pkg.Value))
        }
        $pkgOptions += @('Add package', 'Favorite packages', 'Back')

        $header = if ($WorkingCopy) {
            "Required packages are first and cannot be removed. Optional packages are below them.`nMake changes, then go Back to review the project update.`n`nRole       Package name                  Package ID                              Version"
        } else {
            "Required packages are first and cannot be removed. Optional packages can be changed or removed.`nThis set is used for new projects.`n`nRole       Package name                  Package ID                              Version"
        }
        $selected = Show-Menu -Title "VPM Packages" -Header $header -Options $pkgOptions
        if ($selected -eq -1) {
            if ($WorkingCopy) { return $config.VpmPackages }
            return
        }

        $picked = $pkgOptions[$selected]
        if ($picked -eq "Back") {
            if ($WorkingCopy) { return $config.VpmPackages }
            return
        }

        if ($picked -eq 'Favorite packages') {
            if ($WorkingCopy) {
                Show-WizardError -Title 'Favorites are personal' -Message 'Manage favorites from the main package settings or from a project with F.'
            } else {
                Manage-VrcSetupFavoritePackages -Config $config -ConfigPath $ConfigPath
            }
            continue
        }

        if ($picked -eq "Add package") {
            $existingNames = @($config.VpmPackages.PSObject.Properties | ForEach-Object { $_.Name })
            $newPackages = @(Find-VrcSetupPackages -ExcludePackageNames $existingNames -Title 'Add to my package set' -Header 'Search by name or paste exact package IDs. Selected packages use their newest compatible version.')
            if ($newPackages.Count -eq 0) { continue }
            $config.VpmPackages = Add-VrcSetupPackagesAtLatest -Packages $config.VpmPackages -PackageNames $newPackages
            if (-not $WorkingCopy) { Save-Config -Config $config -ConfigPath $ConfigPath }
            continue
        }

        # A real package selected
        $pkgProp = $packagesList[$selected]
        $pkgName = $pkgProp.Name
        $pkgVersion = $pkgProp.Value
        $isRequiredPkg = Test-IsRequiredPackage -PackageName $pkgName -Config $config

        if ($isRequiredPkg) {
            $action = Show-Menu -Title "Required package" -Header "${pkgName}`nCurrent version: ${pkgVersion}`nThis package is part of the VRChat foundation and stays installed." -Options @('Change version', 'Back')
            if ($action -eq -1 -or $action -eq 1) { continue }

            if ($action -eq 0) {
                if (-not (Test-VpmPackageExists -PackageName $pkgName -ScriptDir $ScriptDir)) {
                    $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                    Show-WizardError -Title "Package not found" -Message "Package not found / not resolvable: ${pkgName}" -Details $tail
                    continue
                }

                $sdkNames = @(Get-VrcSetupSdkPackageNames | Where-Object { $config.VpmPackages.PSObject.Properties.Name -contains $_ })
                $selectParams = @{ PackageName = $pkgName; CurrentVersion = $pkgVersion }
                if (Test-IsVrcSetupSdkPackage -PackageName $pkgName) {
                    $selectParams.AvailableVersions = @(Get-VrcSetupCommonPackageVersions -PackageNames $sdkNames -ScriptDir $ScriptDir)
                }
                $newVersion = Select-VpmVersion @selectParams
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

                $config.VpmPackages = Set-VrcSetupPackageVersion -Packages $config.VpmPackages -PackageName $pkgName -Version $newVersion
                if (-not $WorkingCopy) { Save-Config -Config $config -ConfigPath $ConfigPath }
                continue
            }
        } else {
            $action = Show-Menu -Title 'Optional package' -Header "${pkgName}`nCurrent version: ${pkgVersion}" -Options @('Change version', 'Remove package', 'Back')
            if ($action -eq -1 -or $action -eq 2) { continue }
        }

        if ($action -eq 0) {
            if (-not (Test-VpmPackageExists -PackageName $pkgName -ScriptDir $ScriptDir)) {
                $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                Show-WizardError -Title "Package not found" -Message "Package not found / not resolvable: ${pkgName}" -Details $tail
                continue
            }

            $selectParams = @{ PackageName = $pkgName; CurrentVersion = $pkgVersion }
            if (Test-IsVrcSetupSdkPackage -PackageName $pkgName) {
                $sdkNames = @(Get-VrcSetupSdkPackageNames | Where-Object { $config.VpmPackages.PSObject.Properties.Name -contains $_ })
                $selectParams.AvailableVersions = @(Get-VrcSetupCommonPackageVersions -PackageNames $sdkNames -ScriptDir $ScriptDir)
            }
            $newVersion = Select-VpmVersion @selectParams
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

            $config.VpmPackages = Set-VrcSetupPackageVersion -Packages $config.VpmPackages -PackageName $pkgName -Version $newVersion
            if (-not $WorkingCopy) { Save-Config -Config $config -ConfigPath $ConfigPath }
            continue
        }

        if ($action -eq 1) {
            $confirm = Show-Menu -Title "Remove package" -Header (Add-ConfirmHint -Header "Remove ${pkgName}?") -Options @("Yes, remove", "Cancel") -AllowCancel $false
            if ($confirm -eq 0) {
                $config.VpmPackages.PSObject.Properties.Remove($pkgName)
                if (-not $WorkingCopy) { Save-Config -Config $config -ConfigPath $ConfigPath }
            }
            continue
        }
    }
}

function Set-VrcSetupOptionalPackageSelection {
    param(
        [Parameter(Mandatory)]$Packages,
        [string[]]$SelectedPackageNames,
        $Config
    )

    $selected = @{}
    foreach ($packageName in @($SelectedPackageNames)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$packageName)) {
            $selected[[string]$packageName] = $true
        }
    }

    $result = Copy-VpmPackageSet -Packages $Packages
    $optionalNames = @(
        $result.PSObject.Properties |
            Where-Object { -not (Test-IsRequiredPackage -PackageName $_.Name -Config $Config) } |
            ForEach-Object Name
    )
    foreach ($packageName in $optionalNames) {
        if (-not $selected.ContainsKey($packageName)) {
            $result.PSObject.Properties.Remove($packageName)
        }
    }
    return Add-RequiredPackagesToSet -Packages $result -Config $Config
}

function Add-VrcSetupPackagesAtLatest {
    param(
        [Parameter(Mandatory)]$Packages,
        [Parameter(Mandatory)][string[]]$PackageNames
    )

    $result = Copy-VpmPackageSet -Packages $Packages
    foreach ($packageName in @($PackageNames)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$packageName)) {
            $result | Add-Member -MemberType NoteProperty -Name ([string]$packageName) -Value 'latest' -Force
        }
    }
    return $result
}

function Set-VrcSetupPackagesToLatest {
    param(
        [Parameter(Mandatory)]$Packages,
        [Parameter(Mandatory)][string[]]$PackageNames
    )

    $result = Copy-VpmPackageSet -Packages $Packages
    foreach ($packageName in @($PackageNames)) {
        if ($result.PSObject.Properties.Name -contains $packageName) {
            $result.($packageName) = 'latest'
        }
    }
    return $result
}

function Merge-VrcSetupPackageSets {
    param(
        [Parameter(Mandatory)]$BasePackages,
        [Parameter(Mandatory)]$PackagesToAdd
    )

    $result = Copy-VpmPackageSet -Packages $BasePackages
    foreach ($package in @($PackagesToAdd.PSObject.Properties)) {
        $result | Add-Member -MemberType NoteProperty -Name ([string]$package.Name) -Value ([string]$package.Value) -Force
    }
    return $result
}

function Get-VrcSetupPackageWorkspaceItems {
    param(
        [Parameter(Mandatory)]$CurrentPackages,
        [Parameter(Mandatory)]$DesiredPackages,
        $Config
    )

    $names = @(
        @($CurrentPackages.PSObject.Properties.Name) + @($DesiredPackages.PSObject.Properties.Name) |
            Select-Object -Unique
    )
    $items = foreach ($packageName in $names) {
        $hasCurrent = $CurrentPackages.PSObject.Properties.Name -contains $packageName
        $hasDesired = $DesiredPackages.PSObject.Properties.Name -contains $packageName
        $currentVersion = if ($hasCurrent) { [string]$CurrentPackages.($packageName) } else { '' }
        $desiredVersion = if ($hasDesired) { [string]$DesiredPackages.($packageName) } else { '' }
        $required = Test-IsRequiredPackage -PackageName $packageName -Config $Config
        $status = if (-not $hasCurrent -and $hasDesired) {
            'Add'
        } elseif ($hasCurrent -and -not $hasDesired) {
            'Remove'
        } elseif ($currentVersion -ne $desiredVersion) {
            'Update'
        } elseif ($required) {
            'Required'
        } else {
            'Installed'
        }

        [pscustomobject]@{
            Name = [string]$packageName
            FriendlyName = Get-VrcSetupFriendlyPackageName -PackageName ([string]$packageName)
            CurrentVersion = $currentVersion
            DesiredVersion = $desiredVersion
            Required = [bool]$required
            Status = $status
        }
    }

    $foundationOrder = @('com.vrchat.base', 'com.vrchat.avatars', 'com.vrchat.worlds', 'com.vrchat.core.vpm-resolver')
    return @($items | Sort-Object @{
        Expression = {
            $foundationIndex = [array]::IndexOf($foundationOrder, [string]$_.Name)
            if ($foundationIndex -ge 0) { return $foundationIndex }
            if ($_.Required) { return 10 }
            return 20
        }
    }, FriendlyName, Name)
}

function Format-VrcSetupPackageWorkspaceRow {
    param([Parameter(Mandatory)]$Item)

    $windowWidth = 120
    try { $windowWidth = [Console]::WindowWidth } catch { }
    $isNarrow = ($windowWidth -lt 100)
    $packageWidth = if ($isNarrow) { 24 } else { 32 }
    $versionWidth = if ($isNarrow) { 32 } else { 36 }
    $outcomeWidth = if ($isNarrow) { 12 } else { 18 }

    $installed = if ([string]::IsNullOrWhiteSpace([string]$Item.CurrentVersion)) { '' } else { [string]$Item.CurrentVersion }
    $selectedVersion = if ([string]::IsNullOrWhiteSpace([string]$Item.DesiredVersion)) { '' } elseif ([string]$Item.DesiredVersion -eq 'latest') { 'Newest' } else { [string]$Item.DesiredVersion }
    $version = if ($Item.Status -eq 'Update') { "${installed} -> ${selectedVersion}" } elseif ($Item.Status -eq 'Add') { $selectedVersion } else { $installed }
    $result = switch ([string]$Item.Status) {
        'Add' { $(if ($isNarrow) { 'Add' } else { 'Will be added' }); break }
        'Update' { $(if ($isNarrow) { 'Change' } else { 'Will change' }); break }
        'Remove' { $(if ($isNarrow) { 'Remove' } else { 'Will be removed' }); break }
        'Required' { $(if ($isNarrow) { 'Always' } else { 'Always included' }); break }
        default { 'Included' }
    }
    return ('{0}  {1}  {2}' -f
        (Format-VrcSetupPackageCell -Text ([string]$Item.FriendlyName) -Width $packageWidth),
        (Format-VrcSetupPackageCell -Text $version -Width $versionWidth),
        (Format-VrcSetupPackageCell -Text $result -Width $outcomeWidth)).TrimEnd()
}

function Invoke-VrcSetupInstallerWithProgress {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$Title = 'Working',
        [string]$Header = '',
        [string]$NewProjectName,
        [switch]$OverwriteExistingProject,
        [switch]$ImportExtras,
        [string]$ExcludeUnityPackagePath,
        $PackagesOverride,
        [switch]$SyncPackages
    )

    $installerParams = [ordered]@{ projectPath = $ProjectPath }
    if (-not [string]::IsNullOrWhiteSpace($NewProjectName)) { $installerParams.NewProjectName = $NewProjectName }
    if ($OverwriteExistingProject) { $installerParams.OverwriteExistingProject = $true }
    if ($ImportExtras) { $installerParams.ImportExtras = $true }
    if (-not [string]::IsNullOrWhiteSpace($ExcludeUnityPackagePath)) { $installerParams.ExcludeUnityPackagePath = $ExcludeUnityPackagePath }
    if ($null -ne $PackagesOverride) { $installerParams.PackagesOverride = $PackagesOverride }
    if ($SyncPackages) { $installerParams.SyncPackages = $true }

    $progressCommand = Get-Command -Name 'Invoke-VrcSetupSpectreOperation' -ErrorAction SilentlyContinue
    if ($progressCommand) {
        $operation = {
            param($runtimeRoot, $parameters)
            $env:VRCSETUP_PROGRESS_PLAIN = '1'
            $env:VRCSETUP_EMBEDDED_PROGRESS = '1'
            . (Join-Path $runtimeRoot 'commands\installer.ps1')
            $status = Start-Installer @parameters
            [pscustomobject]@{
                VrcSetupOperationResult = $true
                Status = [int]$status
                LogFile = [string]$global:VRCSETUP_LOGFILE
            }
        }
        $tuiResult = Invoke-VrcSetupSpectreOperation -Title $Title -Header $Header -Operation $operation -ArgumentList @($scriptDir, $installerParams) -ScriptDir $scriptDir
        if ($null -ne $tuiResult) {
            return [pscustomobject]@{ Status = [int]$tuiResult.Status; LogFile = [string]$tuiResult.LogFile; UsedTui = $true }
        }
    }

    Clear-Host
    $status = Start-Installer @installerParams
    return [pscustomobject]@{ Status = [int]$status; LogFile = [string]$global:VRCSETUP_LOGFILE; UsedTui = $false }
}

function Setup-ProjectFlow {
    param(
        [string]$ConfigPath,
        [ValidateSet('create', 'manage')][string]$StartAt
    )

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
        $picked = Show-ChecklistPaged -Title "Cleanup incomplete projects" -PromptTitle 'Choose projects to delete' -Header $header -Items $projects -DefaultSelected $true -MaxVisible 12 -ToLabel {
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
        $workspaceRoot = [System.IO.Directory]::GetParent($scriptDir).FullName
        $resolvedCommon = if ([System.IO.Path]::IsPathRooted($cfgCommon)) { $cfgCommon } else { Join-Path $workspaceRoot $cfgCommon }

        if (-not (Test-Path -LiteralPath $resolvedCommon)) {
            $result.StatusLabel = "Configured but folder missing"
            return $result
        }

        $mainResolved = $PackagePath
        try { $mainResolved = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path } catch { }

        $extraPackages = @()
        $commonPackages = Get-ChildItem -LiteralPath $resolvedCommon -Filter "*.unitypackage" -ErrorAction SilentlyContinue
        foreach ($pkg in $commonPackages) {
            $pkgResolved = $pkg.FullName
            try { $pkgResolved = (Resolve-Path -LiteralPath $pkg.FullName -ErrorAction Stop).Path } catch { }
            if ($pkgResolved -ne $mainResolved) {
                $extraPackages += $pkg.FullName
            }
        }

        $result.ExtrasCount = @($extraPackages).Count
        $result.StatusLabel = if ($result.ExtrasCount -gt 0) { "{0} package(s) found" -f $result.ExtrasCount } else { "0 package(s) found" }
        return $result
    }

    function Format-PackageChangeSummary {
        param([string[]]$Names)

        $items = @($Names)
        if ($items.Count -eq 0) { return 'none' }
        $visible = @($items | Select-Object -First 3)
        $summary = $visible -join ', '
        if ($items.Count -gt $visible.Count) {
            $summary += " (+$($items.Count - $visible.Count) more)"
        }
        return $summary
    }

    function Invoke-ExistingProjectPackageManager {
        param(
            [string]$ProjectPath,
            $Config,
            $ExtrasInfo
        )

        try {
            $currentPackages = Get-VpmProjectPackageSet -ProjectPath $ProjectPath -IncludeLockedPackages @(Get-RequiredPackages -Config $Config)
        } catch {
            Show-WizardError -Title 'Unable to read project packages' -Message $_.Exception.Message
            return
        }

        $workingConfig = [pscustomobject]@{
            VpmPackages = Add-RequiredPackagesToSet -Packages $currentPackages -Config $Config
            DefaultPackages = @(Get-DefaultPackages -Config $Config)
            RequiredPackages = @(Get-RequiredPackages -Config $Config)
        }
        $includeExtras = $false
        $workspaceSelectedIndex = 0

        while ($true) {
            try {
                $workingConfig.VpmPackages = Resolve-VrcSetupSdkPackageSet -Packages $workingConfig.VpmPackages -ScriptDir $scriptDir
            } catch {
                Show-WizardError -Title 'Unable to align VRChat SDK versions' -Message $_.Exception.Message
                return
            }
            $desiredPackages = Copy-VpmPackageSet -Packages $workingConfig.VpmPackages
            $plan = Compare-VpmPackageSets -CurrentPackages $currentPackages -DesiredPackages $desiredPackages
            $workspaceItems = @(Get-VrcSetupPackageWorkspaceItems -CurrentPackages $currentPackages -DesiredPackages $desiredPackages -Config $workingConfig)
            $packageChanges = Get-VrcSetupWorkspacePendingCount -Items $workspaceItems
            $directAction = $null
            $workspaceSelection = $null
            $workspaceCommand = Get-Command -Name 'Show-VrcSetupSpectrePackageWorkspace' -ErrorAction SilentlyContinue
            if ($workspaceCommand) {
                $workspaceSelection = Show-VrcSetupSpectrePackageWorkspace -Items $workspaceItems -ProjectName (Split-Path -Leaf $ProjectPath) -PendingCount $packageChanges -InitialSelectedIndex $workspaceSelectedIndex -IncludeExtras:$includeExtras -ExtrasCount $(if ($ExtrasInfo) { [int]$ExtrasInfo.ExtrasCount } else { 0 }) -HasFavorites:$(@(Get-VrcSetupFavoritePackages -Config $Config).Count -gt 0) -VersionProvider {
                    param($packageName)
                    if (Test-IsVrcSetupSdkPackage -PackageName $packageName) {
                        $sdkNames = @($workspaceItems | Where-Object { Test-IsVrcSetupSdkPackage -PackageName ([string]$_.Name) } | ForEach-Object Name)
                        return @(Get-VrcSetupCommonPackageVersions -PackageNames $sdkNames -ScriptDir $scriptDir)
                    }
                    $versions = @(Get-VrcGetAvailableVersions -PackageName $packageName -ScriptDir $scriptDir)
                    if ($versions.Count -eq 0) { $versions = @(Get-VpmAvailableVersions -PackageName $packageName) }
                    return @($versions)
                } -ScriptDir $scriptDir
            }

            if ($workspaceSelection) {
                $workspaceSelectedIndex = [int]$workspaceSelection.SelectedIndex
                if ($workspaceSelection.Action -eq 'back') { return }
                foreach ($versionChange in @($workspaceSelection.VersionChanges.PSObject.Properties)) {
                    $workingConfig.VpmPackages = Set-VrcSetupPackageVersion -Packages $workingConfig.VpmPackages -PackageName ([string]$versionChange.Name) -Version ([string]$versionChange.Value)
                }
                if (@($workspaceSelection.VersionChanges.PSObject.Properties).Count -gt 0) {
                    $desiredPackages = Copy-VpmPackageSet -Packages $workingConfig.VpmPackages
                    $plan = Compare-VpmPackageSets -CurrentPackages $currentPackages -DesiredPackages $desiredPackages
                    $workspaceItems = @(Get-VrcSetupPackageWorkspaceItems -CurrentPackages $currentPackages -DesiredPackages $desiredPackages -Config $workingConfig)
                    $packageChanges = Get-VrcSetupWorkspacePendingCount -Items $workspaceItems
                }
                if ($workspaceSelection.Action -in @('toggle', 'version')) {
                    $package = $workspaceItems | Where-Object Name -eq $workspaceSelection.PackageName | Select-Object -First 1
                    if (-not $package) { continue }
                    $action = [pscustomobject]@{ Kind = 'package'; Item = $package }
                    $directAction = [string]$workspaceSelection.Action
                } else {
                    $kind = if ($workspaceSelection.Action -eq 'save') { 'apply' } else { [string]$workspaceSelection.Action }
                    $action = [pscustomobject]@{ Kind = $kind; Item = $null }
                }
            } else {
                $changeLabel = if ($packageChanges -eq 0) { 'No package changes' } elseif ($packageChanges -eq 1) { '1 package change ready' } else { "${packageChanges} package changes ready" }
                $importsLabel = if ($includeExtras) { "  ·  $($ExtrasInfo.ExtrasCount) saved import(s) included" } else { '' }
                $header = "Project: $(Split-Path -Leaf $ProjectPath)`n${changeLabel}${importsLabel}"
                $options = @($workspaceItems | ForEach-Object { Format-VrcSetupPackageWorkspaceRow -Item $_ })
                $actions = @($workspaceItems | ForEach-Object { [pscustomobject]@{ Kind = 'package'; Item = $_ } })
                $options += 'Add package'
                $actions += [pscustomobject]@{ Kind = 'add'; Item = $null }
                $options += 'Favorite packages'
                $actions += [pscustomobject]@{ Kind = 'favorites'; Item = $null }
                $options += 'Use my package set'
                $actions += [pscustomobject]@{ Kind = 'defaults'; Item = $null }
                if ($ExtrasInfo -and $ExtrasInfo.ExtrasCount -gt 0) {
                    $options += $(if ($includeExtras) { "Skip $($ExtrasInfo.ExtrasCount) saved UnityPackages" } else { "Include $($ExtrasInfo.ExtrasCount) saved UnityPackages" })
                    $actions += [pscustomobject]@{ Kind = 'extras'; Item = $null }
                }
                $saveLabel = if ($packageChanges -eq 0) { 'Save changes' } elseif ($packageChanges -eq 1) { 'Save 1 change' } else { "Save ${packageChanges} changes" }
                $options += $saveLabel
                $actions += [pscustomobject]@{ Kind = 'apply'; Item = $null }
                $options += 'Back'
                $actions += [pscustomobject]@{ Kind = 'back'; Item = $null }

                $choice = Show-Menu -Title 'Project packages' -Header $header -PromptTitle 'Package                         Version                   After saving' -Options $options -MaxVisible 18
                if ($choice -lt 0 -or $actions[$choice].Kind -eq 'back') { return }
                $action = $actions[$choice]
            }

            if ($action.Kind -eq 'package') {
                $package = $action.Item
                if ($directAction -eq 'toggle') {
                    if ($package.DesiredVersion) {
                        $nextPackages = Copy-VpmPackageSet -Packages $workingConfig.VpmPackages
                        $nextPackages.PSObject.Properties.Remove([string]$package.Name)
                        $workingConfig.VpmPackages = $nextPackages
                    } else {
                        $workingConfig.VpmPackages = Add-VrcSetupPackagesAtLatest -Packages $workingConfig.VpmPackages -PackageNames @([string]$package.Name)
                        if ($package.CurrentVersion) { $workingConfig.VpmPackages.($package.Name) = [string]$package.CurrentVersion }
                    }
                    continue
                }
                if ($directAction -eq 'version') {
                    $selectParams = @{ PackageName = [string]$package.Name; CurrentVersion = [string]$package.DesiredVersion }
                    if (Test-IsVrcSetupSdkPackage -PackageName ([string]$package.Name)) {
                        $sdkNames = @($workspaceItems | Where-Object { Test-IsVrcSetupSdkPackage -PackageName ([string]$_.Name) } | ForEach-Object Name)
                        $selectParams.AvailableVersions = @(Get-VrcSetupCommonPackageVersions -PackageNames $sdkNames -ScriptDir $scriptDir)
                    }
                    $selectedVersion = Select-VpmVersion @selectParams
                    if ($selectedVersion) {
                        $workingConfig.VpmPackages = Set-VrcSetupPackageVersion -Packages $workingConfig.VpmPackages -PackageName ([string]$package.Name) -Version ([string]$selectedVersion)
                    }
                    continue
                }
                $detailHeader = @(
                    [string]$package.FriendlyName,
                    "Installed: $(if ($package.CurrentVersion) { $package.CurrentVersion } else { 'Not installed' })",
                    "After changes: $(if ($package.DesiredVersion) { $package.DesiredVersion } else { 'Remove' })"
                ) -join "`n"
                $detailOptions = @()
                $detailActions = @()
                if (-not $package.Required) {
                    if ($package.DesiredVersion) {
                        $detailOptions += $(if ($package.CurrentVersion) { 'Remove from project' } else { 'Cancel addition' })
                        $detailActions += 'remove'
                    } else {
                        $detailOptions += 'Keep package'
                        $detailActions += 'keep'
                    }
                }
                if ($package.DesiredVersion -ne 'latest') {
                    $detailOptions += 'Use latest version'
                    $detailActions += 'latest'
                }
                $detailOptions += 'Choose version'
                $detailActions += 'version'
                $detailOptions += 'Back'
                $detailActions += 'back'
                $detailChoice = Show-Menu -Title 'Package details' -Header $detailHeader -Options $detailOptions -MaxVisible 8
                if ($detailChoice -lt 0 -or $detailActions[$detailChoice] -eq 'back') { continue }
                $detailAction = $detailActions[$detailChoice]
                if ($detailAction -eq 'remove') {
                    $nextPackages = Copy-VpmPackageSet -Packages $workingConfig.VpmPackages
                    $nextPackages.PSObject.Properties.Remove([string]$package.Name)
                    $workingConfig.VpmPackages = $nextPackages
                } elseif ($detailAction -eq 'keep') {
                    $workingConfig.VpmPackages = Add-VrcSetupPackagesAtLatest -Packages $workingConfig.VpmPackages -PackageNames @([string]$package.Name)
                    if ($package.CurrentVersion) { $workingConfig.VpmPackages.($package.Name) = [string]$package.CurrentVersion }
                } elseif ($detailAction -eq 'latest') {
                    $workingConfig.VpmPackages = Set-VrcSetupPackageVersion -Packages $workingConfig.VpmPackages -PackageName ([string]$package.Name) -Version 'latest'
                } elseif ($detailAction -eq 'version') {
                    $selectParams = @{ PackageName = [string]$package.Name; CurrentVersion = [string]$package.DesiredVersion }
                    if (Test-IsVrcSetupSdkPackage -PackageName ([string]$package.Name)) {
                        $sdkNames = @($workspaceItems | Where-Object { Test-IsVrcSetupSdkPackage -PackageName ([string]$_.Name) } | ForEach-Object Name)
                        $selectParams.AvailableVersions = @(Get-VrcSetupCommonPackageVersions -PackageNames $sdkNames -ScriptDir $scriptDir)
                    }
                    $selectedVersion = Select-VpmVersion @selectParams
                    if ($selectedVersion) {
                        $workingConfig.VpmPackages = Set-VrcSetupPackageVersion -Packages $workingConfig.VpmPackages -PackageName ([string]$package.Name) -Version ([string]$selectedVersion)
                    }
                }
                continue
            }

            if ($action.Kind -eq 'add') {
                $existingNames = @($workingConfig.VpmPackages.PSObject.Properties | ForEach-Object { $_.Name })
                $newPackageIds = @(Find-VrcSetupPackages -ExcludePackageNames $existingNames)
                if ($newPackageIds.Count -eq 0) { continue }
                $workingConfig.VpmPackages = Add-VrcSetupPackagesAtLatest -Packages $workingConfig.VpmPackages -PackageNames $newPackageIds
                continue
            }

            if ($action.Kind -eq 'favorites') {
                $favoriteIds = @(Select-VrcSetupFavoritePackagesForProject -Config $Config -ConfigPath $ConfigPath -CurrentPackages $workingConfig.VpmPackages)
                if ($favoriteIds.Count -gt 0) {
                    $workingConfig.VpmPackages = Add-VrcSetupPackagesAtLatest -Packages $workingConfig.VpmPackages -PackageNames $favoriteIds
                }
                continue
            }

            if ($action.Kind -eq 'defaults') {
                $presetPackages = Add-RequiredPackagesToSet -Packages $Config.VpmPackages -Config $Config
                $workingConfig.VpmPackages = Merge-VrcSetupPackageSets -BasePackages $currentPackages -PackagesToAdd $presetPackages
                continue
            }

            if ($action.Kind -eq 'extras') {
                $includeExtras = -not $includeExtras
                continue
            }

            if ($packageChanges -eq 0 -and -not $includeExtras) {
                Show-WizardError -Title 'Nothing to save' -Message 'Change a package, add one, or include your saved imports first.'
                continue
            }

            if ($includeExtras) {
                $editorCheck = Test-UnityEditorPath -Path ([string]$Config.UnityEditorPath)
                if (-not $editorCheck.Valid) {
                    Show-WizardError -Title 'Unity Editor needed for saved imports' -Message $editorCheck.Message
                    continue
                }
            }

            $reviewHeader = @(
                "Project: $(Split-Path -Leaf $ProjectPath)",
                '',
                "Add: $(Format-PackageChangeSummary -Names $plan.Added)",
                "Update: $(Format-PackageChangeSummary -Names $plan.Updated)",
                "Remove: $(Format-PackageChangeSummary -Names $plan.Removed)",
                $(if ($includeExtras) { "Saved UnityPackages: $($ExtrasInfo.ExtrasCount)" } else { 'Saved UnityPackages: none' }),
                '',
                'This is the only confirmation before saving these changes.'
            ) -join "`n"
            $projectName = Split-Path -Leaf $ProjectPath
            $sdkAdded = @($plan.Added | Where-Object { Test-IsVrcSetupSdkPackage -PackageName $_ })
            $friendlyAdded = @($plan.Added | Where-Object { -not (Test-IsVrcSetupSdkPackage -PackageName $_) } | ForEach-Object { Get-VrcSetupFriendlyPackageName -PackageName $_ })
            if ($sdkAdded.Count -gt 0) {
                $sdkLabels = @($sdkAdded | ForEach-Object { (Get-VrcSetupFriendlyPackageName -PackageName $_) -replace '^VRChat SDK ', '' })
                $sdkVersion = @($sdkAdded | ForEach-Object { [string]$desiredPackages.($_) } | Select-Object -Unique) -join '/'
                $friendlyAdded = @("VRChat SDK ($($sdkLabels -join ' + ')): ${sdkVersion}") + $friendlyAdded
            }
            $sdkUpdated = @($plan.Updated | Where-Object { Test-IsVrcSetupSdkPackage -PackageName $_ })
            $friendlyUpdated = @($plan.Updated | Where-Object { -not (Test-IsVrcSetupSdkPackage -PackageName $_) } | ForEach-Object {
                $friendlyName = Get-VrcSetupFriendlyPackageName -PackageName $_
                "${friendlyName}: $([string]$currentPackages.($_)) -> $([string]$desiredPackages.($_))"
            })
            if ($sdkUpdated.Count -gt 0) {
                $sdkLabels = @($sdkUpdated | ForEach-Object { (Get-VrcSetupFriendlyPackageName -PackageName $_) -replace '^VRChat SDK ', '' })
                $sdkFrom = @($sdkUpdated | ForEach-Object { [string]$currentPackages.($_) } | Select-Object -Unique) -join '/'
                $sdkTo = @($sdkUpdated | ForEach-Object { [string]$desiredPackages.($_) } | Select-Object -Unique) -join '/'
                $friendlyUpdated = @("VRChat SDK ($($sdkLabels -join ' + ')): ${sdkFrom} -> ${sdkTo}") + $friendlyUpdated
            }
            $sdkRemoved = @($plan.Removed | Where-Object { Test-IsVrcSetupSdkPackage -PackageName $_ })
            $friendlyRemoved = @($plan.Removed | Where-Object { -not (Test-IsVrcSetupSdkPackage -PackageName $_) } | ForEach-Object { Get-VrcSetupFriendlyPackageName -PackageName $_ })
            if ($sdkRemoved.Count -gt 0) {
                $sdkLabels = @($sdkRemoved | ForEach-Object { (Get-VrcSetupFriendlyPackageName -PackageName $_) -replace '^VRChat SDK ', '' })
                $friendlyRemoved = @("VRChat SDK ($($sdkLabels -join ' + '))") + $friendlyRemoved
            }
            $reviewConfirmed = $null
            if (Get-Command -Name 'Show-VrcSetupSpectreSaveReview' -ErrorAction SilentlyContinue) {
                $reviewConfirmed = Show-VrcSetupSpectreSaveReview -ProjectName $projectName -Added $friendlyAdded -Updated $friendlyUpdated -Removed $friendlyRemoved -ExtrasCount $(if ($includeExtras) { [int]$ExtrasInfo.ExtrasCount } else { 0 }) -ScriptDir $scriptDir
            }
            if ($null -eq $reviewConfirmed) {
                $applyChoice = Show-Menu -Title 'Save changes' -Header $reviewHeader -Options @('Save now', 'Back')
                $reviewConfirmed = ($applyChoice -eq 0)
            }
            if (-not $reviewConfirmed) { continue }

            while ($true) {
                $execution = Invoke-VrcSetupInstallerWithProgress -ProjectPath $ProjectPath -Title 'Saving project' -Header $projectName -PackagesOverride $desiredPackages -SyncPackages -ImportExtras:$includeExtras
                $status = [int]$execution.Status
                if ($status -eq 0) { return }
                if (-not $execution.UsedTui) {
                    Show-SetupOutcomeSummary -Status $status -ActionLabel 'Apply project changes' -TargetPath $ProjectPath -PackagePath $null -CanLeavePartialProject:$false
                }
                $failureHeader = @(
                    "Project: ${projectName}",
                    'The project was left unchanged where possible.',
                    $(if ($execution.LogFile) { "Full log: $($execution.LogFile)" } else { '' })
                ) -join "`n"
                $failureChoice = Show-Menu -Title 'Changes were not saved' -Header $failureHeader -Options @('Try again', 'Change packages', 'Back to project') -AllowCancel $false
                if ($failureChoice -eq 0) { continue }
                if ($failureChoice -eq 2) { return }
                try {
                    $currentPackages = Get-VpmProjectPackageSet -ProjectPath $ProjectPath -IncludeLockedPackages @(Get-RequiredPackages -Config $Config)
                } catch {
                    Show-WizardError -Title 'Unable to refresh project state' -Message $_.Exception.Message
                    return
                }
                break
            }
            continue
        }
    }

    function Invoke-ExistingProjectFlow {
        param(
            $Config,
            [string]$ProjectPath
        )

        $projectPath = $ProjectPath
        if ([string]::IsNullOrWhiteSpace($projectPath)) {
            $projectPath = Read-WizardPathInput -Title 'Manage existing Unity project' -TitleColor ([ConsoleColor]::Yellow) -Prompt 'Project folder' -BodyLines @(
                'Choose the Unity project you want to update.'
            )
        }
        if ([string]::IsNullOrWhiteSpace($projectPath)) { return }
        if (-not (Test-Path -LiteralPath $projectPath)) {
            Show-WizardError -Title 'Folder not found' -Message $projectPath
            return
        }

        $assetsPath = Join-Path $projectPath 'Assets'
        $packagesPath = Join-Path $projectPath 'Packages'
        if ((-not (Test-Path -LiteralPath $assetsPath)) -or (-not (Test-Path -LiteralPath $packagesPath))) {
            Show-WizardError -Title 'Not a Unity project' -Message 'The folder must contain both Assets and Packages.'
            return
        }

        $extrasInfo = Get-ConfiguredExtraUnityPackagesInfo -Config $Config -PackagePath $null
        Invoke-ExistingProjectPackageManager -ProjectPath $projectPath -Config $Config -ExtrasInfo $extrasInfo
    }

    function Invoke-ProjectLibraryFlow {
        param(
            $Config,
            [string]$ConfigPath
        )

        if (-not $Config -or [string]::IsNullOrWhiteSpace([string]$Config.UnityProjectsRoot)) {
            Show-WizardError -Title 'Projects folder not configured' -Message 'Set the projects folder under Settings, or choose a project folder manually.'
            return
        }

        $root = [string]$Config.UnityProjectsRoot
        $cachePath = Join-Path $scriptDir 'cache\projects.json'
        $forceRefresh = $false
        $sortOrder = if ([string]$Config.ProjectLibrarySort -eq 'name') { 'name' } else { 'recent' }
        while ($true) {
            try {
                $catalog = Get-VrcSetupProjectCatalog -RootPath $root -CachePath $cachePath -ForceRefresh:$forceRefresh -SortOrder $sortOrder
            } catch {
                Show-WizardError -Title 'Unable to scan projects' -Message $_.Exception.Message
                return
            }
            $forceRefresh = $false

            $selected = Show-VrcSetupProjectCatalogSpectre -Catalog $catalog -ScriptDir $scriptDir
            if ($null -eq $selected) {
                # Windows PowerShell and redirected consoles keep the compact text fallback.
                $selected = Select-VrcSetupProjectCatalogAction -Catalog $catalog -ScriptDir $scriptDir
            } elseif ($selected.Action -eq 'library') {
                $selected = Select-VrcSetupProjectCatalogLibraryAction -Catalog $catalog -ScriptDir $scriptDir
            }
            if (-not $selected -or $selected.Action -eq 'back') { return }
            if ($selected.Action -eq 'refresh') {
                $forceRefresh = $true
                continue
            }
            if ($selected.Action -eq 'sort') {
                $sortChoice = Show-Menu -Title 'Project library order' -Header 'Choose how projects are listed.' -Options @('Recently updated', 'Name (A-Z)', 'Back')
                if ($sortChoice -eq 0) { $sortOrder = 'recent' }
                elseif ($sortChoice -eq 1) { $sortOrder = 'name' }
                else { continue }
                $Config | Add-Member -MemberType NoteProperty -Name 'ProjectLibrarySort' -Value $sortOrder -Force
                Save-Config -Config $Config -ConfigPath $ConfigPath
                continue
            }
            if ($selected.Action -eq 'manual') {
                Invoke-ExistingProjectFlow -Config $Config
                continue
            }
            if ($selected.Action -eq 'project') {
                Invoke-ExistingProjectFlow -Config $Config -ProjectPath ([string]$selected.ProjectPath)
                continue
            }
        }
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

        if ([string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath) -or -not (Test-Path -LiteralPath ([string]$State.TargetProjectPath))) {
            $State.ExistingAction = 'create-new'
        }

        $State.ActionLabel = Get-UnityPackageActionLabel -Action $State.ExistingAction
    }

    function Prompt-UnityPackagePackageStep {
        param($State)

        while ($true) {
            $packagePath = Read-WizardPathInput -Title "Create from UnityPackage" -Prompt "UnityPackage file" -BodyLines @(
                "Choose the .unitypackage file you want to import."
            )

            if ([string]::IsNullOrWhiteSpace($packagePath)) { return $false }

            if (-not (Test-Path -LiteralPath $packagePath)) {
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

        $defaultPromptName = if (-not [string]::IsNullOrWhiteSpace([string]$State.SavedProjectName)) {
            [string]$State.SavedProjectName
        } else {
            [string]$State.SuggestedProjectName
        }

        Clear-Host
        Write-Host "Rename project" -ForegroundColor Cyan
        Write-Host ""
        Write-Host ("Source: {0}" -f (Split-Path -Leaf $State.PackagePath)) -ForegroundColor Gray
        Write-Host ("Suggested: {0}" -f $State.SuggestedProjectName) -ForegroundColor White
        if ($State.SavedProjectName) {
            Write-Host ("Previously used: {0}" -f $State.SavedProjectName) -ForegroundColor DarkGray
        }
        Write-Host ""
        Write-Host ("Press ENTER to use: {0}" -f $defaultPromptName) -ForegroundColor DarkGray

        while ($true) {
            $projectName = Read-WizardTextInput -Title 'Project name' -Prompt 'Name' -BodyLines @('Choose the folder name for the new Unity project.') -Hint 'Leave blank to return.'
            $candidateName = if ([string]::IsNullOrWhiteSpace($projectName)) { $defaultPromptName } else { $projectName.Trim() }
            $nameCheck = Test-VrcSetupProjectName -Name $candidateName
            if ($nameCheck.Valid) {
                $State.ProjectName = $candidateName
                break
            }

            Write-Host $nameCheck.Message -ForegroundColor Red
            Write-Host "Enter only the folder name, not a full path." -ForegroundColor DarkGray
        }

        Update-UnityPackageFlowState -State $State -Config $Config
        return $true
    }

    function Prompt-UnityPackageExistingActionStep {
        param($State)

        Update-UnityPackageFlowState -State $State -Config $config

        if ([string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath) -or -not (Test-Path -LiteralPath ([string]$State.TargetProjectPath))) {
            $State.ExistingAction = 'create-new'
            $State.ActionLabel = Get-UnityPackageActionLabel -Action $State.ExistingAction
            return $true
        }

        $extrasLabel = if ($State.ExtrasCount -gt 0) {
            "Use existing + import extras ({0})" -f $State.ExtrasCount
        } else {
            "Use existing + import extras (0)"
        }

        $options = @(
            "Delete and recreate from UnityPackage",
            "Use existing + apply package preset",
            $extrasLabel,
            "Back"
        )

        $header = @(
            "Project already exists: $(Split-Path -Leaf $State.TargetProjectPath)",
            "Folder: $($State.TargetProjectPath)",
            "",
            "Choose what to do with it."
        ) -join "`n"

        $choice = Show-Menu -Title "Existing project found" -Header $header -Options $options
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

        $targetState = if (-not [string]::IsNullOrWhiteSpace([string]$State.TargetProjectPath) -and (Test-Path -LiteralPath ([string]$State.TargetProjectPath))) {
            "Already exists"
        } else {
            "Will be created"
        }

        return @(
            "Source: $(Split-Path -Leaf $State.PackagePath)",
            "Project: $($State.ProjectName)",
            "Folder: $($State.TargetProjectPath)",
            "Status: ${targetState}",
            "Action: $($State.ActionLabel)",
            "Extras: $($State.ExtrasStatus)"
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

        $status = 1
        $execution = $null
        $canLeavePartialProject = ($State.ExistingAction -eq 'create-new' -or $State.ExistingAction -eq 'overwrite')
        switch ($State.ExistingAction) {
            'overwrite' {
                $confirmDel = Show-Menu -Title "Confirm delete" -Header (Add-ConfirmHint -Header ("This will DELETE the existing folder:`n{0}`n`nContinue?" -f $State.TargetProjectPath)) -Options @("Delete and recreate", "Cancel") -AllowCancel $false
                if ($confirmDel -ne 0) { return [pscustomobject]@{ Executed = $false; Status = $null } }
                $execution = Invoke-VrcSetupInstallerWithProgress -ProjectPath $State.PackagePath -Title 'Creating project' -Header $State.ProjectName -NewProjectName $State.ProjectName -OverwriteExistingProject
            }
            'use-existing-vpm' {
                $execution = Invoke-VrcSetupInstallerWithProgress -ProjectPath $State.TargetProjectPath -Title 'Updating project' -Header $State.ProjectName
            }
            'use-existing-extras' {
                $execution = Invoke-VrcSetupInstallerWithProgress -ProjectPath $State.TargetProjectPath -Title 'Importing packages' -Header $State.ProjectName -ImportExtras -ExcludeUnityPackagePath $State.PackagePath
            }
            default {
                $execution = Invoke-VrcSetupInstallerWithProgress -ProjectPath $State.PackagePath -Title 'Creating project' -Header $State.ProjectName -NewProjectName $State.ProjectName
            }
        }

        $status = [int]$execution.Status
        if (-not $execution.UsedTui) {
            Show-SetupOutcomeSummary -Status $status -ActionLabel $State.ActionLabel -TargetPath $State.TargetProjectPath -PackagePath $State.PackagePath -CanLeavePartialProject:$canLeavePartialProject
            Read-Host "Press ENTER to return" | Out-Null
        }
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

            Update-UnityPackageFlowState -State $state -Config $Config
            $automaticNameCheck = Test-VrcSetupProjectName -Name ([string]$state.ProjectName)
            if (-not $automaticNameCheck.Valid) {
                [void](Prompt-UnityPackageIdentityStep -State $state -Config $Config)
            }
            if (-not (Prompt-UnityPackageExistingActionStep -State $state)) {
                $state = New-UnityPackageFlowState
                continue
            }

            while ($true) {
                Update-UnityPackageFlowState -State $state -Config $Config

                $options = @("Change project name")
                if (-not [string]::IsNullOrWhiteSpace([string]$state.TargetProjectPath) -and (Test-Path -LiteralPath ([string]$state.TargetProjectPath))) {
                    $options += "Change existing target action"
                }
                $options += @("Choose another UnityPackage", "Start setup", "Cancel")

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
                    [void](Prompt-UnityPackageIdentityStep -State $state -Config $Config)
                    if (-not (Prompt-UnityPackageExistingActionStep -State $state)) { continue }
                    continue
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

    $setupChoice = if ($StartAt -eq 'create') {
        0
    } elseif ($StartAt -eq 'manage') {
        Show-Menu -Title 'Manage projects' -Header 'Choose a project from your library, or open another project folder manually.' -SectionBreaks @(1) -Options @(
            'Project library',
            'Manage a project by folder',
            'Clean up incomplete projects',
            'Back'
        )
    } else {
        Show-Menu -Title 'Projects' -Header 'Create a project, choose one from your library, or open another folder manually.' -SectionBreaks @(1, 2) -Options @(
            'Create from UnityPackage',
            'Project library',
            'Manage a project by folder',
            'Clean up incomplete projects',
            'Back'
        )
    }

    if ($StartAt -eq 'manage') {
        if ($setupChoice -lt 0 -or $setupChoice -eq 3) { return }
        $setupChoice = @(2, 1, 3)[$setupChoice]
    } elseif ($StartAt -ne 'create' -and $setupChoice -ge 0 -and $setupChoice -lt 4) {
        $setupChoice = @(0, 2, 1, 3)[$setupChoice]
    }

    if ($setupChoice -eq -1 -or $setupChoice -eq 4) { return }

    $config = $null
    if (Test-Path -LiteralPath $ConfigPath) { $config = Load-Config -ConfigPath $ConfigPath }
    if ($config) { $config = Ensure-ConfigDefaults -Config $config }

    if ($setupChoice -eq 3) {
        Cleanup-IncompleteProjectsFlow -Config $config
        return
    }

    if ($setupChoice -eq 1) {
        Invoke-ExistingProjectFlow -Config $config
        return
    }

    if ($setupChoice -eq 2) {
        Invoke-ProjectLibraryFlow -Config $config -ConfigPath $ConfigPath
        return
    }

    # Creating a new project needs both paths. Existing-project package management
    # is intentionally available without a configured projects root.
    $essentials = Test-ConfigEssentialsExist -Config $config
    if (-not $essentials.Ready) {
        # Try auto-fix: if Unity Editor is missing but can be auto-detected, set it now
        $editorVal = [string]$config.UnityEditorPath
        if ([string]::IsNullOrWhiteSpace($editorVal) -or -not (Test-Path -LiteralPath $editorVal)) {
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
        Write-Host "Open 'Settings' from the main menu to fix these." -ForegroundColor Gray
        Read-Host "Press ENTER to continue" | Out-Null
        return
    }

    if ($setupChoice -eq 0) {
        Invoke-UnityPackageSetupFlow -Config $config -ConfigPath $ConfigPath
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
        if (Test-Path -LiteralPath $configPath) { $menuConfig = Load-Config -ConfigPath $configPath }
        $essentials = Test-ConfigEssentialsExist -Config $menuConfig
        $createLabel = 'Create project'
        $manageLabel = 'Manage projects'
        $header = 'Create a VRChat Unity project or manage packages in one you already have.'
        if (-not $essentials.Ready) {
            $createLabel = 'Create project  ·  needs setup'
            $warnings = ($essentials.Missing | ForEach-Object { "  - $_" }) -join "`n"
            $header = "Some paths need attention:`n${warnings}`n`nOpen Settings to fix them."
        }

        $choice = Show-Menu -Title "VRChat Project Setup Wizard" -Header $header -Options @(
            $createLabel,
            $manageLabel,
            'Default package set',
            'Settings',
            'Exit'
        )

        if ($choice -eq -1) { return }

        switch ($choice) {
            0 {
                Setup-ProjectFlow -ConfigPath $configPath -StartAt 'create'
            }
            1 {
                Setup-ProjectFlow -ConfigPath $configPath -StartAt 'manage'
            }
            2 {
                Edit-VpmPackages -ConfigPath $configPath -ScriptDir $scriptDir
            }
            3 {
                Advanced-NamingSettings -ConfigPath $configPath
            }
            4 {
                Write-Host " Goodbye!" -ForegroundColor Cyan
                return
            }
        }
    }
}

