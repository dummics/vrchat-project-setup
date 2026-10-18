[CmdletBinding()]
param([Parameter(Mandatory)][string]$Payload, [Parameter(Mandatory)][string]$Engine,
      [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$OutputDirectory,
      [Parameter(Mandatory)][string]$AssetBaseUri)
$ErrorActionPreference = 'Stop'
$basis = [uri]$AssetBaseUri
if ($basis.Scheme -ne 'https' -or -not $basis.IsDefaultPort -or $basis.UserInfo -or $basis.Query -or $basis.Fragment -or -not $basis.AbsolutePath.EndsWith('/')) { throw 'AssetBaseUri must be an HTTPS directory.' }
$components = [Collections.Generic.List[object]]::new()
$groups = @{}
foreach ($source in @(@{ Prefix='payload'; Root=$Payload }, @{ Prefix='engine'; Root=$Engine })) {
    foreach ($file in Get-ChildItem -LiteralPath $source.Root -Recurse -File -Force | Sort-Object FullName) {
        if ($file.Name -eq '.vrcsetup-portable') { continue }
        $relative = $source.Prefix + '/' + $file.FullName.Substring($source.Root.Length + 1).Replace('\','/')
        $application = $file.Name -match '^(VrcSetup|vrcsetup)' -or $file.Extension -in @('.xbf','.pri','.json') -or $relative -notmatch '^(engine/|payload/app/)'
        $category = if ($application) { 'application' } else {
            $family = if ($relative.StartsWith('engine/')) { 'engine' } elseif ($relative.StartsWith('payload/app/desktop/')) { 'desktop' } else { 'cli' }
            $bucket = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($relative))[0] % 4
            "$family-runtime-$bucket"
        }
        if (-not $groups.ContainsKey($category)) { $groups[$category] = [Collections.Generic.List[object]]::new() }
        $groups[$category].Add(@{ Source=$file.FullName; Relative=$relative; Hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash })
    }
}
foreach ($category in $groups.Keys | Sort-Object) {
    $temporary = Join-Path $OutputDirectory "$category.component"
    $archive = [IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
    $files = [ordered]@{}
    try {
        foreach ($file in $groups[$category] | Sort-Object { $_.Relative }) {
            $entry = $archive.CreateEntry($file.Relative,[IO.Compression.CompressionLevel]::SmallestSize)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($file.Source); $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
            $files[$file.Relative] = $file.Hash
        }
    } finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    $name = "$hash.zip"; $destination = Join-Path $OutputDirectory $name
    Move-Item -LiteralPath $temporary -Destination $destination
    $components.Add([ordered]@{ Name=$name; Sha256=$hash; Bytes=(Get-Item -LiteralPath $destination).Length; Files=$files })
}
$manifest = Join-Path $OutputDirectory 'components.json'
[IO.File]::WriteAllText($manifest, (@{Schema=1;Version=$Version;BaseUri=$AssetBaseUri;Components=@($components);Prefetch=$true} | ConvertTo-Json -Depth 6 -Compress), [Text.UTF8Encoding]::new($false))
Write-Output $manifest
