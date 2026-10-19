using System.IO.Compression;
using System.Text;

namespace VrcSetup.Infrastructure;

public static class SupportLogs
{
    public static string SuggestedName => "VRC-Project-Setup-Logs-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".zip";
    public static string? LatestError(AppPaths paths) => Directory.Exists(paths.Logs)
        ? Directory.EnumerateFiles(paths.Logs, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
    public static async Task ExportAsync(AppPaths paths, string destination, CancellationToken token = default)
    {
        if (!destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a ZIP file.");
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            var files = Directory.Exists(paths.Logs) ? Directory.EnumerateFiles(paths.Logs)
                .Where(x => Path.GetExtension(x) is ".jsonl" or ".log" or ".json")
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(12).ToList() : [];
            files.AddRange(new[] { "components/last-install.log", "components/last-engine.log", "components/last-error.log", "last-result.json" }
                .Select(x => Path.Combine(paths.Home, "updates", x)).Where(File.Exists));
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                await using (var metadata = zip.CreateEntry("VRC-Project-Setup.txt").Open())
                    await metadata.WriteAsync(Encoding.UTF8.GetBytes("VRC Project Setup " + ApplicationUpdates.CurrentVersion + "\nWindows: " + Environment.OSVersion.Version + "\nUTC: " + DateTime.UtcNow.ToString("O")), token);
                long remaining = 16 * 1024 * 1024;
                foreach (var path in files.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    if (remaining <= 0 || path.Equals(destination, StringComparison.OrdinalIgnoreCase)) break;
                    try
                    {
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
                        var length = Math.Min(input.Length, Math.Min(remaining, 4 * 1024 * 1024));
                        input.Position = input.Length - length;
                        var name = path.StartsWith(paths.Logs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "App/" : "Update/";
                        await using var entry = zip.CreateEntry(name + Path.GetFileName(path), CompressionLevel.Fastest).Open();
                        var buffer = new byte[65536]; long left = length;
                        while (left > 0)
                        {
                            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), token);
                            if (read == 0) break;
                            await entry.WriteAsync(buffer.AsMemory(0, read), token); left -= read; remaining -= read;
                        }
                    }
                    catch (IOException) { } // A rotated log must not hide the remaining evidence.
                }
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
