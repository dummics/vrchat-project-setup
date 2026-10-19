using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed record ImportJob(string Id, OperationPlan Plan, CreationRequest Source, string State, string Step, int Progress, string Message, string LogFile,
    DateTime? StartedAt = null, DateTime? FinishedAt = null, bool HiddenFromImports = false)
{
    public string Name => Source.Name;
    public string ProjectPath => Source.TargetPath;
    public bool Active => State is "Queued" or "Running" or "Cancelling";
    public bool Recoverable => State is "Failed" or "Cancelled" or "Interrupted";
}

// A durable, opt-in queue around the same reviewed plans used by both clients.
// No startup auto-resume and no project deletion/cache deletion on cancellation.
public sealed class ImportQueue(VrcSetupApplication app, Func<OperationPlan, IProgress<OperationEvent>?, CancellationToken, Task<OperationReport>>? execute = null) : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim journalGate = new(1);
    private readonly List<ImportJob> jobs = [];
    private readonly Dictionary<string, CancellationTokenSource> cancellations = [];
    private readonly List<Task> workers = [];
    private readonly Dictionary<string, long> lastPublication = [];
    private readonly HashSet<string> claimed = [];
    private TaskCompletionSource capacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource monitoring = new();
    private Task? resourceWorker;
    private SystemResourceUsage? resources;
    private int requestedParallelism = 1;
    public SystemResourceUsage? Resources { get { lock (sync) return resources; } }
    public int EffectiveParallelism { get { lock (sync) return requestedParallelism > 0 ? requestedParallelism : resources?.SuggestedParallelism ?? 1; } }
    public void ConfigureParallelism(int value)
    {
        if (value < 0) throw new ArgumentException("Use zero for automatic scheduling or a positive parallel import count.");
        lock (sync) { requestedParallelism = value; PulseCapacity(); }
        Publish();
    }
    private void PulseCapacity()
    {
        var previous = capacityChanged;
        capacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously); previous.TrySetResult();
    }
    private async Task ClaimAsync(string id, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested(); Task changed;
            lock (sync)
            {
                var first = jobs.FirstOrDefault(x => x.State == "Queued" && !claimed.Contains(x.Id));
                if (claimed.Count < EffectiveParallelism && first?.Id == id)
                { claimed.Add(id); PulseCapacity(); return; }
                changed = capacityChanged.Task;
            }
            await changed.WaitAsync(token);
        }
    }
    private async Task MonitorResourcesAsync()
    {
        var sampler = new SystemResourceSampler();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            do
            {
                var usage = sampler.Sample();
                lock (sync) { resources = usage; if (requestedParallelism == 0) PulseCapacity(); }
                Publish();
            } while (await timer.WaitForNextTickAsync(monitoring.Token));
        }
        catch (OperationCanceledException) { }
    }
    private FileStream? lease;
    private bool initialized, stopping;
    private string Journal => Path.Combine(app.Paths.Home, "imports.json");
    public event Action<ImportJob[]>? Changed;
    public ImportJob[] Snapshot() { lock (sync) return jobs.ToArray(); }
    public bool HasActiveJobs => Snapshot().Any(x => x.Active);

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await journalGate.WaitAsync(token);
        try
        {
            if (initialized) return;
            try { lease = await AtomicFile.LockAsync(Path.Combine(app.Paths.Home, "imports.lock"), token, TimeSpan.FromSeconds(1)); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            { throw new ImportQueueInUseException(error); }
            try
            {
                if (File.Exists(Journal) && new FileInfo(Journal).Length > 32 * 1024 * 1024) throw new IOException("Import history is too large to load safely.");
                var saved = await AtomicFile.ReadJsonAsync<ImportJob[]>(Journal, token) ?? [];
                if (saved.Any(x => x is null || x.Plan?.Projects.Length != 1 || x.Source is null) || saved.Select(x => x.Id).Distinct().Count() != saved.Length)
                    throw new IOException("Import history is invalid; the original file was preserved.");
                foreach (var entry in saved)
                {
                    var job = entry.Active ? entry with { State = "Interrupted", Step = "Interrupted", Message = "Review recovery before continuing." } : entry;
                    if (job.Recoverable)
                    {
                        try
                        {
                            var marker = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(job.ProjectPath, ".vrcsetup", "state.json"), token);
                            if (marker?.Completed == true && !string.IsNullOrWhiteSpace(marker.UnityPackagePath) && ProjectRules.NormalizePath(marker.UnityPackagePath).Equals(ProjectRules.NormalizePath(job.Plan.Projects[0].RecoveryOriginalSource ?? job.Source.UnityPackagePath), StringComparison.OrdinalIgnoreCase))
                                job = job with { State = "Recovered", Step = "Completed", Progress = 100, FinishedAt = marker.CompletedAt, Message = "Project recovery verified." };
                        }
                        catch (Exception error) when (error is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or ArgumentException) { app.Diagnostics.Write("Import state", error.Message); }
                    }
                    lock (sync) jobs.Add(job);
                }
                await SaveAsync(token); initialized = true;
                resourceWorker = Task.Run(MonitorResourcesAsync);
            }
            catch { lease.Dispose(); lease = null; lock (sync) jobs.Clear(); throw; }
        }
        finally { journalGate.Release(); }
        Publish();
    }

    public async Task EnqueueAsync(OperationPlan reviewed, int parallelism = 1, CreationRequest? recoverySource = null, CancellationToken token = default)
    {
        await InitializeAsync(token);
        if (parallelism < 0) throw new ArgumentException("Use zero for automatic scheduling or a positive parallel import count.");
        if (reviewed.Projects.Length == 0 || reviewed.Projects.Select(x => ProjectRules.NormalizePath(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != reviewed.Projects.Length)
            throw new ArgumentException("Each import needs a distinct project destination.");
        var pending = reviewed.Projects.Select(project =>
        {
            var source = project.Creation ?? project.RecoverySource ?? (reviewed.Projects.Length == 1 ? recoverySource : null) ?? throw new ArgumentException("Import source missing.");
            if (!ProjectRules.NormalizePath(project.Path).Equals(ProjectRules.NormalizePath(source.TargetPath), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Import source and project do not match.");
            var id = Guid.NewGuid().ToString("N");
            return new ImportJob(id, new(id, DateTime.UtcNow, [project]), source, "Queued", "Waiting", 0, "", "");
        }).ToArray();
        await journalGate.WaitAsync(token);
        try
        {
            lock (sync)
            {
                if (stopping) throw new IOException("Import queue is closing.");
                var active = jobs.Where(x => x.Active).Select(x => ProjectRules.NormalizePath(x.ProjectPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (pending.Any(x => active.Contains(ProjectRules.NormalizePath(x.ProjectPath)))) throw new IOException("An import for this project is already queued.");
                requestedParallelism = parallelism;
                jobs.AddRange(pending);
                foreach (var item in pending) cancellations[item.Id] = new();
            }
            try { await SaveAsync(token); }
            catch
            {
                lock (sync)
                {
                    jobs.RemoveAll(x => pending.Any(p => p.Id == x.Id));
                    foreach (var item in pending) if (cancellations.Remove(item.Id, out var cancellation)) cancellation.Dispose();
                }
                throw;
            }
            lock (sync)
            {
                workers.RemoveAll(x => x.IsCompletedSuccessfully);
                foreach (var item in pending)
                {
                    var cancellation = cancellations[item.Id];
                    workers.Add(Task.Run(() => RunAsync(item, cancellation)));
                }
                PulseCapacity();
            }
        }
        finally { journalGate.Release(); }
        Publish();
    }

    private async Task RunAsync(ImportJob item, CancellationTokenSource cancellation)
    {
        var acquired = false;
        try
        {
            await ClaimAsync(item.Id, cancellation.Token); acquired = true; cancellation.Token.ThrowIfCancellationRequested();
            Update(item.Id, x => x with { State = "Running", Step = "Starting", Message = "", StartedAt = DateTime.UtcNow });
            await PersistAsync();
            var progress = new InlineProgress(value => Update(item.Id, x => x with
            { Step = Phase(value.Phase), Progress = Math.Max(x.Progress, Percent(value.Phase)), Message = value.Message,
                LogFile = x.LogFile.Length > 0 ? x.LogFile : FindLog(item.Id) }));
            var report = execute is null ? await app.ApplyAsync(item.Plan, progress, cancellation.Token, rememberCreation: false) : await execute(item.Plan, progress, cancellation.Token);
            var outcome = report.Results.FirstOrDefault();
            var state = report.Success ? "Completed" : report.Cancelled ? "Cancelled" : "Failed";
            Update(item.Id, x => x with { State = state, Step = state, Progress = report.Success ? 100 : x.Progress,
                Message = outcome?.Message ?? "Import did not complete.", LogFile = outcome?.LogFile ?? x.LogFile });
        }
        catch (OperationCanceledException) { Update(item.Id, x => x with { State = "Cancelled", Step = "Cancelled", Message = "Import cancelled; project data preserved." }); }
        catch (Exception error)
        {
            app.Diagnostics.Write("Import queue", error.Message, error);
            Update(item.Id, x => x with { State = "Failed", Step = "Failed", Message = error.Message });
        }
        finally
        {
            lock (sync) { if (acquired) claimed.Remove(item.Id); cancellations.Remove(item.Id); PulseCapacity(); }
            cancellation.Dispose();
            await PersistAsync();
        }
    }

    public void Cancel(string id)
    {
        lock (sync)
            if (cancellations.TryGetValue(id, out var cancellation))
            {
                var index = jobs.FindIndex(x => x.Id == id);
                if (index >= 0 && jobs[index].Active) jobs[index] = jobs[index] with { State = "Cancelling", Step = "Stopping safely" };
                cancellation.Cancel();
                PulseCapacity();
            }
        Publish();
    }
    public void CancelAll() { foreach (var job in Snapshot().Where(x => x.Active)) Cancel(job.Id); }

    public async Task<OperationPlan> RecoveryPlanAsync(string id, AppConfiguration configuration, CancellationToken token = default)
    {
        var job = Snapshot().Single(x => x.Id == id);
        if (!job.Recoverable) throw new IOException("This import does not need recovery.");
        var project = job.Plan.Projects.Single(); var source = job.Source;
        UnityProjectDriver.ValidateEditor(source.UnityEditorPath);
        foreach (var platform in source.PublishingPlatforms.Append(source.BuildTarget).Distinct())
            if (!UnityProjectDriver.HasBuildSupport(source.UnityEditorPath, platform)) throw new IOException("Unity build support is missing for " + platform);
        var imports = source.ExtraImports.Concat(project.ExtraImports).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ProjectPlan planned;
        if (!Directory.Exists(project.Path))
            planned = await app.Planner.PlanAsync(project.Path, project.DesiredPolicy, configuration, exact: true, preset: project.PresetName,
                creation: source, extraImports: imports, cancellationToken: token);
        else
        {
            planned = await app.PlanResumeAsync(source, project.DesiredPolicy, configuration, project.PresetName, imports, token, project.RecoveryOriginalSource);
        }
        return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [planned]);
    }

    public async Task ClearCompletedAsync(CancellationToken token = default)
    {
        await InitializeAsync(token); await journalGate.WaitAsync(token);
        try
        {
            lock (sync)
                for (var index = 0; index < jobs.Count; index++)
                    if (jobs[index].State is "Completed" or "Recovered") jobs[index] = jobs[index] with { HiddenFromImports = true };
            await SaveAsync(token);
        }
        finally { journalGate.Release(); }
        Publish();
    }
    private void Update(string id, Func<ImportJob, ImportJob> change)
    {
        var publish = false;
        lock (sync)
        {
            var index = jobs.FindIndex(x => x.Id == id);
            if (index >= 0)
            {
                var previous = jobs[index]; var next = change(previous);
                if (previous.Active && !next.Active) next = next with { FinishedAt = DateTime.UtcNow };
                jobs[index] = next;
                var now = Environment.TickCount64;
                publish = previous.State != next.State || previous.Step != next.Step || now - lastPublication.GetValueOrDefault(id) >= 200;
                if (publish) lastPublication[id] = now;
            }
        }
        if (publish) Publish();
    }
    private void Publish() => Changed?.Invoke(Snapshot());
    private Task SaveAsync(CancellationToken token = default) => AtomicFile.WriteJsonAsync(Journal, Snapshot(), token);
    private async Task PersistAsync()
    {
        await journalGate.WaitAsync();
        try { await SaveAsync(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { app.Diagnostics.Write("Import history", error.Message, error); }
        finally { journalGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        await journalGate.WaitAsync();
        try { lock (sync) { stopping = true; pending = workers.ToArray(); } }
        finally { journalGate.Release(); }
        CancelAll(); await Task.WhenAll(pending);
        monitoring.Cancel(); if (resourceWorker is not null) await resourceWorker;
        lease?.Dispose(); lease = null;
    }
    private string FindLog(string id)
    {
        try { return Directory.Exists(app.Paths.Logs) ? Directory.EnumerateFiles(app.Paths.Logs, "vrcsetup-*-" + id + ".log").FirstOrDefault() ?? "" : ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ""; }
    }
    public async Task<(string Text, string Stamp)> ReadLogTailAsync(string id, string previousStamp = "", CancellationToken token = default)
    {
        var job = Snapshot().FirstOrDefault(x => x.Id == id);
        if (job is null) return ("Import not found.", "");
        return await Task.Run(async () =>
        {
            var file = job.LogFile.Length > 0 ? job.LogFile : FindLog(id);
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return ("Log becomes available when the import starts.", "");
            ProjectRules.EnsureContained(app.Paths.Logs, file);
            var info = new FileInfo(file); var stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            if (stamp == previousStamp) return ("", stamp);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            var offset = Math.Max(0, stream.Length - 65536); stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            if (offset > 0) await reader.ReadLineAsync(token);
            var buffer = new char[65536]; var count = await reader.ReadBlockAsync(buffer, token);
            return (new string(buffer, 0, count), stamp);
        }, token);
    }
    private static string Phase(string phase) => phase switch
    { "Creating" or "Unity create" => "Create", "Prepare" or "Repository" => "Prepare", "Packages" or "VPM" or "Repair" => "Packages", "Import" or "Unity import" => "Import", "Post-import validation" or "Unity settle" => "Validate", "Completed" => "Finish", _ => phase };
    private static int Percent(string phase) => Phase(phase) switch
    { "Create" => 10, "Backup" or "Prepare" => 20, "Packages" => 35, "Import" => 55, "Validate" => 85, "Finish" => 95, _ => 0 };
    private sealed class InlineProgress(Action<OperationEvent> report) : IProgress<OperationEvent>
    { public void Report(OperationEvent value) => report(value); }
}
