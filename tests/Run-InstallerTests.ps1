[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Installer, [Parameter(Mandatory=$true)][string]$TestRoot)
$ErrorActionPreference = 'Stop'
$Installer = [IO.Path]::GetFullPath($Installer)
$TestRoot = [IO.Path]::GetFullPath($TestRoot)
if (Test-Path -LiteralPath $TestRoot) { throw 'Choose a fresh test folder; existing evidence is preserved.' }
[void][IO.Directory]::CreateDirectory($TestRoot)
$previousSkip = $env:VRCSETUP_SKIP_PATH_UPDATE; $previousMenu = $env:VRCSETUP_START_MENU_ROOT
$env:VRCSETUP_SKIP_PATH_UPDATE = '1'; $env:VRCSETUP_START_MENU_ROOT = Join-Path $TestRoot 'menu'
$program = Join-Path $TestRoot ('Program [test] & caf' + [char]0xE9); $dataRoot = Join-Path $TestRoot 'home'
$checks = [Collections.Generic.List[string]]::new()
function Assert-Test([bool]$condition,[string]$message) { if (-not $condition) { throw $message }; $checks.Add($message); Write-Output $message }
function Invoke-Installer([string]$name,[string[]]$Options) {
    $result = Join-Path $TestRoot ($name + '.json')
    $arguments = @('--silent','--root',('"' + $program + '"'),'--home',('"' + $dataRoot + '"'),'--result',('"' + $result + '"')) + $Options
    $process = Start-Process -FilePath $Installer -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    $data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    Assert-Test ($data.Success -eq $true) ('Installer ' + $name + ' completed')
}
try {
    $legacyConfig = Join-Path $program 'setup-scripts\config'; [void][IO.Directory]::CreateDirectory($legacyConfig)
    [IO.File]::WriteAllText((Join-Path $program '.vrcsetup-installed'),'{}')
    $legacyEditor = Join-Path $TestRoot 'Unity\Editor\Unity.exe'; $legacyProjects = Join-Path $TestRoot 'Project'
    [IO.File]::WriteAllText((Join-Path $legacyConfig 'vrcsetup.json'),(@{ UnityEditorPath=$legacyEditor; UnityProjectsRoot=$legacyProjects; VpmPackages=@{ 'com.vrchat.base'='latest'; 'com.vrchat.avatars'='latest'; 'com.vrchat.core.vpm-resolver'='latest' } } | ConvertTo-Json -Depth 4))
    Invoke-Installer 'install' @()
    $migrated = Get-Content -LiteralPath (Join-Path $dataRoot 'config.json') -Raw | ConvertFrom-Json
    Assert-Test ($migrated.UnityEditorPath -eq $legacyEditor -and $migrated.UnityProjectsRoot -eq $legacyProjects) 'Legacy settings migrate outside the portable extraction folder'
    Assert-Test (Test-Path -LiteralPath (Join-Path $program 'app\desktop\VrcSetup.Desktop.exe')) 'Native desktop payload installed'
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $program '.vrcsetup-portable'))) 'Installed settings are external to program files'
    $cli = Join-Path $program 'app\cli\vrcsetup.exe'
    & $cli config set --home $dataRoot --theme Dark
    Assert-Test ($LASTEXITCODE -eq 0) 'Bundled CLI runs without SDK'
    $configHash = (Get-FileHash -LiteralPath (Join-Path $dataRoot 'config.json')).Hash
    Invoke-Installer 'upgrade' @()
    Assert-Test ((Get-FileHash -LiteralPath (Join-Path $dataRoot 'config.json')).Hash -eq $configHash) 'Upgrade preserves user settings'
    $record = Get-Content -LiteralPath (Join-Path $program '.vrcsetup-install.json') -Raw | ConvertFrom-Json
    Assert-Test (Test-Path -LiteralPath $record.PreviousInstall) 'Upgrade retains previous program for rollback'
    $alias = Join-Path $program 'bin\vrcsetup.cmd'
    Remove-Item -LiteralPath $alias
    Invoke-Installer 'repair' @('--repair')
    Assert-Test (Test-Path -LiteralPath $alias) 'Repair recreates missing command alias'
    $env:VRCSETUP_HOME = $dataRoot
    & $alias help | Out-Null
    Assert-Test ($LASTEXITCODE -eq 0) 'Installed command alias resolves bundled CLI'
    $safety = Join-Path $TestRoot 'Project\Assets'; [void][IO.Directory]::CreateDirectory($safety)
    [IO.File]::WriteAllText((Join-Path $safety 'keep.txt'),'personal project')
    & $cli uninstall --root $program --home $dataRoot --yes
    Assert-Test ($LASTEXITCODE -eq 0) 'Running CLI schedules self-uninstall'
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (-not (Test-Path -LiteralPath $program) -and -not (Get-ChildItem -LiteralPath $TestRoot -Directory | Where-Object { $_.Name -like 'Program*.uninstall-*' })) { break }
        Start-Sleep -Milliseconds 500
    }
    Assert-Test (-not (Test-Path -LiteralPath $program)) 'Self-uninstall removes entrypoint folder'
    Assert-Test (-not (Get-ChildItem -LiteralPath $TestRoot -Directory | Where-Object { $_.Name -like 'Program*.uninstall-*' })) 'Hidden helper removes locked executable after process exit'
    Assert-Test ((Get-FileHash -LiteralPath (Join-Path $dataRoot 'config.json')).Hash -eq $configHash) 'Uninstall preserves settings'
    Assert-Test (Test-Path -LiteralPath (Join-Path $safety 'keep.txt')) 'Uninstall preserves Unity projects'
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $env:VRCSETUP_START_MENU_ROOT 'VRChat Project Setup'))) 'Uninstall removes owned shortcuts'
    [IO.File]::WriteAllText((Join-Path $TestRoot 'checks.json'),(@{ Passed=$true; Checks=@($checks) } | ConvertTo-Json -Depth 4))
}
finally { $env:VRCSETUP_SKIP_PATH_UPDATE = $previousSkip; $env:VRCSETUP_START_MENU_ROOT = $previousMenu; Remove-Item Env:VRCSETUP_HOME -ErrorAction SilentlyContinue }
