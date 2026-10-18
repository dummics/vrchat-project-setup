using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class UpdateFeedTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "vrcsetup-feed-" + Guid.NewGuid().ToString("N"));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
    [Theory]
    [InlineData(429)] [InlineData(403)]
    public async Task RateLimitWaitSurvivesServiceRecreationAndUsesServerDeadline(int status)
    {
        var calls = 0;
        var reset = DateTimeOffset.UtcNow.AddMinutes(20);
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString());
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        }));
        var first = await Assert.ThrowsAsync<UpdateCheckDeferredException>(() => new ApplicationUpdates(client, cacheDirectory: directory).CheckAsync("0.2.0", true));
        var second = await Assert.ThrowsAsync<UpdateCheckDeferredException>(() => new ApplicationUpdates(client, cacheDirectory: directory).CheckAsync("0.2.0", false));
        Assert.True(first.RetryAt >= reset);
        Assert.Equal(first.RetryAt, second.RetryAt);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task SecondaryLimitWithoutHeadersIsDeferredButPermissionErrorsRemainErrors()
    {
        using var limited = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("Secondary rate limit exceeded") }));
        await Assert.ThrowsAsync<UpdateCheckDeferredException>(() => new ApplicationUpdates(limited, cacheDirectory: directory).CheckAsync("0.2.0", true));
        using var forbidden = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("Access forbidden") }));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ApplicationUpdates(forbidden).CheckAsync("0.2.0", true));
    }
    [Fact]
    public async Task RepeatedAndConcurrentChecksReuseFreshFeed()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => { Interlocked.Increment(ref calls); return new(HttpStatusCode.OK) { Content = new StringContent("[]") }; }));
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => new ApplicationUpdates(client, cacheDirectory: directory).CheckAsync("0.2.0", true)));
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task ExpiredFeedRevalidatesWithETagAndAcceptsNotModified()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "feed-cache.json"), JsonSerializer.Serialize(new
        {
            Feed = ApplicationUpdateSource.Default.Feed.AbsoluteUri, Json = "[]", ETag = "\"fixture\"", FreshUntil = DateTimeOffset.UtcNow.AddMinutes(-1), RetryAt = DateTimeOffset.MinValue
        }));
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("\"fixture\"", request.Headers.IfNoneMatch.Single().ToString());
            return new(HttpStatusCode.NotModified);
        }));
        Assert.Null(await new ApplicationUpdates(client, cacheDirectory: directory).CheckAsync("0.2.0", true));
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
