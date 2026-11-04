namespace VrcSetup.Infrastructure;

public sealed partial class VrcSetupApplication
{
    public async Task MaintainLocalStorageAsync(CancellationToken token = default)
    {
        LogRetention.Prune(Paths);
        // Source/portable verification must never clean the real installation.
        var root = Paths.PayloadRoot;
        if (!File.Exists(Path.Combine(root, ".vrcsetup-install.json")) ||
            !Path.GetFullPath(AppContext.BaseDirectory).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var parent = Path.GetDirectoryName(root)!; var name = Path.GetFileName(root);
            var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(root, ".vrcsetup-install.json"), token);
            var snapshots = Directory.EnumerateDirectories(parent).Where(x =>
                System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(x), "^" + System.Text.RegularExpressions.Regex.Escape(name) + @"\.(previous|failed)-[a-fA-F0-9]{32}$"));
            if (record is not null && snapshots.Any(x => record.PreviousInstall is null || !Path.GetFullPath(x).Equals(Path.GetFullPath(record.PreviousInstall), StringComparison.OrdinalIgnoreCase)))
                await new InstallationManager(Paths).CleanupPreviousInstallsAsync(root, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { Diagnostics.Write("Installer cleanup", "Deferred cleanup will retry on the next launch.", error); }
    }
}
