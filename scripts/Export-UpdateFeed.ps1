[CmdletBinding()]
param(
    [Parameter(Mandatory)][object[]]$Releases,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$Repository = 'dummics/vrchat-project-setup'
)
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository.' }
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
$entries = @(foreach ($release in $Releases) {
    if ($release.draft) { continue }
    try { $version = Get-ReleaseVersion $release.tag_name } catch { continue }
    $prefix = "https://github.com/$Repository/releases/download/$($release.tag_name)/"
    $assets = @($release.assets | Where-Object {
        $_.browser_download_url -eq ($prefix + $_.name) -and
        ($_.name -in @("Install-VrcSetup-$($version.Text)-win-x64.exe", "VrcSetup-$($version.Text)-win-x64.zip", 'SHA256SUMS.txt', 'components.json') -or
         $_.name -match '^Update-VrcSetup-[0-9A-Za-z.-]+-win-x64\.exe$')
    } | Sort-Object name | ForEach-Object { [ordered]@{ name = $_.name; browser_download_url = $_.browser_download_url } })
    if ($assets.name -notcontains "Install-VrcSetup-$($version.Text)-win-x64.exe" -or $assets.name -notcontains 'SHA256SUMS.txt') { continue }
    [pscustomobject]@{
        Core = $version.Core; Rank = $version.Rank; Iteration = $version.Iteration
        Entry = [ordered]@{ tag_name = $release.tag_name; draft = $false; prerelease = [bool]$release.prerelease; assets = $assets }
    }
})
if ($entries.Count -eq 0) { throw 'No complete public releases; refusing to replace the update feed.' }
$json = ConvertTo-Json -InputObject @($entries | Sort-Object Core,Rank,Iteration -Descending | ForEach-Object Entry) -Depth 6
$target = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
[IO.File]::WriteAllText($target, $json + "`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Releases = $entries.Count; Feed = $target }
