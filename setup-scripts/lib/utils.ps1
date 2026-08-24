# Utilities for vrc setup scripts
function Test-VrcSetupProjectName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return @{ Valid = $false; Message = 'Project name cannot be empty.' }
    }

    $trimmed = $Name.Trim()
    if ($trimmed -in @('.', '..') -or [System.IO.Path]::IsPathRooted($trimmed) -or
        $trimmed.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $trimmed.Contains([System.IO.Path]::DirectorySeparatorChar) -or
        $trimmed.Contains([System.IO.Path]::AltDirectorySeparatorChar)) {
        return @{ Valid = $false; Message = "Project name contains path separators or invalid characters: ${Name}" }
    }

    if ($trimmed.EndsWith(' ') -or $trimmed.EndsWith('.')) {
        return @{ Valid = $false; Message = 'Project name cannot end with a space or period.' }
    }

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($trimmed)
    if ($stem -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
        return @{ Valid = $false; Message = "Project name is reserved by Windows: ${Name}" }
    }

    return @{ Valid = $true; Message = $null }
}

function Install-NUnitPackage {
    param(
        [string]$ProjectPath,
        [switch]$Test
    )

    $manifestPath = Join-Path $ProjectPath "Packages\manifest.json"

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        Write-Host "Warning: manifest.json not found, skipping NUnit" -ForegroundColor Yellow
        return
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

        # Check se NUnit è già presente
        if ($manifest.dependencies.PSObject.Properties.Name -contains "com.unity.test-framework") {
            Write-Host "NUnit Test Framework already present" -ForegroundColor Gray
            return
        }

        $testFrameworkVersion = "1.1.33"
        try {
            $scriptDir = [System.IO.Directory]::GetParent($PSScriptRoot).FullName
            $depsPath = Join-Path $scriptDir "config\\unity-test-framework.dependencies.json"
            if (-not (Test-Path -LiteralPath $depsPath)) {
                # Back-compat (older name)
                $depsPath = Join-Path $scriptDir "config\\vrcsetup.lock.json"
            }

            if (Test-Path -LiteralPath $depsPath) {
                $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
                if ($deps -and $deps.dependencies -and ($deps.dependencies.PSObject.Properties.Name -contains 'com.unity.test-framework')) {
                    $testFrameworkVersion = [string]$deps.dependencies.'com.unity.test-framework'
                }
            }
        } catch {
            # ignore and keep fallback
        }

        Write-Host "Adding NUnit Test Framework (required by VRChat SDK)..." -ForegroundColor Cyan
        if ($Test) {
            Write-Host "[TEST] Would add com.unity.test-framework @ ${testFrameworkVersion}" -ForegroundColor DarkGray
            Add-Content -LiteralPath $global:VRCSETUP_LOGFILE -Value "[TEST] Would add com.unity.test-framework @ ${testFrameworkVersion} to ${ProjectPath}"
            return
        }
        # Aggiungi NUnit
        $manifest.dependencies | Add-Member -MemberType NoteProperty -Name "com.unity.test-framework" -Value $testFrameworkVersion -Force

        # Salva manifest
        $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

        Write-Host "NUnit Test Framework added!" -ForegroundColor Green
    } catch {
        Write-Host "Warning: unable to automatically add NUnit: ${_}" -ForegroundColor Yellow
    }
}

function ConvertTo-SemVerParts {
    param(
        [string]$Version
    )

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return [pscustomobject]@{ IsValid = $false; Original = $Version }
    }

    $v = $Version.Trim()
    if ($v.StartsWith('v')) { $v = $v.Substring(1) }

    # Supports: 1.2.3, 1.2, 1, plus optional prerelease/build: 1.2.3-beta.1+meta
    $m = [regex]::Match($v, '^(?<maj>\d+)(?:\.(?<min>\d+))?(?:\.(?<pat>\d+))?(?:-(?<pre>[0-9A-Za-z\-\.]+))?(?:\+(?<build>.*))?$')
    if (-not $m.Success) {
        return [pscustomobject]@{ IsValid = $false; Original = $Version }
    }

    $maj = [int]$m.Groups['maj'].Value
    $min = if ($m.Groups['min'].Success) { [int]$m.Groups['min'].Value } else { 0 }
    $pat = if ($m.Groups['pat'].Success) { [int]$m.Groups['pat'].Value } else { 0 }
    $pre = if ($m.Groups['pre'].Success) { [string]$m.Groups['pre'].Value } else { $null }

    $preParts = @()
    if (-not [string]::IsNullOrWhiteSpace($pre)) {
        $preParts = $pre.Split('.') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    return [pscustomobject]@{
        IsValid = $true
        Original = $Version
        Major = $maj
        Minor = $min
        Patch = $pat
        PreReleaseParts = @($preParts)
        HasPreRelease = ($preParts.Count -gt 0)
    }
}

