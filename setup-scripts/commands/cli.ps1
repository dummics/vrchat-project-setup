function Write-VrcSetupCliHelp {
    @'
VRChat Project Setup CLI

Usage:
  vrcsetup projects [-Refresh] [-Sort recent|name] [-Json]
  vrcsetup packages list <project> [-Json]
  vrcsetup packages search <words> [-Json]
  vrcsetup packages add <project> <package[@version]> [...] [-DryRun]
  vrcsetup packages remove <project> <package> [...] [-DryRun]
  vrcsetup create <file.unitypackage> [-Name <project>] [-Package <package[@version]> ...] [-DryRun]
  vrcsetup setup <project-or-unitypackage> [-DryRun]
  vrcsetup repair
  vrcsetup uninstall

Examples:
  vrcsetup projects
  vrcsetup projects -Sort name
  vrcsetup packages search gogoloco
  vrcsetup packages add "D:\Unity Projects\Avatar" gogoloco@1.8.6
  vrcsetup packages remove "D:\Unity Projects\Avatar" gogoloco -DryRun
  vrcsetup create ".\Avatar.unitypackage" -Name "My Avatar" -Package gogoloco@latest

Running vrcsetup without arguments opens the interactive wizard.
'@ | Write-Host
}

function Write-VrcSetupCliData {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()]$Data,
        [switch]$Json
    )

    if ($Json) {
        ConvertTo-Json -InputObject @($Data) -Depth 10
        return
    }
    $Data | Format-Table -AutoSize | Out-Host
}

function Resolve-VrcSetupCliPath {
    param([Parameter(Mandatory)][string]$Path)

    $value = [Environment]::ExpandEnvironmentVariables($Path.Trim().Trim('"').Trim("'"))
    if (-not [System.IO.Path]::IsPathRooted($value)) {
        $value = Join-Path (Get-Location).Path $value
    }
    return [System.IO.Path]::GetFullPath($value).TrimEnd('\')
}

function ConvertFrom-VrcSetupPackageSpec {
    param([Parameter(Mandatory)][string]$Spec)

    $value = $Spec.Trim()
    if ($value -match '^(.+?)@([^@]+)$') {
        return [pscustomobject]@{ Name = $Matches[1]; Version = $Matches[2] }
    }
    return [pscustomobject]@{ Name = $value; Version = 'latest' }
}

function Test-VrcSetupCliPackageSpec {
    param(
        [Parameter(Mandatory)]$PackageSpec,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    if ([string]::IsNullOrWhiteSpace([string]$PackageSpec.Name)) {
        Write-Host 'Error: package name cannot be blank.' -ForegroundColor Red
        return $false
    }
    if (-not (Test-VpmPackageExists -PackageName $PackageSpec.Name -ScriptDir $ScriptDir)) {
        Write-Host "Error: package not found: $($PackageSpec.Name)" -ForegroundColor Red
        return $false
    }
    $validation = Test-VpmPackageVersion -PackageName $PackageSpec.Name -Version $PackageSpec.Version -ScriptDir $ScriptDir
    if (-not $validation.Valid) {
        Write-Host "Error: $($validation.Message)" -ForegroundColor Red
        return $false
    }
    return $true
}

function Show-VrcSetupCliPackagePlan {
    param(
        [Parameter(Mandatory)]$Current,
        [Parameter(Mandatory)]$Desired,
        [switch]$DryRun
    )

    $plan = Compare-VpmPackageSets -CurrentPackages $Current -DesiredPackages $Desired
    $mode = if ($DryRun) { 'DRY RUN' } else { 'APPLY' }
    Write-Host "Package plan [${mode}]" -ForegroundColor Cyan
    Write-Host "  Add:    $((@($plan.Added) -join ', ') -replace '^$', 'none')"
    Write-Host "  Update: $((@($plan.Updated) -join ', ') -replace '^$', 'none')"
    Write-Host "  Remove: $((@($plan.Removed) -join ', ') -replace '^$', 'none')"
    return $plan
}

function Invoke-VrcSetupCliPackageMutation {
    param(
        [Parameter(Mandatory)][ValidateSet('add', 'remove')][string]$Action,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string[]]$PackageSpecs,
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string]$ScriptDir,
        [switch]$DryRun
    )

    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) {
        Write-Host "Error: project folder not found: ${ProjectPath}" -ForegroundColor Red
        return 1
    }
    if (-not (Test-VrcSetupUnityProject -Path $ProjectPath)) {
        Write-Host 'Error: target must contain both Assets and Packages.' -ForegroundColor Red
        return 1
    }
    if (-not $PackageSpecs -or $PackageSpecs.Count -eq 0) {
        Write-Host "Error: at least one package is required for '${Action}'." -ForegroundColor Red
        return 1
    }

    try { $current = Get-VpmProjectPackageSet -ProjectPath $ProjectPath } catch {
        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
        return 1
    }
    $desired = Copy-VpmPackageSet -Packages $current

    if ($Action -eq 'add') {
        foreach ($rawSpec in $PackageSpecs) {
            $spec = ConvertFrom-VrcSetupPackageSpec -Spec $rawSpec
            if (-not (Test-VrcSetupCliPackageSpec -PackageSpec $spec -ScriptDir $ScriptDir)) { return 1 }
            $desired | Add-Member -MemberType NoteProperty -Name $spec.Name -Value $spec.Version -Force
        }
    } else {
        foreach ($packageName in $PackageSpecs) {
            if (Test-IsRequiredPackage -PackageName $packageName -Config $Config) {
                Write-Host "Error: required package cannot be removed: ${packageName}" -ForegroundColor Red
                return 1
            }
            if ($desired.PSObject.Properties.Name -notcontains $packageName) {
                Write-Host "Error: project does not directly contain package: ${packageName}" -ForegroundColor Red
                return 1
            }
            $desired.PSObject.Properties.Remove($packageName)
        }
    }

    $desired = Add-RequiredPackagesToSet -Packages $desired -Config $Config
    $plan = Show-VrcSetupCliPackagePlan -Current $current -Desired $desired -DryRun:$DryRun
    if (@($plan.Added).Count -eq 0 -and @($plan.Updated).Count -eq 0 -and @($plan.Removed).Count -eq 0) {
        Write-Host 'No package changes are needed.' -ForegroundColor Green
        return 0
    }
    return (Start-Installer -projectPath $ProjectPath -PackagesOverride $desired -SyncPackages -Test:$DryRun)
}

