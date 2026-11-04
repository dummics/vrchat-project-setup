using System.Net;
using System.Text.Json;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ApplicationUpdateSourceTests
{
    [Fact] public void DefaultDiscoveryUsesStaticFeedAndRetainsReleaseAssetTrust()
    {
        Assert.Equal("https://raw.githubusercontent.com/dummics/vrchat-project-setup/main/.github/updates/releases.json", ApplicationUpdateSource.Default.Feed.AbsoluteUri);
        Assert.Equal(ApplicationUpdates.Repository + "/releases/download/", ApplicationUpdateSource.Default.AssetPrefix);
    }
    [Fact] public async Task StaticVpsFeedSelectsReleasesWithoutGitHubApiOrWiderAssetTrust()
    {
        var source = new ApplicationUpdateSource("https://updates.example.test/releases.json", ApplicationUpdates.Repository, "https://cdn.example.test/downloads/");
        var json = JsonSerializer.Serialize(new[] { new { draft = false, prerelease = true, tag_name = "v0.2.0-beta.12", assets = new[] {
            new { name = "Install-VrcSetup-0.2.0-beta.12-win-x64.exe", browser_download_url = "https://cdn.example.test/downloads/v12/Install-VrcSetup-0.2.0-beta.12-win-x64.exe" },
            new { name = "SHA256SUMS.txt", browser_download_url = "https://cdn.example.test/downloads/v12/SHA256SUMS.txt" } } } });
        using var client = new HttpClient(new Handler(request => { Assert.Equal(source.Feed, request.RequestUri); return new(HttpStatusCode.OK) { Content = new StringContent(json) }; }));
        var release = await new ApplicationUpdates(client, source).CheckAsync("0.2.0-beta.11", true);
        Assert.Equal("0.2.0-beta.12", release!.Version);
        Assert.Null(ApplicationUpdates.SelectRelease(json, "0.2.0-beta.11", true));
        Assert.False(source.AllowsAsset("https://cdn.example.test.evil/downloads/evil.exe", out _));
        Assert.False(source.AllowsAsset("https://cdn.example.test/downloads/../evil.exe", out _));
    }
    [Theory] [InlineData("http://updates.example.test/")] [InlineData("https://user@updates.example.test/")]
    public void BackendConfigurationRequiresHttpsWithoutCredentials(string uri)
    { Assert.Throws<ArgumentException>(() => new ApplicationUpdateSource(uri, ApplicationUpdates.Repository, "https://cdn.example.test/downloads/")); }
    [Theory] [InlineData(true, false, true)] [InlineData(false, false, false)] [InlineData(true, true, false)]
    public async Task PrefetchRequiresVerifiedExplicitCapability(bool support, bool wrongVersion, bool expected)
    {
        var root = ApplicationUpdates.Repository + "/releases/download/v0.2.0-beta.16/";
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new { Version = wrongVersion ? "0.2.0-beta.15" : "0.2.0-beta.16", Prefetch = support });
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(metadata));
        using var client = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.LocalPath.EndsWith("components.json") ? new ByteArrayContent(metadata) : new StringContent(checksum + "  components.json") }));
        var release = new ApplicationRelease("0.2.0-beta.16", new(root + "Install-VrcSetup-0.2.0-beta.16-win-x64.exe"), new(root + "SHA256SUMS.txt"), new(ApplicationUpdates.Repository), Components: new(root + "components.json"));
        Assert.Equal(expected, await new ApplicationUpdates(client).SupportsPrefetchAsync(release));
        Assert.False(await new ApplicationUpdates(client).SupportsPrefetchAsync(release with { Components = null }));
        using var bad = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = request.RequestUri!.LocalPath.EndsWith("components.json") ? new ByteArrayContent(metadata) : new StringContent(new string('0', 64) + "  components.json") }));
        await Assert.ThrowsAsync<IOException>(() => new ApplicationUpdates(bad).SupportsPrefetchAsync(release));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request)); }
}
