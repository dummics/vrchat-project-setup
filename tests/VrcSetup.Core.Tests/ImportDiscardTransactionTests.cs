using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ImportDiscardTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-discard-record-" + Guid.NewGuid().ToString("N"));

    [Fact] public async Task RecordCanBeReadAfterTargetDisappearsButMismatchedBindingIsRejected()
    {
        var paths = new AppPaths(Path.Combine(root, "home"));
        var source = new CreationRequest("avatar.unitypackage", "Avatar", Path.Combine(root, "project"), "Unity.exe", "StandaloneWindows64", [], [])
            { CreationId = Guid.NewGuid().ToString("N") };
        var plan = new ProjectPlan { Name = source.Name, Path = source.TargetPath, ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [], Creation = source };
        var job = new ImportJob(Guid.NewGuid().ToString("N"), new("plan", DateTime.UtcNow, [plan]), source, "Paused", "Paused", 0, "", "") { DiscardRequested = true };
        var identity = new ProjectDirectoryIdentity(1, 2, 3);
        var state = new SetupState { ProjectName = source.Name, CreationId = source.CreationId,
            CreatedTargetPath = source.TargetPath, CreatedDirectoryIdentity = identity };
        var prepared = await ImportDiscardTransaction.PrepareAsync(paths, job, state, identity);
        Assert.False(Directory.Exists(source.TargetPath));
        Assert.Equal(prepared, await ImportDiscardTransaction.ReadAsync(paths, job));
        Assert.Equal("Prepared", prepared.Phase); // Missing target alone is not a completed removal.
        await Assert.ThrowsAnyAsync<IOException>(() => ImportDiscardTransaction.ReadAsync(paths,
            job with { Source = source with { CreationId = Guid.NewGuid().ToString("N") } }));
        await Assert.ThrowsAnyAsync<IOException>(() => ImportDiscardTransaction.PrepareAsync(paths, job, state, identity with { Volume = 7 }));
    }

    [Fact] public void RecordPathsRequireAnImportIdentity()
    {
        var paths = new AppPaths(root);
        Assert.Throws<ArgumentException>(() => ImportDiscardTransaction.FilePath(paths, "../outside"));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
