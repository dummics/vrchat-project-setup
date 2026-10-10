[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$runtime = Join-Path ([IO.Directory]::GetParent($PSScriptRoot).FullName) 'setup-scripts'
. (Join-Path $runtime 'commands\wizard.ps1')
Add-Type -LiteralPath (Join-Path $runtime 'lib\spectre\Spectre.Console.Ansi.dll')
Add-Type -LiteralPath (Join-Path $runtime 'lib\spectre\Spectre.Console.dll')
[Spectre.Console.AnsiConsole]::Record()
& {
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Search packages'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupTextInputRenderable -Value 'gogo' -Prompt 'Search or package ID' -Hint 'Enter Search  ·  Esc Back'))
    $items = @([pscustomobject]@{Id='gogoloco';DisplayName='GoGoLoco';LatestVersion='1.8.6'}, [pscustomobject]@{Id='com.vrcfury.vrcfury';DisplayName='VRCFury';LatestVersion='1.0.0'})
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Add highlighted package'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupPackagePickerRenderable -Items $items -SelectedIndex 0 -Selected @{0=$false;1=$false}))
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Add selected packages'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupPackagePickerRenderable -Items $items -SelectedIndex 1 -Selected @{0=$true;1=$true}))
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Search and pin versions'))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupVersionPickerRenderable -State ([pscustomobject]@{Filter='1.8';Prerelease=$false;Index=1}) -Versions @('1.8.6','1.8.3','1.8.3-beta.1') -CurrentVersion '1.8.3'))
    [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new('Pending project changes'))
    $workspace = @(Get-VrcSetupPackageWorkspaceItems -CurrentPackages ([pscustomobject]@{gogoloco='1.8.3'}) -DesiredPackages ([pscustomobject]@{gogoloco='1.8.6';'com.vrcfury.vrcfury'='latest'}))
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupPackageWorkspaceRenderable -Items $workspace -SelectedIndex 1 -PendingCount 2 -Notice 'VRCFury queued. Save changes to install.'))
    foreach ($status in @('Success','Failed','Cancelled')) {
        [Spectre.Console.AnsiConsole]::Write([Spectre.Console.Rule]::new("Operation result: ${status}"))
        [Spectre.Console.AnsiConsole]::Write((New-VrcSetupProgressRenderable -Lines @('Processing package gogoloco', 'Package operation finished') -Status $status -MaxVisible 5 -CompletionLabel 'Done - Back to project' -Elapsed ([TimeSpan]::FromSeconds(42))))
    }
} > $null
$html = [Spectre.Console.AnsiConsole]::ExportHtml()
$text = [Spectre.Console.AnsiConsole]::ExportText()
if ($text -notmatch '\[x\]' -or $text -notmatch '\[ \]' -or $text -notmatch 'Add GoGoLoco' -or $text -notmatch 'Use version 1.8.6' -or $text -notmatch 'Continue to recovery' -or $text -notmatch 'Done - Back to project') {
    throw 'UI preview is missing a primary action.'
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath)))
[IO.File]::WriteAllText($OutputPath, $html, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText([IO.Path]::ChangeExtension($OutputPath,'.txt'),$text,[Text.UTF8Encoding]::new($false))
Write-Host "PASS: package UI states rendered to ${OutputPath}"
