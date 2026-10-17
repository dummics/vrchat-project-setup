using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class UnityPackageInspector : IUnityPackageInspector
{
    private sealed class Asset(string guid)
    {
        public string Guid = guid, Path = "";
        public readonly HashSet<AssetReference> References = [];
        public readonly HashSet<string> Hints = [];
        public bool Partial;
        public ObservedManifest? Manifest;
    }
    [GeneratedRegex(@"guid:\s*([a-fA-F0-9]{32})", RegexOptions.CultureInvariant)] private static partial Regex Guids();
    [GeneratedRegex(@"m_(Script|Shader):\s*\{[^}]{0,256}guid:\s*([a-fA-F0-9]{32})", RegexOptions.CultureInvariant)] private static partial Regex TypedGuids();
    private static readonly (string Token, string Id)[] Hints = [("VRCFury", "com.vrcfury.vrcfury"), ("Poiyomi", "com.poiyomi.toon"), ("nadena.dev.modular_avatar", "nadena.dev.modular-avatar"), ("nadena.dev.ndmf", "nadena.dev.ndmf"), ("AudioLink", "com.llealloo.audiolink")];
    public async Task<ArchiveObservation> ReadAsync(string path, InspectionOptions options, CancellationToken token)
    {
        var info = new FileInfo(path); var length = info.Length; var modified = info.LastWriteTimeUtc.Ticks;
        var timer = Stopwatch.StartNew(); var assets = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
        var limitations = new HashSet<string>(); bool complete = false; long textBytes = 0, pathBytes = 0; int entries = 0, references = 0;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, true);
        await using var budget = new BudgetStream(gzip, options.ExpandedBytes, options.Timeout, token);
        await using var reader = new TarReader(budget, true);
        try
        {
            while (await reader.GetNextEntryAsync(false, token) is { } entry)
            {
                token.ThrowIfCancellationRequested();
                if (++entries > options.MaxEntries) throw new InspectionLimitException("Archive entry limit reached");
                var parts = entry.Name.TrimStart('.', '/').Split('/');
                if (parts.Length is < 1 or > 2 || !Guid.TryParseExact(parts[0], "N", out var guid)) continue;
                var id = guid.ToString("N");
                if (!assets.TryGetValue(id, out var asset))
                { if (assets.Count >= options.MaxAssets) throw new InspectionLimitException("Asset limit reached"); assets[id] = asset = new(id); }
                if (parts.Length != 2 || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) continue;
                var kind = parts[1];
                if (kind == "pathname")
                {
                    if (entry.Length > 16384) { asset.Partial = true; limitations.Add("Oversized asset path"); continue; }
                    if ((pathBytes += entry.Length) > 16 * 1024 * 1024) throw new InspectionLimitException("Asset path memory limit reached");
                    var bytes = await ReadBounded(entry.DataStream, (int)entry.Length, token);
                    asset.Path = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split('\n')[0].TrimEnd('\r'); continue;
                }
                if (kind is not ("asset" or "asset.meta")) continue;
                if (entry.Length > options.EntryTextBytes || entry.Length > options.TotalTextBytes - textBytes)
                { asset.Partial = true; continue; }
                var prefix = await ReadBounded(entry.DataStream, (int)Math.Min(512, entry.Length), token);
                if (prefix.Contains((byte)0)) continue;
                var start = Encoding.UTF8.GetString(prefix).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
                if (kind == "asset" && !new[] { "%YAML", "{", "[", "using ", "namespace ", "//", "#", "Shader", "fileFormatVersion" }.Any(start.StartsWith)) continue;
                var remaining = await ReadBounded(entry.DataStream, (int)entry.Length - prefix.Length, token);
                var data = Encoding.UTF8.GetString(prefix) + Encoding.UTF8.GetString(remaining); textBytes += entry.Length;
                var typed = TypedGuids().Matches(data).ToDictionarySafe();
                foreach (Match match in Guids().Matches(data))
                {
                    var referenced = match.Groups[1].Value.ToLowerInvariant();
                    if (referenced == id) continue;
                    if (++references > options.MaxReferences) throw new InspectionLimitException("Reference limit reached");
                    asset.References.Add(new(referenced, typed.GetValueOrDefault(referenced) ?? "asset"));
                }
                foreach (var hint in Hints) if (data.Contains(hint.Token, StringComparison.OrdinalIgnoreCase)) asset.Hints.Add(hint.Id);
                if (kind == "asset" && data.TrimStart().StartsWith('{')) asset.Manifest = ParseManifest(data);
            }
            var tail = new byte[16384];
            while (await budget.ReadAsync(tail, token) != 0) { }
            complete = true;
        }
        catch (InspectionLimitException error) { limitations.Add(error.Message); }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or FormatException or ArgumentException)
        { limitations.Add("Archive is incomplete or invalid: " + error.Message); }
        info.Refresh();
        if (info.Length != length || info.LastWriteTimeUtc.Ticks != modified) throw new IOException("Package changed while it was being inspected. Select it again.");
        foreach (var asset in assets.Values)
            if (new[] { ".png", ".jpg", ".jpeg", ".tga", ".dds", ".psd", ".wav", ".ogg", ".mp3", ".dll", ".ttf", ".otf" }.Any(x => asset.Path.EndsWith(x, StringComparison.OrdinalIgnoreCase))) asset.Partial = false;
        if (assets.Values.Any(x => x.Partial)) limitations.Add("Some asset text exceeded the analysis budget");
        foreach (var asset in assets.Values.Where(x => x.Path.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase) && x.Manifest is null))
        { asset.Partial = true; limitations.Add("Some package manifests could not be interpreted"); }
        var result = assets.Values.Select(x => new InspectedAsset(x.Guid, x.Path, x.References.ToArray(), x.Hints.ToArray(), x.Partial,
            x.Path.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase) ? x.Manifest : null)).ToArray();
        return new(1, length, modified, complete, !assets.Values.Any(x => x.Partial) && complete, limitations.ToArray(), result, timer.Elapsed.TotalMilliseconds);
    }
    private static ObservedManifest? ParseManifest(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return null;
            var id = name.GetString()!; if (id.Length > 200) return null; ProjectRules.ValidatePackageId(id);
            var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "dependencies", "vpmDependencies" })
                if (doc.RootElement.TryGetProperty(key, out var map) && map.ValueKind == JsonValueKind.Object)
                    foreach (var property in map.EnumerateObject())
                        if (property.Value.ValueKind == JsonValueKind.String)
                        { if (dependencies.Count >= 2048 || property.Name.Length > 200 || property.Value.GetString()!.Length > 1024) return null; ProjectRules.ValidatePackageId(property.Name); dependencies[property.Name] = property.Value.GetString()!; }
            return new(id, doc.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString()! : "", dependencies);
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { return null; }
    }
    private static async Task<byte[]> ReadBounded(Stream stream, int length, CancellationToken token)
    {
        var data = new byte[length]; await stream.ReadExactlyAsync(data, token); return data;
    }
    private sealed class InspectionLimitException(string message) : IOException(message);
    private sealed class BudgetStream(Stream source, long maximum, TimeSpan timeout, CancellationToken token) : Stream
    {
        private readonly Stopwatch timer = Stopwatch.StartNew(); private long count;
        private void Check() { token.ThrowIfCancellationRequested(); if (timer.Elapsed >= timeout) throw new InspectionLimitException("Analysis time limit reached"); if (count > maximum) throw new InspectionLimitException("Expanded archive limit reached"); }
        public override int Read(byte[] buffer, int offset, int size) { Check(); var read = source.Read(buffer, offset, (int)Math.Min(size, maximum - count + 1)); count += read; Check(); return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { Check(); var read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, maximum - count + 1)], cancellationToken); count += read; Check(); return read; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => count; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
internal static class InspectionRegexExtensions
{
    public static Dictionary<string, string> ToDictionarySafe(this MatchCollection matches)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches) result[match.Groups[2].Value] = match.Groups[1].Value.ToLowerInvariant(); return result;
    }
}
