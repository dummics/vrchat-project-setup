[CmdletBinding()]
param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\setup-scripts\commands\wizard.ps1')
Add-Type -LiteralPath (Join-Path $scriptDir 'lib\spectre\Spectre.Console.Ansi.dll')
Add-Type -LiteralPath (Join-Path $scriptDir 'lib\spectre\Spectre.Console.dll')
# Enable the renderer for this redirected, injected-input test host only.
$script:VrcSetupSpectreAvailable = $true
function Assert-Navigation { param($Condition, $Message); if (-not $Condition) { throw $Message } }
$catalog = [pscustomobject]@{ RootPath='F:\Unity Projects'; ProjectCount=3; SortOrder='recent'; Projects=@(
    [pscustomobject]@{RelativePath='Creator [daily] & café PC';Path='F:\Unity Projects\Creator [daily] & café PC';Kind='Avatar';UnityVersion='2022.3.22f1';PackageCount=7;LastModifiedUtc='2026-10-10T12:00:00Z'},
    [pscustomobject]@{RelativePath='Creator Quest';Path='F:\Unity Projects\Creator Quest';Kind='Avatar';UnityVersion='2022.3.22f1';PackageCount=5;LastModifiedUtc='2026-10-09T12:00:00Z'},
    [pscustomobject]@{RelativePath='World Portal';Path='F:\Unity Projects\World Portal';Kind='World';UnityVersion='2022.3.22f1';PackageCount=3;LastModifiedUtc='2026-10-08T12:00:00Z'}
) }
Assert-Navigation (@(Get-VrcSetupFilteredProjects $catalog.Projects 'creator quest').Count -eq 1) 'Multi-word library search failed.'
Assert-Navigation (@(Get-VrcSetupFilteredProjects $catalog.Projects '[daily]').Count -eq 1) 'Search interpreted brackets as wildcard syntax.'
Assert-Navigation ((Get-VrcSetupMenuActionWeight 'Start importing') -eq 'Primary' -and (Get-VrcSetupMenuActionWeight 'Customize packages') -eq 'Secondary' -and (Get-VrcSetupMenuActionWeight 'Change project name') -eq 'Utility' -and (Get-VrcSetupMenuActionWeight 'Cancel') -eq 'Footer') 'Import actions lost their visual hierarchy.'
$navigationSource = Get-Content -LiteralPath (Join-Path $scriptDir 'lib\navigation-ui.ps1') -Raw
Assert-Navigation ([regex]::Matches($navigationSource,'param\(\$context\)\s+\$context.Refresh\(\)\s+while').Count -eq 2) 'Menu/library must draw their first frame before waiting for keyboard input.'
$originalWidth = [Spectre.Console.AnsiConsole]::Profile.Width
try {
    [Spectre.Console.AnsiConsole]::Record()
    foreach ($width in @(50,80,120,160)) {
        [Spectre.Console.AnsiConsole]::Profile.Width=$width
        [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new("Library - ${width} columns"))
        [Spectre.Console.AnsiConsole]::Write((New-VrcSetupProjectLibraryRenderable -Catalog $catalog -State ([pscustomobject]@{Filter='';Index=1}) -Width $width -Height 30))
    }
    [Spectre.Console.AnsiConsole]::Profile.Width=100
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Main menu'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupNavigationMenuRenderable -Options @('Import avatar or asset','My projects','Packages and creator presets','Settings','Exit') -SectionBreaks @(2,4)))
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Settings'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupNavigationMenuRenderable -Options @('Unity Editor  ·  Ready','Save projects in  ·  Ready','Project names','Additional imports  ·  Disabled','Reset all settings','Back') -SectionBreaks @(2,4,5)))
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Import avatar or asset'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupNavigationMenuRenderable -Options @('Start importing','Package presets','Customize packages','Change project name','Choose another UnityPackage','Cancel') -SectionBreaks @(1,3,5) -Current 3))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupTextInputRenderable -Value '' -Prompt 'Drop .unitypackage here' -Placeholder 'Drag and drop here' -Hint 'Enter Select  ·  Esc Back'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupProjectLibraryRenderable -Catalog $catalog -State ([pscustomobject]@{Filter='missing';Index=0}) -Width 100 -Height 24))
    $text = [Spectre.Console.AnsiConsole]::ExportText()
    Assert-Navigation ($text -match 'No matching projects' -and $text -match 'Drag and drop here' -and $text -match 'Find a project') 'Navigation states failed to render.'
    if ($OutputPath) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath)))
        [IO.File]::WriteAllText($OutputPath,[Spectre.Console.AnsiConsole]::ExportHtml())
        [IO.File]::WriteAllText([IO.Path]::ChangeExtension($OutputPath,'.txt'),$text)
    }
    # Exercise the same key handlers used by the live console.
    $state = [pscustomobject]@{Index=0;Done=$false;Result=-1}
    Update-VrcSetupMenuKey -State $state -Key ([pscustomobject]@{Key='DownArrow'}) -Options @('One','Two','Back')
    Update-VrcSetupMenuKey -State $state -Key ([pscustomobject]@{Key='Enter'}) -Options @('One','Two','Back')
    Assert-Navigation ($state.Result -eq 1 -and $state.Done) 'Menu selection index changed.'
    $state = [pscustomobject]@{Index=0;Done=$false;Result=-1}
    Update-VrcSetupMenuKey -State $state -Key ([pscustomobject]@{Key='Escape'}) -Options @('One','Back')
    Assert-Navigation ($state.Result -eq -1 -and $state.Done) 'Menu cancellation changed.'
    $state = [pscustomobject]@{Filter='';Index=0;Done=$false;Result=$null}
    foreach ($letter in 'Quest'.ToCharArray()) { Update-VrcSetupLibraryKey -State $state -Key ([pscustomobject]@{Key='Character';KeyChar=$letter}) -Catalog $catalog }
    Update-VrcSetupLibraryKey -State $state -Key ([pscustomobject]@{Key='Enter'}) -Catalog $catalog
    Assert-Navigation ($state.Result.Action -eq 'project' -and $state.Result.ProjectPath -eq $catalog.Projects[1].Path) 'Filtered selection opened the wrong project.'
    $state = [pscustomobject]@{Filter='';Index=0;Done=$false;Result=$null}
    Update-VrcSetupLibraryKey -State $state -Key ([pscustomobject]@{Key='F2'}) -Catalog $catalog
    Assert-Navigation ($state.Result.Action -eq 'library') 'Library options are unreachable.'
    $state = [pscustomobject]@{Filter='missing';Index=0;Done=$false;Result=$null}
    Update-VrcSetupLibraryKey -State $state -Key ([pscustomobject]@{Key='Enter'}) -Catalog $catalog
    Assert-Navigation (-not $state.Done -and $null -eq $state.Result) 'Empty search opened a project.'
    $normalized = Normalize-UserPath '"F:\Unity Projects\Creator [daily] & café.unitypackage"'
    Assert-Navigation ($normalized -eq 'F:\Unity Projects\Creator [daily] & café.unitypackage') 'Dropped path normalization regressed.'
} finally { [Spectre.Console.AnsiConsole]::Profile.Width=$originalWidth }
Write-Host 'PASS: responsive navigation, project search, key routing and drop input rendering.'
