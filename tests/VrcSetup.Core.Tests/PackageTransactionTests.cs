using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-transaction-" + Guid.NewGuid().ToString("N"));
    private ProjectPlan Plan(string project) => new() { Name = "Fixture", Path = project, ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string> { ["shader.pro"] = "2.0.0" },
        Changes = [new("shader.pro", null, "2.0.0", "2.0.0", ChangeKind.Add), new("shader.toon", "1.0.0", null, null, ChangeKind.Remove)], LegacyAssetPaths = ["Assets/OldShader"] };
    private async Task<string> Project()
    {
        var project = Path.Combine(root, "Project");
        Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "Packages", "shader.toon"));
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "shader.toon", "package.json"), new { name = "shader.toon", version = "1.0.0" });
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), new { dependencies = new Dictionary<string, object> { ["shader.toon"] = new { version = "1.0.0" } }, locked = new Dictionary<string, object> { ["shader.toon"] = new { version = "1.0.0" } } });
        return project;
    }
    [Fact] public async Task ReplacementInstallsAgainstOneCoherentRequestedSet()
    {
        var project = await Project(); var paths = new AppPaths(Path.Combine(root, "home")); var commands = new List<string[]>();
        var runner = new ToolRunner(bundledPackageTool: "fixture-vrc-get.exe", commandOverride: (_, args, _) =>
        {
            commands.Add(args);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "Packages", "vpm-manifest.json")));
            if (args[0] == "remove") Assert.True(manifest.RootElement.GetProperty("dependencies").TryGetProperty("shader.toon", out _));
            else Assert.False(manifest.RootElement.GetProperty("dependencies").TryGetProperty("shader.toon", out _));
            Assert.False(manifest.RootElement.GetProperty("dependencies").TryGetProperty("shader.pro", out _));
            return Task.FromResult(new ToolResult(0, "", ""));
        });
        await new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths)).ApplyPackagePlanAsync(Plan(project), null, default);
        var install = Assert.Single(commands, x => x[0] == "install"); Assert.Equal("shader.pro", install[1]);
        Assert.Equal("remove", commands[0][0]); Assert.Equal("resolve", commands[^1][0]);
    }
    [Fact] public async Task UnityGeneratedLockDoesNotInvalidateReviewButVpmIntentStillDoes()
    {
        var project = await Project(); var before = ProjectWorkspace.Fingerprint(project);
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "packages-lock.json"), "{\"dependencies\":{}}");
        Assert.Equal(before, ProjectWorkspace.Fingerprint(project));
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), "{\"dependencies\":{}}");
        Assert.NotEqual(before, ProjectWorkspace.Fingerprint(project));
    }
    [Fact] public async Task RecoveryRestoresPhysicalPackagesAndLegacyAssetsWithoutNetworkOrLogin()
    {
        var project = await Project(); var paths = new AppPaths(Path.Combine(root, "home"));
        var workspace = new ProjectWorkspace(paths) { Resolve = (_, _, _) => throw new IOException("Recovery must be offline.") };
        var shader = Path.Combine(project, "Assets", "shader.txt"); await File.WriteAllTextAsync(shader, "licensed local shader");
        // The root may have no migration metadata while an indirect dependency
        // migrates Assets. Recovery must still protect them.
        var backup = await workspace.BackupAsync(Plan(project) with { LegacyAssetPaths = [] }, default);
        Assert.NotNull(backup.AssetArchive); Assert.NotNull(backup.PackageArchive);
        Directory.Delete(Path.Combine(project, "Packages", "shader.toon"), true);
        Directory.CreateDirectory(Path.Combine(project, "Packages", "shader.pro"));
        await File.WriteAllTextAsync(shader, "changed");
        Assert.True(await workspace.RestoreAsync(project, backup, null, default));
        Assert.True(File.Exists(Path.Combine(project, "Packages", "shader.toon", "package.json")));
        Assert.False(Directory.Exists(Path.Combine(project, "Packages", "shader.pro")));
        Assert.Equal("licensed local shader", await File.ReadAllTextAsync(shader));
    }
    [Fact] public async Task VpmReplacementRunsWithAHeldUnityLockfileAndPreservesUnrelatedAssets()
    {
        var project = await Project(); var paths = new AppPaths(Path.Combine(root, "home"));
        Directory.CreateDirectory(Path.Combine(project, "Temp"));
        using var editor = new FileStream(Path.Combine(project, "Temp", "UnityLockfile"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var asset = Path.Combine(project, "Assets", "user-work.txt"); await File.WriteAllTextAsync(asset, "editor work");
        var workspace = new ProjectWorkspace(paths); Assert.True(workspace.IsBusy(project));
        var commands = new List<string[]>();
        var runner = new ToolRunner(bundledPackageTool: "fixture-vrc-get.exe", commandOverride: (_, args, _) =>
        {
            commands.Add(args);
            Assert.True(workspace.IsBusy(project));
            return Task.FromResult(new ToolResult(0, "", ""));
        });
        var result = await new UnityProjectDriver(paths, runner, workspace).ApplyPackagePlanAsync(Plan(project), null, default);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("remove", commands[0][0]); Assert.Contains(commands, x => x[0] == "install"); Assert.Equal("resolve", commands[^1][0]);
        Assert.Equal("editor work", await File.ReadAllTextAsync(asset));
    }
    [Fact] public async Task RepositoryMigrationRulesAreReadForTheExactVersion()
    {
        var repos = Path.Combine(root, "repos"); Directory.CreateDirectory(repos);
        await AtomicFile.WriteJsonAsync(Path.Combine(repos, "shader.json"), new { repo = new { packages = new Dictionary<string, object> { ["shader.pro"] = new { versions = new Dictionary<string, object> {
            ["1.0.0"] = new { legacyPackages = new[] { "shader.toon" } },
            ["2.0.0"] = new { legacyPackages = new[] { "shader.toon", "shader.editor" }, legacyFolders = new Dictionary<string, string> { ["Assets/OldShader"] = "guid" } }
        } } } } });
        var paths = new AppPaths(Path.Combine(root, "home")); var catalog = new PackageCatalog(paths, new ToolRunner(), new ConfigurationStore(paths), repos);
        Assert.Equal(["shader.toon"], (await catalog.GetMetadataAsync("shader.pro", "1.0.0")).Replaces);
        var newer = await catalog.GetMetadataAsync("shader.pro", "2.0.0");
        Assert.Contains("shader.editor", newer.Replaces); Assert.Contains("Assets/OldShader", newer.LegacyAssets);
    }
    [Fact] public async Task SuccessfulSnapshotsReplaceOlderArchivesButNeverFailedOrLegacyBackups()
    {
        var project = await Project(); var workspace = new ProjectWorkspace(new AppPaths(Path.Combine(root, "home")));
        var first = await workspace.BackupAsync(Plan(project), default); await workspace.ConfirmBackupAsync(first);
        var failed = await workspace.BackupAsync(Plan(project), default);
        var second = await workspace.BackupAsync(Plan(project), default); await workspace.ConfirmBackupAsync(second);
        Assert.False(File.Exists(first.PackageArchive)); Assert.True(File.Exists(second.PackageArchive)); Assert.True(File.Exists(failed.PackageArchive));
        Assert.True(File.Exists(Path.Combine(first.Directory, "superseded")));
        await Assert.ThrowsAsync<IOException>(() => workspace.RestoreAsync(project, first, null, default));
    }
    [Fact] public async Task OpenEditorMustAcknowledgeRefreshPauseAndLeaseIsReleasedAfterUse()
    {
        var project = await Project(); var paths = new AppPaths(Path.Combine(root, "home"));
        Directory.CreateDirectory(Path.Combine(project, "Temp"));
        using var editor = new FileStream(Path.Combine(project, "Temp", "UnityLockfile"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var driver = new UnityProjectDriver(paths, new ToolRunner(), new ProjectWorkspace(paths));
        var pending = driver.BeginPackageChangesAsync(Plan(project), null, timeout.Token);
        var leaseFile = Path.Combine(project, ".vrcsetup", "editor-package-lease.json");
        while (!File.Exists(leaseFile)) await Task.Delay(20, timeout.Token);
        Assert.False(pending.IsCompleted);
        using var request = JsonDocument.Parse(await File.ReadAllTextAsync(leaseFile, timeout.Token));
        Assert.True(request.RootElement.GetProperty("expiresUtcTicks").GetInt64() > DateTime.UtcNow.Ticks);
        await File.WriteAllTextAsync(Path.Combine(project, ".vrcsetup", "editor-package-ack.txt"), request.RootElement.GetProperty("id").GetString(), timeout.Token);
        await using (var lease = await pending) Assert.NotNull(lease);
        Assert.False(File.Exists(leaseFile));
    }
    [Fact] public async Task CancelBeforeEditorAcknowledgesLeavesPackagesUntouchedAndRemovesLease()
    {
        var project = await Project(); var paths = new AppPaths(Path.Combine(root, "home"));
        Directory.CreateDirectory(Path.Combine(project, "Temp"));
        using var editor = new FileStream(Path.Combine(project, "Temp", "UnityLockfile"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var manifest = await File.ReadAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UnityProjectDriver(paths, new ToolRunner(), new ProjectWorkspace(paths)).BeginPackageChangesAsync(Plan(project), null, cancel.Token));
        Assert.False(File.Exists(Path.Combine(project, ".vrcsetup", "editor-package-lease.json")));
        Assert.Equal(manifest, await File.ReadAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json")));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
