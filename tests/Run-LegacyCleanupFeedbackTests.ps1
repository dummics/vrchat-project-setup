$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\setup-scripts\commands\installer.ps1')
$root = Join-Path $env:TEMP ('vrcsetup-legacy-feedback-' + [guid]::NewGuid().ToString('N'))
$previousLog = $global:VRCSETUP_LOGFILE
function Assert-Cleanup { param($Condition,$Message); if (-not $Condition) { throw $Message } }
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $root 'Packages\com.poiyomi.toon'))
    [IO.File]::WriteAllText((Join-Path $root 'Packages\com.poiyomi.toon\package.json'), '{"name":"com.poiyomi.toon","legacyPackages":["com.poiyomi.pro"]}')
    [IO.File]::WriteAllText((Join-Path $root 'Packages\vpm-manifest.json'), '{"dependencies":{"com.poiyomi.toon":{"version":"10.0.25"}},"locked":{}}')
    [IO.File]::WriteAllText((Join-Path $root 'Packages\manifest.json'), '{"dependencies":{}}')
    $global:VRCSETUP_LOGFILE = Join-Path $root 'full.log'
    $lines = @('[17:44:17 ERR] Failed to remove package com.poiyomi.pro', '[17:44:17 INF] Added package com.poiyomi.toon to project')
    $result = @{ExitCode=0;Output=($lines -join "`n")}
    $feedback = @(Write-VrcSetupCommandOutput -Entries $lines -Result $result -PackageName 'com.poiyomi.toon' -ProjectPath $root 6>&1) | Out-String
    Assert-Cleanup ($feedback -match 'already absent' -and $feedback -notmatch '\[.*ERR\]') 'Absent legacy package still shows a false failure.'
    Assert-Cleanup ((Get-Content $global:VRCSETUP_LOGFILE -Raw) -match 'Failed to remove package com.poiyomi.pro') 'Raw diagnostic output was lost.'
    foreach ($kind in @('folder','direct','locked','unity','failed','unrelated')) {
        [IO.File]::WriteAllText((Join-Path $root 'Packages\vpm-manifest.json'), '{"dependencies":{},"locked":{}}')
        [IO.File]::WriteAllText((Join-Path $root 'Packages\manifest.json'), '{"dependencies":{}}')
        $result.ExitCode = 0
        if ($kind -eq 'folder') { [void][IO.Directory]::CreateDirectory((Join-Path $root 'Packages\com.poiyomi.pro')) }
        if ($kind -eq 'direct') { [IO.File]::WriteAllText((Join-Path $root 'Packages\vpm-manifest.json'), '{"dependencies":{"com.poiyomi.pro":{"version":"1"}},"locked":{}}') }
        if ($kind -eq 'locked') { [IO.File]::WriteAllText((Join-Path $root 'Packages\vpm-manifest.json'), '{"dependencies":{},"locked":{"com.poiyomi.pro":{"version":"1"}}}') }
        if ($kind -eq 'unity') { [IO.File]::WriteAllText((Join-Path $root 'Packages\manifest.json'), '{"dependencies":{"com.poiyomi.pro":"1"}}') }
        if ($kind -eq 'failed') { $result.ExitCode = 1 }
        $entries = if ($kind -eq 'unrelated') { @('[17:44:17 ERR] Failed to remove package com.example.other') } else { $lines }
        $feedback = @(Write-VrcSetupCommandOutput -Entries $entries -Result $result -PackageName 'com.poiyomi.toon' -ProjectPath $root 6>&1) | Out-String
        Assert-Cleanup ($feedback -match '\[.*ERR\]') "Real or unverified cleanup failure hidden: $kind"
        if ($kind -eq 'folder') { [IO.Directory]::Delete((Join-Path $root 'Packages\com.poiyomi.pro')) }
    }
    Write-Host 'PASS: absent legacy cleanup is explained; raw logs and genuine failures preserved.'
} finally {
    $global:VRCSETUP_LOGFILE = $previousLog
    if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root,$true) }
}