function Compare-SemVer {
    param(
        [string]$A,
        [string]$B
    )

    $pa = ConvertTo-SemVerParts -Version $A
    $pb = ConvertTo-SemVerParts -Version $B

    if (-not $pa.IsValid -or -not $pb.IsValid) {
        # Fallback: lexical compare (case-insensitive)
        return [string]::Compare([string]$A, [string]$B, $true, [Globalization.CultureInfo]::InvariantCulture)
    }

    if ($pa.Major -ne $pb.Major) { return $pa.Major.CompareTo($pb.Major) }
    if ($pa.Minor -ne $pb.Minor) { return $pa.Minor.CompareTo($pb.Minor) }
    if ($pa.Patch -ne $pb.Patch) { return $pa.Patch.CompareTo($pb.Patch) }

    # Release > prerelease
    if ($pa.HasPreRelease -and -not $pb.HasPreRelease) { return -1 }
    if (-not $pa.HasPreRelease -and $pb.HasPreRelease) { return 1 }
    if (-not $pa.HasPreRelease -and -not $pb.HasPreRelease) { return 0 }

    $aParts = @($pa.PreReleaseParts)
    $bParts = @($pb.PreReleaseParts)
    $max = [Math]::Max($aParts.Count, $bParts.Count)

    for ($i = 0; $i -lt $max; $i++) {
        if ($i -ge $aParts.Count) { return -1 } # shorter prerelease is lower precedence
        if ($i -ge $bParts.Count) { return 1 }

        $ai = [string]$aParts[$i]
        $bi = [string]$bParts[$i]

        $aIsNum = $ai -match '^\d+$'
        $bIsNum = $bi -match '^\d+$'

        if ($aIsNum -and $bIsNum) {
            $an = [int]$ai
            $bn = [int]$bi
            if ($an -ne $bn) { return $an.CompareTo($bn) }
            continue
        }

        # Numeric identifiers always have lower precedence than non-numeric
        if ($aIsNum -and -not $bIsNum) { return -1 }
        if (-not $aIsNum -and $bIsNum) { return 1 }

        $cmp = [string]::Compare($ai, $bi, $false, [Globalization.CultureInfo]::InvariantCulture)
        if ($cmp -ne 0) { return $cmp }
    }

    return 0
}

function Sort-SemVerDescending {
    param(
        [string[]]$Versions
    )

    if (-not $Versions) { return @() }

    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($x in $Versions) {
        if (-not [string]::IsNullOrWhiteSpace($x)) { [void]$list.Add([string]$x) }
    }

    $list.Sort([System.Comparison[string]]{
        param($x, $y)
        # Descending: invert the ascending compare
        return -1 * (Compare-SemVer -A $x -B $y)
    })

    return $list.ToArray()
}

function Test-VersionMatchesPattern {
    param(
        [string]$Version,
        [string]$Pattern
    )

    if ([string]::IsNullOrWhiteSpace($Pattern)) { return $true }
    if ([string]::IsNullOrWhiteSpace($Version)) { return $false }

    $p = $Pattern.Trim()

    # Optional raw regex mode: re:<regex>
    if ($p.Length -ge 3 -and $p.Substring(0, 3).ToLowerInvariant() -eq 're:') {
        $rx = $p.Substring(3)
        if ([string]::IsNullOrWhiteSpace($rx)) { return $true }
        try {
            return [regex]::IsMatch($Version, $rx)
        } catch {
            return $false
        }
    }

    # Simple pattern language:
    #  - '*' => any chars
    #  - '?' => any single char
    #  - 'X' => one or more digits
    # Everything else is literal.
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append('^')
    foreach ($ch in $p.ToCharArray()) {
        switch ($ch) {
            '*' { [void]$sb.Append('.*') }
            '?' { [void]$sb.Append('.') }
            'X' { [void]$sb.Append('\d+') }
            default {
                [void]$sb.Append([regex]::Escape([string]$ch))
            }
        }
    }
    [void]$sb.Append('$')

    try {
        return [regex]::IsMatch($Version, $sb.ToString())
    } catch {
        return $false
    }
}

