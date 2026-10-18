[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$export = Join-Path $PSScriptRoot '../scripts/Export-UpdateFeed.ps1'
$target = Join-Path $OutputDirectory 'releases.json'
function Release([string]$version, [bool]$draft = $false, [bool]$complete = $true, [string]$hostUri = 'https://github.com/dummics/vrchat-project-setup') {
    $names = @("Install-VrcSetup-$version-win-x64.exe", 'SHA256SUMS.txt', 'components.json')
    if (-not $complete) { $names = @($names | Where-Object { $_ -ne 'SHA256SUMS.txt' }) }
    [pscustomobject]@{ tag_name="v$version"; draft=$draft; prerelease=$version.Contains('-'); body='Not part of the update contract'; assets=@($names | ForEach-Object { @{ name=$_; browser_download_url="$hostUri/releases/download/v$version/$_" } }) }
}
$fixtures = @((Release '0.2.0-beta.9'), (Release '0.2.0-beta.18'), (Release '1.0.0'), (Release '1.1.0-rc.1'),
    (Release '1.2.0' $true), (Release '1.3.0' $false $false), (Release '1.4.0' $false $true 'https://untrusted.test'))
& $export -Releases $fixtures -OutputPath $target | Out-Null
$feed = @(Get-Content $target -Raw | ConvertFrom-Json)
if ($feed.Count -ne 4 -or ($feed.tag_name -join ',') -ne 'v1.1.0-rc.1,v1.0.0,v0.2.0-beta.18,v0.2.0-beta.9') { throw 'Feed must retain complete releases in semantic order and exclude drafts/incomplete/untrusted assets.' }
if ($feed[0].prerelease -ne $true -or $feed[1].prerelease -ne $false) { throw 'Preserve release channels.' }
if ($feed[0].PSObject.Properties.Name -contains 'body' -or $feed[0].assets.Count -ne 3) { throw 'Export only client metadata.' }
$before = [IO.File]::ReadAllText($target)
& $export -Releases @($fixtures[0]) -OutputPath $target | Out-Null
if (([IO.File]::ReadAllText($target).TrimStart())[0] -ne '[') { throw 'A one-release feed must remain a JSON array.' }
& $export -Releases $fixtures -OutputPath $target | Out-Null
if ([IO.File]::ReadAllText($target) -ne $before) { throw 'Identical releases must produce deterministic output.' }
$rejected = $false
try { & $export -Releases @((Release '1.2.0' $true)) -OutputPath $target | Out-Null } catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText($target) -ne $before) { throw 'An empty valid release set must preserve the previous feed.' }
# Exercise the publication path without a real GitHub server or credentials.
$publication = @{ Content = $null; Writes = 0 }
function Invoke-RestMethod {
    param([string]$Uri, $Headers, [string]$Method = 'Get', $ContentType, $Body)
    if ($Uri -like 'https://api.github.com/repos/dummics/vrchat-project-setup/releases?*') { return $fixtures }
    if ($Uri -like '*/contents/.github/updates/releases.json*') {
        if ($Method -eq 'Put') {
            $payload = [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
            if ($payload.branch -ne 'main') { throw 'Wrong publication branch.' }
            $publication.Content = $payload.content; $publication.Writes++; return @{}
        }
        if ($publication.Content) { return @{ sha='fixture-sha'; content=$publication.Content } }
        throw [Microsoft.PowerShell.Commands.HttpResponseException]::new('Not found', [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::NotFound))
    }
    if ($Method -eq 'Patch' -and $Uri -like 'https://api.github.com/repos/dummics/vrchat-project-setup/releases/*') { return @{} }
    throw "Unexpected request in offline test: $Method $Uri"
}
$addedToken = -not (Test-Path Env:GH_TOKEN)
if ($addedToken) { $env:GH_TOKEN = 'offline-fixture' }
try {
    & (Join-Path $PSScriptRoot '../scripts/Sync-ReleasePresentation.ps1') -FeedBranch main | Out-Null
    $published = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($publication.Content)) | ConvertFrom-Json
    if (@($published).Count -ne 4 -or $publication.Writes -ne 1) { throw 'Create the static feed after a missing-file response.' }
    & (Join-Path $PSScriptRoot '../scripts/Sync-ReleasePresentation.ps1') -FeedBranch main | Out-Null
    if ($publication.Writes -ne 1) { throw 'Unchanged feed publication must not create another commit.' }
} finally { if ($addedToken) { Remove-Item Env:GH_TOKEN } }
[pscustomobject]@{ Passed=$true; Releases=$feed.Count; Fixture=$target }
