using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PlannerExecutorTests
{
    private sealed class CaptureProgress(List<OperationEvent> events) : IProgress<OperationEvent>
    { public void Report(OperationEvent value) => events.Add(value); }
    [Fact] public async Task PackageExecutionPublishesStructuredActionAndVersion()
    {
        var workspace = new MemoryWorkspace();
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var events = new List<OperationEvent>();
        var report = await new ProjectExecutor(workspace, new MemoryDriver(workspace)).ApplyAsync(new("progress", DateTime.UtcNow, [project]), new CaptureProgress(events));
        Assert.True(report.Success);
        var package = Assert.Single(events, x => x.Package?.Id == "tool");
        Assert.Equal(project.Path, package.Project); Assert.Equal("1.1.0", package.Package!.ResolvedVersion);
        Assert.Equal(project.Changes.Single(x => x.Id == "tool").Kind, package.Package.Kind);
    }
    [Fact] public async Task EmptyProjectHasNoSourceArchiveButKeepsPackagePlanning()
    {
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-empty-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "New project");
        var request = new CreationRequest("", "New project", target, "Unity.exe", "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var planner = new ProjectPlanner(new MemoryWorkspace(), new FixtureCatalog());
        var selected = new Dictionary<string, string> { [ProjectRules.BaseSdk] = "latest", [ProjectRules.AvatarSdk] = "latest", [ProjectRules.Resolver] = "latest" };
        var plan = await planner.PlanAsync(target, selected, new() { UnityProjectsRoot = root }, exact: true, creation: request);
        Assert.True(plan.Creation!.EmptyProject); Assert.Empty(plan.ImportFingerprints); Assert.Contains(plan.Changes, x => x.Id == ProjectRules.AvatarSdk);
        await Assert.ThrowsAsync<IOException>(() => planner.PlanAsync(target, selected, new() { UnityProjectsRoot = root }, creation: request with { EmptyProject = false }));
        Assert.False(Directory.Exists(target));
        var workspace = new MemoryWorkspace();
        var driver = new MemoryDriver(workspace);
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("empty", DateTime.UtcNow, [plan]));
        Assert.True(report.Success, string.Join("; ", report.Results.Select(x => x.Message)));
        Assert.True(workspace.Completed); Assert.Equal(0, driver.UnityCalls);
    }
    [Fact] public async Task SdkVersionChangeIsLinkedAndResolvedBeforeApply()
    {
        var workspace = new MemoryWorkspace(); var planner = new ProjectPlanner(workspace, new FixtureCatalog());
        var plan = await planner.PlanAsync(workspace.Project.Path, new Dictionary<string, string> { [ProjectRules.BaseSdk] = "3.10.6" }, new());
        Assert.Equal("3.10.6", plan.Changes.Single(x => x.Id == ProjectRules.AvatarSdk).ResolvedVersion);
        Assert.Equal("3.10.6", plan.DesiredPolicy[ProjectRules.AvatarSdk]);
    }
    [Fact] public async Task LatestIsConcreteInThePlanAndItsPolicyIsPreserved()
    {
        var workspace = new MemoryWorkspace(); var planner = new ProjectPlanner(workspace, new FixtureCatalog());
        var plan = await planner.PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        Assert.Equal("1.1.0", plan.Changes.Single(x => x.Id == "tool").ResolvedVersion); Assert.Equal("latest", plan.DesiredPolicy["tool"]);
    }
    [Fact] public async Task RemovingFoundationFailsBeforeAnyMutation()
    {
        var workspace = new MemoryWorkspace();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string>(), new(), remove: [ProjectRules.BaseSdk]));
        Assert.Equal(0, workspace.Backups);
    }
    [Fact] public async Task AvatarSdkCanBeReplacedWithWorldSdk()
    {
        var workspace = new MemoryWorkspace();
        var session = new PackageEditSession([workspace.Project], new(), []);
        session.Toggle(ProjectRules.AvatarSdk);
        session.SetVersion(ProjectRules.WorldSdk, "3.10.5");
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, session.Selected, new(), remove: session.Removed.ToArray());
        Assert.DoesNotContain(ProjectRules.AvatarSdk, plan.DesiredPolicy.Keys);
        Assert.Contains(ProjectRules.WorldSdk, plan.DesiredPolicy.Keys);
        Assert.Contains(ProjectRules.BaseSdk, plan.RequiredPackages);
        Assert.DoesNotContain(ProjectRules.WorldSdk, plan.RequiredPackages);
    }
    [Fact] public async Task ImportedPlanCannotBypassFoundationProtection()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string>(), new());
        var policy = new Dictionary<string, string>(project.DesiredPolicy); policy.Remove(ProjectRules.BaseSdk);
        project = project with { DesiredPolicy = policy, RequiredPackages = [], Changes = project.Changes.Select(x => x.Id == ProjectRules.BaseSdk ? x with { Kind = ChangeKind.Remove, ResolvedVersion = null } : x).ToArray() };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Failed, report.Results.Single().Status); Assert.Equal(0, workspace.Backups); Assert.Equal(0, driver.Calls);
    }
    [Fact] public async Task MissingEmbeddedPackageIsRepairedEvenIfLockedVersionMatches()
    {
        var workspace = new MemoryWorkspace(); workspace.Project.InstalledVersions.Remove("tool");
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string>(), new());
        Assert.Equal(ChangeKind.Update, plan.Changes.Single(x => x.Id == "tool").Kind);
    }
    [Fact] public async Task ExactPinWinsOverAStaleEmbeddedVersion()
    {
        var workspace = new MemoryWorkspace(); workspace.Project.InstalledVersions["tool"] = "1.1.0";
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "1.0.0" }, new());
        var change = plan.Changes.Single(x => x.Id == "tool");
        Assert.Equal("1.0.0", change.ResolvedVersion); Assert.Equal(ChangeKind.Update, change.Kind);
    }
    [Fact] public async Task ChangedProjectAfterReviewIsSkipped()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        workspace.Project = workspace.Project with { Fingerprint = "changed" };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Skipped, report.Results.Single().Status); Assert.Equal(0, driver.Calls);
    }
    [Fact] public async Task PackageChangesCanRunWhileUnityIsOpen()
    {
        var workspace = new MemoryWorkspace { Busy = true }; var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.True(report.Success); Assert.True(driver.Calls > 0); Assert.Equal("1.1.0", workspace.Project.InstalledVersions["tool"]);
        Assert.Equal(0, driver.UnityCalls); Assert.False(workspace.LeaseHeld);
    }
    [Fact] public async Task AdditionalFileImportsStillNeedAnAvailableUnityProject()
    {
        var workspace = new MemoryWorkspace { Busy = true }; var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        project = project with { ExtraImports = ["extra.unitypackage"] };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Skipped, report.Results.Single().Status); Assert.Equal(0, workspace.Backups); Assert.Equal(0, driver.Calls);
    }
    [Fact] public async Task BackupFailurePreventsToolMutations()
    {
        var workspace = new MemoryWorkspace { BackupFailure = true }; var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Failed, report.Results.Single().Status); Assert.Equal(0, driver.Calls);
    }
    [Fact] public async Task FailedToolTriggersRestoreAndPreservesFailure()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace) { Fail = true };
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Failed, report.Results.Single().Status); Assert.Equal(1, workspace.Restores); Assert.True(workspace.LockHeldDuringRestore); Assert.False(workspace.LeaseHeld);
    }
    [Fact] public async Task SuccessfulExecutionVerifiesMetadataAndCompletes()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.True(report.Success); Assert.Equal("1.1.0", workspace.Project.InstalledVersions["tool"]); Assert.True(workspace.Completed);
    }
    [Fact] public async Task ReimportDoesNotCreateOrRestoreABackup()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        project = project with { RecoverySource = new("source.unitypackage", project.Name, project.Path, "Unity.exe", "StandaloneWindows64", [], []) };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.True(report.Success); Assert.Equal(0, workspace.Backups); Assert.Equal(0, workspace.Restores); Assert.Null(report.Results.Single().BackupPath);
        Assert.Equal(1, driver.Registrations);
    }
    [Fact] public async Task PrereleaseSdkMustExistForEveryLinkedComponent()
    {
        var catalog = new FixtureCatalog(); catalog.Versions[ProjectRules.BaseSdk] = ["3.10.5-beta.1"];
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectPlanner(new MemoryWorkspace(), catalog).PlanAsync(Path.GetFullPath("avatar"), new Dictionary<string, string> { [ProjectRules.BaseSdk] = "3.10.5-beta.1" }, new()));
    }
    [Fact] public async Task CancellationBetweenProjectsDoesNotReportFullSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var workspace = new MemoryWorkspace { CancelOnComplete = cancellation }; var driver = new MemoryDriver(workspace);
        var first = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var second = first with { Name = "second", Path = Path.GetFullPath("second") };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [first, second]), cancellationToken: cancellation.Token);
        Assert.False(report.Success); Assert.True(report.Cancelled); Assert.Equal([second.Path], report.NotProcessed);
    }
}

