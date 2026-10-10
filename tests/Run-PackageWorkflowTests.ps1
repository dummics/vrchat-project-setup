[CmdletBinding()]
param([switch]$Live)
$ErrorActionPreference = 'Stop'
$runtime = Join-Path ([IO.Directory]::GetParent($PSScriptRoot).FullName) 'setup-scripts'
. (Join-Path $runtime 'commands\wizard.ps1')
. (Join-Path $runtime 'commands\cli.ps1')
function Assert-PackageWorkflow { param([bool]$Condition, [string]$Message); if (-not $Condition) { throw $Message } }
$qaRoot = Join-Path $env:TEMP ('vrcsetup-package-qa-' + [guid]::NewGuid().ToString('N'))
$registered = $false
try {
    [void][IO.Directory]::CreateDirectory($qaRoot)
    $configPath = Join-Path $qaRoot 'config.json'
    $config = [pscustomobject]@{ VpmPackages = [pscustomobject]@{} }
    Save-VrcSetupPackagePreset -Config $config -Name 'Creator PC' -Packages ([pscustomobject]@{ gogoloco = '1.8.6'; 'com.example.tool' = 'latest' })
    Save-Config -Config $config -ConfigPath $configPath
    $config = Load-Config -ConfigPath $configPath
    $set = Resolve-VrcSetupPackagePreset -Config $config -Name 'Creator PC' -BasePackages ([pscustomobject]@{ 'com.example.keep' = '2.0.0'; gogoloco = '1.8.5' })
    Assert-PackageWorkflow ($set.gogoloco -eq '1.8.6' -and $set.'com.example.tool' -eq 'latest' -and $set.'com.example.keep' -eq '2.0.0') 'Preset overlay changed an unrelated package or lost version policy.'
    Assert-PackageWorkflow ($set.PSObject.Properties.Name -contains 'com.vrchat.base') 'Required foundation missing.'
    Save-VrcSetupPackagePreset -Config $config -Name 'Creator PC' -Packages ([pscustomobject]@{ gogoloco = 'latest' })
    Assert-PackageWorkflow (@(Get-VrcSetupPackagePresets -Config $config).Count -eq 1) 'Replacing a preset created a duplicate.'
    $rejected = $false
    try { Save-VrcSetupPackagePreset -Config $config -Name 'Broken' -Packages ([pscustomobject]@{ gogoloco = 'random' }) } catch { $rejected = $true }
    Assert-PackageWorkflow $rejected 'Invalid preset version accepted.'
    $rejected = $false
    try { Resolve-VrcSetupPackagePreset -Config $config -Name Missing | Out-Null } catch { $rejected = $true }
    Assert-PackageWorkflow $rejected 'Missing preset silently used a different set.'

    $items = @([pscustomobject]@{ Id = 'one' }, [pscustomobject]@{ Id = 'two' })
    $picked = @(Get-VrcSetupConfirmedPackageSelection -Items $items -Selected @{0=$false;1=$false} -FocusedIndex 1)
    Assert-PackageWorkflow ($picked.Count -eq 1 -and $picked[0].Id -eq 'two') 'Enter did not add the highlighted package.'
    $picked = @(Get-VrcSetupConfirmedPackageSelection -Items $items -Selected @{0=$true;1=$true} -FocusedIndex 1)
    Assert-PackageWorkflow ($picked.Count -eq 2) 'Multi-selection was lost on confirm.'
    $versions = @('2.0.0-beta.1', '1.8.6', '1.8.5')
    Assert-PackageWorkflow ((@(Get-VrcSetupVersionChoices -Versions $versions) -join ',') -eq '1.8.6,1.8.5') 'Prereleases leaked into stable filter.'
    Assert-PackageWorkflow (@(Get-VrcSetupVersionChoices -Versions $versions -Filter 'beta' -IncludePrerelease $true).Count -eq 1) 'Version search did not find a prerelease.'
    Assert-PackageWorkflow (@(Get-VrcSetupVersionChoices -Versions $versions -Filter '[*').Count -eq 0) 'Version filter treated text as wildcard syntax.'

    $project = Join-Path $qaRoot 'Project [test] & café'
    foreach ($folder in @('Assets','Packages','ProjectSettings')) { [void][IO.Directory]::CreateDirectory((Join-Path $project $folder)) }
    [IO.File]::WriteAllText((Join-Path $project 'Packages\manifest.json'), '{"dependencies":{}}')
    [IO.File]::WriteAllText((Join-Path $project 'Packages\vpm-manifest.json'), '{"dependencies":{},"locked":{}}')
    [IO.File]::WriteAllText((Join-Path $project 'ProjectSettings\ProjectVersion.txt'), "m_EditorVersion: 2022.3.22f1`n")
    [IO.File]::WriteAllText((Join-Path $project 'ProjectSettings\ProjectSettings.asset'), "PlayerSettings:`n  productName: Package QA`n")
    $output = @(Invoke-VrcSetupCli -Command presets -Arguments @('list') -ScriptDir $runtime -ConfigPath $configPath -Json)
    Assert-PackageWorkflow ($output[-1] -eq 0 -and ($output[0] | ConvertFrom-Json)[0].Name -eq 'Creator PC') 'CLI preset list failed.'
    $savedBefore = [IO.File]::ReadAllText($configPath)
    $output = @(Invoke-VrcSetupCli -Command presets -Arguments @('remove','Creator PC') -ScriptDir $runtime -ConfigPath $configPath -DryRun)
    Assert-PackageWorkflow ($output[-1] -eq 0 -and [IO.File]::ReadAllText($configPath) -eq $savedBefore) 'Dry-run deleted a preset.'

    # Exercise CLI routing at the installer boundary without launching Unity.
    $oldInstaller = ${function:Start-Installer}
    $oldValidator = ${function:Test-VrcSetupCliPackageSpec}
    $oldSdkResolver = ${function:Resolve-VrcSetupSdkPackageSet}
    try {
        function Start-Installer { param($projectPath, $PackagesOverride, $NewProjectName, [switch]$Test); $script:CapturedPackageSet = $PackagesOverride; return 0 }
        function Test-VrcSetupCliPackageSpec { param($PackageSpec, $ScriptDir); return $true }
        function Resolve-VrcSetupSdkPackageSet { param($Packages, $ScriptDir); return $Packages }
        $sourcePackage = Join-Path $qaRoot 'Avatar.unitypackage'
        [IO.File]::WriteAllText($sourcePackage,'test installer boundary')
        $config.VpmPackages = [pscustomobject]@{ 'com.example.default' = '1.0.0' }
        Save-Config -Config $config -ConfigPath $configPath
        $output = @(Invoke-VrcSetupCli -Command create -Arguments @($sourcePackage) -Preset 'Creator PC' -ScriptDir $runtime -ConfigPath $configPath -DryRun)
        Assert-PackageWorkflow ($output[-1] -eq 0 -and $script:CapturedPackageSet.gogoloco -eq 'latest' -and $script:CapturedPackageSet.PSObject.Properties.Name -notcontains 'com.example.default') 'Creation did not use the chosen preset independently of the default set.'
        [IO.File]::WriteAllText((Join-Path $project 'Packages\vpm-manifest.json'), '{"dependencies":{"com.example.keep":{"version":"2.0.0"}},"locked":{}}')
        $output = @(Invoke-VrcSetupCli -Command setup -Arguments @($project) -Preset 'Creator PC' -ScriptDir $runtime -ConfigPath $configPath -DryRun)
        Assert-PackageWorkflow ($output[-1] -eq 0 -and $script:CapturedPackageSet.'com.example.keep' -eq '2.0.0' -and $script:CapturedPackageSet.gogoloco -eq 'latest') 'Project setup with a preset removed unrelated packages.'
        $output = @(Invoke-VrcSetupCli -Command presets -Arguments @('apply','Creator PC',$project) -ScriptDir $runtime -ConfigPath $configPath -DryRun)
        Assert-PackageWorkflow ($output[-1] -eq 0 -and $script:CapturedPackageSet.'com.example.keep' -eq '2.0.0') 'Preset update removed unrelated packages.'
        $output = @(Invoke-VrcSetupCli -Command create -Arguments @($sourcePackage) -Preset Missing -ScriptDir $runtime -ConfigPath $configPath -DryRun)
        Assert-PackageWorkflow ($output[-1] -eq 1) 'Unknown preset was ignored by creation.'
    } finally {
        ${function:Start-Installer} = $oldInstaller
        ${function:Test-VrcSetupCliPackageSpec} = $oldValidator
        ${function:Resolve-VrcSetupSdkPackageSet} = $oldSdkResolver
        [IO.File]::WriteAllText((Join-Path $project 'Packages\vpm-manifest.json'), '{"dependencies":{},"locked":{}}')
    }

    if ($PSVersionTable.PSEdition -eq 'Core') {
        Add-Type -LiteralPath (Join-Path $runtime 'lib\spectre\Spectre.Console.Ansi.dll')
        Add-Type -LiteralPath (Join-Path $runtime 'lib\spectre\Spectre.Console.dll')
        $rendered = New-VrcSetupVersionPickerRenderable -State ([pscustomobject]@{ Filter='1.8'; Prerelease=$false; Index=1 }) -Versions $versions -CurrentVersion '1.8.5'
        Assert-PackageWorkflow ($rendered -is [Spectre.Console.Rows]) 'Version picker failed to render.'
        $rendered = New-VrcSetupPackagePickerRenderable -Items @([pscustomobject]@{ Id='gogoloco'; DisplayName='GoGoLoco'; LatestVersion='1.8.6' }) -SelectedIndex 0 -Selected @{0=$false}
        Assert-PackageWorkflow ($rendered -is [Spectre.Console.Rows]) 'Package picker failed to render.'
    }
    if ($Live) {
        $scriptDir = $runtime
        $status = Install-PackagesInProject -ProjectPath $project -Packages ([pscustomobject]@{ gogoloco='1.8.3' })
        Assert-PackageWorkflow ($status -eq 0) 'Live package addition failed.'
        $installed = Get-VpmProjectPackageSet -ProjectPath $project
        Assert-PackageWorkflow ($installed.gogoloco -eq '1.8.3') 'Added package version was not written.'
        $status = Install-PackagesInProject -ProjectPath $project -Packages ([pscustomobject]@{ gogoloco='1.8.6' })
        Assert-PackageWorkflow ($status -eq 0) 'Live package update failed.'
        $installed = Get-VpmProjectPackageSet -ProjectPath $project
        Assert-PackageWorkflow ($installed.gogoloco -eq '1.8.6') 'Updated package version was not written.'
        Assert-PackageWorkflow (Test-Path -LiteralPath (Join-Path $project 'Packages\gogoloco\package.json')) 'Package files missing after successful add.'
        $vccBefore = @(Get-Process -Name CreatorCompanion -ErrorAction SilentlyContinue | Select-Object Id, StartTime)
        $registered = Register-VrcSetupCreatorCompanionProject -ProjectPath $project
        Assert-PackageWorkflow $registered 'Creator Companion registration failed.'
        Assert-PackageWorkflow (Register-VrcSetupCreatorCompanionProject -ProjectPath $project) 'Repeated registration failed.'
        $vccSettings = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'VRChatCreatorCompanion\settings.json') -Raw | ConvertFrom-Json
        Assert-PackageWorkflow (@($vccSettings.userProjects) -contains $project) 'Registered project missing from VCC project list.'
        Assert-PackageWorkflow (@($vccSettings.userProjects | Where-Object { $_ -eq $project }).Count -eq 1) 'Repeated registration duplicated a project.'
        foreach ($process in $vccBefore) {
            $stillRunning = Get-Process -Id $process.Id -ErrorAction Stop
            Assert-PackageWorkflow ($stillRunning.StartTime -eq $process.StartTime) 'Creator Companion was restarted.'
        }
        Write-Host 'PASS: live add 1.8.3, update 1.8.6, package files, VCC registration, VCC stayed open.'
    }
    Write-Host 'PASS: package preset and picker workflow tests.'
} finally {
    if ($registered) {
        $removed = Invoke-VpmCapture -Arguments @('remove','project',$project)
        if ($removed.ExitCode -ne 0) { throw "QA registration cleanup failed; project preserved at ${project}" }
    }
    $resolvedQa = [IO.Path]::GetFullPath($qaRoot)
    if ($resolvedQa.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\') -and [IO.Directory]::Exists($resolvedQa)) { [IO.Directory]::Delete($resolvedQa,$true) }
}
