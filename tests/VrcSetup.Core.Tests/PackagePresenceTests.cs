using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackagePresenceTests
{
    [Theory][InlineData("latest", true)][InlineData("1.14.8", false)]
    public async Task ResolveOwnsCompatibleDependencyAndDoesNotTriggerRepeatedDowngrades(string policy, bool success)
    {
        var workspace = new MemoryWorkspace(); var catalog = new FixtureCatalog(); catalog.Versions["nadena.dev.ndmf"] = ["1.14.8"];
        var plan = await new ProjectPlanner(workspace, catalog).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["nadena.dev.ndmf"] = policy }, new());
        var driver = new MemoryDriver(workspace) { AfterResolve = () =>
        {
            workspace.Project.InstalledVersions["nadena.dev.ndmf"] = "1.15.0-beta.2";
            workspace.Project.LockedPackages["nadena.dev.ndmf"] = "1.15.0-beta.2";
        } };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("ndmf", DateTime.UtcNow, [plan]));
        Assert.Equal(success, report.Success); Assert.Equal(2, driver.Calls);
    }
    [Fact] public async Task InstalledDependencyIsPresentAndCanBeMadeExplicitWithoutChangingVersion()
    {
        var workspace = new MemoryWorkspace();
        workspace.Project.InstalledVersions["dependency"] = "1.1.0";
        workspace.Project.LockedPackages["dependency"] = "1.1.0";
        var edit = new PackageEditSession([workspace.Project], new(), []);
        Assert.True(edit.Rows().Single(x => x.Id == "dependency").Included);
        var catalog = new FixtureCatalog(); catalog.Versions["dependency"] = ["1.1.0"];
        var plan = await new ProjectPlanner(workspace, catalog).PlanAsync(workspace.Project.Path,
            new Dictionary<string, string> { ["dependency"] = "latest" }, new());
        Assert.Equal(ChangeKind.Add, plan.Changes.Single(x => x.Id == "dependency").Kind);
        var report = await new ProjectExecutor(workspace, new MemoryDriver(workspace)).ApplyAsync(new("explicit", DateTime.UtcNow, [plan]));
        Assert.True(report.Success);
        Assert.True(workspace.Project.Packages.ContainsKey("dependency"));
        Assert.True(new PackageEditSession([workspace.Project], new(), []).Rows().Single(x => x.Id == "dependency").Included);
    }

    [Fact] public async Task GeneratedPresetPackageIsIgnoredWithoutChangingOriginalSelection()
    {
        var workspace = new MemoryWorkspace();
        var selection = new Dictionary<string, string> { ["com.vrcfury.temp"] = "latest", ["tool"] = "latest" };
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, selection, new());
        Assert.DoesNotContain("com.vrcfury.temp", plan.DesiredPolicy.Keys);
        Assert.DoesNotContain(plan.Changes, x => x.Id == "com.vrcfury.temp");
        Assert.True(selection.ContainsKey("com.vrcfury.temp"));
        Assert.False(PackagePresence.IsGenerated("my.valid.temp"));
    }

    [Theory]
    [InlineData("latest", "1.15.0-beta.2", "1.15.0-beta.2", true)]
    [InlineData("1.14.8", "1.15.0-beta.2", "1.15.0-beta.2", false)]
    [InlineData("latest", "1.15.0-beta.2", "1.14.8", false)]
    [InlineData("latest", "1.14.7", "1.14.7", true)]
    [InlineData("latest", "1.14.8", "1.15.0-beta.2", false)]
    public void VpmOwnsLatestResolutionWhilePinsAndPhysicalMetadataRemainStrict(string policy, string actual, string locked, bool matches)
    {
        var snapshot = new ProjectSnapshot { Path = Path.GetFullPath("fixture"), Name = "fixture", InstalledVersions = new() { ["nadena.dev.ndmf"] = actual }, LockedPackages = new() { ["nadena.dev.ndmf"] = locked } };
        Assert.Equal(matches, PackagePresence.Matches(new("nadena.dev.ndmf", null, policy, "1.14.8", ChangeKind.Add), snapshot));
    }
}
