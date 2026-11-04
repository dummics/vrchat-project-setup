[CmdletBinding(SupportsShouldProcess)]
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$commonGit = (& git -C $repo rev-parse --path-format=absolute --git-common-dir)
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve project Git directory.' }
$project = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($commonGit.Trim()))
if ([IO.Path]::GetFileName($project) -ne '_unityprojectsetup') { throw 'Unexpected project; refusing cleanup.' }
$prefix = $project.TrimEnd('\') + '\'
function Assert-InProject([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside project: $full" }
    $ancestor = $full
    while ($ancestor.Length -gt $project.Length) {
        if ([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) { throw "Linked path: $ancestor" }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}
$active = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -match '^(dotnet|MSBuild|VrcSetup[^\\]*|Unity)\.exe$' -and
    ($_.ExecutablePath -like "$project*" -or $_.CommandLine -like "*$project*")
})
if ($active.Count) { throw ('Project processes are active; stop them before cleaning: ' + (($active | ForEach-Object { $_.Name + ':' + $_.ProcessId }) -join ', ')) }
$checkouts = @(& git -C $repo worktree list --porcelain | Where-Object { $_.StartsWith('worktree ') } | ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) })
$tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($checkout in $checkouts) {
    foreach ($relative in (& git -C $checkout -c core.quotepath=false ls-files)) {
        [void]$tracked.Add([IO.Path]::GetFullPath((Join-Path $checkout $relative)))
    }
    if ($LASTEXITCODE -ne 0) { throw "Cannot verify tracked files: $checkout" }
}
$directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$binary = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@('.exe','.dll','.pdb','.zip','.nupkg','.winmd','.pri','.mui','.lib','.obj','.exp','.ilk','.pch','.res','.resources','.msix','.msixbundle','.cab','.7z','.dmp','.a','.so','.dylib','.cache') | ForEach-Object { [void]$binary.Add($_) }
$payloadMarkers = @('.vrcsetup-payload.json','.vrcsetup-install.json','VrcSetup.Desktop.deps.json','VrcSetup.Installer.deps.json','vrcsetup.deps.json')
foreach ($checkout in $checkouts) {
    if ($checkout -ne $project -and -not $checkout.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Checkout outside project: $checkout" }
    foreach ($sourceRoot in @('src','tests')) {
        $source = Join-Path $checkout $sourceRoot
        if (-not (Test-Path -LiteralPath $source)) { continue }
        foreach ($csproj in (Get-ChildItem -LiteralPath $source -Recurse -File -Filter '*.csproj')) {
            foreach ($name in @('bin','obj')) {
                $path = Join-Path $csproj.DirectoryName $name
                if (Test-Path -LiteralPath $path) { [void]$directories.Add((Assert-InProject $path)) }
            }
        }
    }
    $artifacts = Join-Path $checkout '.work/artifacts'
    if (-not (Test-Path -LiteralPath $artifacts)) { continue }
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push((Assert-InProject $artifacts))
    while ($pending.Count) {
        $directory = $pending.Pop()
        $children = @([IO.DirectoryInfo]::new($directory).EnumerateFileSystemInfos())
        if ($children | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Linked artifact entry under $directory" }
        if ([IO.Path]::GetFileName($directory) -eq 'Library' -and
            [IO.Directory]::Exists((Join-Path ([IO.Path]::GetDirectoryName($directory)) 'Assets')) -and
            [IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName($directory)) 'ProjectSettings/ProjectVersion.txt'))) {
            [void]$directories.Add((Assert-InProject $directory)); continue
        }
        if ($directory -ne $artifacts -and ($children | Where-Object { $_ -is [IO.FileInfo] -and $_.Name -in $payloadMarkers })) {
            [void]$directories.Add((Assert-InProject $directory)); continue
        }
        foreach ($child in $children) {
            if ($child -is [IO.DirectoryInfo]) { $pending.Push($child.FullName) }
            elseif ($binary.Contains($child.Extension)) { [void]$files.Add((Assert-InProject $child.FullName)) }
        }
    }
}
$targets = @($directories | Sort-Object Length | ForEach-Object { [pscustomobject]@{Path=$_;Directory=$true} })
$targets += @($files | ForEach-Object { [pscustomobject]@{Path=$_;Directory=$false} })
$bytes = 0L; $count = 0L; $removedBytes = 0L; $removedCount = 0L
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target.Path)) { continue }
    $full = Assert-InProject $target.Path
    if ($tracked.Contains($full) -or ($target.Directory -and ($tracked | Where-Object { $_.StartsWith($full + '\', [StringComparison]::OrdinalIgnoreCase) }))) { throw "Tracked source under cleanup target: $full" }
    $entries = if ($target.Directory) { @(Get-ChildItem -LiteralPath $full -Recurse -Force) } else { @(Get-Item -LiteralPath $full) }
    if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Linked descendant under $full" }
    $size = ($entries | Where-Object { -not $_.PSIsContainer } | Measure-Object Length -Sum).Sum
    $bytes += $size; $count++
    if ($Apply -and $PSCmdlet.ShouldProcess($full, 'Remove reproducible project output')) {
        # Literal paths, one shell, verified scope. Read-only generated files are disposable.
        $entries | Where-Object { -not $_.PSIsContainer -and $_.IsReadOnly } | ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath $full -Recurse -ErrorAction Stop
        if (Test-Path -LiteralPath $full) { throw "Cleanup incomplete: $full" }
        $removedBytes += $size; $removedCount++
    }
}
[pscustomobject]@{Mode=$(if($Apply){'Apply'}else{'Preview'});Targets=$count;Bytes=$bytes;GiB=[math]::Round($bytes/1GB,3);RemovedTargets=$removedCount;RemovedBytes=$removedBytes;Scope=$project}
