using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using VrcSetup.Bootstrapper;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ComponentDeliveryTests
{
    [Fact] public async Task SuccessfulPreparationPrunesOldRecognizedArchivesAndKeepsCurrentAndUnrelatedFiles()
    {
        using var fixture = new Fixture(); using var client = fixture.Client(); var delivery = new ComponentDelivery(client);
        Directory.CreateDirectory(fixture.Cache);
        var old = Path.Combine(fixture.Cache, new string('a', 64) + ".zip"); await File.WriteAllTextAsync(old, "Obsolete component");
        var unrelated = Path.Combine(fixture.Cache, "owner-export.zip"); await File.WriteAllTextAsync(unrelated, "Owner file");
        await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.False(File.Exists(old)); Assert.Equal("Owner file", await File.ReadAllTextAsync(unrelated));
        Assert.All(fixture.Manifest.Components, x => Assert.True(File.Exists(Path.Combine(fixture.Cache, x.Name))));
    }
    [Fact] public async Task MeasuredProgressCountsMissingBytesAndPreparedFiles()
    {
        using var fixture = new Fixture(); using var client = fixture.Client(); var delivery = new ComponentDelivery(client);
        var reports = new List<VrcSetup.Core.SetupProgress>();
        var progress = new InlineProgress(reports);
        await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed, measured: progress);
        var downloads = reports.Where(x => x.Phase == "download").ToArray();
        var bytes = fixture.Manifest.Components.Sum(x => x.Bytes);
        Assert.NotEmpty(downloads); Assert.All(downloads, x => Assert.Equal(bytes, x.Total));
        Assert.Equal(bytes, downloads[^1].Completed);
        var files = reports.Last(x => x.Phase == "prepare-files");
        Assert.Equal(fixture.Manifest.Components.Sum(x => x.Files.Count), files.Total); Assert.Equal(files.Total, files.Completed);
        reports.Clear();
        await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed, measured: progress);
        Assert.DoesNotContain(reports, x => x.Phase == "download");
    }
    private sealed class InlineProgress(List<VrcSetup.Core.SetupProgress> reports) : IProgress<VrcSetup.Core.SetupProgress>
    { public void Report(VrcSetup.Core.SetupProgress value) => reports.Add(value); }

    [Fact] public async Task NextReleaseDownloadsOnlyTheChangedApplicationComponent()
    {
        using var fixture = new Fixture(); using var client = fixture.Client(); var delivery = new ComponentDelivery(client);
        await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        var application = fixture.Module(new() { ["payload/.vrcsetup-payload.json"] = "next manifest", ["payload/app.txt"] = "next application" });
        var next = fixture.Manifest with { Version = "0.2.0-beta.13", Components = [application, fixture.Manifest.Components[1]] };
        var result = await delivery.PrepareAsync(next, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.Equal(application.Bytes, result.DownloadedBytes); Assert.Equal(1, result.DownloadedComponents); Assert.Equal(1, result.CachedComponents);
    }
    [Fact] public async Task ColdInstallThenWarmUpdateDownloadsNoUnchangedComponents()
    {
        using var fixture = new Fixture(); using var client = fixture.Client(); var delivery = new ComponentDelivery(client);
        var cold = await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        var warm = await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.Equal(2, cold.DownloadedComponents); Assert.True(cold.DownloadedBytes > 0);
        Assert.Equal(0, warm.DownloadedBytes); Assert.Equal(2, warm.CachedComponents); Assert.Equal(2, fixture.Requests);
    }
    [Fact] public async Task VerifiedInstalledFilesAvoidDownloadEvenWithoutAnArchiveCache()
    {
        using var fixture = new Fixture(); using var client = fixture.Client();
        Directory.CreateDirectory(fixture.Installed);
        await File.WriteAllTextAsync(Path.Combine(fixture.Installed, ".vrcsetup-payload.json"), "manifest");
        await File.WriteAllTextAsync(Path.Combine(fixture.Installed, "app.txt"), "application");
        var result = await new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.Equal(1, result.ReusedComponents); Assert.Equal(1, result.DownloadedComponents);
    }
    [Fact] public async Task CorruptCacheAndModifiedInstalledFilesAreReplacedByVerifiedDownloads()
    {
        using var fixture = new Fixture(); using var client = fixture.Client(); var delivery = new ComponentDelivery(client);
        await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        await File.WriteAllTextAsync(Path.Combine(fixture.Cache, fixture.Manifest.Components[0].Name), "broken");
        Directory.CreateDirectory(fixture.Installed); await File.WriteAllTextAsync(Path.Combine(fixture.Installed, "app.txt"), "modified");
        var result = await delivery.PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.Equal(1, result.DownloadedComponents); Assert.Equal(1, result.CachedComponents); Assert.Equal(0, result.ReusedComponents);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CorruptOrTruncatedDownloadsNeverBecomeCachedOrChangeTheExistingInstallation(bool truncate)
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.Installed);
        var sentinel = Path.Combine(fixture.Installed, "personal.txt"); await File.WriteAllTextAsync(sentinel, "preserve");
        using var client = fixture.Client(bytes => truncate ? bytes[..(bytes.Length / 2)] : bytes.Select(x => (byte)(x ^ 42)).ToArray());
        await Assert.ThrowsAsync<IOException>(() => new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed));
        Assert.Empty(Directory.GetFiles(fixture.Cache, "*.zip")); Assert.Empty(Directory.GetFiles(fixture.Cache, "*.partial-*"));
        Assert.Equal("preserve", await File.ReadAllTextAsync(sentinel));
    }
    [Fact] public async Task CancellationDoesNotInstallAndDoesNotLeaveAnArchive()
    {
        using var fixture = new Fixture(); using var client = fixture.Client(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed, token: cancel.Token));
        Assert.False(Directory.Exists(fixture.Installed)); Assert.Empty(Directory.GetFiles(fixture.Cache, "*.zip"));
    }
    [Fact] public async Task ConnectionLostAfterWritingBytesCleansThePartialDownload()
    {
        using var fixture = new Fixture();
        using var client = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream(fixture.Assets[Path.GetFileName(request.RequestUri!.LocalPath)])) }));
        await Assert.ThrowsAsync<IOException>(() => new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed));
        Assert.Empty(Directory.GetFiles(fixture.Cache, "*.partial-*")); Assert.Empty(Directory.GetFiles(fixture.Cache, "*.zip")); Assert.False(Directory.Exists(fixture.Installed));
    }
    [Fact] public async Task TransientNetworkFailureRetriesTheSameImmutableComponent()
    {
        using var fixture = new Fixture(); var requests = 0;
        using var client = new HttpClient(new Handler(request => ++requests == 1 ? throw new HttpRequestException("temporary") : fixture.Response(request)));
        var result = await new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.Equal(2, result.DownloadedComponents); Assert.Equal(3, requests);
    }
    [Theory] [InlineData("http://example.test/")] [InlineData("https://user@example.test/")] [InlineData("https://example.test/no-slash")] [InlineData("https://example.test/?q=1")]
    public void RejectsUnsafeHostingConfiguration(string basis)
    { using var fixture = new Fixture(); Assert.Throws<IOException>(() => ComponentDelivery.Validate(fixture.Manifest with { BaseUri = basis })); }
    [Theory] [InlineData("payload/../escape")] [InlineData("payload/C:/escape")] [InlineData("payload\\escape")] [InlineData("engine/file. ")]
    public void RejectsPathTraversalAndWindowsPathAliases(string path)
    {
        using var fixture = new Fixture(); fixture.Manifest.Components[0].Files.Add(path, new string('a', 64));
        Assert.Throws<IOException>(() => ComponentDelivery.Validate(fixture.Manifest));
    }
    [Fact] public void RejectsOverlappingComponentsAndUnknownSchemas()
    {
        using var fixture = new Fixture(); Assert.Throws<IOException>(() => ComponentDelivery.Validate(fixture.Manifest with { Schema = 2 }));
        fixture.Manifest.Components.Add(fixture.Manifest.Components[0]); Assert.Throws<IOException>(() => ComponentDelivery.Validate(fixture.Manifest));
    }
    [Fact] public async Task ArchiveCannotWriteFilesNotDeclaredInThePinnedManifest()
    {
        using var fixture = new Fixture(); var malicious = Fixture.Archive(new() { ["../escape"] = "evil" });
        var hash = Convert.ToHexString(SHA256.HashData(malicious)).ToLowerInvariant();
        var module = fixture.Manifest.Components[0] with { Name = hash + ".zip", Sha256 = hash, Bytes = malicious.Length };
        fixture.Manifest.Components[0] = module; fixture.Assets[module.Name] = malicious;
        using var client = fixture.Client();
        await Assert.ThrowsAsync<IOException>(() => new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escape")));
    }
    [Fact] public async Task UnusedOldCacheIsPrunedButCurrentComponentsAreRetained()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.Cache);
        var obsolete = Path.Combine(fixture.Cache, new string('f', 64) + ".zip"); await File.WriteAllTextAsync(obsolete, "obsolete"); File.SetLastWriteTimeUtc(obsolete, DateTime.UtcNow.AddDays(-40));
        using var client = fixture.Client(); await new ComponentDelivery(client).PrepareAsync(fixture.Manifest, fixture.Stage(), fixture.Cache, fixture.Installed);
        Assert.False(File.Exists(obsolete)); Assert.Equal(2, Directory.GetFiles(fixture.Cache, "*.zip").Length);
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VrcSetup-delivery-tests-" + Guid.NewGuid().ToString("N"));
        public string Cache => Path.Combine(Root, "cache"); public string Installed => Path.Combine(Root, "installed");
        public string Stage() => Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public Dictionary<string, byte[]> Assets { get; } = new(); public int Requests;
        public ComponentManifest Manifest { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Manifest = new(1, "0.2.0-beta.12", "https://assets.example.test/releases/v12/", [Module(new() { ["payload/.vrcsetup-payload.json"] = "manifest", ["payload/app.txt"] = "application" }), Module(new() { ["engine/VrcSetupInstaller.exe"] = "fixture, never executed" })]);
        }
        public Component Module(Dictionary<string, string> files)
        {
            var bytes = Archive(files); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); Assets[hash + ".zip"] = bytes;
            return new(hash + ".zip", hash, bytes.Length, files.ToDictionary(x => x.Key, x => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.Value)))));
        }
        public static byte[] Archive(Dictionary<string, string> files)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var file in files) { using var output = zip.CreateEntry(file.Key).Open(); output.Write(Encoding.UTF8.GetBytes(file.Value)); }
            return memory.ToArray();
        }
        public HttpResponseMessage Response(HttpRequestMessage request, Func<byte[], byte[]>? transform = null)
        { Requests++; var bytes = Assets[Path.GetFileName(request.RequestUri!.LocalPath)]; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(transform?.Invoke(bytes) ?? bytes) }; }
        public HttpClient Client(Func<byte[], byte[]>? transform = null) => new(new Handler(request => Response(request, transform)));
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request)); }
    private sealed class BrokenStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position > 0) throw new IOException("Connection lost mid-stream.");
            return base.ReadAsync(buffer[..Math.Min(16, buffer.Length)], cancellationToken);
        }
    }
}
