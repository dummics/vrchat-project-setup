using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class UpdateCheckDeferredException(DateTimeOffset retryAt) : IOException("Update checks are temporarily paused. Please try again later.")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

// Shared by manual and scheduled checks. Persisting the quiet period also prevents
// repeated app launches from immediately spending another GitHub API request.
internal static class UpdateFeed
{
    private sealed record State(string Feed, string? Json, string? ETag, DateTimeOffset FreshUntil, DateTimeOffset RetryAt);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public static async Task<string?> ReadAsync(HttpClient client, HttpRequestMessage request, string? directory, CancellationToken token)
    {
        var key = directory is null ? Guid.NewGuid().ToString() : Path.GetFullPath(directory);
        var gate = directory is null ? new SemaphoreSlim(1) : Locks.GetOrAdd(key, _ => new(1));
        await gate.WaitAsync(token);
        try
        {
            var file = directory is null ? null : Path.Combine(directory, "feed-cache.json");
            State? saved = null;
            if (file is not null)
                try { saved = await AtomicFile.ReadJsonAsync<State>(file, token); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            var feed = request.RequestUri!.AbsoluteUri;
            if (saved?.Feed != feed) saved = null;
            var now = DateTimeOffset.UtcNow;
            if (saved?.RetryAt > now) throw new UpdateCheckDeferredException(saved.RetryAt);
            if (saved?.FreshUntil > now && saved.Json is not null) return saved.Json;
            if (saved?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await client.SendAsync(request, token);
            var exhausted = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0";
            var limited = response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode == HttpStatusCode.Forbidden && (exhausted || response.Headers.Contains("Retry-After") ||
                    (await response.Content.ReadAsStringAsync(token)).Contains("rate limit", StringComparison.OrdinalIgnoreCase));
            if (limited)
            {
                var retry = response.Headers.RetryAfter?.Date ?? now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
                if (exhausted && response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) && long.TryParse(resets.FirstOrDefault(), out var seconds))
                    try { var reset = DateTimeOffset.FromUnixTimeSeconds(seconds); if (reset > retry) retry = reset; } catch (ArgumentOutOfRangeException) { }
                if (retry <= now) retry = now.AddMinutes(1);
                retry = retry.AddSeconds(5);
                await SaveAsync(file, new(feed, saved?.Json, saved?.ETag, default, retry), token);
                throw new UpdateCheckDeferredException(retry);
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode != HttpStatusCode.NotModified) response.EnsureSuccessStatusCode();
            var json = response.StatusCode == HttpStatusCode.NotModified && saved?.Json is { } cached
                ? cached : await response.Content.ReadAsStringAsync(token);
            using (var document = JsonDocument.Parse(json))
                if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a releases list.");
            await SaveAsync(file, new(feed, json, response.Headers.ETag?.ToString() ?? saved?.ETag, now.AddMinutes(10), default), token);
            return json;
        }
        finally { gate.Release(); if (directory is null) gate.Dispose(); }
    }
    private static async Task SaveAsync(string? file, State state, CancellationToken token)
    {
        if (file is null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); await AtomicFile.WriteJsonAsync(file, state, token); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
