using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class InstallationRetentionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VrcSetup-retention-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(root, "VrcSetup");
    private InstallationManager Manager => new(AppPaths.ForInstallation(Path.Combine(root, "home"), Target));
    private async Task<string> PayloadAsync(string folder, string? previous = null, string? recordRoot = null, string? home = null)
    {
        Directory.CreateDirectory(folder);
        var hashes = new Dictionary<string, string>();
        foreach (var name in new[] { "app/cli/vrcsetup.exe", "app/desktop/VrcSetup.Desktop.exe", "runtime.dll" })
        {
            var file = Path.Combine(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "Fixture only: " + name); hashes[name] = AtomicFile.Hash(file);
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, ".vrcsetup-payload.json"), new PayloadManifest("0.2.0-beta.26", hashes));
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, ".vrcsetup-install.json"), new InstallationRecord("0.2.0-beta.26", recordRoot ?? Target, DateTime.UtcNow, previous, home));
        return folder;
    }
    private string Snapshot(string kind = "previous") => Target + "." + kind + "-" + Guid.NewGuid().ToString("N");

    [Fact] public async Task KeepsOnlyTheCurrentDirectRollbackAndCanRepeatAcrossUpdates()
    {
        var keep = await PayloadAsync(Snapshot());
        await PayloadAsync(Target, keep);
        var obsolete = new List<string>();
        for (var i = 0; i < 8; i++) obsolete.Add(await PayloadAsync(Snapshot()));
        var failed = await PayloadAsync(Snapshot("failed"));
        Assert.Equal(9, await Manager.CleanupPreviousInstallsAsync(Target));
        Assert.True(Directory.Exists(keep)); Assert.All(obsolete, x => Assert.False(Directory.Exists(x)));
        Assert.False(Directory.Exists(failed)); Assert.True(File.Exists(Path.Combine(Target, "runtime.dll")));
        Assert.Equal(0, await Manager.CleanupPreviousInstallsAsync(Target));
        var next = Snapshot(); Directory.Move(Target, next);
        await PayloadAsync(Target, next);
        Assert.Equal(1, await Manager.CleanupPreviousInstallsAsync(Target));
        Assert.True(Directory.Exists(next)); Assert.False(Directory.Exists(keep));
    }
    [Fact] public async Task PreservesForeignUnrecognizedAndPortableData()
    {
        await PayloadAsync(Target);
        var foreign = await PayloadAsync(Snapshot(), recordRoot: Path.Combine(root, "AnotherApp"));
        var unknown = Snapshot(); Directory.CreateDirectory(unknown); await File.WriteAllTextAsync(Path.Combine(unknown, "user.txt"), "Unique data");
        var nonSnapshot = await PayloadAsync(Target + ".previous-not-a-guid");
        var portable = await PayloadAsync(Snapshot(), home: Path.Combine(Target, "data"));
        Directory.CreateDirectory(Path.Combine(portable, "data")); await File.WriteAllTextAsync(Path.Combine(portable, "data", "config.json"), "Keep");
        var oldPortable = await PayloadAsync(Snapshot()); Directory.CreateDirectory(Path.Combine(oldPortable, "data"));
        Assert.Equal(0, await Manager.CleanupPreviousInstallsAsync(Target));
        foreach (var path in new[] { foreign, unknown, nonSnapshot, portable, oldPortable }) Assert.True(Directory.Exists(path));
        Assert.Equal("Keep", await File.ReadAllTextAsync(Path.Combine(portable, "data", "config.json")));
    }
    [Fact] public async Task InvalidCurrentPayloadCannotDeleteItsRecovery()
    {
        await PayloadAsync(Target); var backup = await PayloadAsync(Snapshot());
        await File.WriteAllTextAsync(Path.Combine(Target, "runtime.dll"), "Broken current install");
        await Assert.ThrowsAsync<IOException>(() => Manager.CleanupPreviousInstallsAsync(Target));
        Assert.True(Directory.Exists(backup));
    }
    [Fact] public async Task ReadOnlyGeneratedFilesDoNotPreventCleanup()
    {
        await PayloadAsync(Target); var obsolete = await PayloadAsync(Snapshot());
        foreach (var file in Directory.EnumerateFiles(obsolete, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        Assert.Equal(1, await Manager.CleanupPreviousInstallsAsync(Target)); Assert.False(Directory.Exists(obsolete));
    }
    [Fact] public async Task MalformedRollbackPointerFailsClosed()
    {
        await PayloadAsync(Target, Path.Combine(root, "unrelated")); var backup = await PayloadAsync(Snapshot());
        Assert.Equal(0, await Manager.CleanupPreviousInstallsAsync(Target)); Assert.True(Directory.Exists(backup));
    }
    [Fact] public async Task LockedObsoleteFileDoesNotBreakInstallAndIsRetried()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows delete sharing is the contract under test.
        await PayloadAsync(Target); var obsolete = await PayloadAsync(Snapshot());
        using (var held = new FileStream(Path.Combine(obsolete, "runtime.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(0, await Manager.CleanupPreviousInstallsAsync(Target));
            Assert.True(File.Exists(Path.Combine(obsolete, ".vrcsetup-install.json")));
        }
        Assert.Equal(1, await Manager.CleanupPreviousInstallsAsync(Target)); Assert.False(Directory.Exists(obsolete));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
