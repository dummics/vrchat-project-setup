using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PartialPlanningTests
{
    [Fact] public void PartialSnapshotCanBeReadBeforeUnityCreatesThePackagesFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-partial-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var snapshot = new VrcSetup.Infrastructure.ProjectWorkspace(new VrcSetup.Infrastructure.AppPaths(Path.Combine(root, "home"))).ReadPartial(root);
            Assert.Equal(root, snapshot.Path);
            Assert.Empty(snapshot.Packages);
            Assert.False(Directory.Exists(Path.Combine(root, "Packages")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public async Task ReviewedPartialSnapshotPlansWithoutReadingAnUninitializedUnityProject()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace);
        var source = new CreationRequest("", workspace.Project.Name, workspace.Project.Path, "Unity.exe", "StandaloneWindows64", [], [], true)
            { CreationId = Guid.NewGuid().ToString("N") };
        var initialization = new PartialProjectInitialization(source, "physical-identity", new Dictionary<string, string>());
        var planner = new ProjectPlanner(workspace, new FixtureCatalog());
        var plan = await planner.PlanAsync(workspace.Project.Path, workspace.Project.Packages, new AppConfiguration(),
            initialization: initialization, initializationSnapshot: workspace.Project);
        Assert.Equal(0, workspace.Reads);
        Assert.Null(plan.Creation);
        Assert.Equal(source, plan.RecoverySource);
        Assert.Equal(initialization, plan.Initialization);
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("partial", DateTime.UtcNow, [plan]));
        Assert.True(report.Success, string.Join("; ", report.Results.Select(x => x.Message)));
        Assert.Equal(1, driver.Initializations);
        Assert.True(driver.InitializationLeaseHeld);
        Assert.True(workspace.Completed);
        Assert.Equal(0, workspace.Backups);
    }

    [Fact] public async Task PartialInitializationCannotBorrowAnUnrelatedSnapshot()
    {
        var workspace = new MemoryWorkspace(); var planner = new ProjectPlanner(workspace, new FixtureCatalog());
        var source = new CreationRequest("", "Avatar", workspace.Project.Path, "Unity.exe", "StandaloneWindows64", [], [], true)
            { CreationId = Guid.NewGuid().ToString("N") };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => planner.PlanAsync(source.TargetPath, workspace.Project.Packages, new AppConfiguration(),
            initialization: new(source, "identity", new Dictionary<string, string>()),
            initializationSnapshot: workspace.Project with { Path = workspace.Project.Path + "-other" }));
        Assert.Equal(0, workspace.Reads);
    }
}
