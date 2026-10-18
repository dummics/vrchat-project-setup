using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class PackageCatalog(AppPaths paths, ToolRunner runner, ConfigurationStore configuration, string? repositoryDirectory = null) : IPackageCatalog
{
    private readonly ConcurrentDictionary<string, string[]> versionsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProjectRules.BaseSdk] = "VRChat SDK Base", [ProjectRules.AvatarSdk] = "VRChat SDK Avatars", [ProjectRules.WorldSdk] = "VRChat SDK Worlds", [ProjectRules.Resolver] = "VRChat Package Resolver",
        [ProjectRules.EasyLogin] = "Easy Login", ["com.vrcfury.vrcfury"] = "VRCFury", ["com.poiyomi.toon"] = "Poiyomi Toon Shader", ["gogoloco"] = "GoGo Loco", ["adjerry91.vrcft.templates"] = "VRCFury Templates", ["nadena.dev.modular-avatar"] = "Modular Avatar", ["nadena.dev.ndmf"] = "NDMF"
    };
    public static string FriendlyName(string id) => FriendlyNames.GetValueOrDefault(id) ?? id switch
    {
        "com.manonchip.avatarlimbscaling" or "com.nanochip.avatarlimbscaling" => "Avatar Limb Scaling", "com.llealloo.audiolink" => "AudioLink",
        "jp.lilxyzw.liltoon" => "lilToon", "vrchat.blackstartx.gesture-manager" => "Gesture Manager", _ => id
    };
    private string RegistryFile => Path.Combine(paths.Cache, "known-packages.json");
    private string VersionFile => Path.Combine(paths.Cache, "package-versions.json");

    public async Task<PackageDefinition[]> KnownAsync(AppConfiguration? config = null, IEnumerable<IReadOnlyDictionary<string, string>>? packageSets = null, CancellationToken cancellationToken = default)
    {
        config ??= await configuration.LoadAsync(cancellationToken);
        var known = await ReadRegistry(cancellationToken);
        foreach (var set in new[] { (IReadOnlyDictionary<string, string>)config.VpmPackages }.Concat(config.PackagePresets.Values).Concat(packageSets ?? []))
            foreach (var entry in set)
            {
                var old = known.GetValueOrDefault(entry.Key) ?? new(entry.Key, FriendlyName(entry.Key), "latest");
                known[entry.Key] = old with { SelectedVersion = entry.Value };
            }
        foreach (var id in config.FavoritePackages) known.TryAdd(id, new(id, FriendlyName(id), "latest"));
        var repositoryNames = (await Task.Run(ReadRepositoryPackages, cancellationToken)).ToDictionary(x => x.Id, x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var id in known.Keys.ToArray())
            if (string.IsNullOrWhiteSpace(known[id].DisplayName) || known[id].DisplayName.Equals(id, StringComparison.OrdinalIgnoreCase))
                known[id] = known[id] with { DisplayName = repositoryNames.GetValueOrDefault(id) is { Length: > 0 } display && !display.Equals(id, StringComparison.OrdinalIgnoreCase) ? display : FriendlyName(id) };
        var metadata = await ReadVersionCache(cancellationToken);
        foreach (var id in known.Keys.ToArray())
            if (metadata.TryGetValue(id, out var cached) && (known[id].UpdatedUtc is null || cached.UpdatedUtc > known[id].UpdatedUtc))
                known[id] = known[id] with { Versions = cached.Versions, UpdatedUtc = cached.UpdatedUtc };
        return known.Values.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).ToArray();
    }

    public async Task<PackageDefinition[]> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var result = new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var definition in await KnownAsync(cancellationToken: cancellationToken))
            if (tokens.All(x => (definition.Id + " " + definition.DisplayName).Contains(x, StringComparison.OrdinalIgnoreCase))) result[definition.Id] = definition;
        var repoDefinitions = ReadRepositoryPackages();
        foreach (var definition in repoDefinitions)
            if (tokens.All(x => (definition.Id + " " + definition.DisplayName).Contains(x, StringComparison.OrdinalIgnoreCase))) result[definition.Id] = definition;
        if (paths.VrcGetPath is not null)
        {
            var response = await runner.RunAsync(paths.VrcGetPath, new[] { "search" }.Concat(tokens), cancellationToken: cancellationToken);
            if (response.ExitCode != 0 && result.Count == 0) throw new IOException("Package search failed: " + response.Output);
            if (response.ExitCode == 0)
                foreach (var definition in ParseSearch(response.StdOut))
                    result[definition.Id] = definition.DisplayName.Equals(definition.Id, StringComparison.OrdinalIgnoreCase) && result.TryGetValue(definition.Id, out var named)
                        ? definition with { DisplayName = named.DisplayName } : definition;
        }
        // Search metadata is a bounded local index, not a saved user selection.
        try { await RememberAsync(result.Values, cancellationToken, preserveSelections: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Optional search index must not hide usable results. */ }
        return result.Values.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).ToArray();
    }

    public async Task<string[]> GetVersionsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ProjectRules.ValidatePackageId(packageId);
        if (versionsCache.TryGetValue(packageId, out var cached)) return cached;
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths.VrcGetPath is not null)
        {
            var result = await runner.RunAsync(paths.VrcGetPath, ["info", "package", packageId, "--json-format", "1"], cancellationToken: cancellationToken);
            if (result.ExitCode == 0)
            {
                try
                {
                    using var document = JsonDocument.Parse(result.StdOut);
                    if (document.RootElement.TryGetProperty("versions", out var list))
                        foreach (var entry in list.EnumerateArray())
                            if (entry.TryGetProperty("version", out var version) && version.GetString() is { } text) versions.Add(text);
                }
                catch (JsonException) { }
            }
        }
        foreach (var definition in ReadRepositoryPackages().Where(x => x.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))) versions.UnionWith(definition.Versions);
        var registry = await ReadRegistry(cancellationToken);
        if (versions.Count == 0)
        {
            var metadata = await ReadVersionCache(cancellationToken);
            if (metadata.TryGetValue(packageId, out var savedVersions)) versions.UnionWith(savedVersions.Versions);
        }
        if (versions.Count == 0 && registry.TryGetValue(packageId, out var saved)) versions.UnionWith(saved.Versions);
        var ordered = SemanticVersion.Sort(versions);
        if (ordered.Length > 0)
        {
            versionsCache[packageId] = ordered;
            // Version lookup can be triggered by a recognition suggestion. It must
            // never turn that suggestion into a My Packages entry.
            try
            {
                using var cacheLock = await AtomicFile.LockAsync(Path.Combine(paths.Cache, "versions.lock"), cancellationToken);
                var metadata = await ReadVersionCache(cancellationToken);
                metadata[packageId] = new(packageId, FriendlyName(packageId), ordered[0]) { Versions = ordered, UpdatedUtc = DateTime.UtcNow };
                var config = await configuration.LoadAsync(cancellationToken);
                await AtomicFile.WriteJsonAsync(VersionFile, BoundCache(metadata.Values, config), cancellationToken);
                if (versionsCache.Count > 512)
                {
                    var pinned = ProtectedIds(config);
                    foreach (var key in versionsCache.Keys.Where(x => !pinned.Contains(x)).Take(versionsCache.Count - 512)) versionsCache.TryRemove(key, out _);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // Optional cache; choices remain usable.
        }
        return ordered;
    }

    public async Task RememberAsync(IEnumerable<PackageDefinition> definitions, CancellationToken cancellationToken = default, bool preserveSelections = false)
    {
        using var registryLock = await AtomicFile.LockAsync(Path.Combine(paths.Cache, "catalog.lock"), cancellationToken);
        var known = await ReadRegistry(cancellationToken);
        foreach (var item in definitions)
        {
            var old = known.GetValueOrDefault(item.Id);
            known[item.Id] = item with
            {
                DisplayName = item.DisplayName.Equals(item.Id, StringComparison.OrdinalIgnoreCase) && old is not null ? old.DisplayName : item.DisplayName,
                SelectedVersion = preserveSelections && old is not null ? old.SelectedVersion : item.SelectedVersion,
                Versions = item.Versions.Length > 0 ? item.Versions : old?.Versions ?? [], UpdatedUtc = item.UpdatedUtc ?? DateTime.UtcNow
            };
        }
        await AtomicFile.WriteJsonAsync(RegistryFile, BoundCache(known.Values, await configuration.LoadAsync(cancellationToken)), cancellationToken);
    }
    private static HashSet<string> ProtectedIds(AppConfiguration config) => config.FavoritePackages.Concat(config.VpmPackages.Keys)
        .Concat(config.PackagePresets.Values.SelectMany(x => x.Keys)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static PackageDefinition[] BoundCache(IEnumerable<PackageDefinition> definitions, AppConfiguration config)
    {
        var pinned = ProtectedIds(config); var values = definitions.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Where(x => pinned.Contains(x.Id)).Concat(values.Where(x => !pinned.Contains(x.Id) && x.UpdatedUtc >= DateTime.UtcNow.AddDays(-30))
            .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(512)).OrderBy(x => x.Id).ToArray();
    }
    public async Task RefreshAsync(IEnumerable<PackageDefinition> definitions, IProgress<OperationEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        var items = definitions.ToArray(); var done = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested(); versionsCache.TryRemove(item.Id, out _);
            try
            {
                var versions = await GetVersionsAsync(item.Id, cancellationToken);
                if (versions.Length == 0) progress?.Report(new(DateTime.UtcNow, "Catalog", $"Unavailable: {item.Id}; saved definition kept.", EventSeverity.Warning));
            }
            catch (IOException error) { progress?.Report(new(DateTime.UtcNow, "Catalog", error.Message, EventSeverity.Warning)); }
            progress?.Report(new(DateTime.UtcNow, "Catalog", $"Refreshed {++done}/{items.Length}"));
        }
    }
    private async Task<Dictionary<string, PackageDefinition>> ReadRegistry(CancellationToken cancellationToken)
    {
        try { return (await AtomicFile.ReadJsonAsync<PackageDefinition[]>(RegistryFile, cancellationToken) ?? []).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }
    private async Task<Dictionary<string, PackageDefinition>> ReadVersionCache(CancellationToken cancellationToken)
    {
        try { return (await AtomicFile.ReadJsonAsync<PackageDefinition[]>(VersionFile, cancellationToken) ?? []).DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }
    private PackageDefinition[] ReadRepositoryPackages()
    {
        var directory = repositoryDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRChatCreatorCompanion", "Repos");
        if (!Directory.Exists(directory)) return [];
        var results = new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.TryGetProperty("repo", out var repository)) root = repository;
                if (!root.TryGetProperty("packages", out var packages)) continue;
                foreach (var package in packages.EnumerateObject())
                {
                    if (!package.Value.TryGetProperty("versions", out var versions)) continue;
                    var ordered = SemanticVersion.Sort(versions.EnumerateObject().Select(x => x.Name));
                    if (ordered.Length == 0) continue;
                    var latest = versions.GetProperty(ordered[0]);
                    var name = latest.TryGetProperty("displayName", out var display) ? display.GetString() ?? FriendlyName(package.Name) : FriendlyName(package.Name);
                    var description = latest.TryGetProperty("description", out var text) ? text.GetString() ?? "" : "";
                    if (results.TryGetValue(package.Name, out var existing)) ordered = SemanticVersion.Sort(ordered.Concat(existing.Versions));
                    results[package.Name] = new(package.Name, name, SemanticVersion.Sort(ordered, false).FirstOrDefault() ?? ordered[0], description) { Versions = ordered };
                }
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return results.Values.ToArray();
    }
    public static PackageDefinition[] ParseSearch(string output)
    {
        var results = new List<PackageDefinition>();
        foreach (var block in Regex.Split(output, @"\r?\n\s*\r?\n"))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !Regex.IsMatch(x, @"^[we]:")).ToArray();
            if (lines.Length == 0 || lines[0].Contains("No matching package found")) continue;
            var match = Regex.Match(lines[0], @"^(.+?)\s+version\s+(.+)$");
            if (!match.Success) continue;
            var id = lines.Length > 1 && lines[1].StartsWith('(') && lines[1].EndsWith(')') ? lines[1][1..^1] : match.Groups[1].Value;
            try { ProjectRules.ValidatePackageId(id); } catch (ArgumentException) { continue; }
            results.Add(new(id, match.Groups[1].Value == id ? FriendlyName(id) : match.Groups[1].Value, match.Groups[2].Value, lines.Length > 2 ? lines[2] : ""));
        }
        return results.ToArray();
    }
}
