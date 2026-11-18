namespace VrcSetup.Infrastructure;

public static class SharedLog
{
    public static async Task<string> ReadTailAsync(string path, CancellationToken token = default)
    {
        const int limit = 64 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var offset = Math.Max(0, stream.Length - limit);
        stream.Position = offset;
        using var reader = new StreamReader(stream);
        var buffer = new char[limit];
        var count = await reader.ReadBlockAsync(buffer, token);
        var firstLineEnd = offset > 0 ? Array.IndexOf(buffer, '\n', 0, count) : -1;
        return new string(buffer, firstLineEnd + 1, count - firstLineEnd - 1);
    }

    public static async Task<string> ReadAsync(string path, CancellationToken token = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(token);
            }
            catch (IOException) when (attempt < 15) { await Task.Delay(200, token); }
        }
    }
}
