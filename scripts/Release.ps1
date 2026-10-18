# One intentional release action. Ordinary source commits never create updates.
[CmdletBinding()]
param([switch]$NextBeta, [string]$Version, [switch]$Publish)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseVersion.ps1')
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$remote = 'https://github.com/dummics/vrchat-project-setup.git'
Push-Location $repoRoot
try {
    function Git-Checked { param([string[]]$Arguments); $output = & git @Arguments; if ($LASTEXITCODE -ne 0) { throw "git $($Arguments[0]) failed." }; return $output }
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    $props = [IO.File]::ReadAllText($propsPath)
    $current = ([xml]$props).Project.PropertyGroup.Version
    if ($NextBeta -and $Version) { throw 'Choose -NextBeta or -Version.' }
    if ($NextBeta) {
        if ($current -notmatch '^(\d+\.\d+\.\d+)-beta\.(\d+)$') { throw 'Use -Version to start a new beta series.' }
        $Version = $Matches[1] + '-beta.' + (1 + [int]$Matches[2])
    }
    if (-not $Version) { throw 'Use -NextBeta or -Version x.y.z[-beta.N|-rc.N]. Add -Publish to publish; otherwise this is a preview.' }
    $Version = (Get-ReleaseVersion $Version).Text
    if ((Compare-ReleaseVersion $Version $current) -le 0) { throw 'The release version must be newer than Directory.Build.props.' }
    $branch = (Git-Checked -Arguments @('branch','--show-current')).Trim()
    if (-not $branch) { throw 'Release from a named branch, not detached HEAD.' }
    $dirty = Git-Checked -Arguments @('status','--porcelain')
    if ($Publish -and $dirty) { throw 'Commit the intended changes first. Release never stages unrelated files.' }
    $tag = 'v' + $Version
    $existing = Git-Checked -Arguments @('ls-remote','--tags',$remote,('refs/tags/' + $tag))
    if ($existing -or (Git-Checked -Arguments @('tag','--list',$tag))) { throw "Tag $tag already exists; release tags are immutable." }
    [pscustomobject]@{Current=$current;Next=$Version;Branch=$branch;Tag=$tag;Publish=[bool]$Publish;Workflow='windows-release.yml'}
    if (-not $Publish) { return }
    $remoteHead = Git-Checked -Arguments @('ls-remote',$remote,('refs/heads/' + $branch))
    if ($remoteHead) {
        $remoteSha = ($remoteHead -split '\s+')[0]
        & git merge-base --is-ancestor $remoteSha HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Local branch does not contain its remote tip. Fetch and reconcile before publishing.' }
    }
    [IO.File]::WriteAllText($propsPath, $props.Replace('<Version>' + $current + '</Version>', '<Version>' + $Version + '</Version>'), [Text.UTF8Encoding]::new($false))
    Git-Checked -Arguments @('add','--','Directory.Build.props')
    Git-Checked -Arguments @('commit','-m',('chore(release): ' + $Version))
    Git-Checked -Arguments @('tag',$tag)
    # Publish branch and tag atomically. CI owns build, verification and assets.
    Git-Checked -Arguments @('push','--atomic',$remote,('HEAD:refs/heads/' + $branch),('refs/tags/' + $tag))
    Write-Output "Release submitted: https://github.com/dummics/vrchat-project-setup/actions/workflows/windows-release.yml"
} finally { Pop-Location }
