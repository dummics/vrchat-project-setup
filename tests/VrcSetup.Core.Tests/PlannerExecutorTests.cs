using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PlannerExecutorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompilationNoticeAndBlockedImportHaveDistinctDurableOutcomes(bool imported)
    {
        var root = Path.GetFullPath("compilation-fixture");
        var target = Path.Combine(root, "project");
        var request = new CreationRequest("", "project", target, "Unity.exe", "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var workspace = new MemoryWorkspace();
        var driver = new MemoryDriver(workspace) { ImportFailure = imported ? new UnityCompilationWarningException("Imported with script errors") : new UnityCompilationBlockedException("Unity reported script compilation errors.") };
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(target, new Dictionary<string, string> { [ProjectRules.BaseSdk] = "latest", [ProjectRules.Resolver] = "latest" }, new() { UnityProjectsRoot = root }, creation: request);
        plan = plan with { ExtraImports = ["fixture.unitypackage"] };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("compilation", DateTime.UtcNow, [plan]));
        var outcome = Assert.Single(report.Results);
        Assert.Equal(imported, report.Success);
        Assert.Equal(imported, workspace.Completed);
        Assert.Equal(imported, outcome.UnityCompilationNeedsAttention);
        Assert.Equal(!imported, outcome.UnityCompilationBlockedImport);
        Assert.Equal(imported ? OperationStatus.CompletedWithWarnings : OperationStatus.Failed, outcome.Status);
        Assert.Equal(imported ? 1 : 0, driver.Registrations);
        Assert.Equal(0, workspace.Backups);
        Assert.Null(outcome.BackupPath);
    }
    [Fact] public async Task InvalidSourceStopsBeforeAnyProjectMutation()
    {
        var workspace = new MemoryWorkspace();
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path,
            new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var driver = new MemoryDriver(workspace) { RejectSource = true };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("invalid-source", DateTime.UtcNow, [project]));
        Assert.False(report.Success);
        Assert.Equal(0, workspace.Backups);
        Assert.Equal(0, workspace.Restores);
        Assert.Equal(0, driver.Calls);
        Assert.False(workspace.Completed);
    }
    [Fact] public async Task SourceLeaseIsReleasedAfterPackageFailure()
    {
        var workspace = new MemoryWorkspace();
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path,
            new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var driver = new MemoryDriver(workspace) { Fail = true, TrackSourceLease = true };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("source-failure", DateTime.UtcNow, [project]));
        Assert.False(report.Success);
        Assert.True(driver.SourceLeaseReleased);
    }
    [Theory, InlineData(true), InlineData(false)]
    public async Task RecoveryOfPreexistingProjectBacksUpBeforeMutationAndPreservesOpenEditor(bool editorOpen)
    {
        var workspace = new MemoryWorkspace { Busy = editorOpen };
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path,
            new Dictionary<string, string> { ["tool"] = "latest" }, new());
        plan = plan with { RecoverySource = new("", workspace.Project.Name, workspace.Project.Path, "Unity.exe", "StandaloneWindows64", [], [], true) };
        var report = await new ProjectExecutor(workspace, new MemoryDriver(workspace) { Fail = true }).ApplyAsync(new("recovery-backup", DateTime.UtcNow, [plan]));
        Assert.False(report.Success);
        Assert.Equal(1, workspace.Backups);
        Assert.Equal(editorOpen ? 0 : 1, workspace.Restores);
        Assert.Equal("backup", report.Results.Single().BackupPath);
    }

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
    [Fact] public async Task PackageChangesCompleteWhileUnityIsOpenWithoutLaunchingAnotherEditor()
    {
        var workspace = new MemoryWorkspace { Busy = true }; var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.True(report.Success); Assert.True(driver.Calls > 0); Assert.Equal(1, workspace.Backups);
        Assert.Equal("1.1.0", workspace.Project.InstalledVersions["tool"]);
        Assert.Equal(0, driver.UnityCalls); Assert.False(workspace.LeaseHeld);
    }
    [Fact] public async Task AdditionalFileImportsStillNeedAnAvailableUnityProject()
    {
        var workspace = new MemoryWorkspace { Busy = true }; var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        project = project with { ExtraImports = ["extra.unitypackage"] };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Failed, report.Results.Single().Status); Assert.Equal(0, workspace.Backups); Assert.Equal(0, driver.Calls);
    }
    [Fact] public async Task FailedPackageChangeWithUnityOpenPreservesBackupWithoutRestoringEditorFiles()
    {
        var workspace = new MemoryWorkspace { Busy = true }; var driver = new MemoryDriver(workspace) { Fail = true };
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("open-failure", DateTime.UtcNow, [project]));
        var result = Assert.Single(report.Results);
        Assert.Equal(OperationStatus.Failed, result.Status); Assert.NotNull(result.BackupPath);
        Assert.Equal(1, workspace.Backups); Assert.Equal(0, workspace.Restores); Assert.False(workspace.LeaseHeld);
        Assert.Equal(0, driver.UnityCalls);
    }
    [Fact] public async Task BackupFailurePreventsToolMutations()
    {
        var workspace = new MemoryWorkspace { BackupFailure = true }; var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Failed, report.Results.Single().Status); Assert.Equal(0, driver.Calls);
    }
    private sealed class EditorSession : IAsyncDisposable
    {
        public int Releases;
        public ValueTask DisposeAsync() { Releases++; return ValueTask.CompletedTask; }
    }
    [Fact] public async Task EditorExitDuringPackageFailureDoesNotRestoreOverConcurrentWork()
    {
        var workspace = new MemoryWorkspace { Busy = true };
        var driver = new MemoryDriver(workspace) { AfterResolve = () => { workspace.Busy = false; throw new IOException("Editor exited during package operation"); } };
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("editor-exit", DateTime.UtcNow, [project]));
        Assert.Equal(OperationStatus.Failed, Assert.Single(report.Results).Status);
        Assert.NotNull(report.Results[0].BackupPath); Assert.Equal(0, workspace.Restores); Assert.False(workspace.LeaseHeld);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task OpenEditorSessionIsReleasedOnFailureAndCancellation(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var workspace = new MemoryWorkspace { Busy = true }; var editor = new EditorSession();
        var driver = new MemoryDriver(workspace) { EditorSession = editor, Fail = !cancel };
        if (cancel) driver.AfterResolve = () => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("lease-failure", DateTime.UtcNow, [project]), cancellationToken: cancellation.Token);
        Assert.Equal(cancel ? OperationStatus.Cancelled : OperationStatus.Failed, Assert.Single(report.Results).Status);
        Assert.Equal(1, editor.Releases); Assert.Equal(0, workspace.Restores); Assert.False(workspace.LeaseHeld);
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
    [Fact] public async Task ReimportOfPreexistingProjectKeepsABackupWithoutRestoringOnSuccess()
    {
        var workspace = new MemoryWorkspace(); var driver = new MemoryDriver(workspace);
        var project = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(workspace.Project.Path, new Dictionary<string, string> { ["tool"] = "latest" }, new());
        project = project with { RecoverySource = new("source.unitypackage", project.Name, project.Path, "Unity.exe", "StandaloneWindows64", [], []) };
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [project]));
        Assert.True(report.Success); Assert.Equal(1, workspace.Backups); Assert.Equal(0, workspace.Restores); Assert.Equal("backup", report.Results.Single().BackupPath);
        Assert.Equal(1, driver.Registrations);
    }
    [Fact] public async Task PrereleaseSdkMustExistForEveryLinkedComponent()
    {
        var catalog = new FixtureCatalog(); catalog.Versions[ProjectRules.BaseSdk] = ["3.10.5-beta.1"];
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectPlanner(new MemoryWorkspace(), catalog).PlanAsync(Path.GetFullPath("avatar"), new Dictionary<string, string> { [ProjectRules.BaseSdk] = "3.10.5-beta.1" }, new()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalRegistrationFailureNeverRollsBackAReadyProject(bool throws)
    {
        var root = Path.GetFullPath("registration-fixture");
        var target = Path.Combine(root, "project");
        var request = new CreationRequest("", "project", target, "Unity.exe", "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var workspace = new MemoryWorkspace();
        var driver = new MemoryDriver(workspace) { FailRegistration = true, RegistrationFailure = throws ? new InvalidOperationException("VCC unavailable") : null };
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(target, new Dictionary<string, string> { [ProjectRules.BaseSdk] = "latest", [ProjectRules.Resolver] = "latest" }, new() { UnityProjectsRoot = root }, creation: request);
        var events = new List<OperationEvent>();
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("registration", DateTime.UtcNow, [plan]), new CaptureProgress(events));
        Assert.True(report.Success);
        var outcome = Assert.Single(report.Results);
        Assert.Equal(OperationStatus.CompletedWithWarnings, outcome.Status);
        Assert.True(outcome.CreatorCompanionPending);
        Assert.True(workspace.Completed);
        Assert.Equal(0, workspace.Restores);
        Assert.DoesNotContain(events, x => x.Severity == EventSeverity.Error);
        Assert.Contains(events, x => x.Phase == "VCC" && x.Severity == EventSeverity.Warning);
    }
    [Fact] public async Task CancelledOptionalRegistrationKeepsCreatedProjectSuccessful()
    {
        using var cancellation = new CancellationTokenSource();
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-reg-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "project");
        var request = new CreationRequest("", "project", target, "Unity.exe", "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var workspace = new MemoryWorkspace { CancelOnComplete = cancellation };
        var driver = new MemoryDriver(workspace) { CancelRegistration = true };
        var plan = await new ProjectPlanner(workspace, new FixtureCatalog()).PlanAsync(target, new Dictionary<string, string> { [ProjectRules.BaseSdk] = "latest", [ProjectRules.Resolver] = "latest" }, new() { UnityProjectsRoot = root }, creation: request);
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("id", DateTime.UtcNow, [plan]), cancellationToken: cancellation.Token);
        Assert.True(report.Success); Assert.Equal(OperationStatus.CompletedWithWarnings, Assert.Single(report.Results).Status);
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
    public Dictionary<string, PackageVersionMetadata> Metadata { get; } = new();
    public Task<PackageVersionMetadata> GetMetadataAsync(string id, string version, CancellationToken cancellationToken = default) => Task.FromResult(Metadata.GetValueOrDefault(id + "@" + version) ?? PackageVersionMetadata.Empty);
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
    public bool Busy; public bool BackupFailure; public int Backups, Restores, Reads; public bool Completed; public bool LeaseHeld, LockHeldDuringRestore; public CancellationTokenSource? CancelOnComplete;
    public Task<ProjectSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) { Reads++; return Task.FromResult(Project with { Path = path }); }
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
    public Exception? ImportFailure;
    public bool RejectSource, TrackSourceLease, SourceLeaseReleased;
    public Task<IAsyncDisposable?> HoldImportSourceAsync(ProjectPlan plan, CancellationToken token) => RejectSource
        ? Task.FromException<IAsyncDisposable?>(new IOException("Source changed")) : Task.FromResult<IAsyncDisposable?>(TrackSourceLease ? new SourceLease(() => SourceLeaseReleased = true) : null);
    private sealed class SourceLease(Action release) : IAsyncDisposable
    { public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; } }
    public int Initializations;
    public bool InitializationLeaseHeld;
    public Task InitializePartialAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken token)
    { Initializations++; InitializationLeaseHeld = workspace.LeaseHeld; workspace.Project = workspace.Project with { Fingerprint = "initialized" }; return Task.CompletedTask; }
    public IAsyncDisposable? EditorSession;
    public Task<IAsyncDisposable?> BeginPackageChangesAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken token) => Task.FromResult(EditorSession);
    public bool Fail, CancelRegistration, FailRegistration;
    public Exception? RegistrationFailure;
    public int Calls, UnityCalls, Registrations;
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
    public Task ImportAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { UnityCalls++; return ImportFailure is null ? Task.CompletedTask : Task.FromException(ImportFailure); }
    public Task SettleAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) { UnityCalls++; return Task.CompletedTask; }
    public Task<bool> RegisterAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        Registrations++;
        if (FailRegistration) progress?.Report(new(DateTime.UtcNow, "VPM", "[ERROR] Cannot add project", EventSeverity.Error, project));
        if (RegistrationFailure is not null) throw RegistrationFailure;
        return CancelRegistration && cancellationToken.IsCancellationRequested ? Task.FromCanceled<bool>(cancellationToken) : Task.FromResult(!FailRegistration);
    }
}
