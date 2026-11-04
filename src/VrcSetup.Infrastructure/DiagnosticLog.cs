using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class DiagnosticLog(AppPaths paths)
{
    private readonly object gate = new();
    public bool DeveloperMode { get; set; }
    public bool Verbose { get; set; }
    public bool TraceTools { get; set; }
    public string FilePath => Path.Combine(paths.Logs, "VRC-Project-Setup-Errors-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
    public void Configure(DiagnosticSettings settings)
    { DeveloperMode = settings.DeveloperMode; Verbose = settings.Verbose; TraceTools = settings.TraceTools; }
    public void Write(string area, string message, Exception? error = null, string? operationId = null)
    {
        if (error is null && !DeveloperMode && !Verbose && !TraceTools) return;
        var detail = error?.ToString();
        Append(new { Timestamp = DateTime.UtcNow, Area = area, OperationId = operationId, Message = message[..Math.Min(message.Length, 8192)], Error = detail is null ? null : detail[..Math.Min(detail.Length, 32768)] });
    }
    public void RecordAction(string area, string operationId, string state, string message = "") =>
        Append(new { Timestamp = DateTime.UtcNow, Area = area, OperationId = operationId, State = state, Message = message[..Math.Min(message.Length, 2048)] });
    private void Append(object record)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(paths.Logs);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(record) + Environment.NewLine);
                LogRetention.Prune(paths, FilePath);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(failure); }
    }
}
