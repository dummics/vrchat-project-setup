using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageMigrationTests
{
    [Fact] public void ReplacementConflictsAndOpenUnityHaveActionableMessages()
    {
        var conflict = new InvalidOperationException("These packages cannot be installed together: shader.pro, shader.editor. Choose the package you want to keep.");
        Assert.True(UserErrors.IsExpected(conflict));
        Assert.Equal(string.Format(UiText.Text("{0} replaces {1}. Choose which package to keep before applying changes."), "Pro", "Editor"), UserErrors.Message(conflict, id => id == "shader.pro" ? "Pro" : "Editor"));
        Assert.Contains("Avatar", UserErrors.Message(new ProjectsInUseException(["Avatar"])));
    }
    [Fact] public async Task RemovingAnAbsentPackageProducesNoPhantomRemoval()
    {
        var workspace = new MemoryWorkspace();
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string>(), new(), remove: ["absent"]);
        Assert.DoesNotContain(plan.Changes, x => x.Id == "absent");
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ReplacementsFollowMetadataOfTheSelectedVersion(bool bundlesEditor)
    {
        var workspace = new MemoryWorkspace(); var catalog = new FixtureCatalog();
        workspace.Project.Packages["shader.free"] = "1.0.0"; workspace.Project.InstalledVersions["shader.free"] = "1.0.0";
        workspace.Project.Packages["shader.editor"] = "1.0.0"; workspace.Project.InstalledVersions["shader.editor"] = "1.0.0";
        catalog.Versions["shader.free"] = ["1.0.0"]; catalog.Versions["shader.editor"] = ["1.0.0"]; catalog.Versions["shader.pro"] = ["1.0.0", "2.0.0"];
        catalog.Metadata["shader.free@1.0.0"] = new(["shader.pro"], []);
        catalog.Metadata["shader.pro@1.0.0"] = new(["shader.free"], ["Assets/OldShader"]);
        catalog.Metadata["shader.pro@2.0.0"] = new(["shader.free", "shader.editor"], ["Assets/OldShader"]);
        var plan = await new ProjectPlanner(workspace, catalog).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["shader.pro"] = bundlesEditor ? "2.0.0" : "1.0.0" }, new());
        Assert.DoesNotContain("shader.free", plan.DesiredPolicy.Keys);
        Assert.Equal(!bundlesEditor, plan.DesiredPolicy.ContainsKey("shader.editor"));
        Assert.Contains(plan.Changes, x => x.Id == "shader.free" && x.Kind == ChangeKind.Remove);
        Assert.Contains("Assets/OldShader", plan.LegacyAssetPaths);
    }
    [Fact] public async Task MutuallyReplacingExplicitSelectionsFailBeforeMutation()
    {
        var workspace = new MemoryWorkspace(); var catalog = new FixtureCatalog();
        catalog.Metadata["tool@1.1.0"] = new(["custom"], []); catalog.Metadata["custom@1.0.0"] = new(["tool"], []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectPlanner(workspace, catalog).PlanAsync(workspace.Project.Path,
            new Dictionary<string, string> { ["tool"] = "1.1.0", ["custom"] = "1.0.0" }, new()));
        Assert.Equal(0, workspace.Backups);
    }
    [Fact] public async Task MissingLockNeverUsesHistoricalVersionForDowngrade()
    {
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-missing-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Assets")); Directory.CreateDirectory(Path.Combine(root, "Packages"));
            await AtomicFile.WriteJsonAsync(Path.Combine(root, "Packages", "vpm-manifest.json"), new { dependencies = new Dictionary<string, object>(), locked = new Dictionary<string, object>() });
            string[]? command = null;
            var runner = new ToolRunner(bundledPackageTool: "fixture-vrc-get.exe", commandOverride: (_, args, _) => { command = args; return Task.FromResult(new ToolResult(0, "", "")); });
            var paths = new AppPaths(Path.Combine(root, "home"));
            await new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths)).ChangePackageAsync(root, new("shader.pro", "11.0.38", "10.0.11", "10.0.11", ChangeKind.Update), null, default);
            Assert.Equal("install", command![0]);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task OneWayReplacementNeverSilentlyDropsAnExplicitSelection(bool selectBoth)
    {
        var workspace = new MemoryWorkspace(); var catalog = new FixtureCatalog();
        catalog.Metadata["tool@1.0.0"] = new(["custom"], []);
        var selected = new Dictionary<string, string> { ["custom"] = "1.0.0" };
        if (selectBoth) selected["tool"] = "1.0.0";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectPlanner(workspace, catalog).PlanAsync(workspace.Project.Path, selected, new()));
    }
}
