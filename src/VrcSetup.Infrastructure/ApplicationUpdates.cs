using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed record ApplicationRelease(string Version, Uri Installer, Uri Checksums, Uri Page, Uri? DeltaInstaller = null, string? BaseVersion = null);

// GitHub hosts the feed, installer and checksums. No credentials or local data are sent.
public sealed class ApplicationUpdates(HttpClient client)
{
    public const string Repository = "https://github.com/dummics/vrchat-project-setup";
    public static string CurrentVersion => typeof(ApplicationUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.2.0-beta.1";
    public static string BugReportUrl => Repository + "/issues/new?title=" + Uri.EscapeDataString("[Bug]: ") + "&body="
        + Uri.EscapeDataString("App version: " + CurrentVersion + "\n\nWhat did you do?\n\nWhat happened?\n\nWhat did you expect?\n\nOptional screenshot or log excerpt (remove personal details):\n");
    private const long MaximumInstallerBytes = 1024L * 1024 * 1024;
    private const string AssetPrefix = Repository + "/releases/download/";

    public async Task<ApplicationRelease?> CheckAsync(string installed, bool includeBeta, CancellationToken token = default)
    {
        using var request = Request("https://api.github.com/repos/dummics/vrchat-project-setup/releases?per_page=100");
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        return SelectRelease(json, installed, includeBeta);
    }

    public static ApplicationRelease? SelectRelease(string json, string installed, bool includeBeta)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a GitHub releases list.");
        var current = new SemanticVersion(installed);
        var candidates = new List<(SemanticVersion Version, ApplicationRelease Release)>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            try
            {
            if (entry.GetProperty("draft").GetBoolean()) continue;
            var tag = entry.GetProperty("tag_name").GetString() ?? "";
            SemanticVersion version;
            try { version = new(tag); } catch (Exception e) when (e is ArgumentException or OverflowException) { continue; }
            if (version.Major == 2 && version.PreRelease.FirstOrDefault() == "preview") continue;
            if (!includeBeta && (version.IsPrerelease || entry.GetProperty("prerelease").GetBoolean())) continue;
            // Old 2.0 previews predate the owner-approved 0.2 beta numbering.
            var legacyMigration = current.Major == 2 && current.PreRelease.FirstOrDefault() == "preview" && version.Major == 0 && version.Minor >= 2;
            if (!legacyMigration && version.CompareTo(current) <= 0) continue;
            var normalized = tag.TrimStart('v');
            var installerName = $"Install-VrcSetup-{normalized}-win-x64.exe";
            var assets = entry.GetProperty("assets").EnumerateArray().ToArray();
            string? Asset(string name) => assets.FirstOrDefault(x => x.GetProperty("name").GetString() == name) is var asset && asset.ValueKind == JsonValueKind.Object ? asset.GetProperty("browser_download_url").GetString() : null;
            if (!TryAsset(Asset(installerName), out var installer) || !TryAsset(Asset("SHA256SUMS.txt"), out var checksums)) continue;
            var page = new Uri(Repository + "/releases/tag/" + Uri.EscapeDataString(tag));
            var baseVersion = installed.TrimStart('v');
            TryAsset(Asset($"Update-VrcSetup-{normalized}-from-{baseVersion}-win-x64.exe"), out var delta);
            candidates.Add((version, new(normalized, installer!, checksums!, page, delta, delta is null ? null : baseVersion)));
            }
            catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException) { /* Ignore incomplete release metadata. */ }
        }
        return candidates.OrderByDescending(x => x.Version).FirstOrDefault().Release;
    }

    private static bool TryAsset(string? value, out Uri? uri)
    {
        uri = null;
        return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.AbsoluteUri.StartsWith(AssetPrefix, StringComparison.Ordinal) && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort;
    }
    private static HttpRequestMessage Request(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("VrcSetup/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    public async Task<string> DownloadAsync(ApplicationRelease release, string directory, IProgress<int>? progress = null, CancellationToken token = default, string? installedRoot = null)
    {
        if (release.DeltaInstaller is not null && release.BaseVersion is not null && installedRoot is not null
            && await DeltaUpdates.CanUseAsync(installedRoot, release.BaseVersion, token))
        {
            try { return await DownloadInstallerAsync(release, release.DeltaInstaller, $"Update-VrcSetup-{release.Version}-from-{release.BaseVersion}-win-x64.exe", directory, progress, token); }
            catch (Exception error) when (error is IOException or HttpRequestException) { /* Full installer is the compatible fallback. */ }
        }
        return await DownloadInstallerAsync(release, release.Installer, $"Install-VrcSetup-{release.Version}-win-x64.exe", directory, progress, token);
    }
    private async Task<string> DownloadInstallerAsync(ApplicationRelease release, Uri installer, string name, string directory, IProgress<int>? progress, CancellationToken token)
    {
        if (!TryAsset(installer.AbsoluteUri, out _) || !TryAsset(release.Checksums.AbsoluteUri, out _)) throw new IOException("Unrecognized update source.");
        using var checksumRequest = Request(release.Checksums.AbsoluteUri);
        using var checksumResponse = await client.SendAsync(checksumRequest, token); checksumResponse.EnsureSuccessStatusCode();
        var checksumText = await checksumResponse.Content.ReadAsStringAsync(token);
        if (checksumText.Length > 64 * 1024) throw new IOException("Update checksum file is too large.");
        if (Path.GetFileName(installer.LocalPath) != name || name != Path.GetFileName(name)) throw new IOException("Unexpected update installer name.");
        var hashes = checksumText.Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == name).Select(parts => parts[0]).ToArray();
        if (hashes.Length != 1 || hashes[0].Length != 64 || !hashes[0].All(Uri.IsHexDigit)) throw new IOException("Installer checksum is missing or ambiguous.");
        var hash = hashes[0];
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name);
        if (File.Exists(target))
        {
            await using var cached = File.OpenRead(target);
            if (Convert.ToHexString(await SHA256.HashDataAsync(cached, token)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            { progress?.Report(100); return target; }
        }
        var temporary = target + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var request = Request(installer.AbsoluteUri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length > MaximumInstallerBytes) throw new IOException("Installer exceeds the update size limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count; if (total > MaximumInstallerBytes) throw new IOException("Installer exceeds the update size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if (length > 0) progress?.Report((int)Math.Min(99, total * 100 / length.Value));
                }
            }
            await using var downloaded = File.OpenRead(temporary);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, token));
            if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update verification failed. Download again.");
            downloaded.Close(); File.Move(temporary, target, true); progress?.Report(100); return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
