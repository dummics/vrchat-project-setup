using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal sealed class ImportBridge(Window window, VrcSetupApplication engine) : IDisposable
{
    internal const string Page = "https://app.vrcsetup.test/index.html";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Path, PackageIdentity? Identity)> sources = [];
    private readonly CancellationTokenSource lifetime = new();
    private CoreWebView2? web;
    private bool handlingRequest;
    private OperationPlan? reviewed;
    private string? continuationId;
    private OperationPlan? packageReview;
    private AppConfiguration? preferencesBaseline;
    private SourceDraft[] initial = [];
    private readonly string candidateProjects = Path.Combine(engine.Paths.Home, "candidate-projects");
    internal bool IsBusy => handlingRequest;

    internal static bool IsPage(string? uri) => string.Equals(uri, Page, StringComparison.Ordinal);

    internal void Attach(CoreWebView2 core)
    {
        web = core;
        core.WebMessageReceived += Receive;
    }

    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsPage(args.Source) || web is null) return;
        string? id = null;
        var ownsRequest = false;
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            id = root.GetProperty("id").GetString();
            if (handlingRequest) throw new InvalidOperationException("Attendi l’operazione in corso.");
            handlingRequest = ownsRequest = true;
            var result = await Dispatch(root.GetProperty("method").GetString(), root.GetProperty("data"), args);
            Send(new { id, result });
        }
        catch (Exception error) when (!lifetime.IsCancellationRequested)
        {
            engine.Diagnostics.Write("React candidate", error.Message, error);
            Send(new { id, error = UserErrors.Message(error) });
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        finally { if (ownsRequest) handlingRequest = false; }
    }

    private async Task<object> Dispatch(string? method, JsonElement data, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var token = lifetime.Token;
        switch (method)
        {
            case "state":
                return await State(token);
            case "pick":
                var picker = new OpenFileDialog { Filter = "UnityPackage|*.unitypackage", Multiselect = true };
                return picker.ShowDialog(window) == true ? await Stage(picker.FileNames, token) : [];
            case "drop":
                return await Stage(args.AdditionalObjects.OfType<CoreWebView2File>().Select(x => x.Path), token);
            case "workspace":
                var config = await engine.Configuration.LoadAsync(token);
                config.UnityEditorPath = (await Task.Run(UnityEditorDiscovery.FindEditors, token))
                    .FirstOrDefault(x => x.Version == UnityEditorDiscovery.PinnedVersion).Path ?? "";
                if (config.UnityEditorPath.Length == 0) throw new IOException("Unity 2022.3.22f1 non trovato.");
                Directory.CreateDirectory(candidateProjects);
                config.UnityProjectsRoot = candidateProjects;
                await engine.Configuration.SaveAsync(config, token);
                return await State(token);
            case "remove":
                sources.Remove(data.GetGuid());
                reviewed = null;
                return true;
            case "review":
                reviewed = null;
                continuationId = null;
                var settings = await engine.Configuration.LoadAsync(token);
                if (!string.Equals(settings.UnityProjectsRoot, candidateProjects, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Configura la cartella isolata della candidata.");
                var plans = new List<ProjectPlan>();
                foreach (var choice in data.Deserialize<DraftChoice[]>(Json) ?? [])
                {
                    if (!ProjectRules.BuildTargets.Contains(choice.Target)) throw new ArgumentException("Piattaforma non valida.");
                    var source = sources[choice.Id];
                    var plan = await engine.PlanCreationAsync(source.Path, settings, name: choice.Name,
                        preset: choice.Preset, packages: choice.Packages, buildTarget: choice.Target, cancellationToken: token);
                    if (source.Identity is not null && plan.Projects.Any(x => (x.Creation ?? x.RecoverySource)?.SourceIdentity != source.Identity))
                        throw new IOException("Il file è cambiato. Rimuovilo e aggiungilo di nuovo per verificare i pacchetti.");
                    plans.AddRange(plan.Projects);
                }
                if (plans.Count == 0 || plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
                    throw new ArgumentException("Scegli una destinazione distinta per ogni progetto.");
                reviewed = new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
                return new { reviewed.Id, destinations = plans.Select(x => x.Path).ToArray() };
            case "reviewContinuation":
                reviewed = null;
                continuationId = null;
                var job = CurrentJob(data.GetString());
                if (!job.Recoverable || job.HasCompilationBlock)
                    throw new IOException("Questa importazione non può essere ripresa.");
                if (!job.Source.EmptyProject && !File.Exists(job.Source.UnityPackagePath))
                    throw new IOException("Ricollega l’unità che contiene il file originale e riprova.");
                if (!job.Source.EmptyProject && job.Source.SourceIdentity is { } identity &&
                    await PackageSourceIdentity.ReadAsync(job.Source.UnityPackagePath, token) != identity)
                    throw new IOException("Il file originale è cambiato. Ripristinalo prima di riprendere.");
                reviewed = await engine.Imports.RecoveryPlanAsync(job.Id, await engine.Configuration.LoadAsync(token), token);
                continuationId = job.Id;
                return new { reviewed.Id, destinations = reviewed.Projects.Select(x => x.Path).ToArray(), continuation = true };
            case "start":
                if (reviewed is not { } accepted || data.GetString() != accepted.Id) throw new InvalidOperationException("Rivedi l’importazione.");
                var expectedId = continuationId;
                await engine.Imports.EnqueueAsync(accepted, parallelism: 0, token: token, expectedImportId: expectedId);
                reviewed = null;
                continuationId = null;
                if (expectedId is null) { sources.Clear(); initial = []; }
                return await State(token);
            case "pause":
                await engine.Imports.PauseAsync(CurrentJob(data.GetString()).Id, token);
                return await State(token);
            case "dismiss":
                await engine.Imports.DismissAsync(CurrentJob(data.GetString()).Id, token);
                return await State(token);
            case "discard":
                await Discard(CurrentJob(data.GetString()), token);
                return await State(token);
            case "clearFinished":
                await engine.Imports.ClearFinishedAsync(token);
                return await State(token);
            case "retrySave":
                if (!await engine.Imports.RetryHistorySaveAsync(CurrentJob(data.GetString()).Id, token))
                    throw new IOException("Impossibile salvare lo stato. Controlla lo spazio sul disco e riprova.");
                return await State(token);
            case "openImportProject":
                var import = CurrentJob(data.GetString());
                if (import.Active || import.DiscardRequested)
                    throw new IOException("Attendi che l’importazione si fermi.");
                await OpenProject(import.ProjectPath, token);
                return true;
            case "projects":
                return await Projects(token);
            case "importPreferences":
                preferencesBaseline = await engine.Configuration.LoadAsync(token);
                return new { preferencesBaseline.MaxParallelImports, preferencesBaseline.AutoHideCompletedImports };
            case "saveImportPreferences":
                var baseline = preferencesBaseline ?? throw new IOException("Riapri le impostazioni prima di salvare.");
                var preferences = data.Deserialize<ImportPreferences>(Json) ?? throw new ArgumentException("Impostazioni non valide.");
                if (preferences.MaxParallelImports is < 0 or > 8) throw new ArgumentException("Scegli un limite da 1 a 8 o Automatico.");
                var updated = baseline.Copy();
                updated.MaxParallelImports = preferences.MaxParallelImports;
                updated.AutoHideCompletedImports = preferences.AutoHideCompletedImports;
                preferencesBaseline = await engine.Configuration.MergeAsync(baseline, updated, token);
                ConfigureQueue(preferencesBaseline);
                return await State(token);
            case "projectPackages":
                packageReview = null;
                return await ReadProjectPackages(data.GetString(), token);
            case "reviewPackageRestore":
                packageReview = null;
                var restorePath = CandidateProjectPath(data.GetString());
                EnsureProjectIdle(restorePath);
                if (engine.Projects.IsBusy(restorePath)) throw new IOException("Chiudi Unity prima di ripristinare i pacchetti.");
                packageReview = await engine.Planner.PlanPackageReinstallAsync(restorePath, token);
                return new
                {
                    packageReview.Id,
                    packages = packageReview.Projects.Single().Changes.Select(x => new { Name = PackageCatalog.FriendlyName(x.Id), Version = x.ResolvedVersion })
                };
            case "applyPackageRestore":
                if (packageReview is not { } restore || data.GetString() != restore.Id)
                    throw new IOException("Rivedi il ripristino dei pacchetti.");
                packageReview = null;
                var restoreProject = restore.Projects.Single().Path;
                EnsureProjectIdle(restoreProject);
                if (engine.Projects.IsBusy(restoreProject)) throw new IOException("Chiudi Unity prima di ripristinare i pacchetti.");
                var outcome = await Task.Run(() => engine.ApplyAsync(restore, cancellationToken: token), token);
                var result = outcome.Results.FirstOrDefault();
                if (!outcome.Success) throw new IOException(result?.Message ?? "Impossibile ripristinare i pacchetti. Consulta il log in Attività.");
                return new { warning = result?.Status == OperationStatus.CompletedWithWarnings ? UserErrors.Message(new IOException(result.Message)) : null };
            case "activity":
                return await Activity(token);
            case "openOperationLog":
                var report = (await new ActivityHistory(engine.Paths).ReadAsync(token)).FirstOrDefault(x => x.Id == data.GetString());
                var log = report?.Results.Select(x => x.LogFile).FirstOrDefault(File.Exists) ?? report?.ReportFile;
                if (log is null || !File.Exists(log)) throw new IOException("Il log non è più disponibile.");
                ProjectRules.EnsureContained(engine.Paths.Logs, log);
                if (Path.GetExtension(log).ToLowerInvariant() is not (".log" or ".txt" or ".json" or ".jsonl"))
                    throw new IOException("Formato del log non riconosciuto.");
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(log) { UseShellExecute = true })) { }
                return true;
            case "exportDiagnostics":
                var save = new SaveFileDialog { FileName = SupportLogs.SuggestedName, Filter = "ZIP|*.zip", DefaultExt = ".zip" };
                if (save.ShowDialog(window) != true) return false;
                await SupportLogs.ExportAsync(engine.Paths, save.FileName, token);
                return true;
            case "openProject":
                var selected = (await Projects(token)).SingleOrDefault(x => x.Path.Equals(data.GetString(), StringComparison.OrdinalIgnoreCase));
                if (selected is null) throw new IOException("Il progetto non è più disponibile. Aggiorna l’elenco.");
                await OpenProject(selected.Path, token);
                return true;
            case "log":
                return (await engine.Imports.ReadLogTailAsync(data.GetString() ?? "", token: token)).Text;
            default:
                throw new ArgumentException("Operazione sconosciuta.");
        }
    }

    private string CandidateProjectPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Scegli un progetto.");
        path = ProjectRules.NormalizePath(path);
        ProjectRules.EnsureContained(candidateProjects, path);
        if (path.Equals(candidateProjects, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Scegli un progetto nella cartella di prova.");
        return path;
    }

    private void EnsureProjectIdle(string path)
    {
        if (engine.Imports.Snapshot().Any(x => (x.Active || x.DiscardRequested) &&
            x.ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Attendi la fine dell’importazione.");
    }

    private async Task<object> ReadProjectPackages(string? path, CancellationToken token)
    {
        var projectPath = CandidateProjectPath(path);
        EnsureProjectIdle(projectPath);
        var project = await engine.Projects.ReadAsync(projectPath, token);
        return new
        {
            project.Path, project.Name,
            CanRestore = !project.Incomplete && PackagePresence.Selection(project.LockedPackages).Count > 0,
            packages = PackagePresence.Selection(project.LockedPackages).Keys
                .Union(PackagePresence.Selection(project.Packages).Keys, StringComparer.OrdinalIgnoreCase)
                .Union(PackagePresence.Selection(project.InstalledVersions).Keys, StringComparer.OrdinalIgnoreCase)
                .Select(id => new
                {
                    id, Name = project.PackageDisplayNames.GetValueOrDefault(id) ?? PackageCatalog.FriendlyName(id),
                    Installed = project.InstalledVersions.GetValueOrDefault(id),
                    Locked = project.LockedPackages.GetValueOrDefault(id)
                }).OrderBy(x => x.Name).ToArray()
        };
    }

    private async Task Discard(ImportJob job, CancellationToken token)
    {
        if (!CanDiscard(job)) throw new IOException("Non è possibile annullare questa importazione.");
        ProjectRules.EnsureContained(candidateProjects, job.ProjectPath);
        if (job.ProjectPath.Equals(candidateProjects, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La cartella dei progetti non può essere rimossa.");
        var noFolder = IsAbsent(job.ProjectPath);
        if (!noFolder && job.Source.CreationId is null)
            throw new IOException("La cartella del progetto non può essere rimossa automaticamente.");
        var message = noFolder
            ? $"Annullare l’importazione di {job.Name}?"
            : $"Annullare l’importazione di {job.Name} ed eliminare la cartella creata per questo progetto?";
        if (MessageBox.Show(window, message, "Annulla importazione", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        using var stopWait = CancellationTokenSource.CreateLinkedTokenSource(token);
        stopWait.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await engine.Imports.RequestDiscardAsync(job.Id, stopWait.Token);
            await engine.Imports.WaitForStoppedAsync(job.Id, stopWait.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("L’arresto sta richiedendo più tempo. Aggiorna l’elenco e riprova.");
        }
        var cleanup = new OwnedProjectDiscard(engine.Paths, engine.Projects);
        await engine.Imports.FinishDiscardAsync(job.Id,
            (current, cancel) => noFolder ? ConfirmAbsent(current.ProjectPath, cancel)
                : Task.Run(() => cleanup.RemoveAsync(current, candidateProjects, cancel), cancel), token, noProjectFolder: noFolder);
    }

    private static bool CanDiscard(ImportJob job) => !job.HistorySaveFailed && !job.DiscardCompleted
        && (job.Source.CreationId is not null || IsAbsent(job.ProjectPath))
        && (job.Active || job.Recoverable || job.DiscardRequested);

    private static bool IsAbsent(string path)
    {
        if (Path.GetDirectoryName(path) is not { } parent || !Directory.Exists(parent)) return false;
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return Directory.Exists(parent); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static Task ConfirmAbsent(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAbsent(path)) throw new IOException("Controlla la cartella del progetto prima di completare l’annullamento.");
        return Task.CompletedTask;
    }

    private async Task<ActivityRow[]> Activity(CancellationToken token)
    {
        var jobs = engine.Imports.Snapshot();
        var jobPlans = jobs.Select(x => x.Plan.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reports = await new ActivityHistory(engine.Paths).ReadAsync(token);
        return jobs.Select(x => new ActivityRow(x.Id, x.Name, x.FinishedAt ?? x.StartedAt ?? x.Plan.CreatedUtc,
                x.Source.EmptyProject ? "project" : "import", JobStatus(x)))
            .Concat(reports.Where(x => !jobPlans.Contains(x.Id)).Select(x => new ActivityRow(
                x.Id, string.Join(", ", x.Results.Select(result => result.Project).Distinct()),
                x.CompletedUtc, "operation", x.Success ? x.Results.Any(y => y.Status == OperationStatus.CompletedWithWarnings)
                    ? "warning" : "completed" : x.Cancelled ? "cancelled" : "failed")))
            .OrderByDescending(x => x.Date).ToArray();
    }

    private static string JobStatus(ImportJob job)
    {
        if (job.DiscardCompleted) return "cancelled";
        if (job.UnityCompilationNeedsAttention || job.PackageContentsNeedRestore || job.CreatorCompanionPending || job.CreatorCompanionUnknown)
            return "warning";
        return job.State.ToLowerInvariant();
    }

    private async Task<CandidateProject[]> Projects(CancellationToken token)
    {
        if (!Directory.Exists(candidateProjects)) return [];
        var projects = await engine.Projects.ScanAsync(candidateProjects, cancellationToken: token);
        var jobs = engine.Imports.Snapshot();
        return await Task.Run(() => projects.Select(project =>
        {
            var job = ImportQueue.CurrentForProject(jobs, project.Path);
            return new CandidateProject(project.Name, project.Path, project.UnityVersion, project.LastUpdatedUtc,
                project.CreatedUtc, project.Incomplete, engine.Projects.IsBusy(project.Path), job?.Active == true,
                job is { Recoverable: true, RecoveryNoticeDismissed: false } ? job.Id : null);
        }).ToArray(), token);
    }

    private async Task OpenProject(string path, CancellationToken token)
    {
        path = CandidateProjectPath(path);
        if (!File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt")))
            throw new IOException("Il progetto non è ancora pronto per Unity.");
        EnsureProjectIdle(path);
        if (engine.Projects.IsBusy(path)) throw new IOException("Il progetto è già aperto in Unity.");
        var editor = (await engine.Configuration.LoadAsync(token)).UnityEditorPath;
        UnityProjectDriver.ValidateEditor(editor);
        var start = new System.Diagnostics.ProcessStartInfo(editor) { UseShellExecute = false };
        start.ArgumentList.Add("-projectPath");
        start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start);
    }

    internal void ConfigureQueue(AppConfiguration configuration)
        => engine.Imports.ConfigureAutomaticLimit(configuration.MaxParallelImports == 0 ? 8 : Math.Clamp(configuration.MaxParallelImports, 1, 8));

    private object[] VisibleJobs(bool hideCompleted)
    {
        var jobs = engine.Imports.Snapshot();
        return jobs.GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => ImportQueue.CurrentForProject(group, group.Key)!)
            .Where(x => !x.HiddenFromImports &&
                !(hideCompleted && x.CanHide && x.State is ("Completed" or "Recovered") &&
                    !x.CreatorCompanionPending && !x.CreatorCompanionUnknown && !x.UnityCompilationNeedsAttention && !x.PackageContentsNeedRestore))
            .OrderByDescending(x => x.Plan.CreatedUtc)
            .Select(x => (object)new
            {
                x.Id, x.Name, x.ProjectPath, x.State, x.Progress, x.Active, x.Recoverable, x.HasCompilationBlock,
                x.UnityCompilationNeedsAttention, x.PackageContentsNeedRestore, x.HistorySaveFailed,
                x.RecoveryNoticeDismissed, x.DiscardCompleted, x.DiscardRequested,
                CanDiscard = CanDiscard(x),
                DiscardRemovesFolder = x.Source.CreationId is not null && Directory.Exists(x.ProjectPath),
                CanDismiss = x.CanDismiss && !x.RecoveryNoticeDismissed
                    && (!x.Recoverable || Directory.Exists(x.ProjectPath))
            }).ToArray();
    }

    private ImportJob CurrentJob(string? id)
    {
        var snapshot = engine.Imports.Snapshot();
        var job = snapshot.FirstOrDefault(x => x.Id == id);
        if (job is null || ImportQueue.CurrentForProject(snapshot, job.ProjectPath)?.Id != job.Id)
            throw new IOException("L’importazione è cambiata. Aggiorna l’elenco e selezionala di nuovo.");
        return job;
    }

    private async Task<object> State(CancellationToken token)
    {
        var settings = await engine.Configuration.LoadAsync(token);
        return new
        {
            root = settings.UnityProjectsRoot == candidateProjects ? candidateProjects : "",
            packages = settings.VpmPackages,
            presets = settings.PackagePresets,
            initial,
            jobs = VisibleJobs(settings.AutoHideCompletedImports)
        };
    }

    internal async Task<SourceDraft[]> Stage(IEnumerable<string> paths, CancellationToken token)
    {
        var settings = await engine.Configuration.LoadAsync(token);
        var added = new List<(SourceDraft Draft, string Path, PackageIdentity? Identity)>();
        foreach (var file in paths.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)
                || sources.Values.Any(x => x.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            var report = await engine.Inspection.InspectForImportAsync(file, cancellationToken: token);
            var suggestions = report.Suggestions
                .Where(x => x.Confidence is DependencyConfidence.Declared or DependencyConfidence.Detected)
                .Select(x => new PackageChoice(x.Id, PackageCatalog.FriendlyName(x.Id), DependencyAdvisor.SuggestedVersion(x)))
                .Where(x => x.Version is not null).ToArray();
            var id = Guid.NewGuid();
            added.Add((new(id, Path.GetFileName(file), ProjectRules.SuggestName(file, settings),
                ProjectRules.SuggestBuildTarget(file), suggestions), file, report.SourceIdentity));
        }
        foreach (var item in added) sources.Add(item.Draft.Id, (item.Path, item.Identity));
        reviewed = null;
        return added.Select(item => item.Draft).ToArray();
    }

    internal async Task SeedSmokeAsync(string folder)
    {
        var inputs = new List<string>();
        for (var i = 1; i <= 6; ++i)
        {
            var input = Path.Combine(folder, $"avatarquestv{i}.unitypackage");
            using (var gzip = new System.IO.Compression.GZipStream(File.Create(input), System.IO.Compression.CompressionMode.Compress))
            using (var tar = new System.Formats.Tar.TarWriter(gzip))
            {
                var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/pathname")
                {
                    DataStream = new MemoryStream("Assets/Avatar.prefab"u8.ToArray())
                };
                tar.WriteEntry(entry);
            }
            inputs.Add(input);
        }
        initial = await Stage(inputs, lifetime.Token);
        var project = Path.Combine(candidateProjects, "Avatar di prova");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"),
            """{"dependencies":{"example.fixture":{"version":"1.0.0"}},"locked":{"example.fixture":{"version":"1.0.0"}}}""");
        var source = new CreationRequest(inputs[0], "Avatar di prova", project, "", "Android", ["Android"], []);
        var plan = new OperationPlan("smoke-compilation", DateTime.UtcNow,
            [new() { Path = project, Name = source.Name, DesiredPolicy = new Dictionary<string, string>(), Changes = [], ExpectedFingerprint = "" }]);
        var log = Path.Combine(engine.Paths.Logs, "smoke-import.log");
        Directory.CreateDirectory(engine.Paths.Logs);
        await File.WriteAllTextAsync(log, "Script compilation failed in test fixture.");
        var job = new ImportJob("smoke-compilation", plan, source, "Failed", "Validate", 85, "", log)
        { UnityCompilationBlockedImport = true };
        var completed = source with { TargetPath = Path.Combine(candidateProjects, "Import completato"), Name = "Import completato" };
        var completedPlan = new OperationPlan("smoke-completed", DateTime.UtcNow, [plan.Projects[0] with { Path = completed.TargetPath, Name = completed.Name }]);
        var completedJob = new ImportJob("smoke-completed", completedPlan, completed, "Completed", "Finish", 100, "", log);
        await AtomicFile.WriteJsonAsync(Path.Combine(engine.Paths.Home, "imports.json"), new[] { job, completedJob }, lifetime.Token);
    }

    private void Send(object response)
    {
        if (!lifetime.IsCancellationRequested && web is { } core && IsPage(core.Source))
            core.PostWebMessageAsJson(JsonSerializer.Serialize(response, Json));
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    internal sealed record SourceDraft(Guid Id, string File, string Name, string Target, PackageChoice[] Suggestions);
    internal sealed record PackageChoice(string Id, string Name, string? Version);
    private sealed record DraftChoice(Guid Id, string Name, string Target, string? Preset, Dictionary<string, string> Packages);
    private sealed record CandidateProject(string Name, string Path, string UnityVersion, DateTime LastUpdatedUtc,
        DateTime CreatedUtc, bool Incomplete, bool Open, bool Importing, string? RecoveryId);
    private sealed record ActivityRow(string Id, string Name, DateTime Date, string Kind, string Status);
    private sealed record ImportPreferences(int MaxParallelImports, bool AutoHideCompletedImports);
}
