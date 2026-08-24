# Project state marker helpers (incomplete project cleanup)

function Get-VrcSetupStateDir {
    param([string]$ProjectPath)
    return (Join-Path $ProjectPath ".vrcsetup")
}

function Get-VrcSetupStatePath {
    param([string]$ProjectPath)
    return (Join-Path (Get-VrcSetupStateDir -ProjectPath $ProjectPath) "state.json")
}

function Read-VrcSetupProjectState {
    param([string]$ProjectPath)

    $statePath = Get-VrcSetupStatePath -ProjectPath $ProjectPath
    if (-not (Test-Path -LiteralPath $statePath)) { return $null }

    try {
        return (Get-Content -LiteralPath $statePath -Raw -ErrorAction Stop | ConvertFrom-Json)
    } catch {
        return $null
    }
}

function Write-VrcSetupProjectState {
    param(
        [string]$ProjectPath,
        $State
    )

    $stateDir = Get-VrcSetupStateDir -ProjectPath $ProjectPath
    if (-not (Test-Path -LiteralPath $stateDir)) {
        New-Item -Path $stateDir -ItemType Directory -Force | Out-Null
    }

    $statePath = Get-VrcSetupStatePath -ProjectPath $ProjectPath
    $State | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

function Initialize-VrcSetupProjectState {
    param(
        [string]$ProjectPath,
        [string]$UnityPackagePath,
        [string]$ProjectName
    )

    $now = (Get-Date).ToString('o')
    $state = [pscustomobject]@{
        schemaVersion = 1
        projectName = $ProjectName
        unityPackagePath = $UnityPackagePath
        startedAt = $now
        lastUpdatedAt = $now
        completedAt = $null
        completed = $false
        steps = [pscustomobject][ordered]@{
            created = $true
            nunit = $false
            vpm = $false
            importMain = $false
            importExtras = $false
            finalize = $false
        }
    }

    Write-VrcSetupProjectState -ProjectPath $ProjectPath -State $state
    return $state
}

function Set-VrcSetupProjectStep {
    param(
        [string]$ProjectPath,
        [string]$Step,
        [bool]$Done
    )

    if ([string]::IsNullOrWhiteSpace($Step)) { return }

    $state = Read-VrcSetupProjectState -ProjectPath $ProjectPath
    if (-not $state) { return }

    try {
        if ($null -eq $state.steps) {
            $state | Add-Member -MemberType NoteProperty -Name 'steps' -Value ([pscustomobject][ordered]@{}) -Force
        }

        # ConvertFrom-Json materializes nested objects as PSCustomObject (not a hashtable).
        # Support both PSCustomObject and IDictionary.
        if ($state.steps -is [System.Collections.IDictionary]) {
            $state.steps[$Step] = $Done
        } else {
            $state.steps | Add-Member -MemberType NoteProperty -Name $Step -Value $Done -Force
        }

        $state.lastUpdatedAt = (Get-Date).ToString('o')
        Write-VrcSetupProjectState -ProjectPath $ProjectPath -State $state
    } catch { }
}

function Complete-VrcSetupProjectState {
    param(
        [string]$ProjectPath,
        [switch]$Force
    )

    $state = Read-VrcSetupProjectState -ProjectPath $ProjectPath
    if (-not $state) { return }

    try {
        $requiredSteps = @('created','nunit','vpm','importMain','importExtras','finalize')
        $allDone = $true

        if (-not $Force) {
            foreach ($k in $requiredSteps) {
                $v = $false
                try {
                    if ($state.steps -and ($state.steps.PSObject.Properties.Name -contains $k)) {
                        $v = [bool]($state.steps.$k)
                    } elseif ($state.steps -is [System.Collections.IDictionary] -and $state.steps.Contains($k)) {
                        $v = [bool]($state.steps[$k])
                    } else {
                        $v = $false
                    }
                } catch { $v = $false }

                if (-not $v) { $allDone = $false; break }
            }
        }

        if ($Force -or $allDone) {
            $state.completed = $true
            $state.completedAt = (Get-Date).ToString('o')
            $state.lastUpdatedAt = $state.completedAt
        } else {
            # Leave it incomplete so cleanup can detect it.
            $state.completed = $false
            $state.completedAt = $null
            $state.lastUpdatedAt = (Get-Date).ToString('o')
        }

        Write-VrcSetupProjectState -ProjectPath $ProjectPath -State $state
    } catch { }
}

function Get-VrcSetupIncompleteProjects {
    param([string]$UnityProjectsRoot)

    if ([string]::IsNullOrWhiteSpace($UnityProjectsRoot)) { return @() }
    if (-not (Test-Path -LiteralPath $UnityProjectsRoot)) { return @() }

    $result = @()
    Get-ChildItem -LiteralPath $UnityProjectsRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $p = $_.FullName
        $state = Read-VrcSetupProjectState -ProjectPath $p
        if (-not $state) { return }

        $completed = $false
        try { $completed = [bool]$state.completed } catch { $completed = $false }
        if ($completed) { return }

        $lastStep = ''
        try {
            if ($state.steps) {
                foreach ($k in @('finalize','importExtras','importMain','vpm','nunit','created')) {
                    if ($state.steps.PSObject.Properties.Name -contains $k) {
                        if (-not [bool]$state.steps.$k) {
                            $lastStep = $k
                        }
                    }
                }
            }
        } catch { }

        $result += [pscustomobject]@{
            ProjectPath = $p
            ProjectName = [string]$state.projectName
            StartedAt = [string]$state.startedAt
            LastUpdatedAt = [string]$state.lastUpdatedAt
            PendingStep = $lastStep
        }
    }

    return $result
}
