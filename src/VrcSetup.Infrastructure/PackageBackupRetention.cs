using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

internal static class PackageBackupRetention
{
    public static async Task ConfirmAsync(BackupRecord backup)
    {
        if (backup.PackageArchive is null) return;
        try { await File.WriteAllTextAsync(Path.Combine(backup.Directory, "completed"), ""); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        var parent = Path.GetDirectoryName(backup.Directory)!;
        // Keep the last successful pre-change snapshot and every failed snapshot.
        // Older manifest-only backups are outside this new archive lifecycle.
        string[] folders;
        try { folders = Directory.GetDirectories(parent, "operation-*"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        foreach (var folder in folders)
        {
            if (folder.Equals(backup.Directory, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(folder, "completed"))) continue;
            try
            {
                ProjectRules.EnsureContained(parent, folder);
                var old = await AtomicFile.ReadJsonAsync<BackupRecord>(Path.Combine(folder, "backup.json"));
                if (old?.PackageArchive is null) continue;
                var files = new[] { old.PackageArchive, old.AssetArchive }.OfType<string>().ToArray();
                foreach (var file in files)
                {
                    ProjectRules.EnsureContained(folder, file);
                    if (Path.GetDirectoryName(Path.GetFullPath(file)) != Path.GetFullPath(folder)
                        || Path.GetFileName(file) is not ("packages.zip" or "assets.zip")) throw new IOException("Unexpected recovery archive path.");
                }
                await File.WriteAllTextAsync(Path.Combine(folder, "superseded"), "Use the latest successful recovery copy.");
                foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }
}
