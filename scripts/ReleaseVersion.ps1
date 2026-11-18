# Shared SemVer policy for local publishing and CI. No Git or network side effects.
function Get-ReleaseVersion {
    param([Parameter(Mandatory)][string]$Value)
    if ($Value -cnotmatch '^v?(?<version>(?<core>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))(?:-(?<channel>alpha|beta|rc)\.(?<iteration>0|[1-9]\d*))?)(?:\+build\.(?<build>[1-9]\d*))?$') { throw "Invalid release version: $Value" }
    $rank = switch ($Matches.channel) { alpha {0}; beta {1}; rc {2}; default {3} }
    $base = $Matches.version
    $build = if ($Matches.build) { [long]$Matches.build } else { [long]0 }
    $assetId = if ($build) { "$base-build.$build" } else { $base }
    [pscustomobject]@{ Text=$Value.TrimStart('v'); Base=$base; AssetId=$assetId; Build=$build;
        Core=[version]$Matches.core; Rank=$rank; Iteration=$(if($Matches.iteration){[int]$Matches.iteration}else{0}) }
}
function Compare-ReleaseVersion {
    param([string]$Left, [string]$Right)
    $a = Get-ReleaseVersion $Left; $b = Get-ReleaseVersion $Right
    $result = $a.Core.CompareTo($b.Core)
    if ($result -eq 0) { $result = $a.Rank.CompareTo($b.Rank) }
    if ($result -eq 0) { $result = $a.Iteration.CompareTo($b.Iteration) }
    if ($result -eq 0) { $result = $a.Build.CompareTo($b.Build) }
    return $result
}
