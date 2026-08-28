[CmdletBinding(PositionalBinding = $true)]
param(
    [Parameter(Position = 0)][string]$Command,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Arguments,
    [string]$projectPath,
    [switch]$Test,
    [switch]$Wizard,
    [switch]$Json,
    [switch]$DryRun,
    [switch]$Refresh,
    [ValidateSet('recent', 'name')][string]$Sort,
    [string]$Name,
    [string[]]$Package
)

# Top-level entry point for vrc setup
$scriptDir = $PSScriptRoot
. "${scriptDir}\lib\config.ps1"
. "${scriptDir}\commands\wizard.ps1"
. "${scriptDir}\commands\installer.ps1"
. "${scriptDir}\commands\cli.ps1"

$configPath = Join-Path $scriptDir "config\vrcsetup.json"
$defaultsPath = Join-Path $scriptDir "config\vrcsetup.defaults"
[void](Initialize-ConfigIfMissing -ConfigPath $configPath -DefaultsPath $defaultsPath)

if ($Wizard -or ((-not $Command) -and (-not $projectPath))) {
    Start-Wizard
    exit 0
}

if ($Command) {
    $cliOutput = @(Invoke-VrcSetupCli -Command $Command -Arguments $Arguments -ScriptDir $scriptDir -ConfigPath $configPath -Json:$Json -DryRun:($DryRun -or $Test) -Refresh:$Refresh -SortOrder $Sort -Name $Name -Package $Package)
    if ($cliOutput.Count -eq 0) {
        exit 1
    }
    $cliStatus = [int]$cliOutput[-1]
    if ($cliOutput.Count -gt 1) {
        $cliOutput[0..($cliOutput.Count - 2)] | Write-Output
    }
    exit $cliStatus
}
$status = Start-Installer -projectPath $projectPath -Test:$Test
exit $status
