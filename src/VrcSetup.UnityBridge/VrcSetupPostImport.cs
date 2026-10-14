using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Loaded only into the newly created project's Editor folder.
[InitializeOnLoad]
public static class VrcSetupPostImport
{
    private const string Active = "VrcSetup.Finalize.Active";
    private const string Started = "VrcSetup.Finalize.Started";
    private const string Plan = "VrcSetup.Import.Plan";
    private const string Next = "VrcSetup.Import.Next";
    [Serializable] private class ImportPlan { public string[] packages; }
    [Serializable] private class Platforms { public string[] targets; public bool pending; }
    private static double stableSince = -1;

    static VrcSetupPostImport()
    {
        if (Application.isBatchMode && SessionState.GetBool(Active, false)) Attach();
        if (!Application.isBatchMode) EditorApplication.delayCall += ApplyPublishingPlatforms;
    }

    private static void Attach()
    {
        stableSince = -1;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        AssetDatabase.importPackageFailed -= ImportFailed;
        AssetDatabase.importPackageFailed += ImportFailed;
        AssetDatabase.importPackageCancelled -= ImportCancelled;
        AssetDatabase.importPackageCancelled += ImportCancelled;
    }

    private static void ImportFailed(string package, string error) { Debug.LogError("[vrc-setup] Import failed: " + package + ": " + error); Finish(1); }
    private static void ImportCancelled(string package) { Debug.LogError("[vrc-setup] Import cancelled: " + package); Finish(1); }

    // No -quit: the editor must keep pumping compilation/import callbacks.
    public static void Run()
    {
        SessionState.SetString(Started, DateTime.UtcNow.Ticks.ToString());
        SessionState.SetBool(Active, true);
        Attach();
        Debug.Log("[vrc-setup] Post-import settle started...");
        try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    // Import and settle in the same editor session; queue position survives domain reload.
    public static void RunImport()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-vrcsetupImportPlan");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing import plan");
            var json = File.ReadAllText(args[index + 1]);
            var plan = JsonUtility.FromJson<ImportPlan>(json);
            if (plan == null || plan.packages == null || plan.packages.Length == 0) throw new ArgumentException("Empty import plan");
            foreach (var path in plan.packages) if (!File.Exists(path)) throw new FileNotFoundException("UnityPackage missing", path);
            SessionState.SetString(Plan, json);
            SessionState.SetInt(Next, 0);
            SessionState.SetString(Started, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Active, true);
            Attach();
            Debug.Log("[vrc-setup] Import queue started.");
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    public static void ApplyPublishingPlatforms()
    {
        var path = Path.Combine(Application.dataPath, "../.vrcsetup/platforms.json");
        if (!File.Exists(path)) return;
        try
        {
            var settings = JsonUtility.FromJson<Platforms>(File.ReadAllText(path));
            if (settings == null || !settings.pending) return;
            // Use the SDK's public property when available; older SDKs retain Unity's active target.
            Type sdk = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((sdk = assembly.GetType("VRC.SDK3A.Editor.AvatarBuilderSessionState")) != null) break;
            var property = sdk == null ? null : sdk.GetProperty("AvatarPlatforms", BindingFlags.Public | BindingFlags.Static);
            if (property == null) { Debug.LogWarning("[vrc-setup] This SDK cannot preselect publishing platforms; select them in the SDK panel."); return; }
            var targets = new List<BuildTarget>();
            foreach (var target in settings.targets) targets.Add((BuildTarget)Enum.Parse(typeof(BuildTarget), target));
            property.SetValue(null, targets);
            settings.pending = false;
            File.WriteAllText(path, JsonUtility.ToJson(settings));
            Debug.Log("[vrc-setup] SDK publishing platforms selected.");
        }
        catch (Exception error) { Debug.LogWarning("[vrc-setup] Could not preselect SDK platforms: " + error.Message); }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false)) { EditorApplication.update -= Tick; return; }
        long started;
        var planJson = SessionState.GetString(Plan, "");
        if (!long.TryParse(SessionState.GetString(Started, ""), out started) ||
            DateTime.UtcNow.Ticks - started > TimeSpan.FromMinutes(string.IsNullOrEmpty(planJson) ? 10 : 30).Ticks)
        {
            Debug.LogError("[vrc-setup] Post-import settle TIMEOUT; project remains incomplete.");
            Finish(1);
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { stableSince = -1; return; }
        if (stableSince < 0) { stableSince = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup - stableSince < 2) return;
        try
        {
            if (EditorUtility.scriptCompilationFailed) { Debug.LogError("[vrc-setup] Script compilation failed; project remains incomplete."); Finish(1); return; }
            if (!string.IsNullOrEmpty(planJson))
            {
                var plan = JsonUtility.FromJson<ImportPlan>(planJson);
                var next = SessionState.GetInt(Next, 0);
                if (next < plan.packages.Length)
                {
                    SessionState.SetInt(Next, next + 1);
                    SessionState.SetString(Started, DateTime.UtcNow.Ticks.ToString());
                    stableSince = -1;
                    Debug.Log("[vrc-setup] Importing " + (next + 1) + "/" + plan.packages.Length + ": " + plan.packages[next]);
                    AssetDatabase.ImportPackage(plan.packages[next], false);
                    return;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[vrc-setup] Post-import settle complete, quitting.");
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    private static void Finish(int status)
    {
        SessionState.SetBool(Active, false);
        SessionState.EraseString(Started);
        SessionState.EraseString(Plan);
        SessionState.EraseInt(Next);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(status);
    }
}
