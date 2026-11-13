namespace VrcSetup.Infrastructure;

public sealed partial class ImportQueue
{
    public Task PauseAsync(string id, CancellationToken token = default) => RequestPauseAsync(id, false, token);

    private async Task RequestPauseAsync(string id, bool closing, CancellationToken token = default)
    {
        lock (sync) if (stopping && !closing) throw new ObjectDisposedException(nameof(ImportQueue));
        await InitializeAsync(token);
        await journalGate.WaitAsync(token);
        ImportJob? previous = null;
        try
        {
            lock (sync)
            {
                if ((stopping && !closing) || lease is null) throw new ObjectDisposedException(nameof(ImportQueue));
                var index = jobs.FindIndex(x => x.Id == id);
                if (index < 0 || !jobs[index].Active || jobs[index].State is "Pausing" or "Cancelling") return;
                previous = jobs[index];
                jobs[index] = previous with { PauseRequested = true, State = "Pausing", Message = "Pausing…" };
                PulseCapacity();
            }
            try { await SaveAsync(token); }
            catch
            {
                lock (sync)
                {
                    var index = jobs.FindIndex(x => x.Id == id);
                    if (index >= 0)
                    {
                        var current = jobs[index];
                        jobs[index] = current with { PauseRequested = previous.PauseRequested,
                            State = current.State == "Pausing" ? previous.State : current.State,
                            Step = current.Step == "Pausing" ? previous.Step : current.Step,
                            Message = current.Message == "Pausing…" ? previous.Message : current.Message };
                        PulseCapacity();
                    }
                }
                throw;
            }
            // The durable intent exists before a cooperative stop is requested.
            lock (sync)
            {
                if (cancellations.TryGetValue(id, out var cancellation)) cancellation.Cancel();
                PulseCapacity();
            }
        }
        finally { journalGate.Release(); Publish(); }
    }

    public async Task PauseAllAsync(CancellationToken token = default)
    {
        await InitializeAsync(token);
        foreach (var job in Snapshot().Where(x => x.Active)) await PauseAsync(job.Id, token);
    }
}
