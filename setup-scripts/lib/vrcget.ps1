# vrc-get helpers (portable/local)
# NOTE: This script is dot-sourced by the wizard.

function Get-VrcGetExecutablePath {
    param(
        [string]$ScriptDir
    )

    if ([string]::IsNullOrWhiteSpace($ScriptDir)) { return $null }

    # Always prefer local, shipped exe (portable). No PATH / winget assumptions.
    $folder = Join-Path $ScriptDir "lib\\vrc-get"
    if (-not (Test-Path -LiteralPath $folder)) { return $null }

    # Prefer a canonical name if present, otherwise pick any exe in the folder.
    $preferred = Join-Path $folder "vrc-get.exe"
    if (Test-Path -LiteralPath $preferred) { return $preferred }

    try {
        $exe = Get-ChildItem -LiteralPath $folder -Filter "*.exe" -File -ErrorAction SilentlyContinue |
            Sort-Object -Property Name |
            Select-Object -First 1
        if ($exe) { return $exe.FullName }
    } catch { }

    return $null
}

function Invoke-VrcGetCapture {
    param(
        [string[]]$Arguments,
        [string]$ScriptDir
    )

    $exe = Get-VrcGetExecutablePath -ScriptDir $ScriptDir
    if ([string]::IsNullOrWhiteSpace($exe)) {
        return @{ ExitCode = 127; Output = "vrc-get not found (expected under setup-scripts/lib/vrc-get/)"; StdOut = ""; StdErr = "" }
    }

    $stdout = ""
    $stderr = ""
    $exitCode = 0
    $tmpErr = $null
    try {
        $tmpErr = [System.IO.Path]::GetTempFileName()
        $stdout = (& $exe @Arguments 2> $tmpErr | Out-String)
        $exitCode = $LASTEXITCODE

        try {
            $stderr = (Get-Content -LiteralPath $tmpErr -Raw -ErrorAction SilentlyContinue)
        } catch {
            $stderr = ""
        }
    } catch {
        $stdout = ""
        $stderr = (${_} | Out-String)
        $exitCode = 1
    } finally {
        if ($tmpErr -and (Test-Path -LiteralPath $tmpErr)) {
            try { Remove-Item -LiteralPath $tmpErr -Force -ErrorAction SilentlyContinue } catch { }
        }
    }

    $output = ((@($stdout, $stderr) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n")
    $global:VRCSETUP_LAST_TOOL_OUTPUT = $output
    return @{ ExitCode = $exitCode; Output = $output; StdOut = $stdout; StdErr = $stderr; ExePath = $exe }
}

# Cache: package id -> versions array
$script:VrcGetVersionsCache = @{}

function Get-VrcGetAvailableVersions {
    param(
        [string]$PackageName,
        [string]$ScriptDir
    )

    if ([string]::IsNullOrWhiteSpace($PackageName)) { return @() }

    if ($script:VrcGetVersionsCache.ContainsKey($PackageName)) {
        return $script:VrcGetVersionsCache[$PackageName]
    }

    $res = Invoke-VrcGetCapture -Arguments @('info', 'package', $PackageName, '--json-format', '1') -ScriptDir $ScriptDir
    if ($res.ExitCode -ne 0) {
        $script:VrcGetVersionsCache[$PackageName] = @()
        return @()
    }

    try {
        $jsonText = $res.StdOut
        if ([string]::IsNullOrWhiteSpace($jsonText)) { $jsonText = $res.Output }
        $json = ($jsonText | ConvertFrom-Json -ErrorAction Stop)
        $versions = @()
        if ($json -and $json.versions) {
            foreach ($v in $json.versions) {
                if ($v.version) { $versions += [string]$v.version }
            }
        }
        $versions = $versions | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
        $versions = Sort-SemVerDescending -Versions $versions
        $script:VrcGetVersionsCache[$PackageName] = @($versions)
        return $script:VrcGetVersionsCache[$PackageName]
    } catch {
        $script:VrcGetVersionsCache[$PackageName] = @()
        return @()
    }
}

function Search-VrcGetPackages {
    param(
        [string]$Query,
        [string]$ScriptDir
    )

    if ([string]::IsNullOrWhiteSpace($Query)) { return @() }
    $tokens = @(($Query -split "\s+") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($tokens.Count -eq 0) { return @() }

    $res = Invoke-VrcGetCapture -Arguments (@('search') + @($tokens)) -ScriptDir $ScriptDir
    if ($res.ExitCode -ne 0) { return @() }

    $text = [string]$res.StdOut
    if ([string]::IsNullOrWhiteSpace($text)) { $text = [string]$res.Output }
    if ($text -match "No matching package found") { return @() }

    # Defensive: drop warning/error prefixes if they ever land in stdout.
    $text = (($text -split "\r?\n") | Where-Object { $_ -notmatch '^\s*[we]:\s' }) -join "`n"

    $blocks = $text -split "(\r?\n){2,}" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $results = @()

    foreach ($b in $blocks) {
        $lines = ($b -split "\r?\n") | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        if ($lines.Count -eq 0) { continue }

        $first = $lines[0]
        $id = $null
        $display = $null
        $latestVersion = $null

        if ($lines.Count -ge 2 -and $lines[1] -match "^\((.+)\)$") {
            $id = $Matches[1]
        }

        if ($first -match "^(.+?)\s+version\s+(.+)$") {
            $namePart = $Matches[1].Trim()
            $latestVersion = $Matches[2].Trim()
            if (-not $id) {
                $id = $namePart
            } else {
                if ($namePart -ne $id) { $display = $namePart }
            }
        } else {
            if (-not $id) { $id = $first }
        }

        if ([string]::IsNullOrWhiteSpace($id)) { continue }

        $desc = $null
        if ($lines.Count -ge 3) {
            $desc = $lines[2]
        } elseif ($lines.Count -ge 2 -and -not ($lines[1] -match "^\(.+\)$")) {
            $desc = $lines[1]
        }

        $results += [pscustomobject]@{
            Id = $id
            DisplayName = $display
            LatestVersion = $latestVersion
            Description = $desc
        }
    }

    return $results
}
