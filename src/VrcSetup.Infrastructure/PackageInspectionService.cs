using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Local-only advisory service. No Unity invocation, catalog network calls or project writes.
public sealed class PackageInspectionService(AppPaths paths, IUnityPackageInspector? reader = null)
{
    private readonly SemaphoreSlim work = new(1), indexGate = new(1);
    private readonly ConcurrentDictionary<string, SharedJob> jobs = new();
    private SignatureSnapshot? index;
    private string Cache => Path.Combine(paths.Cache, "inspection");
    private sealed class SharedJob
    {
        public readonly CancellationTokenSource Cancellation = new();
        public int Consumers;
        public Task<PackageInspectionReport>? Running;
    }
    public async Task<PackageInspectionReport> InspectAsync(string file, InspectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); file = ProjectRules.NormalizePath(file);
        if (!file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a .unitypackage file.");
        // File metadata, JSON parsing and decompression all run away from a UI dispatcher.
        return await Task.Run(async () =>
        {
            var info = new FileInfo(file); if (!info.Exists) throw new IOException("UnityPackage not found.");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.ToUpperInvariant() + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks + ":v2:" + JsonSerializer.Serialize(options))));
            SharedJob job;
            while (true)
            {
                job = jobs.GetOrAdd(key, _ => new());
                lock (job)
                {
                    if (job.Cancellation.IsCancellationRequested) { jobs.TryRemove(new KeyValuePair<string, SharedJob>(key, job)); continue; }
                    job.Consumers++;
                    job.Running ??= RunAsync(file, key, options, job.Cancellation.Token);
                    break;
                }
            }
            try { return await job.Running.WaitAsync(cancellationToken); }
            finally
            {
                lock (job)
                {
                    if (--job.Consumers == 0)
                    {
                        if (!job.Running.IsCompleted) job.Cancellation.Cancel();
                        jobs.TryRemove(new KeyValuePair<string, SharedJob>(key, job));
                        _ = job.Running.ContinueWith(completed => { _ = completed.Exception; job.Cancellation.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
            }
        }, cancellationToken);
    }
    private async Task<PackageInspectionReport> RunAsync(string file, string key, InspectionOptions options, CancellationToken token)
    {
        var snapshot = await LoadIndexAsync(token);
        await work.WaitAsync(token);
        try
        {
            var cacheFile = Path.Combine(Cache, "archive-" + key + ".json");
            var cached = await ReadCache<ArchiveObservation>(cacheFile, 10 * 1024 * 1024, token);
            var stamp = new FileInfo(file);
            if (Usable(cached) && cached!.Length == stamp.Length && cached.ModifiedTicks == stamp.LastWriteTimeUtc.Ticks)
                return DependencyAdvisor.Match(cached, snapshot, true, token);
            var observation = await (reader ?? new UnityPackageInspector()).ReadAsync(file, options, token);
            if (observation.ArchiveComplete)
            {
                var manifests = observation.Assets.Where(x => x.Manifest is not null).Select(x => x.Manifest!).DistinctBy(x => x.Name).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
                var learned = observation.Assets.Select(asset =>
                {
                    var parts = asset.Path.Split('/');
                    var kind = SignatureKind(asset.Path + ".meta");
                    return parts.Length > 2 && parts[0] == "Packages" && kind is not null && manifests.TryGetValue(parts[1], out var manifest)
                        ? new PackageSignature(asset.Guid, manifest.Name, manifest.Version, kind, "archive:" + manifest.Name + "@" + manifest.Version) : null;
                }).OfType<PackageSignature>().ToArray();
                if (learned.Length > 0)
                    try { await RememberSignaturesAsync(learned, token); snapshot = await LoadIndexAsync(token); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            // Cache failures are nonfatal: a read-only profile still gets an inspection.
            if (observation.ArchiveComplete)
                try { await AtomicFile.WriteJsonAsync(cacheFile, observation, token); TrimCache(cacheFile); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return DependencyAdvisor.Match(observation, snapshot, token: token);
        }
        finally { work.Release(); }
    }
    public Task<int> IndexProjectAsync(string project, CancellationToken token = default) => Task.Run(async () =>
    {
        var packages = Path.Combine(ProjectRules.NormalizePath(project), "Packages");
        if (!Directory.Exists(packages)) return 0;
        var added = new List<PackageSignature>(); var started = System.Diagnostics.Stopwatch.StartNew();
        foreach (var folder in Directory.EnumerateDirectories(packages))
        {
            token.ThrowIfCancellationRequested();
            if (started.Elapsed > TimeSpan.FromSeconds(30)) break;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            var manifestPath = Path.Combine(folder, "package.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536) continue;
            string id, version;
            try
            {
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, token));
                id = manifest.RootElement.GetProperty("name").GetString()!; ProjectRules.ValidatePackageId(id);
                version = manifest.RootElement.GetProperty("version").GetString()!;
            }
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or ArgumentException) { continue; }
            var current = await LoadIndexAsync(token);
            if (current.Signatures.Any(x => x.PackageId == id && x.Version == version && x.Source.EndsWith(":complete"))) continue;
            var packageSignatures = new List<PackageSignature>(); var packageComplete = true;
            var metas = Directory.EnumerateFiles(folder, "*.meta", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint });
            foreach (var meta in metas)
            {
                token.ThrowIfCancellationRequested();
                if (started.Elapsed > TimeSpan.FromSeconds(30) || added.Count + packageSignatures.Count >= 20000) { packageComplete = false; break; }
                var kind = SignatureKind(meta); if (kind is null) continue;
                await work.WaitAsync(token);
                try
                {
                    if (new FileInfo(meta).Length > 65536) continue;
                    var text = await File.ReadAllTextAsync(meta, token);
                    var match = Regex.Match(text, @"(?m)^guid:\s*([a-fA-F0-9]{32})", RegexOptions.CultureInvariant);
                    if (match.Success) packageSignatures.Add(new(match.Groups[1].Value.ToLowerInvariant(), id, version, kind, "local:" + id + "@" + version));
                }
                catch (IOException) { }
                finally { work.Release(); }
                await Task.Delay(2, token); // Yield disk access to foreground inspections between files.
            }
            added.AddRange(packageSignatures.Select(x => packageComplete ? x with { Source = x.Source + ":complete" } : x));
        }
        if (added.Count > 0) await RememberSignaturesAsync(added, token);
        return added.Count;
    }, token);
    public async Task RememberSignaturesAsync(IEnumerable<PackageSignature> signatures, CancellationToken token = default)
    {
        await LoadIndexAsync(token); await indexGate.WaitAsync(token);
        try
        {
            var local = await ReadCache<PackageSignature[]>(Path.Combine(Cache, "signatures.json"), 16 * 1024 * 1024, token) ?? [];
            var merged = local.Concat(signatures).Where(Valid).Distinct().TakeLast(50000).ToArray();
            if (local.SequenceEqual(merged)) return;
            await AtomicFile.WriteJsonAsync(Path.Combine(Cache, "signatures.json"), merged, token);
            index = null;
        }
        finally { indexGate.Release(); }
    }
    public async Task<SignatureSnapshot> LoadIndexAsync(CancellationToken token = default)
    {
        if (index is { } loaded) return loaded;
        await indexGate.WaitAsync(token);
        try
        {
            if (index is { } ready) return ready;
            var seed = await ReadCache<PackageSignature[]>(Path.Combine(paths.Resources, "dependency-signatures.json"), 5 * 1024 * 1024, token) ?? [];
            var local = await ReadCache<PackageSignature[]>(Path.Combine(Cache, "signatures.json"), 16 * 1024 * 1024, token) ?? [];
            var signatures = seed.Concat(local).Where(Valid).Distinct().ToArray();
            var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(signatures)));
            return index = new(revision, signatures);
        }
        finally { indexGate.Release(); }
    }
    private static bool Usable(ArchiveObservation? value)
    {
        if (value is not { Schema: 1, ArchiveComplete: true, Assets: not null, Limitations: not null }) return false;
        if (value.Assets.Select(x => x?.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Assets.Length) return false;
        try
        {
            foreach (var asset in value.Assets)
            {
                if (asset is null || !Guid.TryParseExact(asset.Guid, "N", out _) || asset.Path is null || asset.References is null || asset.Hints is null ||
                    asset.References.Any(r => r is null || !Guid.TryParseExact(r.Guid, "N", out _) || r.Kind is null)) return false;
                foreach (var hint in asset.Hints) ProjectRules.ValidatePackageId(hint);
                if (asset.Manifest is { } manifest)
                {
                    ProjectRules.ValidatePackageId(manifest.Name);
                    if (manifest.Dependencies is null) return false;
                    foreach (var dependency in manifest.Dependencies) { ProjectRules.ValidatePackageId(dependency.Key); if (dependency.Value is null) return false; }
                }
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }
    private static bool Valid(PackageSignature value)
    {
        if (value is null || !Guid.TryParseExact(value.Guid, "N", out _) || string.IsNullOrWhiteSpace(value.PackageId) || value.Source is not { Length: < 1024 }) return false;
        try { ProjectRules.ValidatePackageId(value.PackageId); return true; } catch (ArgumentException) { return false; }
    }
    private static string? SignatureKind(string file) => new[] { ".cs.meta", ".dll.meta" }.Any(x => file.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ? "script" : new[] { ".shader.meta", ".shadergraph.meta" }.Any(x => file.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ? "shader" : null;
    private static async Task<T?> ReadCache<T>(string file, int maximum, CancellationToken token)
    {
        try { return File.Exists(file) && new FileInfo(file).Length <= maximum ? await AtomicFile.ReadJsonAsync<T>(file, token) : default; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }
    private void TrimCache(string active)
    {
        var files = new DirectoryInfo(Cache).EnumerateFiles("archive-*.json").OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
        long bytes = 0; int count = 0;
        foreach (var file in files)
        {
            bytes += file.Length;
            if ((++count > 256 || bytes > 240L * 1024 * 1024 || file.Length > 10 * 1024 * 1024) && file.FullName != active) file.Delete();
        }
        if (new FileInfo(active).Length > 10 * 1024 * 1024) File.Delete(active);
    }
}
