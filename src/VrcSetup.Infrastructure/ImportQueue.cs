using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed record ImportJob(string Id, OperationPlan Plan, CreationRequest Source, string State, string Step, int Progress, string Message, string LogFile)
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
    private SemaphoreSlim slots = new(1);
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
            lease = await AtomicFile.LockAsync(Path.Combine(app.Paths.Home, "imports.lock"), token, TimeSpan.FromSeconds(1));
            try
            {
                if (File.Exists(Journal) && new FileInfo(Journal).Length > 32 * 1024 * 1024) throw new IOException("Import history is too large to load safely.");
                var saved = await AtomicFile.ReadJsonAsync<ImportJob[]>(Journal, token) ?? [];
                if (saved.Any(x => x is null || x.Plan?.Projects.Length != 1 || x.Source is null) || saved.Select(x => x.Id).Distinct().Count() != saved.Length)
                    throw new IOException("Import history is invalid; the original file was preserved.");
                lock (sync) jobs.AddRange(saved.Select(x => x.Active ? x with { State = "Interrupted", Step = "Interrupted", Message = "Review recovery before continuing." } : x));
                await SaveAsync(token); initialized = true;
            }
            catch { lease.Dispose(); lease = null; lock (sync) jobs.Clear(); throw; }
        }
        finally { journalGate.Release(); }
        Publish();
    }

    public async Task EnqueueAsync(OperationPlan reviewed, int parallelism = 1, CreationRequest? recoverySource = null, CancellationToken token = default)
    {
        await InitializeAsync(token);
        if (parallelism is < 1 or > 2) throw new ArgumentException("Choose one or two parallel imports.");
        if (reviewed.Projects.Length == 0 || reviewed.Projects.Select(x => ProjectRules.NormalizePath(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != reviewed.Projects.Length)
            throw new ArgumentException("Each import needs a distinct project destination.");
        var pending = reviewed.Projects.Select(project =>
        {
            var source = project.Creation ?? (reviewed.Projects.Length == 1 ? recoverySource : null) ?? throw new ArgumentException("Import source missing.");
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
                if (jobs.Count + pending.Length > 2000) throw new IOException("Clear completed import history before adding more imports.");
                var active = jobs.Where(x => x.Active).Select(x => ProjectRules.NormalizePath(x.ProjectPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (pending.Any(x => active.Contains(ProjectRules.NormalizePath(x.ProjectPath)))) throw new IOException("An import for this project is already queued.");
                if (cancellations.Count == 0 && !jobs.Any(x => x.Active)) { slots.Dispose(); slots = new(parallelism); }
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
                    var permission = slots.WaitAsync(cancellation.Token);
                    workers.Add(Task.Run(() => RunAsync(item, cancellation, permission)));
                }
            }
        }
        finally { journalGate.Release(); }
        Publish();
    }

    private async Task RunAsync(ImportJob item, CancellationTokenSource cancellation, Task permission)
    {
        var acquired = false;
        try
        {
            await permission; acquired = true; cancellation.Token.ThrowIfCancellationRequested();
            Update(item.Id, x => x with { State = "Running", Step = "Starting", Message = "" });
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
            lock (sync) { if (acquired) slots.Release(); cancellations.Remove(item.Id); }
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
            var state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(project.Path, ".vrcsetup", "state.json"), token);
            if (state is null || state.Completed || !ProjectRules.NormalizePath(state.UnityPackagePath).Equals(ProjectRules.NormalizePath(source.UnityPackagePath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Recovery refused: this folder is not the unfinished import. Open its log or manage the project manually.");
            if (!Directory.Exists(Path.Combine(project.Path, "Assets")) || !File.Exists(Path.Combine(project.Path, "Packages", "manifest.json")))
                throw new IOException("Unity project creation was interrupted before initialization. Preserve this folder and choose a new project name in Create.");
            planned = await app.Planner.PlanAsync(project.Path, project.DesiredPolicy, configuration, exact: true, preset: project.PresetName,
                extraImports: imports.Append(source.UnityPackagePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), cancellationToken: token);
            planned = planned with { UnityEditorPath = source.UnityEditorPath };
        }
        return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [planned]);
    }

    public async Task ClearCompletedAsync(CancellationToken token = default)
    {
        await InitializeAsync(token); await journalGate.WaitAsync(token);
        try { lock (sync) jobs.RemoveAll(x => x.State == "Completed"); await SaveAsync(token); }
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
                var previous = jobs[index]; var next = change(previous); jobs[index] = next;
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
        lease?.Dispose(); lease = null;
    }
    private string FindLog(string id)
    {
        try { return Directory.Exists(app.Paths.Logs) ? Directory.EnumerateFiles(app.Paths.Logs, "vrcsetup-*-" + id + ".log").FirstOrDefault() ?? "" : ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ""; }
    }
    private static string Phase(string phase) => phase switch
    { "Creating" or "Unity create" => "Create", "Prepare" or "Repository" => "Prepare", "Packages" or "VPM" or "Repair" => "Packages", "Import" or "Unity import" => "Import", "Post-import validation" => "Validate", "Completed" => "Finish", _ => phase };
    private static int Percent(string phase) => Phase(phase) switch
    { "Create" => 10, "Backup" or "Prepare" => 20, "Packages" => 35, "Import" => 55, "Validate" => 85, "Finish" => 95, _ => 0 };
    private sealed class InlineProgress(Action<OperationEvent> report) : IProgress<OperationEvent>
    { public void Report(OperationEvent value) => report(value); }
}
