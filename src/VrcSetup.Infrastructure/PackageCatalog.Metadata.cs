using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class PackageCatalog
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string), PackageVersionMetadata> packageMetadata = new();
    public Task<PackageVersionMetadata> GetMetadataAsync(string packageId, string version, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (packageMetadata.TryGetValue((packageId, version), out var cached)) return cached;
        var directory = repositoryDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRChatCreatorCompanion", "Repos");
        if (!Directory.Exists(directory)) return PackageVersionMetadata.Empty;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.TryGetProperty("repo", out var repository)) root = repository;
                if (!root.TryGetProperty("packages", out var packages) || !packages.TryGetProperty(packageId, out var package)
                    || !package.TryGetProperty("versions", out var versions) || !versions.TryGetProperty(version, out var metadata)) continue;
                var replaces = metadata.TryGetProperty("legacyPackages", out var legacy) && legacy.ValueKind == JsonValueKind.Array
                    ? legacy.EnumerateArray().Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() : [];
                var assets = new[] { "legacyFolders", "legacyFiles" }.SelectMany(key =>
                    metadata.TryGetProperty(key, out var map) && map.ValueKind == JsonValueKind.Object ? map.EnumerateObject().Select(x => x.Name).ToArray() : []).ToArray();
                var result = new PackageVersionMetadata(replaces, assets);
                packageMetadata[(packageId, version)] = result;
                return result;
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return PackageVersionMetadata.Empty;
    }, cancellationToken);
}
