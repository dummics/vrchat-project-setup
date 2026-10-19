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
    public void Write(string area, string message, Exception? error = null)
    {
        if (error is null && !DeveloperMode && !Verbose && !TraceTools) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(paths.Logs);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(new { Timestamp = DateTime.UtcNow, Area = area, Message = message, Error = error?.ToString() }) + Environment.NewLine);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(failure); }
    }
}
