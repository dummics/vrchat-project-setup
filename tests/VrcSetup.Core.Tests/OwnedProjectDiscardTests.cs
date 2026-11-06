using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class OwnedProjectDiscardTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-owned-discard-" + Guid.NewGuid().ToString("N"));

    [Fact] public async Task OwnedProjectRemovalRecordsCompletionAndCanBeRepeated()
    {
        if (!OperatingSystem.IsWindows()) return;
        var paths = new AppPaths(Path.Combine(root, "home"));
        var projects = Path.Combine(root, "projects"); var target = Path.Combine(projects, "Avatar");
        Directory.CreateDirectory(Path.Combine(target, "Assets", "Nested"));
        await File.WriteAllTextAsync(Path.Combine(target, "Assets", "Nested", "fixture.txt"), "fixture");
        var source = new CreationRequest("avatar.unitypackage", "Avatar", target, "", "StandaloneWindows64", [], [])
            { CreationId = Guid.NewGuid().ToString("N") };
        var identity = ProjectDirectoryIdentity.TryRead(target);
        Assert.NotNull(identity);
        await AtomicFile.WriteJsonAsync(Path.Combine(target, ".vrcsetup", "state.json"), new SetupState
        { ProjectName = source.Name, CreationId = source.CreationId, CreatedTargetPath = target, CreatedDirectoryIdentity = identity });
        var plan = new ProjectPlan { Name = source.Name, Path = target, ExpectedFingerprint = "", DesiredPolicy = [], Changes = [], Creation = source };
        var job = new ImportJob(Guid.NewGuid().ToString("N"), new("plan", DateTime.UtcNow, [plan]), source, "Paused", "Paused", 0, "", "") { DiscardRequested = true };
        var service = new OwnedProjectDiscard(paths, new ProjectWorkspace(paths));
        await Assert.ThrowsAnyAsync<IOException>(() => service.RemoveAsync(job with { Source = source with { CreationId = null } }, projects));
        Assert.True(File.Exists(Path.Combine(target, "Assets", "Nested", "fixture.txt")));
        await service.RemoveAsync(job, projects);
        Assert.False(Directory.Exists(target));
        Assert.Equal("Removed", (await ImportDiscardTransaction.ReadAsync(paths, job))!.Phase);
        await service.RemoveAsync(job, projects);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "replacement.txt"), "preserve");
        await Assert.ThrowsAnyAsync<IOException>(() => service.RemoveAsync(job, projects));
        Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(target, "replacement.txt")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
