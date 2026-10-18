using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ApplicationUpdatesTests
{
    private static object Release(string version, bool draft = false, bool beta = true, string host = ApplicationUpdates.Repository, bool complete = true) => new
    {
        tag_name = "v" + version, draft, prerelease = beta,
        assets = new[] { new { name = $"Install-VrcSetup-{version}-win-x64.exe", browser_download_url = $"{host}/releases/download/v{version}/Install-VrcSetup-{version}-win-x64.exe" },
            new { name = complete ? "SHA256SUMS.txt" : "missing.txt", browser_download_url = $"{host}/releases/download/v{version}/SHA256SUMS.txt" } }
    };
    [Fact] public void SelectsNewestCompletePublishedReleaseAndHonorsBetaPreference()
    {
        var json = JsonSerializer.Serialize(new[] { Release("0.2.0", beta: false), Release("0.2.1-beta.2"), Release("0.2.1-beta.10"), Release("0.3.0", draft: true), Release("0.4.0", complete: false) });
        Assert.Equal("0.2.1-beta.10", ApplicationUpdates.SelectRelease(json, "0.2.0-beta.1", true)!.Version);
        Assert.Equal("0.2.0", ApplicationUpdates.SelectRelease(json, "0.2.0-beta.1", false)!.Version);
        Assert.Null(ApplicationUpdates.SelectRelease(json, "0.2.0", false));
    }
    [Fact] public void HandlesRenumberingWithoutReofferingOldPreviewsOrDowngrades()
    {
        var json = JsonSerializer.Serialize(new[] { Release("2.0.0-preview.99"), Release("0.2.0-beta.1") });
        Assert.Equal("0.2.0-beta.1", ApplicationUpdates.SelectRelease(json, "2.0.0-preview.14", true)!.Version);
        Assert.Null(ApplicationUpdates.SelectRelease(json, "0.2.0-beta.1", true));
        Assert.Null(ApplicationUpdates.SelectRelease(json, "0.2.1", true));
    }
    [Fact] public void FutureStableVersionsAndOptInBetasRemainSupported()
    {
        var json = JsonSerializer.Serialize(new[] { Release("1.0.0", beta: false), Release("1.1.0-beta.1"), Release("2.0.0-preview.99") });
        Assert.Equal("1.0.0", ApplicationUpdates.SelectRelease(json, "0.2.0-beta.7", false)!.Version);
        Assert.Equal("1.1.0-beta.1", ApplicationUpdates.SelectRelease(json, "0.2.0-beta.7", true)!.Version);
        Assert.Null(ApplicationUpdates.SelectRelease(json, "1.0.0", false));
    }
    [Theory]
    [InlineData("https://example.org/dummics/vrchat-project-setup")]
    [InlineData("https://github.com/other/repository")]
    [InlineData("http://github.com/dummics/vrchat-project-setup")]
    public void RejectsUntrustedInstallerSources(string source)
    { Assert.Null(ApplicationUpdates.SelectRelease(JsonSerializer.Serialize(new[] { Release("0.3.0", host: source) }), "0.2.0-beta.1", true)); }
    [Fact] public async Task NoPublishedReleaseIsAQuietEmptyFeed()
    {
        using var client = new HttpClient(new FixtureHandler(_ => new(HttpStatusCode.NotFound)));
        Assert.Null(await new ApplicationUpdates(client).CheckAsync("0.2.0-beta.1", true));
    }
    [Fact] public void IncompleteMetadataIsIgnored()
    { Assert.Null(ApplicationUpdates.SelectRelease("[{},null,{\"draft\":false,\"tag_name\":\"0.3.0\"}]", "0.2.0-beta.1", true)); }
    [Fact] public void BugReportContainsOnlyVersionAndGenericPrompts()
    {
        var url = Uri.UnescapeDataString(ApplicationUpdates.BugReportUrl);
        Assert.Contains("App version: " + ApplicationUpdates.CurrentVersion, url);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), url);
        Assert.StartsWith(ApplicationUpdates.Repository + "/issues/new?", url);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DownloadVerifiesChecksumAndRemovesIncompletePayload(bool valid)
    {
        var payload = Encoding.UTF8.GetBytes("fixture installer bytes; never executed");
        var version = "0.2.1-beta.1"; var name = $"Install-VrcSetup-{version}-win-x64.exe";
        var hash = valid ? Convert.ToHexString(SHA256.HashData(payload)) : new string('0', 64);
        using var client = new HttpClient(new FixtureHandler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.LocalPath.EndsWith(".txt")
            ? new StringContent(hash + "  " + name + "\n") : new ByteArrayContent(payload) }));
        var directory = Path.Combine(Path.GetTempPath(), "VrcSetup-update-fixture-" + Guid.NewGuid().ToString("N"));
        var root = ApplicationUpdates.Repository + "/releases/download/v" + version + "/";
        try
        {
            var service = new ApplicationUpdates(client); var release = new ApplicationRelease(version, new(root + name), new(root + "SHA256SUMS.txt"), new(ApplicationUpdates.Repository));
            if (valid) Assert.Equal(payload, await File.ReadAllBytesAsync(await service.DownloadAsync(release, directory)));
            else { await Assert.ThrowsAsync<IOException>(() => service.DownloadAsync(release, directory)); Assert.Empty(Directory.GetFiles(directory)); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request)); }

    [Fact] public void DeltaIsSelectedOnlyForTheExactInstalledBase()
    {
        var root = ApplicationUpdates.Repository + "/releases/download/v0.2.0-beta.7/";
        var json = JsonSerializer.Serialize(new[] { new { tag_name = "v0.2.0-beta.7", draft = false, prerelease = true,
            assets = new[] { "Install-VrcSetup-0.2.0-beta.7-win-x64.exe", "Update-VrcSetup-0.2.0-beta.7-from-0.2.0-beta.6-win-x64.exe", "SHA256SUMS.txt" }
                .Select(name => new { name, browser_download_url = root + name }) } });
        Assert.NotNull(ApplicationUpdates.SelectRelease(json, "0.2.0-beta.6", true)!.DeltaInstaller);
        Assert.Null(ApplicationUpdates.SelectRelease(json, "0.2.0-beta.5", true)!.DeltaInstaller);
    }
    [Theory]
    [InlineData(true, true)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task DeltaUsesVerifiedBaseAndFallsBackToFull(bool validBase, bool validDelta)
    {
        var directory = Path.Combine(Path.GetTempPath(), "VrcSetup-delta-download-" + Guid.NewGuid().ToString("N"));
        var installed = Path.Combine(directory, "installed");
        var bytes = Encoding.UTF8.GetBytes("fixture bytes"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var full = "Install-VrcSetup-0.2.0-beta.7-win-x64.exe";
        var delta = "Update-VrcSetup-0.2.0-beta.7-from-0.2.0-beta.6-win-x64.exe";
        var root = ApplicationUpdates.Repository + "/releases/download/v0.2.0-beta.7/";
        var requested = new List<string>();
        try
        {
            var files = new Dictionary<string, string>();
            foreach (var name in new[] { "app/cli/vrcsetup.exe", "app/desktop/VrcSetup.Desktop.exe" })
            { var path = Path.Combine(installed, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, bytes); files[name] = hash; }
            await AtomicFile.WriteJsonAsync(Path.Combine(installed, ".vrcsetup-payload.json"), new PayloadManifest("0.2.0-beta.6", files));
            await AtomicFile.WriteJsonAsync(Path.Combine(installed, ".vrcsetup-install.json"), new InstallationRecord("0.2.0-beta.6", installed, DateTime.UtcNow, null));
            if (!validBase) await File.WriteAllTextAsync(Path.Combine(installed, "app/cli/vrcsetup.exe"), "changed");
            using var client = new HttpClient(new FixtureHandler(request =>
            {
                var name = Path.GetFileName(request.RequestUri!.LocalPath); requested.Add(name);
                return new(HttpStatusCode.OK) { Content = name.EndsWith(".txt")
                    ? new StringContent(hash + "  " + full + "\n" + (validDelta ? hash : new string('0', 64)) + "  " + delta + "\n")
                    : new ByteArrayContent(bytes) };
            }));
            var release = new ApplicationRelease("0.2.0-beta.7", new(root + full), new(root + "SHA256SUMS.txt"), new(ApplicationUpdates.Repository), new(root + delta), "0.2.0-beta.6");
            var service = new ApplicationUpdates(client);
            var downloaded = await service.DownloadAsync(release, Path.Combine(directory, "download"), installedRoot: installed);
            Assert.Equal(validBase && validDelta ? delta : full, Path.GetFileName(downloaded));
            if (!validBase) Assert.DoesNotContain(delta, requested);
            var requests = requested.Count(x => x.EndsWith(".exe"));
            await service.DownloadAsync(release with { DeltaInstaller = null }, Path.Combine(directory, "download"));
            if (!validBase || !validDelta) Assert.Equal(requests, requested.Count(x => x.EndsWith(".exe")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
