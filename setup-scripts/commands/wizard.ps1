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
    return $p
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
    if (-not ($Config.PSObject.Properties.Name -contains 'DefaultPackages') -or $null -eq $Config.DefaultPackages) {
        $Config | Add-Member -MemberType NoteProperty -Name "DefaultPackages" -Value (Get-DefaultPackages -Config $null) -Force
    }

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
            if ([System.IO.Path]::IsPathRooted($cfgCommon)) {
                $commonPackagesStatus = $cfgCommon
            }
            else {
                $commonPackagesStatus = Join-Path $workspaceRoot $cfgCommon
            }
        }

        $optPrefix = "Prefix: '$($config.Naming.DefaultPrefix)'"
        $optSuffix = "Suffix: '$($config.Naming.DefaultSuffix)'"
        $optRegex = "Regex remove patterns: ${patternsCount}"
        $optRemember = "Remember unitypackage names: ${remember}"
        $optUnityPackages = "UnityPackages folder (extra imports): ${commonPackagesStatus}"

        $sel = Show-Menu -Title "Advanced settings" -Header "Edit your defaults. Enter to select." -Options @(
            $optPrefix,
            $optSuffix,
            $optRegex,
            $optRemember,
            $optUnityPackages,
            "Back"
        )

        if ($sel -eq -1 -or $sel -eq 5) { Save-Config -Config $config -ConfigPath $ConfigPath; return }

        switch ($sel) {
            0 {
                $p = Read-Host "Default prefix (blank to clear)"
                $config.Naming.DefaultPrefix = if ($p) { $p } else { "" }
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            1 {
                $s = Read-Host "Default suffix (blank to clear)"
                $config.Naming.DefaultSuffix = if ($s) { $s } else { "" }
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            2 {
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
            3 {
                $config.Naming.RememberUnityPackageNames = -not $config.Naming.RememberUnityPackageNames
                Save-Config -Config $config -ConfigPath $ConfigPath
            }
            4 {
                Clear-Host
                Write-Host "UnityPackages folder (extra imports)" -ForegroundColor Cyan
                Write-Host "" 
                Write-Host "When you create a project from a UnityPackage, the installer can also import all *.unitypackage found in this folder." -ForegroundColor Gray
                Write-Host "Current: ${commonPackagesStatus}" -ForegroundColor DarkGray
                Write-Host "" 
                Write-Host "Paste/drag a folder path. Type 'disable' (or 'none' / 'clear') to turn it off." -ForegroundColor Yellow

                $inputPath = Normalize-UserPath (Read-Host "Folder path")
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
                    $create = Show-Menu -Title "Folder not found" -Header "Create folder?`n${resolved}" -Options @("Create", "Cancel") -AllowCancel $false
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
            $allPackages = Get-AllVpmPackageNames
            $manualOption = "(Enter package name manually)"
            $searchOption = "(Search packages with vrc-get)"
            $hasVrcGet = -not [string]::IsNullOrWhiteSpace((Get-VrcGetExecutablePath -ScriptDir $ScriptDir))

            $opts = @()
            $pinned = @()
            if ($hasVrcGet) {
                $opts += $searchOption
                $pinned += $searchOption
            }
            $opts += $manualOption
            $pinned += $manualOption
            $opts += @($allPackages)

            $hint = "Type to filter. Enter selects. If no match, Enter uses the typed text."
            if (($allPackages.Count -eq 0) -and (-not $hasVrcGet)) {
                $hint += "`nTip: put vrc-get .exe under setup-scripts/lib/vrc-get/ to enable search."
            }
            $filterParams = @{
                Title         = "Add package"
                Header        = $hint
                Options       = $opts
                PinnedOptions = $pinned
                Placeholder   = "type package name (e.g. gogoloco, poiyomi)"
            }
            $pickedName = Show-MenuFilter @filterParams
            if ($null -eq $pickedName) { continue }

            $newPackage = $null
            if ($pickedName -eq $searchOption) {
                $q = Read-Host "Search query"
                $q = $q.Trim()
                if ([string]::IsNullOrWhiteSpace($q)) { continue }

                $found = Search-VrcGetPackages -Query $q -ScriptDir $ScriptDir
                if ($found.Count -eq 0) {
                    $tail = Get-LastTextLines -Text (Get-VrcSetupLastToolOutput) -MaxLines 25
                    Show-WizardError -Title "No matches" -Message "No packages matched: ${q}" -Details $tail
                    continue
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
                    Title       = "Search results"
                    Header      = "Select a package from vrc-get search results."
                    Options     = $displayOptions
                    Placeholder = "type to filter results"
                }
                $pickStr = Show-MenuFilter @searchParams

                if ($null -eq $pickStr) { continue }

                # Map back to Id
                $idx = [array]::IndexOf($displayOptions, $pickStr)
                if ($idx -lt 0) { continue }
                $newPackage = $found[$idx].Id
            }
            elseif ($pickedName -eq $manualOption) {
                $newPackage = Read-Host "Package name"
                $newPackage = $newPackage.Trim()
            }
            else {
                $newPackage = $pickedName
            }

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
            $confirm = Show-Menu -Title "Remove package" -Header "Remove ${pkgName}?" -Options @("Yes, remove", "Cancel") -AllowCancel $false
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

        $confirm = Show-Menu -Title "Confirm delete" -Header ("Delete {0} selected project(s)?\nThis deletes the entire project folders." -f $picked.Count) -Options @("Delete", "Cancel") -AllowCancel $false
        if ($confirm -ne 0) { return }

        Clear-Host
        foreach ($p in $picked) {
            $pp = [string]$p.ProjectPath
            if ([string]::IsNullOrWhiteSpace($pp)) { continue }
            Write-Host "Deleting: ${pp}" -ForegroundColor Yellow
            try {
                Remove-Item -Path $pp -Recurse -Force -ErrorAction Stop
            } catch {
                Write-Host "Failed: ${pp} (${_})" -ForegroundColor Red
            }
        }
        Write-Host "Cleanup done." -ForegroundColor Green
        Read-Host "Press ENTER to continue" | Out-Null
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

    if ($setupChoice -eq 0) {
        Clear-Host
        Write-Host "Drag here the .unitypackage file (or paste the full path):" -ForegroundColor Cyan
        $packagePath = Normalize-UserPath (Read-Host "UnityPackage path")
        if ([string]::IsNullOrWhiteSpace($packagePath)) { return }
        if (-not (Test-Path $packagePath)) { Write-Host "Path not found: ${packagePath}" -ForegroundColor Red; Read-Host "Press ENTER"; return }
        if ($packagePath -notlike "*.unitypackage") { Write-Host "Must be a .unitypackage file." -ForegroundColor Red; Read-Host "Press ENTER"; return }

        $rawDefault = [System.IO.Path]::GetFileNameWithoutExtension($packagePath)
        $defaultName = Apply-ProjectNamingRules -BaseName $rawDefault -Config $config

        $savedName = $null
        if ($config -and $config.SavedProjectNames -and $config.SavedProjectNames.PSObject.Properties.Name -contains $packagePath) {
            $savedName = $config.SavedProjectNames.($packagePath)
        }

        Clear-Host
        Write-Host "UnityPackage setup" -ForegroundColor Cyan
        Write-Host "" 
        Write-Host "UnityPackage:" -ForegroundColor Gray
        Write-Host "  ${packagePath}" -ForegroundColor White

        Write-Host "" 
        Write-Host "Suggested project name:" -ForegroundColor Gray
        Write-Host "  ${defaultName}" -ForegroundColor White

        if ($savedName) {
            Write-Host "Saved name for this package:" -ForegroundColor DarkGray
            Write-Host "  ${savedName}" -ForegroundColor Gray
        } elseif ($config -and $config.LastProjectName) {
            Write-Host "Last used project name:" -ForegroundColor DarkGray
            Write-Host "  $($config.LastProjectName)" -ForegroundColor Gray
        }

        Write-Host "" 
        Write-Host "Tip: press ENTER to accept the suggested name." -ForegroundColor DarkGray
        $projectName = (Read-Host "Project name")
        if ([string]::IsNullOrWhiteSpace($projectName)) { $projectName = if ($savedName) { $savedName } else { $defaultName } } else { $projectName = $projectName.Trim() }

        $targetProjectPath = $null
        try {
            if ($config -and -not [string]::IsNullOrWhiteSpace([string]$config.UnityProjectsRoot)) {
                $targetProjectPath = Join-Path ([string]$config.UnityProjectsRoot) $projectName
            }
        } catch { $targetProjectPath = $null }

        $existingAction = $null
        if ($targetProjectPath -and (Test-Path $targetProjectPath)) {
            $existingAction = Show-Menu -Title "Project already exists" -Header ("Target already exists:`n${targetProjectPath}`n`nChoose what to do:") -Options @(
                "Delete existing and recreate (from UnityPackage)",
                "Use existing: setup VPM only",
                "Use existing: setup VPM + import extra UnityPackages",
                "Cancel"
            ) -AllowCancel $false

            if ($existingAction -eq 3 -or $existingAction -eq -1) { return }
        }

        $actionLabel = "Create new project"
        if ($existingAction -eq 0) { $actionLabel = "Delete existing and recreate" }
        elseif ($existingAction -eq 1) { $actionLabel = "Use existing (VPM only)" }
        elseif ($existingAction -eq 2) { $actionLabel = "Use existing (VPM + import extras)" }

        $confirmHeader = @(
            "UnityPackage: ${packagePath}",
            "Project name: ${projectName}",
            "Target: ${targetProjectPath}",
            "Action: ${actionLabel}",
            "",
            "Proceed?"
        ) -join "`n"
        $confirm = Show-Menu -Title "Confirm" -Header $confirmHeader -Options @("Proceed", "Cancel") -AllowCancel $false
        if ($confirm -ne 0) { return }

        if ($config) {
            $config | Add-Member -MemberType NoteProperty -Name "LastProjectName" -Value $projectName -Force
            $config | Add-Member -MemberType NoteProperty -Name "LastUnityPackagePath" -Value $packagePath -Force
            if ($config.Naming.RememberUnityPackageNames) {
                $config.SavedProjectNames | Add-Member -MemberType NoteProperty -Name $packagePath -Value $projectName -Force
            }
            Save-Config -Config $config -ConfigPath $ConfigPath
        }

        # Avoid leftover TUI lines before starting installer output
        Clear-Host
        if ($existingAction -eq 0) {
            $confirmDel = Show-Menu -Title "Confirm delete" -Header ("This will DELETE the existing folder:`n${targetProjectPath}`n`nContinue?") -Options @("Delete and recreate", "Cancel") -AllowCancel $false
            if ($confirmDel -ne 0) { return }
            Clear-Host
            Start-Installer -projectPath $packagePath -NewProjectName $projectName -OverwriteExistingProject
        }
        elseif ($existingAction -eq 1) {
            Start-Installer -projectPath $targetProjectPath
        }
        elseif ($existingAction -eq 2) {
            Start-Installer -projectPath $targetProjectPath -ImportExtras -ExcludeUnityPackagePath $packagePath
        }
        else {
            Start-Installer -projectPath $packagePath -NewProjectName $projectName
        }
        Read-Host "Press ENTER to return"
        return
    }

    if ($setupChoice -eq 1) {
        Clear-Host
        Write-Host "Drag here the Unity project folder (or paste the path):" -ForegroundColor Yellow
        $projectPath = Normalize-UserPath (Read-Host "Project path")
        if ([string]::IsNullOrWhiteSpace($projectPath)) { return }
        if (-not (Test-Path $projectPath)) { Write-Host "Path not found: ${projectPath}" -ForegroundColor Red; Read-Host "Press ENTER"; return }

        $assetsPath = Join-Path $projectPath "Assets"
        if (-not (Test-Path $assetsPath)) { Write-Host "Not a Unity project (missing Assets)." -ForegroundColor Red; Read-Host "Press ENTER"; return }

        $confirmHeader = "Project folder: ${projectPath}`n\nProceed?"
        $confirm = Show-Menu -Title "Confirm" -Header $confirmHeader -Options @("Proceed", "Cancel") -AllowCancel $false
        if ($confirm -ne 0) { return }

        # Avoid leftover TUI lines before starting installer output
        Clear-Host
        Start-Installer -projectPath $projectPath
        Read-Host "Press ENTER to return"
        return
    }
}

# --- Main launcher for interactive wizard ---
function Start-Wizard {
    while ($true) {
        $header = "Use arrows + Enter. ESC cancels." 
        $choice = Show-Menu -Title "VRChat Project Setup Wizard" -Header $header -Options @(
            "Setup project (UnityPackage or existing)",
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
                $confirm = Show-Menu -Title "Reset configuration" -Header "Reset config file?" -Options @("Yes, reset", "Cancel") -AllowCancel $false
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

