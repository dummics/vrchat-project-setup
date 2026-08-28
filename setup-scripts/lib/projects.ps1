function Test-VrcSetupUnityProject {
    param([Parameter(Mandatory)][string]$Path)

    return (Test-Path -LiteralPath (Join-Path $Path 'Assets') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $Path 'Packages') -PathType Container)
}

function Get-VrcSetupProjectFingerprint {
    param([Parameter(Mandatory)][string]$ProjectPath)

    $parts = @()
    foreach ($relativePath in @('Packages\vpm-manifest.json', 'ProjectSettings\ProjectVersion.txt')) {
        $path = Join-Path $ProjectPath $relativePath
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $file = Get-Item -LiteralPath $path
            $parts += "${relativePath}:$($file.Length):$($file.LastWriteTimeUtc.Ticks)"
        } else {
            $parts += "${relativePath}:missing"
        }
    }
    return ($parts -join '|')
}

function Get-VrcSetupProjectMetadata {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$ProjectsRoot
    )

    $fullPath = [System.IO.Path]::GetFullPath($ProjectPath).TrimEnd('\')
    $manifestPath = Join-Path $fullPath 'Packages\vpm-manifest.json'
    $versionPath = Join-Path $fullPath 'ProjectSettings\ProjectVersion.txt'
    $packageNames = @()
    $manifestStatus = 'No VPM manifest'

    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($manifest -and $manifest.dependencies) {
                $packageNames = @($manifest.dependencies.PSObject.Properties.Name | Sort-Object)
            }
            $manifestStatus = 'Ready'
        } catch {
            $manifestStatus = 'Invalid VPM manifest'
        }
    }

    $unityVersion = 'Unknown'
    if (Test-Path -LiteralPath $versionPath -PathType Leaf) {
        $versionLine = Get-Content -LiteralPath $versionPath -TotalCount 1 -ErrorAction SilentlyContinue
        if ([string]$versionLine -match '^m_EditorVersion:\s*(.+?)\s*$') {
            $unityVersion = $Matches[1]
        }
    }

    $hasAvatar = $packageNames -contains 'com.vrchat.avatars'
    $hasWorld = $packageNames -contains 'com.vrchat.worlds'
    $kind = if ($hasAvatar -and $hasWorld) {
        'Avatar + World'
    } elseif ($hasAvatar) {
        'Avatar'
    } elseif ($hasWorld) {
        'World'
    } elseif ($packageNames -contains 'com.vrchat.base') {
        'VRChat'
    } else {
        'Unity'
    }

    $relativePath = Split-Path -Leaf $fullPath
    if (-not [string]::IsNullOrWhiteSpace($ProjectsRoot)) {
        try {
            $rootUri = [Uri](([System.IO.Path]::GetFullPath($ProjectsRoot).TrimEnd('\') + '\'))
            $projectUri = [Uri]($fullPath + '\')
            $relativePath = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($projectUri).ToString()).Replace('/', '\').TrimEnd('\')
        } catch { }
    }

    $modified = (Get-Item -LiteralPath $fullPath).LastWriteTimeUtc
    foreach ($metadataPath in @($manifestPath, $versionPath)) {
        if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
            $candidate = (Get-Item -LiteralPath $metadataPath).LastWriteTimeUtc
            if ($candidate -gt $modified) { $modified = $candidate }
        }
    }

    return [pscustomobject]@{
        Name = Split-Path -Leaf $fullPath
        Path = $fullPath
        RelativePath = $relativePath
        Kind = $kind
        UnityVersion = $unityVersion
        PackageCount = @($packageNames).Count
        PackageNames = @($packageNames)
        Status = $manifestStatus
        LastModifiedUtc = $modified.ToString('o')
        Fingerprint = Get-VrcSetupProjectFingerprint -ProjectPath $fullPath
    }
}

function Find-VrcSetupUnityProjectPaths {
    param(
        [Parameter(Mandatory)][string]$RootPath,
        [ValidateRange(0, 5)][int]$MaxDepth = 2
    )

    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    if (Test-VrcSetupUnityProject -Path $root) { return @($root) }

    $excluded = @('Assets', 'Library', 'Logs', 'obj', 'Packages', 'ProjectSettings', 'Temp', 'UserSettings', '.git', '.vrcsetup', '.vpm-validation-cache')
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue([pscustomobject]@{ Path = $root; Depth = 0 })
    $results = @()

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        foreach ($directory in Get-ChildItem -LiteralPath $current.Path -Directory -Force -ErrorAction SilentlyContinue) {
            if ($excluded -contains $directory.Name) { continue }
            if (($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }

            if (Test-VrcSetupUnityProject -Path $directory.FullName) {
                $results += $directory.FullName
                continue
            }
            if ($current.Depth -lt $MaxDepth) {
                $queue.Enqueue([pscustomobject]@{ Path = $directory.FullName; Depth = ($current.Depth + 1) })
            }
        }
    }

    return @($results | Sort-Object -Unique)
}

function Get-VrcSetupProjectCatalog {
    param(
        [Parameter(Mandatory)][string]$RootPath,
        [Parameter(Mandatory)][string]$CachePath,
        [switch]$ForceRefresh,
        [ValidateRange(0, 5)][int]$MaxDepth = 2
    )

    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) {
        throw "Projects folder not found: ${RootPath}"
    }

    $root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $startedAt = [DateTime]::UtcNow
    $cacheByPath = @{}
    if (-not $ForceRefresh -and (Test-Path -LiteralPath $CachePath -PathType Leaf)) {
        try {
            $saved = Get-Content -LiteralPath $CachePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($saved -and ([string]$saved.RootPath).TrimEnd('\') -ieq $root) {
                foreach ($project in @($saved.Projects)) {
                    if ($project -and -not [string]::IsNullOrWhiteSpace([string]$project.Path)) {
                        $cacheByPath[[string]$project.Path] = $project
                    }
                }
            }
        } catch { }
    }

    $projects = @()
    $cacheHits = 0
    $refreshed = 0
    foreach ($path in @(Find-VrcSetupUnityProjectPaths -RootPath $root -MaxDepth $MaxDepth)) {
        $fingerprint = Get-VrcSetupProjectFingerprint -ProjectPath $path
        $cached = $cacheByPath[$path]
        if (-not $ForceRefresh -and $cached -and [string]$cached.Fingerprint -eq $fingerprint) {
            $projects += $cached
            $cacheHits++
        } else {
            $projects += Get-VrcSetupProjectMetadata -ProjectPath $path -ProjectsRoot $root
            $refreshed++
        }
    }
    $projects = @($projects | Sort-Object Kind, Name, Path)

    $catalog = [pscustomobject]@{
        SchemaVersion = 1
        RootPath = $root
        ScannedAtUtc = [DateTime]::UtcNow.ToString('o')
        Projects = $projects
    }
    try {
        $cacheDirectory = Split-Path -Parent $CachePath
        [System.IO.Directory]::CreateDirectory($cacheDirectory) | Out-Null
        $temporaryPath = "${CachePath}.$([guid]::NewGuid().ToString('N')).tmp"
        $catalog | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $CachePath -Force
    } catch { }

    return [pscustomobject]@{
        RootPath = $root
        ScannedAtUtc = $catalog.ScannedAtUtc
        Projects = $projects
        ProjectCount = $projects.Count
        CacheHits = $cacheHits
        Refreshed = $refreshed
        DurationMs = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds
    }
}
