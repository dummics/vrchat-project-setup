using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ImportQueueTests : IDisposable
{
    [Fact] public async Task OrphanRecoveryEnqueuesReviewedSourceAndPersistsCompletion()
    {
        var app = App; var original = Plan("Orphan ready").Projects.Single();
        var movedSource = original.Creation! with { UnityPackagePath = Path.Combine(root, "moved.unitypackage"), SourceIdentity = new PackageIdentity(8, new string('A', 64)) };
        var project = original with { Creation = null, RecoverySource = movedSource };
        var plan = new OperationPlan("orphan-reviewed", DateTime.UtcNow, [project]);
        var received = new TaskCompletionSource<OperationPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var queue = new ImportQueue(app, (actual, _, _) => { received.TrySetResult(actual); return Task.FromResult(Completed(actual)); }))
        {
            await queue.EnqueueAsync(plan, requireNoProjectHistory: true);
            var executed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(movedSource.MatchesInputs(executed.Projects.Single().RecoverySource));
            var id = Assert.Single(queue.Snapshot()).Id;
            await queue.WaitForStoppedAsync(id).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Completed", Assert.Single(queue.Snapshot()).State);
        }
        await using var reopened = new ImportQueue(app);
        await reopened.InitializeAsync();
        var restored = Assert.Single(reopened.Snapshot());
        Assert.Equal("Completed", restored.State);
        Assert.True(movedSource.MatchesInputs(restored.Source));
    }
    [Fact] public async Task OrphanRecoveryCannotOverwriteAConcurrentImportHistory()
    {
        var app = App; var plan = Plan("History appeared");
        var existing = new ImportJob("existing", plan, plan.Projects.Single().Creation!, "Paused", "Paused", 20, "", "");
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { existing });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => queue.EnqueueAsync(plan, requireNoProjectHistory: true));
        Assert.Equal(existing.Id, Assert.Single(queue.Snapshot()).Id);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistrationRetryOnlyUpdatesTheNoticeAndPersistsItsResult(bool registered)
    {
        var app = App; var plan = Plan("Registration");
        var job = new ImportJob("vcc-retry", plan, plan.Projects.Single().Creation!, "Completed", "Completed", 100,
            "Project ready; Creator Companion registration failed. Add the existing project in VCC.", "") { CreatorCompanionPending = true };
        var journal = Path.Combine(app.Paths.Home, "imports.json");
        await AtomicFile.WriteJsonAsync(journal, new[] { job });
        await using (var queue = new ImportQueue(app, (_, _, _) => throw new InvalidOperationException("Never repeat the import")))
        {
            await queue.InitializeAsync();
            var calls = 0;
            Assert.Equal(registered, await queue.RetryRegistrationAsync(job.Id, (selected, _) => { Assert.Equal(job.ProjectPath, selected.ProjectPath); calls++; return Task.FromResult(registered); }));
            Assert.Equal(1, calls);
            Assert.Equal(!registered, queue.Snapshot().Single().CreatorCompanionPending);
            Assert.Equal("Completed", queue.Snapshot().Single().State);
        }
        await using var reopened = new ImportQueue(app); await reopened.InitializeAsync();
        Assert.Equal(!registered, reopened.Snapshot().Single().CreatorCompanionPending);
    }
    [Fact] public async Task FailedRegistrationNoticeSaveDoesNotPublishSuccess()
    {
        var app = App; var plan = Plan("Notice save");
        var job = new ImportJob("vcc-save", plan, plan.Projects.Single().Creation!, "Completed", "Completed", 100, "", "") { CreatorCompanionPending = true };
        var journal = Path.Combine(app.Paths.Home, "imports.json"); var saved = journal + ".saved";
        await AtomicFile.WriteJsonAsync(journal, new[] { job });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        var publishedSuccess = false;
        queue.Changed += jobs => publishedSuccess |= jobs.Any(x => !x.CreatorCompanionPending);
        try
        {
            var failure = await Record.ExceptionAsync(() => queue.RetryRegistrationAsync(job.Id, (_, _) =>
            {
                File.Move(journal, saved); Directory.CreateDirectory(journal); return Task.FromResult(true);
            }));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.True(queue.Snapshot().Single().CreatorCompanionPending);
            Assert.False(publishedSuccess);
        }
        finally { Directory.Delete(journal); File.Move(saved, journal); }
    }
    [Fact] public async Task SupersededRegistrationNoticeCannotRetryAnOlderImport()
    {
        var app = App; var plan = Plan("Newer import");
        var old = new ImportJob("old-vcc", plan, plan.Projects.Single().Creation!, "Completed", "Completed", 100, "", "") { CreatorCompanionPending = true };
        var current = old with { Id = "new-vcc", CreatorCompanionPending = false };
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { old, current });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        Assert.False(await queue.RetryRegistrationAsync(old.Id, (_, _) => throw new InvalidOperationException("Must not register stale import")));
    }
    [Fact] public async Task ShutdownCannotReleaseQueueOwnershipWhileDiscardCleanupIsRunning()
    {
        var app = App; var plan = Plan("Discard shutdown");
        var job = new ImportJob("discard-close", plan, plan.Projects.Single().Creation!, "Paused", "Paused", 40, "", "") { DiscardRequested = true };
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { job });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = queue.FinishDiscardAsync(job.Id, async (_, _) => { entered.TrySetResult(); await release.Task; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            var shutdown = queue.ShutdownAsync(cancellation.Token);
            Assert.False(shutdown.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shutdown);
            await using var competing = new ImportQueue(app);
            await Assert.ThrowsAnyAsync<IOException>(() => competing.InitializeAsync());
            Assert.True(queue.Snapshot().Single().DiscardRequested);
        }
        finally { release.TrySetResult(); await finish.WaitAsync(TimeSpan.FromSeconds(5)); }
        await queue.ShutdownAsync();
        await using var reopened = new ImportQueue(app); await reopened.InitializeAsync();
        Assert.True(reopened.Snapshot().Single().DiscardCompleted);
    }

    [Fact] public async Task DiscardOutcomeIsNotPublishedWhenItsJournalWriteFails()
    {
        var app = App; var plan = Plan("Discard persistence");
        var job = new ImportJob("discard-save", plan, plan.Projects.Single().Creation!, "Paused", "Paused", 40, "", "") { DiscardRequested = true };
        var journal = Path.Combine(app.Paths.Home, "imports.json");
        var saved = journal + ".saved";
        await AtomicFile.WriteJsonAsync(journal, new[] { job });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        var terminalPublished = false;
        queue.Changed += jobs => terminalPublished |= jobs.Any(x => x.DiscardCompleted);
        var cleanupCalls = 0;
        try
        {
            var failure = await Record.ExceptionAsync(() => queue.FinishDiscardAsync(job.Id, (_, _) =>
            {
                cleanupCalls++;
                File.Move(journal, saved);
                Directory.CreateDirectory(journal);
                return Task.CompletedTask;
            }));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.False(terminalPublished);
            Assert.True(queue.Snapshot().Single().DiscardRequested);
            Assert.False(queue.Snapshot().Single().DiscardCompleted);
        }
        finally
        {
            if (Directory.Exists(journal)) Directory.Delete(journal);
            if (File.Exists(saved)) File.Move(saved, journal);
        }
        // Simulate idempotent cleanup after the first cleanup succeeded but its receipt did not save.
        await queue.FinishDiscardAsync(job.Id, (_, _) => { cleanupCalls++; return Task.CompletedTask; });
        Assert.Equal(2, cleanupCalls);
        Assert.True(terminalPublished);
        Assert.True(queue.Snapshot().Single().DiscardCompleted);
    }

    [Fact] public async Task FailedDiscardCleanupStaysPendingAndSuccessfulRetryIsIdempotent()
    {
        var app = App; var plan = Plan("Discard outcome"); var source = plan.Projects.Single().Creation!;
        var job = new ImportJob("discard-outcome", plan, source, "Paused", "Paused", 40, "", "") { DiscardRequested = true };
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { job });
        await using var queue = new ImportQueue(app); await queue.InitializeAsync();
        await Assert.ThrowsAnyAsync<IOException>(() => queue.FinishDiscardAsync(job.Id, (_, _) => throw new IOException("fixture failure")));
        Assert.True(queue.Snapshot().Single().DiscardRequested);
        Assert.False(queue.Snapshot().Single().DiscardCompleted);
        var calls = 0;
        await queue.FinishDiscardAsync(job.Id, (_, _) => { calls++; return Task.CompletedTask; });
        Assert.True(queue.Snapshot().Single().DiscardCompleted);
        Assert.False(queue.Snapshot().Single().Recoverable);
        await queue.FinishDiscardAsync(job.Id, (_, _) => { calls++; return Task.CompletedTask; });
        Assert.Equal(1, calls);
        var saved = await AtomicFile.ReadJsonAsync<ImportJob[]>(Path.Combine(app.Paths.Home, "imports.json"));
        Assert.True(saved!.Single().DiscardCompleted);
    }

    [Fact] public async Task WaitingForOneImportDoesNotTreatTheStopRequestAsWorkerExit()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var queue = new ImportQueue(App, async (plan, _, token) =>
        {
            entered.TrySetResult(); await release.Task;
            token.ThrowIfCancellationRequested(); return Completed(plan);
        });
        try
        {
            await queue.EnqueueAsync(Plan("Slow worker"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var id = queue.Snapshot().Single().Id;
            await queue.RequestDiscardAsync(id);
            using var cancellation = new CancellationTokenSource();
            var stopped = queue.WaitForStoppedAsync(id, cancellation.Token);
            Assert.False(stopped.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped);
            Assert.True(queue.HasActiveJobs);
            release.TrySetResult();
            await queue.WaitForStoppedAsync(id).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(queue.HasActiveJobs);
            Assert.True(queue.Snapshot().Single().DiscardRequested);
        }
        finally { release.TrySetResult(); }
    }

    [Fact] public async Task DiscardIntentSurvivesRestartReservesTheTargetAndCannotBeHidden()
    {
        var app = App; var plan = Plan("Discard"); var source = plan.Projects.Single().Creation!;
        var job = new ImportJob("discard", plan, source, "Paused", "Paused", 40, "", "");
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { job });
        await using (var queue = new ImportQueue(app))
        {
            await queue.InitializeAsync();
            await queue.RequestDiscardAsync(job.Id);
            Assert.True(queue.Snapshot().Single().DiscardRequested);
            Assert.False(queue.Snapshot().Single().Recoverable);
            await queue.ClearFinishedAsync(); await queue.DismissAsync(job.Id);
            Assert.False(queue.Snapshot().Single().HiddenFromImports);
        }
        await using var restarted = new ImportQueue(app);
        await restarted.InitializeAsync();
        Assert.True(restarted.Snapshot().Single().DiscardRequested);
        Assert.False(restarted.Snapshot().Single().Active);
        Assert.False(restarted.Snapshot().Single().Recoverable);
        await Assert.ThrowsAnyAsync<IOException>(() => restarted.EnqueueAsync(plan));
        Assert.Single(restarted.Snapshot());
        Assert.False(Directory.Exists(source.TargetPath));
    }

    [Fact] public void CurrentImportPrefersActiveWorkAndDoesNotReviveAnOlderPausedImport()
    {
        var plan = Plan("History"); var source = plan.Projects.Single().Creation!;
        var paused = new ImportJob("paused", plan, source, "Paused", "Paused", 40, "", "");
        var completed = paused with { Id = "completed", State = "Completed", HiddenFromImports = true,
            Plan = plan with { CreatedUtc = plan.CreatedUtc.AddSeconds(1) } };
        var active = paused with { Id = "active", State = "Running" };
        Assert.Equal(completed.Id, ImportQueue.CurrentForProject([paused, completed], source.TargetPath.ToUpperInvariant())?.Id);
        Assert.Equal(active.Id, ImportQueue.CurrentForProject([completed, paused, active], source.TargetPath)?.Id);
        var afterClockChange = completed with { Id = "clock-change", Plan = plan with { CreatedUtc = plan.CreatedUtc.AddHours(-1) } };
        Assert.Equal(afterClockChange.Id, ImportQueue.CurrentForProject([paused, completed, afterClockChange], source.TargetPath)?.Id);
        Assert.Null(ImportQueue.CurrentForProject([paused, completed], Path.Combine(root, "Other")));
    }

    [Fact] public async Task ContinuationChecksItsPredecessorInsideTheQueueBeforeAddingWork()
    {
        var app = App; var plan = Plan("History"); var source = plan.Projects.Single().Creation!;
        var paused = new ImportJob("paused", plan, source, "Paused", "Paused", 40, "", "");
        var completed = paused with { Id = "completed", State = "Completed",
            Plan = plan with { CreatedUtc = plan.CreatedUtc.AddSeconds(1) } };
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { paused, completed });
        var executed = 0;
        await using var queue = new ImportQueue(app, (p, _, _) => { Interlocked.Increment(ref executed); return Task.FromResult(Completed(p)); });
        await queue.InitializeAsync();
        await Assert.ThrowsAnyAsync<IOException>(() => queue.EnqueueAsync(plan, expectedImportId: paused.Id));
        Assert.Equal(2, queue.Snapshot().Length);
        Assert.Equal(0, executed);
        await queue.EnqueueAsync(plan, expectedImportId: completed.Id);
        await WaitUntil(() => !queue.HasActiveJobs);
        Assert.Equal(1, executed);
        await Assert.ThrowsAnyAsync<IOException>(() => queue.EnqueueAsync(plan, expectedImportId: completed.Id));
        Assert.Equal(3, queue.Snapshot().Length);
        Assert.Equal(1, executed);
    }

    [Fact] public async Task CancellingShutdownWaitPreservesTheLeaseAndTheActiveWorker()
    {
        var app = App;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var queue = new ImportQueue(app, async (plan, _, token) =>
        {
            using var registration = token.Register(() => stopRequested.TrySetResult());
            entered.TrySetResult();
            await release.Task; // A worker may take time to acknowledge its stop request.
            token.ThrowIfCancellationRequested();
            return Completed(plan);
        });
        using var wait = new CancellationTokenSource();
        try
        {
            await queue.EnqueueAsync(Plan("Slow stop"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shutdown = queue.ShutdownAsync(wait.Token);
            await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wait.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shutdown);
            Assert.True(queue.HasActiveJobs);
            Assert.Equal("Pausing", queue.Snapshot().Single().State);
            await using (var competing = new ImportQueue(app))
                await Assert.ThrowsAnyAsync<IOException>(() => competing.InitializeAsync());
            release.TrySetResult();
            await WaitUntil(() => !queue.HasActiveJobs);
            Assert.Equal("Paused", queue.Snapshot().Single().State);
            await queue.ShutdownAsync();
            await using var reopened = new ImportQueue(app);
            await reopened.InitializeAsync();
            Assert.Equal("Paused", reopened.Snapshot().Single().State);
        }
        finally { release.TrySetResult(); }
    }

    [Fact] public async Task FailedPauseJournalWriteDoesNotStopTheWorkerOrLeavePauseIntent()
    {
        var app = App;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        await using var queue = new ImportQueue(app, async (plan, _, token) =>
        {
            using var registration = token.Register(() => cancelled = true);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Completed(plan);
        });
        try
        {
            await queue.EnqueueAsync(Plan("Unsaved pause"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var journal = Path.Combine(app.Paths.Home, "imports.json");
            var saved = journal + ".saved";
            File.Move(journal, saved);
            Directory.CreateDirectory(journal); // An isolated fixture blocks replacement deterministically.
            try
            {
                var failure = await Record.ExceptionAsync(() => queue.PauseAsync(queue.Snapshot().Single().Id));
                Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
                Assert.False(cancelled);
                Assert.False(queue.Snapshot().Single().PauseRequested);
                Assert.Equal("Running", queue.Snapshot().Single().State);
            }
            finally
            {
                Directory.Delete(journal);
                File.Move(saved, journal);
            }
        }
        finally { release.TrySetResult(); }
        await WaitUntil(() => !queue.HasActiveJobs);
    }

    [Fact] public async Task PausingAQueuedImportDoesNotExecuteIt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<string>();
        await using var queue = new ImportQueue(App, async (plan, _, token) =>
        {
            lock (executed) executed.Add(plan.Projects.Single().Name);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Completed(plan);
        });
        try
        {
            await queue.EnqueueAsync(Plan("First", "Queued"), 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = queue.Snapshot().Single(x => x.Name == "Queued");
            Assert.Equal("Queued", queued.State);
            await queue.PauseAsync(queued.Id);
            await WaitUntil(() => queue.Snapshot().Single(x => x.Id == queued.Id).State == "Paused");
            release.TrySetResult();
            await WaitUntil(() => !queue.HasActiveJobs);
            lock (executed) Assert.Equal(new[] { "First" }, executed);
        }
        finally { release.TrySetResult(); }
    }

    [Fact] public async Task PauseWaitsForTheWorkerAndDoesNotOverwriteACompletedResult()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var queue = new ImportQueue(App, async (plan, _, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            // Deliberately delay acknowledgement: a cancellation request is not an exit.
            await release.Task;
            return Completed(plan);
        });
        try
        {
            await queue.EnqueueAsync(Plan("Completing"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var id = queue.Snapshot().Single().Id;
            await queue.PauseAsync(id);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Pausing", queue.Snapshot().Single().State);
            Assert.True(queue.HasActiveJobs);
            await queue.ClearFinishedAsync();
            Assert.False(queue.Snapshot().Single().HiddenFromImports);
            release.TrySetResult();
            await WaitUntil(() => !queue.HasActiveJobs);
            Assert.Equal("Completed", queue.Snapshot().Single().State);
            Assert.Equal(100, queue.Snapshot().Single().Progress);
        }
        finally { release.TrySetResult(); }
    }

    [Fact] public async Task PauseIntentIsOnDiskBeforeTheWorkerStopsAndSurvivesRestart()
    {
        var app = App;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<ImportJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var queue = new ImportQueue(app, async (plan, _, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Completed(plan); }
            finally
            {
                var journal = await AtomicFile.ReadJsonAsync<ImportJob[]>(Path.Combine(app.Paths.Home, "imports.json"));
                stopped.TrySetResult(journal!.Single());
            }
        }))
        {
            await queue.EnqueueAsync(Plan("Paused"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await queue.PauseAsync(queue.Snapshot().Single().Id);
            Assert.True((await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5))).PauseRequested);
            await WaitUntil(() => !queue.HasActiveJobs);
            Assert.Equal("Paused", queue.Snapshot().Single().State);
            await queue.ClearFinishedAsync(); await queue.DismissAsync(queue.Snapshot().Single().Id);
            Assert.False(queue.Snapshot().Single().HiddenFromImports);
        }
        await using var reopened = new ImportQueue(app);
        await reopened.InitializeAsync();
        Assert.Equal("Paused", reopened.Snapshot().Single().State);
        Assert.True(reopened.Snapshot().Single().Recoverable);
        Assert.False(reopened.Snapshot().Single().Active);
    }

    [Fact] public async Task RestartAfterPersistedPauseRequestDoesNotResumeAutomatically()
    {
        var app = App; var plan = Plan("Interrupted pause");
        var job = new ImportJob("paused", plan, plan.Projects.Single().Creation!, "Pausing", "Pausing", 35, "", "") { PauseRequested = true };
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), new[] { job });
        var executed = 0;
        await using var queue = new ImportQueue(app, (p, _, _) => { executed++; return Task.FromResult(Completed(p)); });
        await queue.InitializeAsync();
        Assert.Equal(0, executed); Assert.Equal("Paused", queue.Snapshot().Single().State);
        Assert.Equal(job.Source.UnityPackagePath, queue.Snapshot().Single().Source.UnityPackagePath);
    }

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
    [Fact] public async Task ClearFinishedHidesCompletedFailedAndInterruptedRowsButRetainsRecoveryAfterRestart()
    {
        var app = App; var plan = Plan("Complete", "Failed", "Active");
        var log = Path.Combine(app.Paths.Logs, "kept.log"); Directory.CreateDirectory(app.Paths.Logs); await File.WriteAllTextAsync(log, "evidence");
        var jobs = plan.Projects.Select((p, i) => new ImportJob("job" + i, new("op" + i, plan.CreatedUtc, [p]), p.Creation!,
            new[] { "Completed", "Failed", "Running" }[i], "Import", 50, "message", log)).ToArray();
        await AtomicFile.WriteJsonAsync(Path.Combine(app.Paths.Home, "imports.json"), jobs);
        await using (var queue = new ImportQueue(app))
        {
            await queue.InitializeAsync(); await queue.ClearFinishedAsync();
            Assert.Equal(3, queue.Snapshot().Length);
            Assert.True(queue.Snapshot().Single(x => x.Id == "job0").HiddenFromImports);
            Assert.All(queue.Snapshot(), x => Assert.True(x.HiddenFromImports));
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
        await queue.ClearFinishedAsync();
        Assert.All(queue.Snapshot(), job => { Assert.True(job.Active); Assert.False(job.HiddenFromImports); });
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
