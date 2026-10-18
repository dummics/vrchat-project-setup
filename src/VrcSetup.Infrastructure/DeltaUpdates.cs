using System.Security.Cryptography;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// A delta carries complete replacement files, never binary patches. Unchanged
// files are copied from an exact, verified base into the normal staging payload.
public sealed record DeltaManifest(string BaseVersion, Dictionary<string, string> Reuse);

public static class DeltaUpdates
{
    public const string ManifestName = ".vrcsetup-delta.json";

    public static async Task<bool> CanUseAsync(string root, string version, CancellationToken token = default)
    {
        try
        {
            var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(root, ".vrcsetup-install.json"), token);
            var manifest = await AtomicFile.ReadJsonAsync<PayloadManifest>(Path.Combine(root, ".vrcsetup-payload.json"), token);
            if (record?.Version != version || manifest?.Version != version) return false;
            await new InstallationManager(AppPaths.ForInstallation(null, root)).ValidatePayloadAsync(root, token);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { return false; }
    }

    public static async Task ReconstructAsync(string payload, string installedRoot, CancellationToken token = default)
    {
        var delta = await AtomicFile.ReadJsonAsync<DeltaManifest>(Path.Combine(payload, ManifestName), token)
            ?? throw new IOException("Update manifest missing.");
        var target = await AtomicFile.ReadJsonAsync<PayloadManifest>(Path.Combine(payload, ".vrcsetup-payload.json"), token)
            ?? throw new IOException("Payload manifest missing.");
        var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(installedRoot, ".vrcsetup-install.json"), token);
        var basis = await AtomicFile.ReadJsonAsync<PayloadManifest>(Path.Combine(installedRoot, ".vrcsetup-payload.json"), token);
        if (record?.Version != delta.BaseVersion || basis?.Version != delta.BaseVersion)
            throw new IOException("This update needs a different installed version. Use the full installer.");
        foreach (var file in delta.Reuse)
        {
            token.ThrowIfCancellationRequested();
            if (!target.Files.TryGetValue(file.Key, out var expected) || !basis.Files.TryGetValue(file.Key, out var original)
                || !expected.Equals(file.Value, StringComparison.OrdinalIgnoreCase) || !expected.Equals(original, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Invalid reused file in update manifest.");
            var source = ProjectRules.NormalizePath(file.Key, installedRoot);
            var destination = ProjectRules.NormalizePath(file.Key, payload);
            ProjectRules.EnsureContained(installedRoot, source); ProjectRules.EnsureContained(payload, destination);
            await using var input = File.OpenRead(source);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Installed files have changed. Use the full installer.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            input.Position = 0;
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, token);
        }
    }
}