function Invoke-VrcSetupCli {
    param(
        [Parameter(Mandatory)][string]$Command,
        [string[]]$Arguments,
        [string]$ScriptDir,
        [string]$ConfigPath,
        [switch]$Json,
        [switch]$DryRun,
        [switch]$Refresh,
        [ValidateSet('recent', 'name')][string]$SortOrder,
        [string]$Name,
        [string[]]$Package
    )

    $commandName = $Command.Trim().ToLowerInvariant()
    if ($commandName -in @('help', '-h', '--help', '/?')) {
        Write-VrcSetupCliHelp
        return 0
    }

    $config = Load-Config -ConfigPath $ConfigPath
    if (-not $config) {
        Write-Host 'Error: configuration is missing. Run vrcsetup once to complete setup.' -ForegroundColor Red
        return 1
    }
    $config = Ensure-ConfigDefaults -Config $config

    if ($commandName -eq 'projects') {
        if ([string]::IsNullOrWhiteSpace([string]$config.UnityProjectsRoot)) {
            Write-Host 'Error: projects folder is not configured.' -ForegroundColor Red
            return 1
        }
        try {
            $effectiveSort = if ($SortOrder) { $SortOrder } elseif ([string]$config.ProjectLibrarySort -eq 'name') { 'name' } else { 'recent' }
            $catalog = Get-VrcSetupProjectCatalog -RootPath ([string]$config.UnityProjectsRoot) -CachePath (Join-Path $ScriptDir 'cache\projects.json') -ForceRefresh:$Refresh -SortOrder $effectiveSort
        } catch {
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
            return 1
        }
        $rows = @($catalog.Projects | Select-Object Name, Kind, UnityVersion, PackageCount, @{ Name = 'LastUpdatedUtc'; Expression = { $_.LastModifiedUtc } }, Status, Path)
        Write-VrcSetupCliData -Data $rows -Json:$Json
        if (-not $Json) { Write-Host "Projects: $($catalog.ProjectCount) | order: $($catalog.SortOrder) | cache reused: $($catalog.CacheHits) | refreshed: $($catalog.Refreshed) | $($catalog.DurationMs) ms" -ForegroundColor DarkGray }
        return 0
    }

    if ($commandName -eq 'packages') {
        if (-not $Arguments -or $Arguments.Count -eq 0) {
            Write-Host 'Error: use packages list, search, add or remove.' -ForegroundColor Red
            return 1
        }
        $action = $Arguments[0].ToLowerInvariant()
        if ($action -eq 'search') {
            $query = (@($Arguments | Select-Object -Skip 1) -join ' ').Trim()
            if ([string]::IsNullOrWhiteSpace($query)) {
                Write-Host 'Error: package search needs one or more words.' -ForegroundColor Red
                return 1
            }
            $matches = @(Search-VrcGetPackages -Query $query -ScriptDir $ScriptDir)
            Write-VrcSetupCliData -Data $matches -Json:$Json
            if ($matches.Count -eq 0) { return 2 }
            return 0
        }
        if ($Arguments.Count -lt 2) {
            Write-Host "Error: packages ${action} needs a project path." -ForegroundColor Red
            return 1
        }
        $project = Resolve-VrcSetupCliPath -Path $Arguments[1]
        if ($action -eq 'list') {
            if (-not (Test-VrcSetupUnityProject -Path $project)) {
                Write-Host "Error: Unity project not found: ${project}" -ForegroundColor Red
                return 1
            }
            try { $packageSet = Get-VpmProjectPackageSet -ProjectPath $project } catch {
                Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
                return 1
            }
            $rows = @($packageSet.PSObject.Properties | ForEach-Object { [pscustomobject]@{ Package = $_.Name; Version = [string]$_.Value } })
            Write-VrcSetupCliData -Data $rows -Json:$Json
            return 0
        }
        if ($action -notin @('add', 'remove')) {
            Write-Host "Error: unknown packages action '${action}'." -ForegroundColor Red
            return 1
        }
        $specs = @($Arguments | Select-Object -Skip 2)
        return (Invoke-VrcSetupCliPackageMutation -Action $action -ProjectPath $project -PackageSpecs $specs -Config $config -ScriptDir $ScriptDir -DryRun:$DryRun)
    }

    if ($commandName -eq 'create') {
        if (-not $Arguments -or $Arguments.Count -eq 0) {
            Write-Host 'Error: create needs a .unitypackage path.' -ForegroundColor Red
            return 1
        }
        $unityPackagePath = Resolve-VrcSetupCliPath -Path $Arguments[0]
        if (-not (Test-Path -LiteralPath $unityPackagePath -PathType Leaf) -or $unityPackagePath -notlike '*.unitypackage') {
            Write-Host "Error: UnityPackage not found: ${unityPackagePath}" -ForegroundColor Red
            return 1
        }
        $packages = Copy-VpmPackageSet -Packages $config.VpmPackages
        foreach ($rawSpec in @($Package)) {
            $spec = ConvertFrom-VrcSetupPackageSpec -Spec $rawSpec
            if (-not (Test-VrcSetupCliPackageSpec -PackageSpec $spec -ScriptDir $ScriptDir)) { return 1 }
            $packages | Add-Member -MemberType NoteProperty -Name $spec.Name -Value $spec.Version -Force
        }
        $packages = Add-RequiredPackagesToSet -Packages $packages -Config $config
        $projectName = if ([string]::IsNullOrWhiteSpace($Name)) { [System.IO.Path]::GetFileNameWithoutExtension($unityPackagePath) } else { $Name }
        Write-Host "Create project: ${projectName}" -ForegroundColor Cyan
        Write-Host "UnityPackage: ${unityPackagePath}" -ForegroundColor DarkGray
        Write-Host "Direct VPM packages: $(@($packages.PSObject.Properties).Count)" -ForegroundColor DarkGray
        return (Start-Installer -projectPath $unityPackagePath -NewProjectName $projectName -PackagesOverride $packages -Test:$DryRun)
    }

    if ($commandName -eq 'setup') {
        if (-not $Arguments -or $Arguments.Count -eq 0) {
            Write-Host 'Error: setup needs a project or UnityPackage path.' -ForegroundColor Red
            return 1
        }
        return (Start-Installer -projectPath (Resolve-VrcSetupCliPath -Path $Arguments[0]) -Test:$DryRun)
    }

    Write-Host "Error: unknown command '${Command}'." -ForegroundColor Red
    Write-Host 'Run: vrcsetup help' -ForegroundColor Yellow
    return 1
}
