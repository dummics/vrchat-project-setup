using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcSetup.Core;

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
            var resultFile = Path.Combine(paths.Home, "updates", "last-result.json");
            var updateSucceeded = false;
            try { using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, token)); updateSucceeded = result.RootElement.TryGetProperty("Success", out var success) && success.ValueKind == JsonValueKind.True; }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
            var updates = new[] { "components/last-error.log", "last-result.json", "components/last-install.log", "components/last-engine.log" }
                .Select(x => Path.Combine(paths.Home, "updates", x)).Where(File.Exists);
            updates = updates.Where(x => !Path.GetFileName(x).Equals("last-error.log", StringComparison.OrdinalIgnoreCase) || !updateSucceeded || File.GetLastWriteTimeUtc(x) > File.GetLastWriteTimeUtc(resultFile));
            var recentReports = logs.Where(x => Path.GetFileName(x).StartsWith("operation-", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(x) == ".json").Take(12).ToArray();
            var parsed = recentReports.ToDictionary(x => x, ReadReport);
            var reports = recentReports.Where(x => parsed[x]?.Success == false).Take(2).Concat(recentReports.Take(2)).Distinct().Take(4).ToArray();
            var related = reports.SelectMany(x => parsed[x]?.Results ?? []).SelectMany(x => new[] { Path.GetFileName(x.LogFile ?? ""), Path.GetFileName(Regex.Match(x.Message, @"Log:\s*(.+\.log)").Groups[1].Value.Trim()) }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var process = logs.Where(x => Path.GetExtension(x) == ".log" && related.Contains(Path.GetFileName(x))).Concat(logs.Where(x => Path.GetExtension(x) == ".log").Take(2)).Distinct().Take(5);
            var files = errors.Concat(updates).Concat(reports).Concat(process).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var summary = new StringBuilder("VRC Project Setup " + ApplicationUpdates.CurrentVersion +
                "\nWindows: " + Environment.OSVersion.Version + "\nUTC: " + DateTime.UtcNow.ToString("O") +
                "\n\nShare this ZIP with contact@dummics.com or Discord: dummics.\nLogs may contain local file paths and project names.\n\nLatest operations:\n");
            foreach (var report in reports.Select(x => parsed[x]).OfType<OperationReport>())
                foreach (var outcome in report.Results)
                    summary.AppendLine(report.CompletedUtc.ToString("O") + " | " + outcome.Project + " | " + outcome.Status + " | " + outcome.Message[..Math.Min(outcome.Message.Length, 1200)]);
            summary.AppendLine("\nApplication errors (timestamps distinguish earlier incidents):");
            var manifest = new StringBuilder("Recent diagnostics only; settings and project assets are excluded.\n");
            var foundErrors = false;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                long remaining = 4 * 1024 * 1024;
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
                        var limit = 512 * 1024;
                        var length = (int)Math.Min(originalLength, Math.Min(remaining, limit));
                        input.Position = originalLength - length;
                        var data = new byte[length];
                        var read = await input.ReadAtLeastAsync(data, length, false, token);
                        // A truncated JSON document is useless. Export a valid compact
                        // report rather than the tail of a large serialized plan.
                        if (Path.GetExtension(path) == ".json" && originalLength > length)
                        {
                            var report = parsed.GetValueOrDefault(path);
                            data = JsonSerializer.SerializeToUtf8Bytes(new { Notice = "Large plan omitted; original report kept locally.", report?.Id, report?.CompletedUtc, Results = report?.Results.Take(24).Select(x => new { x.Project, x.Status, Message = x.Message[..Math.Min(x.Message.Length, 1200)] }) });
                            read = data.Length;
                        }
                        if (read > remaining) { manifest.AppendLine(name + ": omitted (size limit)"); continue; }
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
    private static OperationReport? ReadReport(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) return null;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<OperationReport>(input, JsonDefaults.Options);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
    private static async Task WriteTextAsync(ZipArchive zip, string name, string text, CancellationToken token)
    {
        await using var entry = zip.CreateEntry(name).Open();
        await entry.WriteAsync(Encoding.UTF8.GetBytes(text), token);
    }
}
