using System.Text;

namespace VrcSetup.Infrastructure;

public static class LogRetention
{
    public const long TotalBytes = 20L * 1024 * 1024;
    public const int DetailBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();

    public static void Prune(AppPaths paths, string? keep = null)
    {
        lock (Gate)
        {
            if (!Directory.Exists(paths.Logs)) return;
            var files = new DirectoryInfo(paths.Logs).GetFiles().Where(IsDiagnostic).OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
            foreach (var file in files)
            {
                if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                var limit = file.Extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase) ? 512 * 1024 : DetailBytes;
                if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || file.Length <= limit) continue;
                try
                {
                    // Active writers are never truncated or deleted by maintenance.
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    TrimTail(stream, limit / 2);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            long used = 0;
            foreach (var file in files)
            {
                file.Refresh(); if (!file.Exists) continue;
                used += file.Length;
                if (used <= TotalBytes || file.FullName.Equals(keep, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    var length = file.Length; file.Delete(); used -= length;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    internal static void TrimTail(FileStream stream, int bytes)
    {
        if (stream.Length <= bytes) return;
        stream.Position = stream.Length - bytes;
        var tail = new byte[bytes]; var count = stream.Read(tail);
        var start = Array.IndexOf(tail, (byte)'\n', 0, count) + 1;
        stream.Position = 0; stream.SetLength(0);
        stream.Write(tail.AsSpan(start, count - start));
        stream.Flush();
    }

    private static bool IsDiagnostic(FileInfo file) => !file.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
        (file.Extension is ".log" or ".jsonl" || file.Name.StartsWith("operation-", StringComparison.OrdinalIgnoreCase) && file.Extension == ".json");
}
