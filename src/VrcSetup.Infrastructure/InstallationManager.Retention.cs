using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class InstallationManager
{
    // Keep exactly the direct rollback of the current successful installation.
    // Older snapshots are not an application archive; releases provide that history.
    public async Task<int> CleanupPreviousInstallsAsync(string? target = null, CancellationToken token = default)
    {
        target = ProjectRules.NormalizePath(target ?? DefaultRoot);
        using var lease = AcquireInstallation(target);
        if (HasLinkedAncestor(target)) throw new IOException("Refusing cleanup through a linked installation path.");
        var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(target, Marker), token)
            ?? throw new IOException("Installation marker missing.");
        if (!SamePath(record.Root, target)) throw new IOException("Installation marker does not match this folder.");
        await ValidatePayloadAsync(target, token);
        token.ThrowIfCancellationRequested();
        return await CleanupPreviousInstallsCoreAsync(target, record);
    }

    private async Task<int> CleanupPreviousInstallsCoreAsync(string target, InstallationRecord current)
    {
        var removed = 0;
        try
        {
            if (!SamePath(current.Root, target) || HasLinkedAncestor(target)) return 0;
            var parent = Path.GetDirectoryName(target)!;
            var name = Path.GetFileName(target);
            // Fail closed on a malformed rollback pointer rather than deleting the intended recovery.
            if (current.PreviousInstall is { } keep &&
                (!SamePath(Path.GetDirectoryName(Path.GetFullPath(keep))!, parent) || !IsSnapshot(Path.GetFileName(keep), name, "previous"))) return 0;
            foreach (var candidate in Directory.EnumerateDirectories(parent))
            {
                var candidateName = Path.GetFileName(candidate);
                if (!IsSnapshot(candidateName, name, "previous") && !IsSnapshot(candidateName, name, "failed")) continue;
                if (current.PreviousInstall is { } previous && SamePath(candidate, previous)) continue;
                try
                {
                    if (ContainsLinks(candidate) || File.Exists(Path.Combine(candidate, ".git")) || Directory.Exists(Path.Combine(candidate, ".git"))) continue;
                    var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(candidate, Marker));
                    if (record is null || !SamePath(record.Root, target) || !File.Exists(Path.Combine(candidate, ".vrcsetup-payload.json"))) continue;
                    if (File.Exists(Path.Combine(candidate, ".vrcsetup-portable")) || Directory.Exists(Path.Combine(candidate, "data"))) continue;
                    if (Within(candidate, paths.Home) || record.Home is { } home &&
                        (Within(candidate, home) || Within(target, home) && Directory.Exists(Path.Combine(candidate, Path.GetRelativePath(target, home))))) continue;
                    if (Within(candidate, AppContext.BaseDirectory)) continue;
                    foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
                    {
                        // Keep identity until bulk deletion succeeds, so a locked file can
                        // be retried on the next update instead of becoming an unknown orphan.
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                        if (SamePath(file, Path.Combine(candidate, Marker)) || SamePath(file, Path.Combine(candidate, ".vrcsetup-payload.json"))) continue;
                        File.Delete(file);
                    }
                    Directory.Delete(candidate, true); removed++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { LogRetentionFailure(candidate, error); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { LogRetentionFailure(target, error); }
        return removed;
    }

    private void LogRetentionFailure(string path, Exception error)
    {
        // Cleanup must never roll back a successfully installed application.
        try { new DiagnosticLog(paths).Write("Installer cleanup", "Could not remove obsolete installation: " + path, error); }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }
    private static bool IsSnapshot(string candidate, string name, string kind)
    {
        var prefix = name + "." + kind + "-";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(candidate[prefix.Length..], "N", out _);
    }
    private static bool SamePath(string a, string b) => Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string root, string path) => SamePath(root, path) || Path.GetFullPath(path)
        .StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool HasLinkedAncestor(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
    private static bool ContainsLinks(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = new DirectoryInfo(pending.Pop());
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                if (item is DirectoryInfo child) pending.Push(child.FullName);
            }
        }
        return false;
    }
}
