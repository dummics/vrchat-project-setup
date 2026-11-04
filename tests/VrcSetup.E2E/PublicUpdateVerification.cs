using System.Security.Cryptography;
using VrcSetup.Infrastructure;

// Opt-in network check: never executes the downloaded installer or changes an app.
internal static class PublicUpdateVerification
{
    public static async Task RunAsync(string previous, string expected, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a new evidence directory; existing results are preserved.");
        Directory.CreateDirectory(evidence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var service = new ApplicationUpdates(client, cacheDirectory: Path.Combine(evidence, "cache"));
        var release = await service.CheckAsync(previous, true, timeout.Token);
        if (release?.Version != expected) throw new IOException($"Expected {expected}, received {release?.Version ?? "no update"}.");
        if ((await service.CheckAsync(previous, true, timeout.Token))?.Version != expected)
            throw new IOException("The cached feed did not preserve the available update.");
        if (await service.CheckAsync(expected, true, timeout.Token) is not null)
            throw new IOException("The installed version should not offer itself as an update.");
        if (await service.CheckAsync("99.0.0", true, timeout.Token) is not null)
            throw new IOException("The updater must not offer a downgrade.");
        var stable = await service.CheckAsync(previous, false, timeout.Token);
        if (stable?.Version == expected && new VrcSetup.Core.SemanticVersion(expected).IsPrerelease)
            throw new IOException("A beta was offered with beta updates disabled.");
        var beforeDownload = handler.Requests.Count;
        if (beforeDownload != 1) throw new IOException("Repeated checks did not reuse the feed cache.");
        if (!await service.SupportsPrefetchAsync(release, timeout.Token))
            throw new IOException("Published background preparation metadata is unavailable or invalid.");
        var installer = await service.DownloadAsync(release, Path.Combine(evidence, "download"), token: timeout.Token);
        if (handler.Requests.Any(x => new Uri(x).Host == "api.github.com"))
            throw new IOException("Static update discovery unexpectedly used the GitHub API.");
        await using var installerStream = File.OpenRead(installer);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, timeout.Token));
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "public-update.json"), new
        {
            Passed = true, Previous = previous, Expected = expected, FeedRequests = beforeDownload,
            CachedDetection = true, NoSameVersionUpdate = true, NoDowngrade = true, BetaFilter = true,
            PrefetchMetadataVerified = true, InstallerChecksumVerified = true, InstallerExecuted = false,
            Sha256 = sha256,
            Requests = handler.Requests
        }, timeout.Token);
        Console.WriteLine("PASS: public feed, cached checks, version/channel filtering, preparation metadata and installer checksum.");
    }

    private sealed class RecordingHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests.Add(request.RequestUri!.AbsoluteUri); return base.SendAsync(request, token); }
    }
}
