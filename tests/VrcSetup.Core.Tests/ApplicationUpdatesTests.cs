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
}
