param([switch]$LiveUnity)
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '..\setup-scripts'
. (Join-Path $runtime 'lib\import-platform.ps1')
. (Join-Path $runtime 'lib\spectre.ps1')
function Assert-Platform { param($Condition,$Message); if (-not $Condition) { throw $Message } }
Assert-Platform ((Get-VrcSetupSuggestedBuildTarget 'Avatar_Quest.unitypackage') -eq 'Android') 'Quest suggestion failed.'
Assert-Platform ((Get-VrcSetupSuggestedBuildTarget 'Avatar PC.unitypackage') -eq 'StandaloneWindows64') 'PC default failed.'
Assert-Platform ((Get-VrcSetupSuggestedBuildTarget 'Conquest.unitypackage') -eq 'StandaloneWindows64') 'Substring falsely matched Quest.'
$state = [pscustomobject]@{ Value = 'Avatar by Creator'; Cursor = 17; Done = $false; Cancelled = $false }
for ($i=0; $i -lt 11; $i++) { Update-VrcSetupTextInputKey $state ([ConsoleKeyInfo]::new([char]8,[ConsoleKey]::Backspace,$false,$false,$false)) }
Assert-Platform ($state.Value -eq 'Avatar') 'Editing a prefilled suffix failed.'
Update-VrcSetupTextInputKey $state ([ConsoleKeyInfo]::new([char]0,[ConsoleKey]::Home,$false,$false,$false))
Update-VrcSetupTextInputKey $state ([ConsoleKeyInfo]::new([char]'X',[ConsoleKey]::X,$false,$false,$false))
Assert-Platform ($state.Value -eq 'XAvatar') 'Insertion at cursor failed.'
Update-VrcSetupTextInputKey $state ([ConsoleKeyInfo]::new([char]0,[ConsoleKey]::Delete,$false,$false,$false))
Assert-Platform ($state.Value -eq 'Xvatar') 'Delete at cursor failed.'
Write-Host 'PASS: target suggestions and prefilled text editing.'
if (-not $LiveUnity) { return }
$config = Get-Content (Join-Path $env:LOCALAPPDATA 'Programs\VrcSetup\setup-scripts\config\vrcsetup.json') -Raw | ConvertFrom-Json
$editor = $config.UnityEditorPath
$qa = Join-Path $env:TEMP ('vrcsetup-platform-qa-'+[guid]::NewGuid().ToString('N'))
$artifacts = Join-Path $PSScriptRoot '..\.work\artifacts'
[void][IO.Directory]::CreateDirectory($artifacts)
$process = $null
try {
    foreach ($dir in @('Assets\Editor','Packages','ProjectSettings','.vrcsetup')) { [void][IO.Directory]::CreateDirectory((Join-Path $qa $dir)) }
    [IO.File]::WriteAllText((Join-Path $qa 'Packages\manifest.json'),'{"dependencies":{}}')
    [IO.File]::WriteAllText((Join-Path $qa 'ProjectSettings\ProjectVersion.txt'),"m_EditorVersion: 2022.3.22f1`n")
    Copy-Item (Join-Path $runtime 'lib\unity\VrcSetupPostImport.cs') (Join-Path $qa 'Assets\Editor\VrcSetupPostImport.cs')
    # Compile the real installed SDK session-state class in the disposable fixture, without credentials.
    $sdk = @(Get-ChildItem -LiteralPath $config.UnityProjectsRoot -Directory | ForEach-Object {
        $candidate = Join-Path $_.FullName 'Packages\com.vrchat.avatars\Editor\VRCSDK\SDK3A\AvatarBuilderSessionState.cs'
        if (Test-Path -LiteralPath $candidate) { $candidate }
    } | Select-Object -First 1)
    Assert-Platform ($sdk.Count -eq 1) 'SDK session state source not found.'
    Copy-Item -LiteralPath $sdk[0] -Destination (Join-Path $qa 'Assets\Editor\AvatarBuilderSessionState.cs')
    [IO.File]::WriteAllText((Join-Path $qa 'Assets\Editor\PlatformQa.cs'), @'
using System; using System.IO; using UnityEditor; using UnityEngine;
public static class PlatformQa {
 public static void Export() {
  Directory.CreateDirectory("Assets/Payload");
  File.WriteAllText("Assets/Payload/Probe.cs", "public class ImportProbe { public const int Stage = 1; }");
  AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
  AssetDatabase.ExportPackage("Assets/Payload", ".vrcsetup/main.unitypackage", ExportPackageOptions.Recurse);
  File.WriteAllText("Assets/Payload/Probe.cs", "public class ImportProbe { public const int Stage = 2; }");
  AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
  AssetDatabase.ExportPackage("Assets/Payload", ".vrcsetup/extra.unitypackage", ExportPackageOptions.Recurse);
  AssetDatabase.DeleteAsset("Assets/Payload");
  AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
 }
 public static void Check() {
  if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new Exception("Android target not persisted");
  Type probe = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((probe = assembly.GetType("ImportProbe")) != null) break;
  if (probe == null || (int)probe.GetField("Stage").GetRawConstantValue() != 2) throw new Exception("Import order/domain reload failed");
  VrcSetupPostImport.ApplyPublishingPlatforms();
  var selected = VRC.SDK3A.Editor.AvatarBuilderSessionState.AvatarPlatforms;
  if (selected.Count != 2 || !selected.Contains(BuildTarget.Android) || !selected.Contains(BuildTarget.StandaloneWindows64)) throw new Exception("SDK platforms not selected");
  VRC.SDK3A.Editor.AvatarBuilderSessionState.AvatarPlatforms = new System.Collections.Generic.List<BuildTarget>{BuildTarget.Android};
  VrcSetupPostImport.ApplyPublishingPlatforms();
  if (VRC.SDK3A.Editor.AvatarBuilderSessionState.AvatarPlatforms.Count != 1) throw new Exception("User SDK choice overwritten");
  Debug.Log("[platform-qa] Android, import order, script reload, SDK multiselect and one-shot selection PASS");
  EditorApplication.Exit(0);
 }
}
'@)
    function Invoke-QaUnity {
        param([string]$Method,[string]$LogName,[string]$Additional='',[string]$Target='StandaloneWindows64')
        $log = Join-Path $artifacts $LogName
        $launcher = Join-Path $env:USERPROFILE '.codex\bin\Run-Hidden.vbs'
        $args = '"'+$launcher+'" "'+$editor+'" -batchmode -nographics -projectPath "'+$qa+'" -buildTarget '+$Target+' -executeMethod '+$Method+' -logFile "'+$log+'" '+$Additional
        $script:qaProcess = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\wscript.exe') -ArgumentList $args -WindowStyle Hidden -PassThru
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while (-not $script:qaProcess.WaitForExit(1000)) { if ($watch.Elapsed.TotalSeconds -gt 180) { throw "QA exceeded 180 seconds: $Method" } }
        Assert-Platform ($script:qaProcess.ExitCode -eq 0) "Unity QA failed: $log"
        Write-Host ("PASS: {0}, {1:N1}s" -f $Method,$watch.Elapsed.TotalSeconds)
    }
    Invoke-QaUnity 'PlatformQa.Export' 'platform-export.log'
    $plan = Join-Path $qa '.vrcsetup\import-plan.json'
    [IO.File]::WriteAllText($plan,(@{ packages = @((Join-Path $qa '.vrcsetup\main.unitypackage'),(Join-Path $qa '.vrcsetup\extra.unitypackage')) } | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $qa '.vrcsetup\platforms.json'),'{"targets":["StandaloneWindows64","Android"],"pending":true}')
    Invoke-QaUnity 'VrcSetupPostImport.RunImport' 'platform-import.log' ('-vrcsetupImportPlan "'+$plan+'"') 'Android'
    Invoke-QaUnity 'PlatformQa.Check' 'platform-reopen.log' '' 'Android'
    Assert-Platform ((Get-Content (Join-Path $artifacts 'platform-reopen.log') -Raw) -match 'one-shot selection PASS') 'Final assertions missing.'
    Write-Host 'PASS: real Unity and real SDK class in disposable project.'
} finally {
    . (Join-Path $runtime 'commands\installer.ps1')
    foreach ($entry in @(Get-UnityProcessesUsingProjectPath $qa)) { Stop-Process -Id $entry.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($script:qaProcess -and -not $script:qaProcess.HasExited) { $script:qaProcess.Kill() }
    $resolved = [IO.Path]::GetFullPath($qa)
    if ($resolved.StartsWith(([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
