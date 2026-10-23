using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ImportQueueTests : IDisposable
{
    [Fact] public async Task DismissFailedCreationPersistsWithItsHistory()
    {
        var app = App; var plan = Plan("Failed", "Active");
        var jobs = plan.Projects.Select((p, i) => new ImportJob("job" + i, new("op" + i, plan.CreatedUtc, [p]), p.Creation! with { EmptyProject = true },
            i == 0 ? "Failed" : "Running", "Create", 0, "message", "")).ToArray();
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), jobs);
        await using (var queue = new ImportQueue(app))
        {
            await queue.InitializeAsync(); await queue.DismissAsync("job0");
            Assert.True(queue.Snapshot().Single(x => x.Id == "job0").HiddenFromImports);
        }
        await using var reopened = new ImportQueue(app); await reopened.InitializeAsync();
        Assert.True(reopened.Snapshot().Single(x => x.Id == "job0").HiddenFromImports);
        Assert.Equal(jobs[0].Plan.Id, reopened.Snapshot().Single(x => x.Id == "job0").Plan.Id);
    }
    [Fact] public async Task ClearCompletedHidesRowsButRetainsHistoryPlanAndLogAfterRestart()
    {
        var app = App; var plan = Plan("Complete", "Failed", "Active");
        var log = Path.Combine(app.Paths.Logs, "kept.log"); Directory.CreateDirectory(app.Paths.Logs); await File.WriteAllTextAsync(log, "evidence");
        var jobs = plan.Projects.Select((p, i) => new ImportJob("job" + i, new("op" + i, plan.CreatedUtc, [p]), p.Creation!,
            new[] { "Completed", "Failed", "Running" }[i], "Import", 50, "message", log)).ToArray();
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), jobs);
        await using (var queue = new ImportQueue(app))
        {
            await queue.InitializeAsync(); await queue.ClearCompletedAsync();
            Assert.Equal(3, queue.Snapshot().Length);
            Assert.True(queue.Snapshot().Single(x => x.Id == "job0").HiddenFromImports);
            Assert.All(queue.Snapshot().Where(x => x.Id != "job0"), x => Assert.False(x.HiddenFromImports));
        }
        await using var reopened = new ImportQueue(app); await reopened.InitializeAsync();
        var kept = reopened.Snapshot().Single(x => x.Id == "job0");
        Assert.True(kept.HiddenFromImports); Assert.Equal(jobs[0].Plan.Id, kept.Plan.Id);
        Assert.Equal(jobs[0].Source.UnityPackagePath, kept.Source.UnityPackagePath); Assert.Equal(jobs[0].Source.TargetPath, kept.Source.TargetPath);
        Assert.Equal(jobs[0].Source.PublishingPlatforms, kept.Source.PublishingPlatforms); Assert.Equal("evidence", await File.ReadAllTextAsync(kept.LogFile));
    }
    [Fact] public async Task UnrestrictedSchedulingStartsSevenJobsAndRecordsTimes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var started = 0;
        await using var queue = new ImportQueue(App, async (plan, _, token) =>
        {
            Interlocked.Increment(ref started); await release.Task.WaitAsync(token); return Completed(plan);
        });
        await queue.EnqueueAsync(Plan(Enumerable.Range(1, 7).Select(x => "Avatar" + x).ToArray()), int.MaxValue);
        await WaitUntil(() => Volatile.Read(ref started) == 7);
        var active = queue.Snapshot()[0]; await queue.DismissAsync(active.Id);
        Assert.False(queue.Snapshot().Single(x => x.Id == active.Id).HiddenFromImports);
        Assert.All(queue.Snapshot(), x => Assert.NotNull(x.StartedAt));
        release.SetResult(); await WaitUntil(() => !queue.HasActiveJobs);
        Assert.All(queue.Snapshot(), x => Assert.NotNull(x.FinishedAt));
    }
    private readonly string root = Path.Combine(Path.GetTempPath(), "VrcSetup.Queue.Tests", Guid.NewGuid().ToString("N"));
    private VrcSetupApplication App => new(Path.Combine(root, "home"));
    private OperationPlan Plan(params string[] names) => new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, names.Select(name =>
    {
        var path = Path.Combine(root, name);
        return new ProjectPlan { Name = name, Path = path, ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [],
            Creation = new(Path.Combine(root, name + ".unitypackage"), name, path, "Unity.exe", "StandaloneWindows64", ["StandaloneWindows64"], []) };
    }).ToArray());
    private static OperationReport Completed(OperationPlan plan) => new(plan.Id, DateTime.UtcNow,
        plan.Projects.Select(x => new ProjectOutcome(x.Name, x.Path, OperationStatus.Completed, "Done")).ToArray(), []);
    private static async Task WaitUntil(Func<bool> ready)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!ready()) { if (DateTime.UtcNow > timeout) throw new TimeoutException("Queue did not settle."); await Task.Delay(10); }
    }
    [Fact] public async Task ParallelLimitAndCompletedHistoryAreDurable()
    {
        var app = App; var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0; var maximum = 0; var executed = 0;
        await using (var queue = new ImportQueue(app, async (plan, progress, token) =>
        {
            var active = Interlocked.Increment(ref running); Interlocked.Increment(ref executed);
            lock (release) maximum = Math.Max(maximum, active);
            try { await release.Task.WaitAsync(token); return Completed(plan); }
            finally { Interlocked.Decrement(ref running); }
        }))
        {
            await queue.EnqueueAsync(Plan("First", "Second", "Third"), 2);
            await WaitUntil(() => Volatile.Read(ref executed) == 2);
            Assert.Equal(2, maximum); Assert.Single(queue.Snapshot(), x => x.State == "Queued");
            release.SetResult(); await WaitUntil(() => !queue.HasActiveJobs);
            Assert.All(queue.Snapshot(), x => Assert.Equal("Completed", x.State));
        }
        await using var reopened = new ImportQueue(app);
        await reopened.InitializeAsync(); Assert.Equal(3, reopened.Snapshot().Length);
        Assert.All(reopened.Snapshot(), x => Assert.False(x.Active));
    }
    [Fact] public async Task CancelQueuedJobNeverExecutesItAndDuplicateDestinationIsRejected()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var executed = 0;
        await using var queue = new ImportQueue(App, async (plan, progress, token) =>
        { Interlocked.Increment(ref executed); entered.TrySetResult(); await release.Task.WaitAsync(token); return Completed(plan); });
        var first = Plan("First"); await queue.EnqueueAsync(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<IOException>(() => queue.EnqueueAsync(first));
        await queue.EnqueueAsync(Plan("Second"));
        var second = queue.Snapshot().Single(x => x.Name == "Second"); queue.Cancel(second.Id);
        release.SetResult(); await WaitUntil(() => !queue.HasActiveJobs);
        Assert.Equal(1, Volatile.Read(ref executed));
        Assert.Equal("Cancelled", queue.Snapshot().Single(x => x.Id == second.Id).State);
    }
    [Fact] public async Task InterruptedHistoryDoesNotAutoResume()
    {
        var app = App; var plan = Plan("Interrupted");
        var job = new ImportJob(plan.Id, plan, plan.Projects[0].Creation!, "Running", "Import", 55, "", "");
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { job });
        var executed = 0;
        await using var queue = new ImportQueue(app, (p, progress, token) => { executed++; return Task.FromResult(Completed(p)); });
        await queue.InitializeAsync(); Assert.Equal(0, executed);
        Assert.Equal("Interrupted", queue.Snapshot().Single().State);
        Assert.True(queue.Snapshot().Single().Recoverable);
    }
    [Fact] public async Task RecoveryReconciliationKeepsExplicitSourceOwnershipWhenArchiveWasRelocated()
    {
        var app = App; var plan = Plan("Relocated", "Unrelated");
        var original = Path.Combine(root, "original.unitypackage");
        var jobs = plan.Projects.Select((project, i) =>
        {
            Directory.CreateDirectory(project.Path);
            AtomicFile.WriteJsonAsync(Path.Combine(project.Path, ".vrcsetup", "state.json"), new SetupState { Completed = true, UnityPackagePath = i == 0 ? original : Path.Combine(root, "another.unitypackage") }).GetAwaiter().GetResult();
            var reviewed = project with { RecoveryOriginalSource = original };
            return new ImportJob("recovery" + i, new("plan" + i, plan.CreatedUtc, [reviewed]), project.Creation!, "Interrupted", "Import", 50, "", "");
        }).ToArray();
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), jobs);
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        Assert.Equal("Recovered", queue.Snapshot().Single(x => x.Id == "recovery0").State);
        Assert.Equal("Interrupted", queue.Snapshot().Single(x => x.Id == "recovery1").State);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task EmptyRecoveryCompletionRequiresTheRecordedProjectIdentity(bool matching)
    {
        var app = App; var original = Plan("Empty").Projects.Single();
        var source = original.Creation! with { UnityPackagePath = "", EmptyProject = true };
        var project = original with { Creation = source };
        Directory.CreateDirectory(project.Path);
        await AtomicFile.WriteJsonAsync(Path.Combine(project.Path, ".vrcsetup", "state.json"),
            new SetupState { Completed = true, ProjectName = matching ? source.Name : "Different project", UnityPackagePath = "" });
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"),
            new[] { new ImportJob("empty-recovery", new("empty-plan", DateTime.UtcNow, [project]), source, "Interrupted", "Import", 50, "", "") });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        Assert.Equal(matching ? "Recovered" : "Interrupted", queue.Snapshot().Single().State);
    }
    [Fact] public async Task ClearAnalysisCachePreservesKnowledgeAndHistory()
    {
        var app = App; var cache = Path.Combine(app.Paths.Cache, "inspection"); Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "archive-fixture.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(cache, "signatures.json"), "[]");
        await File.WriteAllTextAsync(Path.Combine(app.Paths.Home, "imports.json"), "[]");
        Assert.Equal(1, await app.Inspection.ClearObservationCacheAsync());
        Assert.True(File.Exists(Path.Combine(cache, "signatures.json")));
        Assert.True(File.Exists(Path.Combine(app.Paths.Home, "imports.json")));
    }
    [Fact] public void InvalidOversizedVersionsDoNotBreakSorting() => Assert.Equal(new[] { "1.2.3" }, SemanticVersion.Sort(["99999999999999999.1.0", "1.2.3"]));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
