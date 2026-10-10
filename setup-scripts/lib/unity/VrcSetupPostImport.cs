using System;
using UnityEditor;
using UnityEngine;

// Loaded only into the newly created project's Editor folder.
[InitializeOnLoad]
public static class VrcSetupPostImport
{
    private const string Active = "VrcSetup.Finalize.Active";
    private const string Started = "VrcSetup.Finalize.Started";
    private static double stableSince = -1;

    static VrcSetupPostImport()
    {
        if (Application.isBatchMode && SessionState.GetBool(Active, false)) Attach();
    }

    private static void Attach()
    {
        stableSince = -1;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

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

    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false)) { EditorApplication.update -= Tick; return; }
        long started;
        if (!long.TryParse(SessionState.GetString(Started, ""), out started) ||
            DateTime.UtcNow.Ticks - started > TimeSpan.FromMinutes(10).Ticks)
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
        EditorApplication.update -= Tick;
        EditorApplication.Exit(status);
    }
}
