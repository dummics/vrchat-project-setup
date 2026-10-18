# Shared SemVer policy for local publishing and CI. No Git or network side effects.
function Get-ReleaseVersion {
    param([Parameter(Mandatory)][string]$Value)
    if ($Value -notmatch '^v?(?<core>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))(?:-(?<channel>alpha|beta|rc)\.(?<iteration>0|[1-9]\d*))?$') { throw "Invalid release version: $Value" }
    $rank = switch ($Matches.channel) { alpha {0}; beta {1}; rc {2}; default {3} }
    [pscustomobject]@{ Text=$Value.TrimStart('v'); Core=[version]$Matches.core; Rank=$rank; Iteration=$(if($Matches.iteration){[int]$Matches.iteration}else{0}) }
}
function Compare-ReleaseVersion {
    param([string]$Left, [string]$Right)
    $a = Get-ReleaseVersion $Left; $b = Get-ReleaseVersion $Right
    $result = $a.Core.CompareTo($b.Core)
    if ($result -eq 0) { $result = $a.Rank.CompareTo($b.Rank) }
    if ($result -eq 0) { $result = $a.Iteration.CompareTo($b.Iteration) }
    return $result
}
