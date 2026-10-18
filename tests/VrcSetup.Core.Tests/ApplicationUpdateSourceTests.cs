using System.Net;
using System.Text.Json;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ApplicationUpdateSourceTests
{
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
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request)); }
}