internal sealed class FixtureCatalog : IPackageCatalog
{
    public Dictionary<string, string[]> Versions { get; } = new()
    {
        [ProjectRules.BaseSdk] = ["3.10.5", "3.10.6"], [ProjectRules.AvatarSdk] = ["3.10.5", "3.10.6"], [ProjectRules.WorldSdk] = ["3.10.5", "3.10.6"],
        [ProjectRules.Resolver] = ["0.1.29"], ["tool"] = ["1.0.0", "1.1.0", "2.0.0-beta"], ["custom"] = ["1.0.0"]
    };
    public Task<string[]> GetVersionsAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Versions.GetValueOrDefault(id) ?? []);
    public Task<PackageDefinition[]> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<PackageDefinition>());
}
internal sealed class MemoryWorkspace : IProjectWorkspace
{
    public ProjectSnapshot Project = new()
    {
        Name = "avatar", Path = Path.GetFullPath("avatar"), Fingerprint = "original",
        Packages = new() { [ProjectRules.BaseSdk] = "3.10.5", [ProjectRules.AvatarSdk] = "3.10.5", [ProjectRules.Resolver] = "0.1.29", ["tool"] = "1.0.0", ["custom"] = "1.0.0" },
        InstalledVersions = new() { [ProjectRules.BaseSdk] = "3.10.5", [ProjectRules.AvatarSdk] = "3.10.5", [ProjectRules.Resolver] = "0.1.29", ["tool"] = "1.0.0", ["custom"] = "1.0.0" },
        LockedPackages = new() { ["tool"] = "1.0.0" }
    };
    public bool Busy; public bool BackupFailure; public int Backups, Restores; public bool Completed; public bool LeaseHeld, LockHeldDuringRestore; public CancellationTokenSource? CancelOnComplete;
    public Task<ProjectSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Project with { Path = path });
    public bool IsBusy(string path) => Busy;
    public Task<IDisposable> AcquireAsync(string path, CancellationToken cancellationToken) { LeaseHeld = true; return Task.FromResult<IDisposable>(new Lease(() => LeaseHeld = false)); }
    public Task<BackupRecord> BackupAsync(ProjectPlan plan, CancellationToken cancellationToken) { Backups++; if (BackupFailure) throw new IOException("Backup denied"); return Task.FromResult(new BackupRecord("backup", new Dictionary<string, string?>())); }
    public Task<bool> RestoreAsync(string project, BackupRecord backup, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { Restores++; LockHeldDuringRestore = LeaseHeld; return Task.FromResult(true); }
    public Task SaveProfileAsync(ProjectPlan plan, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetStepAsync(string path, string step, bool done, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task CompleteAsync(string path, CancellationToken cancellationToken) { Completed = true; CancelOnComplete?.Cancel(); return Task.CompletedTask; }
    private sealed class Lease(Action close) : IDisposable { public void Dispose() => close(); }
}
internal sealed class MemoryDriver(MemoryWorkspace workspace) : IProjectDriver
{
    public bool Fail; public int Calls, UnityCalls, Registrations;
    public Action? AfterResolve;
    public Task PrepareAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    public Task CreateAsync(CreationRequest request, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<ToolResult> ChangePackageAsync(string project, PackageChange change, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        if (Fail) return Task.FromResult(new ToolResult(0, "[ERR] Could not get match", ""));
        if (change.Kind == ChangeKind.Remove) { workspace.Project.Packages.Remove(change.Id); workspace.Project.InstalledVersions.Remove(change.Id); }
        else { workspace.Project.Packages[change.Id] = change.ResolvedVersion!; workspace.Project.InstalledVersions[change.Id] = change.ResolvedVersion!; workspace.Project.LockedPackages[change.Id] = change.ResolvedVersion!; }
        return Task.FromResult(new ToolResult(0, "Added package " + change.Id, ""));
    }
    public Task<ToolResult> ResolveAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { AfterResolve?.Invoke(); return Task.FromResult(new ToolResult(0, "Resolved", "")); }
    public Task ImportAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { UnityCalls++; return Task.CompletedTask; }
    public Task SettleAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { UnityCalls++; return Task.CompletedTask; }
    public Task<bool> RegisterAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { Registrations++; return Task.FromResult(true); }
}
