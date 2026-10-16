using System.Collections.Concurrent;
namespace VrcSetup_Desktop;

public sealed class AsyncVersionCache(Func<string, CancellationToken, Task<string[]>> load, CancellationToken cancellation = default)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string[]>>> requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim slots = new(3);
    public async Task<string[]> GetAsync(string id)
    {
        var entry = requests.GetOrAdd(id, key => new(() => Task.Run(async () =>
        {
            await slots.WaitAsync(cancellation);
            try { return await load(key, cancellation); }
            finally { slots.Release(); }
        }, cancellation)));
        try { var result = await entry.Value; if (result.Length == 0) ((ICollection<KeyValuePair<string, Lazy<Task<string[]>>>>)requests).Remove(new(id, entry)); return result; }
        catch { ((ICollection<KeyValuePair<string, Lazy<Task<string[]>>>>)requests).Remove(new(id, entry)); throw; }
    }
}
