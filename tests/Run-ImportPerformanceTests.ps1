[CmdletBinding()]
param([switch]$LiveUnity)
$ErrorActionPreference='Stop'
$runtime=Join-Path $PSScriptRoot '..\setup-scripts'
. (Join-Path $runtime 'commands\installer.ps1')
function Assert-Import { param($Condition,$Message); if (-not $Condition) { throw $Message } }
$qaRoot=Join-Path $env:TEMP ('vrcsetup-import-perf-'+[guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($qaRoot)
    $log=Join-Path $qaRoot 'large.log'
    [IO.File]::WriteAllText($log,('Unity asset import details '+('x'*120)+"`n")*20000)
    $cache=@{}
    Assert-Import ((Get-VrcSetupChangedLogTail $log $cache) -like 'Unity asset*') 'Initial log tail was lost.'
    Assert-Import ($null -eq (Get-VrcSetupChangedLogTail $log $cache)) 'Unchanged log was read again.'
    [IO.File]::AppendAllText($log,"Next import`n")
    Assert-Import ((Get-VrcSetupChangedLogTail $log $cache) -eq 'Next import') 'Log append was missed.'
    [IO.File]::WriteAllText($log,"Truncated`n")
    Assert-Import ((Get-VrcSetupChangedLogTail $log $cache) -eq 'Truncated') 'Log truncation was missed.'
    Remove-Item -LiteralPath $log
    Assert-Import ($null -eq (Get-VrcSetupChangedLogTail $log $cache)) 'Deleted log failed.'
    [IO.File]::WriteAllText($log,"Recreated`n")
    Assert-Import ((Get-VrcSetupChangedLogTail $log $cache) -eq 'Recreated') 'Recreated log was missed.'
    [IO.File]::WriteAllText($log,('Unity asset import details '+('x'*120)+"`n")*20000)
    [void](Get-VrcSetupChangedLogTail $log $cache)
    $old=Measure-Command { for ($i=0;$i -lt 100;$i++) { Get-Content -LiteralPath $log -Tail 1 | Out-Null } }
    $fast=Measure-Command { for ($i=0;$i -lt 100;$i++) { Get-VrcSetupChangedLogTail $log $cache | Out-Null } }
    Write-Host ("Log polling, 100 unchanged reads: old {0:N1} ms; cached {1:N1} ms" -f $old.TotalMilliseconds,$fast.TotalMilliseconds)

    # Real installer routing, fake package tool: dependency adds must avoid a
    # second process, while latest and missing package files still need repair.
    $project=Join-Path $qaRoot 'Project [test]'
    [void][IO.Directory]::CreateDirectory((Join-Path $project 'Packages'))
    $script:qaDependencies=[ordered]@{}
    function Save-QaManifests {
        [IO.File]::WriteAllText((Join-Path $project 'Packages\vpm-manifest.json'),(@{dependencies=$script:qaDependencies;locked=$script:qaDependencies} | ConvertTo-Json -Depth 8))
        [IO.File]::WriteAllText((Join-Path $project 'Packages\manifest.json'),'{"dependencies":{}}')
    }
    Save-QaManifests
    function Resolve-VrcSetupSdkPackageSet { param($Packages,$ScriptDir); return $Packages }
    function Move-LegacyEasyLoginPackage { return [pscustomobject]@{Success=$true;Changed=$false} }
    function Write-VrcSetupLog { param($Message) }
    $script:calls=@()
    function Invoke-VpmCapture {
        param($Arguments)
        $script:calls += ($Arguments -join ' ')
        if ($Arguments[0] -eq 'add') {
            $id=($Arguments[2] -split '@')[0]
            $version=if ($Arguments[2] -match '@(.+)$') { $Matches[1] } else { '2.0.0' }
            $script:qaDependencies[$id]=@{version=$version}
            if ($id -eq 'com.example.parent') { $script:qaDependencies['com.example.dependency']=@{version='1.0.0'} }
            foreach ($name in $script:qaDependencies.Keys) {
                $folder=Join-Path (Join-Path $project 'Packages') $name
                [void][IO.Directory]::CreateDirectory($folder)
                [IO.File]::WriteAllText((Join-Path $folder 'package.json'),(@{name=$name;version=$script:qaDependencies[$name].version} | ConvertTo-Json))
            }
            Save-QaManifests
        }
        return [pscustomobject]@{ExitCode=0;OutputLines=@();Output=''}
    }
    $packages=[pscustomobject][ordered]@{'com.example.parent'='1.0.0';'com.example.dependency'='1.0.0'}
    $status=Install-PackagesInProject -ProjectPath $project -Packages $packages
    Assert-Import ($status -eq 0 -and @($script:calls | Where-Object { $_ -like 'add *' }).Count -eq 1) 'Dependency was re-added or final validation failed.'
    $script:calls=@()
    $status=Install-PackagesInProject -ProjectPath $project -Packages ([pscustomobject]@{'com.example.parent'='latest'})
    Assert-Import ($status -eq 0 -and @($script:calls | Where-Object { $_ -like 'add *' }).Count -eq 1) 'Latest update request was incorrectly skipped.'
    Remove-Item -LiteralPath (Join-Path $project 'Packages\com.example.dependency\package.json')
    $script:calls=@()
    $status=Install-PackagesInProject -ProjectPath $project -Packages ([pscustomobject]@{'com.example.dependency'='1.0.0'})
    Assert-Import ($status -eq 0 -and @($script:calls | Where-Object { $_ -like 'add *' }).Count -eq 1) 'Missing package files bypassed repair.'
    Write-Host 'PASS: log changes/truncation/recreation, exact dependency reuse, latest updates and missing-file repair.'
    function Get-CimInstance {
        return @([pscustomobject]@{Name='Unity.exe';ProcessId=42;CommandLine=('Unity.exe -projectPath "'+$project+'"')})
    }
    try {
        $found=@(Get-UnityProcessesUsingProjectPath -ProjectPath $project)
        Assert-Import ($found.Count -eq 1 -and $found[0].ProcessId -eq 42) 'Unity process detection leaked regex match data instead of process IDs.'
    } finally { Remove-Item Function:\Get-CimInstance }

    if ($LiveUnity) {
        $config=Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs\VrcSetup\setup-scripts\config\vrcsetup.json') -Raw | ConvertFrom-Json
        $editor=[string]$config.UnityEditorPath
        Assert-Import (Test-Path -LiteralPath $editor) 'Configured Unity editor not found.'
        $unityProject=Join-Path $qaRoot 'Unity QA'
        foreach ($folder in @('Assets\Editor','Packages','ProjectSettings')) { [void][IO.Directory]::CreateDirectory((Join-Path $unityProject $folder)) }
        [IO.File]::WriteAllText((Join-Path $unityProject 'Packages\manifest.json'),'{"dependencies":{}}')
        [IO.File]::WriteAllText((Join-Path $unityProject 'ProjectSettings\ProjectVersion.txt'),"m_EditorVersion: 2022.3.22f1`n")
        Copy-Item -LiteralPath (Join-Path $runtime 'lib\unity\VrcSetupPostImport.cs') -Destination (Join-Path $unityProject 'Assets\Editor\VrcSetupPostImport.cs')
        [IO.File]::WriteAllText((Join-Path $unityProject 'Assets\Editor\QaDriver.cs'), 'using System; using System.IO; using UnityEngine; using UnityEditor; public static class QaDriver { public static void Run() { File.WriteAllText(Path.Combine(Application.dataPath, "ReloadProbe.cs"), "public class ReloadProbe {}"); VrcSetupPostImport.Run(); } public static void Timeout() { VrcSetupPostImport.Run(); SessionState.SetString("VrcSetup.Finalize.Started", DateTime.UtcNow.AddMinutes(-11).Ticks.ToString()); } }')
        $unityLog=Join-Path $qaRoot 'unity-qa.log'
        $launcher=Join-Path $env:USERPROFILE '.codex\bin\Run-Hidden.vbs'
        $arguments='"'+$launcher+'" "'+$editor+'" -batchmode -nographics -projectPath "'+$unityProject+'" -executeMethod QaDriver.Run -logFile "'+$unityLog+'"'
        $watch=[Diagnostics.Stopwatch]::StartNew()
        $hostProcess=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\wscript.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try {
            while (-not $hostProcess.WaitForExit(1000)) {
                foreach ($process in @(Get-UnityProcessesUsingProjectPath -ProjectPath $unityProject)) {
                    Assert-Import ((Get-Process -Id $process.ProcessId).MainWindowHandle -eq 0) 'Unity QA opened a window.'
                }
                if ($watch.Elapsed.TotalSeconds -gt 180) { throw 'Unity QA exceeded 180 seconds.' }
            }
            $unityOutput=[IO.File]::ReadAllText($unityLog)
            Assert-Import ($hostProcess.ExitCode -eq 0 -and $unityOutput -match 'Post-import settle complete') 'Live Unity finalization failed.'
            Assert-Import (Test-Path -LiteralPath (Join-Path $unityProject 'Library\ScriptAssemblies\Assembly-CSharp.dll')) 'Domain reload probe did not compile.'
            Write-Host ("PASS: real Unity 2022.3 finalization and script reload, {0:N1}s, exit 0." -f $watch.Elapsed.TotalSeconds)
            $timeoutLog=Join-Path $qaRoot 'unity-timeout-qa.log'
            $timeoutArguments=$arguments.Replace('QaDriver.Run','QaDriver.Timeout').Replace($unityLog,$timeoutLog)
            $watch.Restart()
            $hostProcess=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\wscript.exe') -ArgumentList $timeoutArguments -WindowStyle Hidden -PassThru
            while (-not $hostProcess.WaitForExit(1000)) { if ($watch.Elapsed.TotalSeconds -gt 90) { throw 'Timeout-path QA exceeded 90 seconds.' } }
            Assert-Import ($hostProcess.ExitCode -ne 0 -and [IO.File]::ReadAllText($timeoutLog) -match 'project remains incomplete') 'Finalization timeout was reported as success.'
            Write-Host 'PASS: real Unity finalization timeout exits with failure.'
        } finally {
            if (Test-Path -LiteralPath $unityLog) {
                $artifactDir=Join-Path $PSScriptRoot '..\.work\artifacts'
                [void][IO.Directory]::CreateDirectory($artifactDir)
                Copy-Item -LiteralPath $unityLog -Destination (Join-Path $artifactDir 'unity-finalize-qa.log') -Force
                if ($timeoutLog -and (Test-Path -LiteralPath $timeoutLog)) { Copy-Item -LiteralPath $timeoutLog -Destination (Join-Path $artifactDir 'unity-timeout-qa.log') -Force }
            }
            # Only a test editor with this unique disposable project can be stopped.
            foreach ($process in @(Get-UnityProcessesUsingProjectPath -ProjectPath $unityProject)) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
            if (-not $hostProcess.HasExited) { $hostProcess.Kill() }
        }
    }
} finally {
    if ([IO.Path]::GetFullPath($qaRoot).StartsWith([IO.Path]::GetFullPath($env:TEMP),[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $qaRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
