// VRC Project Setup package refresh coordination. No project content is edited here.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class VrcSetupPackageSession
{
    [Serializable] private sealed class Request { public string id; public long expiresUtcTicks; }
    [Serializable] private sealed class Package { public string name; public string version; }
    private static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".vrcsetup"));
    private static readonly string Lease = Path.Combine(Root, "editor-package-lease.json");
    private static readonly string Ack = Path.Combine(Root, "editor-package-ack.txt");
    private static string active;
    private static long lastExpiry;
    private static double nextPoll;
    private static double idleSince;
    private static bool paused;
    private static bool resolving;
    private static double resolveDeadline;
    private static int resolveGeneration;

    static VrcSetupPackageSession()
    {
        EditorApplication.update += Poll;
        EditorApplication.quitting += Finish;
        UnityEditor.PackageManager.Events.registeredPackages += _ =>
        {
            var generation = resolveGeneration;
            if (resolving) EditorApplication.delayCall += () =>
            {
                if (resolving && resolveGeneration == generation && RegistryIsCurrent()) Finish();
            };
        };
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .1;
        if (resolving)
        {
            if (RegistryIsCurrent() || EditorApplication.timeSinceStartup >= resolveDeadline) Finish();
            return;
        }
        Request request = null;
        try { if (File.Exists(Lease)) request = JsonUtility.FromJson<Request>(File.ReadAllText(Lease)); }
        catch (IOException) { if (active != null && DateTime.UtcNow.Ticks >= lastExpiry) Release(); return; }
        catch (UnauthorizedAccessException) { if (active != null && DateTime.UtcNow.Ticks >= lastExpiry) Release(); return; }
        catch (ArgumentException) { }
        if (request == null || string.IsNullOrEmpty(request.id) || request.expiresUtcTicks <= DateTime.UtcNow.Ticks)
        {
            if (active != null) Release();
            return;
        }
        lastExpiry = request.expiresUtcTicks;
        if (active == request.id) return;
        if (active != null) { Release(); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { idleSince = 0; return; }
        if (idleSince == 0) { idleSince = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup < idleSince + .5) return;
        AssetDatabase.DisallowAutoRefresh();
        EditorApplication.LockReloadAssemblies();
        paused = true;
        idleSince = 0;
        active = request.id;
        try { File.WriteAllText(Ack, active); }
        catch (IOException) { Release(); }
    }

    private static void Release()
    {
        if (active == null) return;
        active = null;
        // Keep refresh suspended until UPM has consumed removed/replaced
        // package roots; otherwise compilation can use stale source paths.
        try
        {
            resolving = true;
            resolveGeneration++;
            resolveDeadline = EditorApplication.timeSinceStartup + 20;
            UnityEditor.PackageManager.Client.Resolve();
        }
        catch { Finish(); }
    }

    private static bool RegistryIsCurrent()
    {
        try
        {
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            foreach (var registered in packages)
                if (registered.source == UnityEditor.PackageManager.PackageSource.Embedded &&
                    !File.Exists(Path.Combine(registered.resolvedPath, "package.json"))) return false;
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages"));
            foreach (var directory in Directory.GetDirectories(folder))
            {
                var file = Path.Combine(directory, "package.json");
                if (!File.Exists(file)) continue;
                var physical = JsonUtility.FromJson<Package>(File.ReadAllText(file));
                if (physical == null || string.IsNullOrEmpty(physical.name)) continue;
                if (!Array.Exists(packages, p => p.name == physical.name && p.version == physical.version)) return false;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static void Finish()
    {
        resolving = false;
        if (!paused) return;
        active = null;
        paused = false;
        try { if (File.Exists(Ack)) File.Delete(Ack); } catch (IOException) { }
        try
        {
            AssetDatabase.AllowAutoRefresh();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        finally
        {
            EditorApplication.UnlockReloadAssemblies();
        }
    }
}
#endif
