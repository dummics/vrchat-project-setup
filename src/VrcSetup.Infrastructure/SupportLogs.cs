using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VrcSetup.Infrastructure;

public static class SupportLogs
{
    public static string SuggestedName => "VRC-Project-Setup-Diagnostics-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".zip";
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
            var logs = Directory.Exists(paths.Logs) ? Directory.EnumerateFiles(paths.Logs)
                .Where(x => Path.GetExtension(x).ToLowerInvariant() is ".jsonl" or ".log" or ".json")
                .OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : [];
            // Reserve space for errors from every area before verbose Unity output.
            var errors = logs.Where(x => Path.GetExtension(x).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)).Take(4);
            var updates = new[] { "components/last-error.log", "last-result.json", "components/last-install.log", "components/last-engine.log" }
                .Select(x => Path.Combine(paths.Home, "updates", x)).Where(File.Exists);
            var reports = logs.Where(x => Path.GetExtension(x).Equals(".json", StringComparison.OrdinalIgnoreCase)).Take(4);
            var process = logs.Where(x => Path.GetExtension(x).Equals(".log", StringComparison.OrdinalIgnoreCase)).Take(8);
            var files = errors.Concat(updates).Concat(reports).Concat(process).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var summary = new StringBuilder("VRC Project Setup " + ApplicationUpdates.CurrentVersion +
                "\nWindows: " + Environment.OSVersion.Version + "\nUTC: " + DateTime.UtcNow.ToString("O") +
                "\n\nShare this ZIP with contact@dummics.com or Discord: dummics.\nLogs may contain local file paths and project names.\n\nRecent application errors:\n");
            var manifest = new StringBuilder("Recent diagnostics only; settings and project assets are excluded.\n");
            var foundErrors = false;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                long remaining = 16 * 1024 * 1024;
                foreach (var path in files)
                {
                    token.ThrowIfCancellationRequested();
                    if (path.Equals(destination, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = (path.StartsWith(paths.Logs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "App/" : "Update/") + Path.GetFileName(path);
                    if (remaining <= 0) { manifest.AppendLine(name + ": omitted (size limit)"); continue; }
                    try
                    {
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
                        var originalLength = input.Length;
                        var limit = Path.GetExtension(path).Equals(".log", StringComparison.OrdinalIgnoreCase) ? 2 * 1024 * 1024 : 512 * 1024;
                        var length = (int)Math.Min(originalLength, Math.Min(remaining, limit));
                        input.Position = originalLength - length;
                        var data = new byte[length];
                        var read = await input.ReadAtLeastAsync(data, length, false, token);
                        await using (var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open())
                            await entry.WriteAsync(data.AsMemory(0, read), token);
                        remaining -= read;
                        manifest.AppendLine(name + ": " + read + " bytes" + (originalLength > read ? " (tail; older content omitted)" : ""));
                        if (Path.GetExtension(path).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
                        {
                            var count = 0;
                            foreach (var line in Encoding.UTF8.GetString(data, 0, read).Split('\n').Reverse())
                            {
                                if (count == 8) break;
                                try
                                {
                                    using var json = JsonDocument.Parse(line);
                                    var item = json.RootElement;
                                    if (!item.TryGetProperty("Error", out var error) || error.ValueKind != JsonValueKind.String) continue;
                                    var time = item.TryGetProperty("Timestamp", out var timestamp) ? timestamp.ToString() : "";
                                    var area = item.TryGetProperty("Area", out var source) ? source.ToString() : "Application";
                                    var message = item.TryGetProperty("Message", out var text) ? text.ToString() : "Error";
                                    summary.AppendLine(time + " | " + area + " | " + message[..Math.Min(message.Length, 1500)]);
                                    count++; foundErrors = true;
                                }
                                catch (JsonException) { } // A live or truncated record may be incomplete.
                            }
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { manifest.AppendLine(name + ": unavailable (" + error.GetType().Name + ")"); }
                }
                if (!foundErrors) summary.AppendLine("No recorded application exceptions. Check App and Update for operation details.");
                manifest.AppendLine("Older files omitted: " + logs.Except(files, StringComparer.OrdinalIgnoreCase).Count());
                await WriteTextAsync(zip, "VRC-Project-Setup.txt", summary.ToString(), token);
                await WriteTextAsync(zip, "Archive-contents.txt", manifest.ToString(), token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task WriteTextAsync(ZipArchive zip, string name, string text, CancellationToken token)
    {
        await using var entry = zip.CreateEntry(name).Open();
        await entry.WriteAsync(Encoding.UTF8.GetBytes(text), token);
    }
}
