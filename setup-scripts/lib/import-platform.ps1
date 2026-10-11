function Get-VrcSetupSuggestedBuildTarget {
    param([string]$PackagePath)
    if ([IO.Path]::GetFileNameWithoutExtension($PackagePath) -match '(?i)(?:^|[\s_.()-])(?:quest|android)(?:$|[\s_.()-]|\d)') { return 'Android' }
    return 'StandaloneWindows64'
}

function Get-VrcSetupBuildTargetLabel {
    param([string]$BuildTarget)
    switch ($BuildTarget) { 'Android' { 'Android / Quest' } 'iOS' { 'iOS' } default { 'PC / Windows' } }
}

function Test-VrcSetupBuildTargetSupport {
    param([string]$EditorPath, [string]$BuildTarget)
    $module = switch ($BuildTarget) { 'Android' { 'AndroidPlayer' } 'iOS' { 'iOSSupport' } default { 'windowsstandalonesupport' } }
    return Test-Path -LiteralPath (Join-Path (Split-Path -Parent $EditorPath) "Data\PlaybackEngines\$module")
}
