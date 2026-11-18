[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$payloadRoot = (Resolve-Path -LiteralPath $Payload).Path
$runtimeRoot = (Resolve-Path -LiteralPath $Runtime).Path
$destination = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $destination) { throw 'The offline archive already exists.' }
if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot '.vrcsetup-portable') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $payloadRoot 'app/desktop/web/index.html') -PathType Leaf)) {
    throw 'Build a portable React payload first.'
}
$browser = Join-Path $runtimeRoot 'msedgewebview2.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $browser
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation') {
    throw 'The Fixed Version executable must have a valid Microsoft signature.'
}
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($browser))
try {
    $reader.BaseStream.Position = 0x3c
    $reader.BaseStream.Position = $reader.ReadInt32()
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Use the x64 runtime.' }
} finally { $reader.Dispose() }
$runtimeFiles = @(Get-ChildItem -LiteralPath $runtimeRoot -Recurse -Force)
if ((Get-Item -LiteralPath $runtimeRoot).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    ($runtimeFiles | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {
    throw 'The Fixed Version runtime cannot contain linked files or directories.'
}
$manifest = Get-Content -LiteralPath (Join-Path $payloadRoot '.vrcsetup-payload.json') -Raw | ConvertFrom-Json -AsHashtable
$partial = $destination + '.partial'
$archive = [IO.Compression.ZipFile]::Open($partial, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $payloadRoot -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($payloadRoot, $file.FullName).Replace('\', '/')
        if ($relative -ne '.vrcsetup-payload.json') {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::SmallestSize)
        }
    }
    foreach ($file in $runtimeFiles | Where-Object { -not $_.PSIsContainer }) {
        $relative = 'app/desktop/webview2-runtime/' + [IO.Path]::GetRelativePath($runtimeRoot, $file.FullName).Replace('\', '/')
        if ($manifest.Files.Contains($relative)) { throw "Duplicate runtime path: $relative" }
        $manifest.Files[$relative] = (Get-FileHash -LiteralPath $file.FullName).Hash
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::SmallestSize)
    }
    $writer = [IO.StreamWriter]::new($archive.CreateEntry('.vrcsetup-payload.json').Open())
    try { $writer.Write(($manifest | ConvertTo-Json -Depth 5 -Compress)) }
    finally { $writer.Dispose() }
} catch {
    $archive.Dispose()
    Remove-Item -LiteralPath $partial -ErrorAction SilentlyContinue
    throw
} finally { $archive.Dispose() }
Move-Item -LiteralPath $partial -Destination $destination
Write-Output $destination
