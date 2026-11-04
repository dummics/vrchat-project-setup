namespace VrcSetup.Infrastructure;

public static class SharedLog
{
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
