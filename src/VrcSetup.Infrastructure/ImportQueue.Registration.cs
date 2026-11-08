using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class ImportQueue
{
    // Registration is optional. Retrying it must never execute the import plan again.
    public async Task<bool> RetryRegistrationAsync(string id, Func<ImportJob, CancellationToken, Task<bool>> register, CancellationToken token = default)
    {
        await InitializeAsync(token);
        await disposalGate.WaitAsync(token);
        try
        {
            ImportJob job;
            lock (sync)
            {
                if (stopping || lease is null) throw new ObjectDisposedException(nameof(ImportQueue));
                job = jobs.Single(x => x.Id == id);
                if (job.State is not ("Completed" or "Recovered") || job.DiscardRequested || job.DiscardCompleted || !job.CreatorCompanionPending || CurrentForProject(jobs, job.ProjectPath)?.Id != id) return false;
            }
            using var projectLease = await app.Projects.AcquireAsync(job.ProjectPath, token);
            lock (sync) if (CurrentForProject(jobs, job.ProjectPath)?.Id != id) return false;
            if (!await register(job, token)) return false;
            await journalGate.WaitAsync(token);
            try
            {
                ImportJob[] candidate;
                lock (sync)
                {
                    if (CurrentForProject(jobs, job.ProjectPath)?.Id != id) return false;
                    candidate = jobs.Select(x => x.Id != id ? x : x with
                    {
                        CreatorCompanionPending = false,
                        CreatorCompanionUnknown = false,
                        Message = x.Message == "Project ready; Creator Companion registration failed. Add the existing project in VCC." ? "" : x.Message
                    }).ToArray();
                }
                await AtomicFile.WriteJsonAsync(Journal, candidate, token);
                lock (sync)
                {
                    var index = jobs.FindIndex(x => x.Id == id);
                    jobs[index] = candidate.Single(x => x.Id == id);
                }
            }
            finally { journalGate.Release(); }
            Publish();
            return true;
        }
        finally { disposalGate.Release(); }
    }
}
