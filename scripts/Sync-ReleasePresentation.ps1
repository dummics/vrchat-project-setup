[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Repository = 'dummics/vrchat-project-setup',
    [string]$SnapshotPath,
    [string]$ReadmeBranch,
    [string]$FeedBranch
)
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository.' }
$headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
$base = 'https://api.github.com/repos/' + $Repository
$releases = @(); $page = 1
do {
    $batch = Invoke-RestMethod "$base/releases?per_page=100&page=$page" -Headers $headers
    $batch = @($batch)
    $releases += $batch; $page++
} while ($batch.Count -eq 100)
if ($SnapshotPath) { $releases | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8 }
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
$ranked = foreach ($release in $releases) {
    if ($release.draft) { continue }
    try { $key = Get-ReleaseVersion $release.tag_name } catch { continue }
    [pscustomobject]@{ Release=$release; Version=$key.Text; Core=$key.Core; Rank=$key.Rank; Iteration=$key.Iteration }
}
$latest = $ranked | Sort-Object Core,Rank,Iteration -Descending | Select-Object -First 1
if (-not $latest) { throw 'No numbered public release found.' }
$latestUrl = $latest.Release.html_url
$start = '<!-- vrcsetup-download:start -->'; $end = '<!-- vrcsetup-download:end -->'
foreach ($release in $releases) {
    $match = [regex]::Match($release.tag_name, '^v?(\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?)$')
    if ($release.draft -or -not $match.Success) { continue }
    $version = $match.Groups[1].Value
    $installer = @($release.assets | Where-Object name -In @("VrcSetup-$version-win-x64.exe", "Install-VrcSetup-$version-win-x64.exe") | Sort-Object name | Select-Object -First 1)
    if ($installer.Count -ne 1) { throw "No unified installer for $($release.tag_name); left untouched." }
    $asset = $installer[0]
    $badge = '[![Download this version](https://img.shields.io/badge/Download_this_version-Windows_x64-2ea493?style=for-the-badge)](' + $asset.browser_download_url + ')'
    $body = [string]$release.body
    $body = [regex]::Replace($body, '(?s)<!-- vrcsetup-download:start -->.*?<!-- vrcsetup-download:end -->\s*', '')
    $latestLine = if ($version -ne $latest.Version) { 'Latest version: **[' + $latest.Version + '](' + $latestUrl + ')**' + "`n`n" } else { '' }
    $body = "$start`n$latestLine$badge`n$end`n`n" + $body.TrimStart()
    if ($release.name -ne $version -or $release.body -ne $body) {
        if ($PSCmdlet.ShouldProcess($release.tag_name, 'Set version-only title and installer download button')) {
            if (-not $env:GH_TOKEN) { throw 'A repository-scoped GH_TOKEN is required for changes. Use the maintenance workflow.' }
            $payload = @{ name = $version; body = $body } | ConvertTo-Json -Depth 4
            $null = Invoke-RestMethod "$base/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload))
        }
    }
    $label = "VrcSetup-$version-win-x64.exe"
    if ($asset.label -ne $label -and $PSCmdlet.ShouldProcess($asset.name, "Set display label to $label")) {
        if (-not $env:GH_TOKEN) { throw 'A repository-scoped GH_TOKEN is required for changes.' }
        $payload = @{ label = $label } | ConvertTo-Json
        $null = Invoke-RestMethod "$base/releases/assets/$($asset.id)" -Method Patch -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload))
    }
    [pscustomobject]@{ Version = $version; Installer = $asset.name; DisplayLabel = $label }
}
if ($ReadmeBranch) {
    $readme = Invoke-RestMethod "$base/contents/README.md?ref=$([uri]::EscapeDataString($ReadmeBranch))" -Headers $headers
    $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($readme.content))
    $pattern = [regex]::new([regex]::Escape("https://github.com/$Repository/releases") + '(?:/tag/[^\s)]+)?')
    $updated = $pattern.Replace($text, $latestUrl, 1)
    if ($text -ne $updated -and $PSCmdlet.ShouldProcess("$ReadmeBranch/README.md", "Point download to $($latest.Version)")) {
        if (-not $env:GH_TOKEN) { throw 'A repository-scoped GH_TOKEN is required for changes.' }
        $payload = @{ message='docs(readme): point download to ' + $latest.Version; branch=$ReadmeBranch; sha=$readme.sha; content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($updated)) } | ConvertTo-Json
        $null = Invoke-RestMethod "$base/contents/README.md" -Method Put -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload))
    }
}
if ($FeedBranch) {
    $feedFile = Join-Path $PSScriptRoot '../.work/artifacts/update-feed/releases.json'
    & (Join-Path $PSScriptRoot 'Export-UpdateFeed.ps1') -Releases $releases -Repository $Repository -OutputPath $feedFile
    $json = [IO.File]::ReadAllText($feedFile)
    $contentUrl = "$base/contents/.github/updates/releases.json"
    $existing = $null
    try { $existing = Invoke-RestMethod ($contentUrl + '?ref=' + [uri]::EscapeDataString($FeedBranch)) -Headers $headers }
    catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
    $oldJson = if ($existing) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($existing.content)) } else { '' }
    if ($oldJson -ne $json -and $PSCmdlet.ShouldProcess("$FeedBranch/.github/updates/releases.json", 'Publish static update feed')) {
        if (-not $env:GH_TOKEN) { throw 'A repository-scoped GH_TOKEN is required for changes.' }
        $payload = @{ message='chore(updates): refresh static release feed'; branch=$FeedBranch; content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)) }
        if ($existing) { $payload.sha = $existing.sha }
        $null = Invoke-RestMethod $contentUrl -Method Put -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json)))
    }
}
