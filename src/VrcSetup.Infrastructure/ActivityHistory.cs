using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Operation reports are the canonical persisted outcome; history never reads log bodies.
public sealed class ActivityHistory(AppPaths paths)
{
    public async Task<OperationReport[]> ReadAsync(CancellationToken token = default)
    {
        var files = await Task.Run(() => Directory.Exists(paths.Logs)
            ? Directory.EnumerateFiles(paths.Logs, "operation-*.json").Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).Take(300).ToArray()
            : [], token);
        var reports = new List<OperationReport>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (file.Length > 8 * 1024 * 1024) continue;
            try
            {
                var report = await AtomicFile.ReadJsonAsync<OperationReport>(file.FullName, token);
                if (report is { Results: not null, NotProcessed: not null } && !string.IsNullOrWhiteSpace(report.Id)
                    && report.Results.All(x => x is { Project: not null, Path: not null, Message: not null })
                    && (report.Plan is null || report.Plan.Projects is { } projects && projects.All(x => x is { Changes: not null, DesiredPolicy: not null, ExtraImports: not null } && x.Changes.All(c => c is { Id: not null }))))
                    reports.Add(report with { ReportFile = file.FullName });
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { /* Incomplete reports are retried on the next visit. */ }
        }
        return reports.OrderByDescending(x => x.CompletedUtc).DistinctBy(x => x.Id).ToArray();
    }
}
