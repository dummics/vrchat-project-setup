namespace VrcSetup.Infrastructure;

public sealed partial class ImportQueue
{
    // cleanup must be an idempotent, ownership-checked implementation. No production
    // consumer is wired until that implementation and its recovery are verified.
    public async Task FinishDiscardAsync(string id, Func<ImportJob, CancellationToken, Task> cleanup, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        await disposalGate.WaitAsync(token);
        try
        {
            await WaitForStoppedAsync(id, token);
            ImportJob job;
            lock (sync)
            {
                if (stopping || lease is null) throw new ObjectDisposedException(nameof(ImportQueue));
                job = jobs.Single(x => x.Id == id);
                if (job.DiscardCompleted) return;
                if (!job.DiscardRequested || CurrentForProject(jobs, job.ProjectPath)?.Id != id)
                    throw new IOException("This import changed. Choose it again to continue.");
            }
            // Do not hold the journal gate while cleaning: other projects must be
            // able to finish their own worker persistence independently.
            await cleanup(job, token);
            await journalGate.WaitAsync(token);
            try
            {
                var completed = job with { DiscardRequested = false, DiscardCompleted = true,
                    State = "Cancelled", Step = "Cancelled", Message = "Import cancelled.", FinishedAt = DateTime.UtcNow };
                var snapshot = Snapshot();
                snapshot[Array.FindIndex(snapshot, x => x.Id == id)] = completed;
                await AtomicFile.WriteJsonAsync(Journal, snapshot, token);
                lock (sync) jobs[jobs.FindIndex(x => x.Id == id)] = completed;
            }
            finally { journalGate.Release(); Publish(); }
        }
        finally { disposalGate.Release(); }
    }

    public async Task WaitForStoppedAsync(string id, CancellationToken token = default)
    {
        await InitializeAsync(token);
        Task? worker;
        lock (sync)
        {
            var job = jobs.SingleOrDefault(x => x.Id == id) ?? throw new IOException("Import not found.");
            jobWorkers.TryGetValue(id, out worker);
            if (worker is null && job.Active) throw new IOException("The import has not stopped.");
        }
        if (worker is not null) await worker.WaitAsync(token);
        lock (sync)
            if (jobs.Single(x => x.Id == id).Active) throw new IOException("The import has not stopped.");
    }

    // Persist intent before requesting stop. Cleanup is a separate, verified transaction.
    public async Task RequestDiscardAsync(string id, CancellationToken token = default)
    {
        await InitializeAsync(token);
        await journalGate.WaitAsync(token);
        ImportJob? previous = null;
        try
        {
            lock (sync)
            {
                if (stopping || lease is null) throw new ObjectDisposedException(nameof(ImportQueue));
                var index = jobs.FindIndex(x => x.Id == id);
                if (index < 0) throw new IOException("Import not found.");
                previous = jobs[index];
                if (previous.DiscardCompleted) return;
                if (CurrentForProject(jobs, previous.ProjectPath)?.Id != id)
                    throw new IOException("This import changed. Choose it again to continue.");
                jobs[index] = previous with { DiscardRequested = true, HiddenFromImports = false };
            }
            try { await SaveAsync(token); }
            catch
            {
                lock (sync)
                {
                    var index = jobs.FindIndex(x => x.Id == id);
                    if (index >= 0) jobs[index] = jobs[index] with
                    { DiscardRequested = previous.DiscardRequested, HiddenFromImports = previous.HiddenFromImports };
                }
                throw;
            }
        }
        finally { journalGate.Release(); Publish(); }
        await PauseAsync(id, token);
    }
}
