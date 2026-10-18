using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class BatchEditingTests
{
    [Fact] public async Task LatestPolicyDoesNotReinstallTheVersionAlreadyInstalled()
    {
        var workspace = new MemoryWorkspace(); workspace.Project.InstalledVersions["tool"] = "1.1.0";
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        Assert.Equal(ChangeKind.Keep, plan.Changes.Single(x => x.Id == "tool").Kind);
        Assert.Equal("latest", plan.DesiredPolicy["tool"]);
    }
    [Fact] public void BatchReviewShowsCommonTransitionsOnceAndKeepsVersionDifferencesSeparate()
    {
        var shared = new PackageChange("added", null, "latest", "1.2.0", ChangeKind.Add);
        ProjectPlan Project(string name, string old) => new() { Name = name, Path = Path.GetFullPath(name), ExpectedFingerprint = "fixture", DesiredPolicy = new Dictionary<string, string>(), Changes = [shared, new("sdk", old, "3.2.0", "3.2.0", ChangeKind.Update), new("unchanged", "1.0.0", "latest", "1.0.0", ChangeKind.Keep)] };
        var groups = PackageReview.Group(new("batch", DateTime.UtcNow, [Project("PC", "3.0.0"), Project("Quest", "3.1.0")]));
        Assert.Equal(3, groups.Length); Assert.Equal(2, groups[0].Projects.Length);
        Assert.Single(groups.SelectMany(x => x.Changes), x => x.Id == "added");
        Assert.Equal(2, groups.Where(x => x.Projects.Length == 1).Count());
        Assert.DoesNotContain(groups.SelectMany(x => x.Changes), x => x.Kind == ChangeKind.Keep);
    }
    [Fact] public void ProjectSetIdentityDoesNotDependOnOrderOrCaseAndKeepsStagedIntent()
    {
        var a = new ProjectSnapshot { Name = "PC", Path = Path.GetFullPath("PC") };
        var b = new ProjectSnapshot { Name = "Quest", Path = Path.GetFullPath("Quest") };
        var edit = new PackageEditSession([a, b], new(), []); edit.SetVersion("tool", "1.2.0");
        Assert.True(edit.MatchesProjects([b, a with { Path = a.Path.ToUpperInvariant() }]));
        Assert.False(edit.MatchesProjects([a])); Assert.Equal("1.2.0", edit.Selected["tool"]);
    }
    [Fact] public async Task InstalledDisplayNameReplacesTechnicalFallbackInManagement()
    {
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets")); var folder = Path.Combine(root, "Packages", "com.creator.tool"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "package.json"), "{\"name\":\"com.creator.tool\",\"displayName\":\"Creator Tool\",\"version\":\"1.0.0\"}");
            var project = await new ProjectWorkspace(new AppPaths(Path.Combine(root, "home"))).ReadAsync(root);
            var edit = new PackageEditSession([project], new(), [new("com.creator.tool", "com.creator.tool", "1.0.0")]);
            Assert.Equal("Creator Tool", edit.Rows().Single(x => x.Id == "com.creator.tool").Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
