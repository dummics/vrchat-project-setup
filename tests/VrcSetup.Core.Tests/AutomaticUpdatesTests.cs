using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class AutomaticUpdatesTests
{
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T18:00:00Z"); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vrcsetup-auto-" + Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public Clock Time { get; } = new();
        public AutomaticUpdates Updates { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, ".vrcsetup-install.json"), "{}");
            Paths = AppPaths.ForInstallation(Path.Combine(Root, "home"), Root); Updates = new(Paths, Time);
        }
        public async Task<string> Prepare(string version = "0.2.0-beta.16")
        {
            var installer = Path.Combine(Paths.Home, "updates", version, $"Install-VrcSetup-{version}-win-x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installer)!); await File.WriteAllTextAsync(installer, "verified download fixture");
            await Updates.SetReadyAsync(await Updates.ReadAsync(), version, installer); return installer;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    [Fact] public async Task SchedulePersistsAcrossRestartsAndBacksOffFailures()
    {
        using var f = new Fixture(); var state = await f.Updates.ReadAsync(); Assert.True(f.Updates.Due(state));
        await f.Updates.ReserveCheckAsync(state);
        Assert.InRange(state.NextCheckUtc, f.Time.Now.AddHours(4), f.Time.Now.AddHours(4).AddMinutes(15));
        var restarted = new AutomaticUpdates(f.Paths, f.Time); Assert.False(restarted.Due(await restarted.ReadAsync()));
        f.Time.Now = f.Time.Now.AddHours(5); Assert.True(restarted.Due(await restarted.ReadAsync()));
    }
    [Fact] public async Task ReadyUpdateIsRehashedBoundToInstallationAndConsumedOnce()
    {
        using var f = new Fixture(); await f.Prepare();
        Assert.NotNull(await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, true));
        Assert.Null(await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, true));
        await f.Prepare(); Assert.Null((await f.Updates.ReadAsync()).Ready);
    }
    [Fact] public async Task ModifiedInstallerNeverRuns()
    {
        using var f = new Fixture(); var path = await f.Prepare(); await File.AppendAllTextAsync(path, "corrupt");
        Assert.Null(await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, true));
    }
    [Theory] [InlineData(false, true, "0.2.0-beta.15", false)] [InlineData(true, false, "0.2.0-beta.15", false)]
    [InlineData(true, true, "0.2.0-beta.16", false)] [InlineData(true, true, "0.2.0-beta.17", false)]
    [InlineData(true, true, "0.2.0-beta.15", true)]
    public async Task PreferencesVersionAndExpiryControlAutomaticHandoff(bool enabled, bool beta, string current, bool expired)
    {
        using var f = new Fixture(); await f.Prepare(); if (expired) f.Time.Now = f.Time.Now.AddDays(3);
        Assert.Null(await f.Updates.TakeReadyAsync(current, enabled, beta));
    }
    [Fact] public async Task LatestPreparedVersionSupersedesIntermediateAndStableChannelWorks()
    {
        using var f = new Fixture(); await f.Prepare(); await f.Prepare("0.2.1");
        Assert.Equal("0.2.1", (await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, false))!.Version);
    }
    [Fact] public async Task PortableCopyAndConcurrentPreparationCannotAutoInstall()
    {
        using var f = new Fixture(); await f.Prepare();
        using (var lease = f.Updates.TryAcquire()) { Assert.NotNull(lease); Assert.Null(f.Updates.TryAcquire()); Assert.Null(await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, true)); }
        File.Delete(Path.Combine(f.Root, ".vrcsetup-install.json")); Assert.Null(await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, true));
    }
    [Fact] public async Task WrongDirectoryAndDifferentInstallationAreRejected()
    {
        using var f = new Fixture(); await Assert.ThrowsAsync<IOException>(() => f.Updates.SetReadyAsync(new(), "0.2.0-beta.16", Path.Combine(f.Root, "outside.exe")));
        await f.Prepare(); var other = Path.Combine(f.Root, "other"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, ".vrcsetup-install.json"), "{}");
        Assert.Null(await new AutomaticUpdates(AppPaths.ForInstallation(f.Paths.Home, other), f.Time).TakeReadyAsync("0.2.0-beta.15", true, true));
    }
    [Fact] public async Task CorruptStateDoesNotBlockStartup()
    {
        using var f = new Fixture(); Directory.CreateDirectory(Path.Combine(f.Paths.Home, "updates")); await File.WriteAllTextAsync(Path.Combine(f.Paths.Home, "updates", "automatic.json"), "{");
        Assert.Null(await f.Updates.TakeReadyAsync("0.2.0-beta.15", true, true)); Assert.True(f.Updates.Due(await f.Updates.ReadAsync()));
    }
    [Fact] public async Task BootstrapperRetentionDoesNotTouchComponentCacheOrOtherFiles()
    {
        using var f = new Fixture(); var cache = Path.Combine(f.Paths.Home, "updates", "components"); Directory.CreateDirectory(cache);
        var sentinel = Path.Combine(cache, "keep.zip"); await File.WriteAllTextAsync(sentinel, "cache");
        for (var n = 16; n <= 20; n++) await f.Prepare("0.2.0-beta." + n);
        Assert.False(File.Exists(Path.Combine(f.Paths.Home, "updates", "0.2.0-beta.16", "Install-VrcSetup-0.2.0-beta.16-win-x64.exe")));
        Assert.True(File.Exists((await f.Updates.ReadAsync()).Ready!.Installer)); Assert.True(File.Exists(sentinel));
    }
}
