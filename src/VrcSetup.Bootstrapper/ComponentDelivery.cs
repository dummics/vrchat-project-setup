using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrcSetup.Bootstrapper;

public sealed record Component(string Name, string Sha256, long Bytes, Dictionary<string, string> Files);
public sealed record ComponentManifest(int Schema, string Version, string BaseUri, List<Component> Components, bool Prefetch = false);
public sealed record DeliveryResult(long DownloadedBytes, int DownloadedComponents, int CachedComponents, int ReusedComponents);
[JsonSerializable(typeof(ComponentManifest))]
[JsonSerializable(typeof(DeliveryResult))]
[JsonSerializable(typeof(string))]
internal partial class DeliveryJson : JsonSerializerContext;
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }

// Hosting is an HTTPS directory of immutable, content-addressed ZIPs. No GitHub
// API assumptions here: the exact same contract works on static VPS/CDN hosting.
public sealed class ComponentDelivery(HttpClient client)
{
    public static ComponentManifest Read(Stream stream)
    {
        var manifest = JsonSerializer.Deserialize(stream, DeliveryJson.Default.ComponentManifest) ?? throw new IOException("Missing component manifest.");
        Validate(manifest); return manifest;
    }
    public static void Validate(ComponentManifest manifest)
    {
        if (manifest.Schema != 1 || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Components is not { Count: > 0 and <= 64 }
            || !Uri.TryCreate(manifest.BaseUri, UriKind.Absolute, out var basis) || basis.Scheme != "https" || !basis.IsDefaultPort
            || basis.UserInfo != "" || basis.Query != "" || basis.Fragment != "" || !basis.AbsolutePath.EndsWith('/'))
            throw new IOException("Unsupported component manifest or source.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var module in manifest.Components)
        {
            if (!Hash(module.Sha256) || module.Name != module.Sha256.ToLowerInvariant() + ".zip" || !names.Add(module.Name)
                || module.Bytes is <= 0 or > 512L * 1024 * 1024 || module.Files is not { Count: > 0 }) throw new IOException("Invalid component metadata.");
            bytes += module.Bytes;
            foreach (var file in module.Files)
            {
                _ = Contained(Path.GetTempPath(), file.Key);
                if (!(file.Key.StartsWith("payload/", StringComparison.Ordinal) || file.Key.StartsWith("engine/", StringComparison.Ordinal))
                    || !Hash(file.Value) || !paths.Add(file.Key)) throw new IOException("Invalid component file.");
            }
        }
        if (paths.Count > 10000 || bytes > 2L * 1024 * 1024 * 1024 || !paths.Contains("payload/.vrcsetup-payload.json")
            || !paths.Contains("engine/VrcSetupInstaller.exe")) throw new IOException("Incomplete component manifest.");
    }
    private static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal static string Contained(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/')
            || relative.Split('/').Any(x => x is "" or "." or ".." || x.EndsWith('.') || x.EndsWith(' '))) throw new IOException("Unsafe component path.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Component path escapes staging.");
        return path;
    }
    public async Task<DeliveryResult> PrepareAsync(ComponentManifest manifest, string staging, string cache, string installedRoot,
        IProgress<(int Percent, string Step)>? progress = null, CancellationToken token = default)
    {
        Validate(manifest);
        if (Directory.Exists(staging)) throw new IOException("Use fresh staging.");
        Directory.CreateDirectory(staging); Directory.CreateDirectory(cache);
        // Concurrent bootstrappers cannot prune/use partial component archives together.
        await using var lease = new FileStream(Path.Combine(cache, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        foreach (var partial in Directory.GetFiles(cache, "*.partial-*"))
            if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(partial), "^[a-f0-9]{64}\\.zip\\.partial-[a-f0-9]{32}$")) File.Delete(partial);
        long downloaded = 0; int received = 0, cached = 0, reused = 0;
        var total = manifest.Components.Sum(x => x.Bytes);
        long completedBytes = 0;
        foreach (var module in manifest.Components)
        {
            token.ThrowIfCancellationRequested();
            if (await ReuseAsync(module, staging, installedRoot, token)) reused++;
            else
            {
                var zip = Path.Combine(cache, module.Name);
                if (await MatchesAsync(zip, module.Sha256, module.Bytes, token)) cached++;
                else
                {
                    if (File.Exists(zip)) File.Delete(zip);
                    var downloadedNow = await DownloadAsync(new Uri(new Uri(manifest.BaseUri), module.Name), zip, module, new InlineProgress<long>(n =>
                        progress?.Report(((int)Math.Min(99, (completedBytes + n) * 100 / total), "Downloading components…"))), token);
                    downloaded += downloadedNow; received++;
                }
                await ExtractAsync(zip, staging, module, token);
                File.SetLastWriteTimeUtc(zip, DateTime.UtcNow);
            }
            completedBytes += module.Bytes;
            progress?.Report(((int)(completedBytes * 100 / total), "Verifying files"));
        }
        Prune(cache, manifest.Components.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return new(downloaded, received, cached, reused);
    }
    private static async Task<bool> ReuseAsync(Component module, string staging, string installed, CancellationToken token)
    {
        if (module.Files.Keys.Any(x => !x.StartsWith("payload/", StringComparison.Ordinal))) return false;
        foreach (var file in module.Files)
            if (!await MatchesAsync(Contained(installed, file.Key[8..]), file.Value, null, token)) return false;
        foreach (var file in module.Files)
        {
            var destination = Contained(staging, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Contained(installed, file.Key[8..]), destination);
            if (!await MatchesAsync(destination, file.Value, null, token)) throw new IOException("Installed component changed during reuse.");
        }
        return true;
    }
    private static async Task<bool> MatchesAsync(string path, string hash, long? bytes, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        return (bytes is null || stream.Length == bytes) && Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    private async Task<long> DownloadAsync(Uri uri, string destination, Component module, IProgress<long> progress, CancellationToken token)
    {
        var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            // Retry transient transport failures; never retry checksum/structure failures.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.UserAgent.ParseAdd("VrcSetup-Setup/1");
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is { } length && length != module.Bytes) throw new IOException("Unexpected component size.");
                    await using (var input = await response.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920]; long total = 0; int count;
                        while ((count = await input.ReadAsync(buffer, token)) > 0)
                        {
                            total += count; if (total > module.Bytes) throw new IOException("Oversized component.");
                            await output.WriteAsync(buffer.AsMemory(0, count), token); progress.Report(total);
                        }
                    }
                    if (!await MatchesAsync(temporary, module.Sha256, module.Bytes, token)) throw new IOException("Component verification failed.");
                    File.Move(temporary, destination, true); return module.Bytes;
                }
                catch (HttpRequestException) when (attempt < 2 && !token.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task ExtractAsync(string zip, string staging, Component module, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(zip); var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        if (archive.Entries.Count != module.Files.Count) throw new IOException("Unexpected component archive structure.");
        foreach (var entry in archive.Entries)
        {
            if (!module.Files.TryGetValue(entry.FullName, out var expected) || !found.Add(entry.FullName)
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || entry.Length < 0 || entry.Length > 512L * 1024 * 1024)
                throw new IOException("Unexpected component archive entry.");
            expanded += entry.Length; if (expanded > 2L * 1024 * 1024 * 1024) throw new IOException("Expanded component is too large.");
            var path = Contained(staging, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await input.CopyToAsync(output, token);
            if (!await MatchesAsync(path, expected, entry.Length, token)) throw new IOException("Component file verification failed.");
        }
        if (found.Count != module.Files.Count) throw new IOException("Incomplete component archive.");
    }
    private static void Prune(string cache, HashSet<string> current)
    {
        var unused = new DirectoryInfo(cache).GetFiles("*.zip").Where(x => !current.Contains(x.Name)).OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
        long bytes = new DirectoryInfo(cache).GetFiles("*.zip").Where(x => current.Contains(x.Name)).Sum(x => x.Length);
        foreach (var file in unused)
        {
            bytes += file.Length;
            if (bytes > 512L * 1024 * 1024 || file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) try { file.Delete(); } catch (IOException) { }
        }
    }
}
