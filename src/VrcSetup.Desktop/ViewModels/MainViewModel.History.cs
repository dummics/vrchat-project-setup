using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<UiHistoryEntry> ImportHistory { get; } = [];
    public ObservableCollection<UiHistoryEntry> OperationHistory { get; } = [];
    private OperationReport[] savedOperations = [];
    private bool loadingHistory;
    public Visibility ImportHistoryEmptyVisibility => ImportHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OperationHistoryEmptyVisibility => OperationHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HistoryErrorsVisibility => ImportHistory.Concat(OperationHistory).Any(x => x.State is "Failed" or "Interrupted" or "Partially applied") ? Visibility.Visible : Visibility.Collapsed;
    public bool CanClearCompletedImports => ImportJobs.Any(x => x.Job.State is "Completed" or "Recovered");
    [ObservableProperty] public partial bool AutoHideCompletedImports { get; set; }
    partial void OnAutoHideCompletedImportsChanged(bool value) => ImportsChanged(app.Imports.Snapshot());

    public async Task RefreshSavedHistoryAsync()
    {
        if (loadingHistory) return;
        loadingHistory = true;
        try
        {
            savedOperations = await new ActivityHistory(app.Paths).ReadAsync(lifetime.Token);
            ReconcileHistory(app.Imports.Snapshot());
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { app.Diagnostics.Write("Activity history", error.Message, error); }
        finally { loadingHistory = false; }
    }
    private void ReconcileHistory(ImportJob[] jobs)
    {
        var jobRows = jobs.Select(x => new UiHistoryEntry(x, savedOperations.FirstOrDefault(r => r.Id == x.Plan.Id))).ToArray();
        var imported = jobRows.Where(x => x.IsImport).ToList();
        var ids = jobs.Select(x => x.Plan.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = savedOperations.Where(x => !ids.Contains(x.Id)).Select(x => new UiHistoryEntry(x)).ToArray();
        imported.AddRange(others.Where(x => x.IsImport));
        Reconcile(ImportHistory, imported.OrderByDescending(x => x.Timestamp).ToArray());
        Reconcile(OperationHistory, jobRows.Where(x => !x.IsImport).Concat(others.Where(x => !x.IsImport)).OrderByDescending(x => x.Timestamp).ToArray());
        OnPropertyChanged(nameof(ImportHistoryEmptyVisibility)); OnPropertyChanged(nameof(OperationHistoryEmptyVisibility)); OnPropertyChanged(nameof(HistoryErrorsVisibility));
        static void Reconcile(ObservableCollection<UiHistoryEntry> rows, UiHistoryEntry[] desired)
        {
            var ids = desired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var i = rows.Count - 1; i >= 0; i--) if (!ids.Contains(rows[i].Id)) rows.RemoveAt(i);
            for (var i = 0; i < desired.Length; i++)
            {
                var current = rows.FirstOrDefault(x => x.Id == desired[i].Id);
                if (current is null) rows.Insert(i, desired[i]);
                else { current.Update(desired[i]); var old = rows.IndexOf(current); if (old != i) rows.Move(old, i); }
            }
        }
    }
    public async Task OpenHistoryLogAsync(UiHistoryEntry entry) => await IndependentAction(() =>
    {
        if (File.Exists(entry.LogFile)) { ProjectRules.EnsureContained(app.Paths.Logs, entry.LogFile); OpenPath(entry.LogFile); }
        else OpenLogsCommand.Execute(null);
        return Task.CompletedTask;
    });
    public async Task OpenHistoryProjectAsync(UiHistoryEntry entry) => await Guard(async () =>
    {
        var choices = entry.Paths.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (choices.Length == 0) throw new DirectoryNotFoundException();
        var path = choices.Length == 1 ? choices[0] : await ui.ChooseAsync("Choose project", choices);
        if (path is not null) OpenPath(path);
    });
    public async Task CheckHistoryProjectAsync(UiHistoryEntry entry) => await Guard(async () =>
    {
        if (entry.Job is not { Active: false } job) return;
        entry.Checking = true;
        try
        {
            entry.Health = await Task.Run(async () =>
            {
                var owner = job.Plan.Projects.Single().RecoveryOriginalSource ?? job.Source.UnityPackagePath;
                var destination = await new ImportDestinationInspector(app.Projects).InspectAsync(job.ProjectPath, owner, lifetime.Token, job.Source.EmptyProject);
                if (destination.Kind is not (ImportDestinationKind.Completed or ImportDestinationKind.Recoverable)) return UserErrors.Message(new IOException(destination.Message));
                var project = await app.Projects.ReadAsync(job.ProjectPath, lifetime.Token);
                var missing = job.Plan.Projects.Single().DesiredPolicy.Keys.Where(id => !project.InstalledVersions.ContainsKey(id)).ToArray();
                var marker = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(job.ProjectPath, ".vrcsetup", "state.json"), lifetime.Token);
                return (missing.Length == 0 ? UiText.Text("All selected packages are installed.") : UiText.Text("Missing packages: ") + string.Join(", ", missing.Select(PackageCatalog.FriendlyName)))
                    + "\n" + UiText.Text(marker?.Completed == true ? "Import completed." : "Choose Continue import to finish this project.")
                    + (job.Source.EmptyProject || File.Exists(job.Source.UnityPackagePath) ? "" : "\n" + UiText.Text("You will be asked to choose the original package again."));
            }, lifetime.Token);
        }
        finally { entry.Checking = false; }
    });
    public async Task RepairHistoryImportAsync(UiHistoryEntry entry) => await Guard(async () =>
    {
        if (entry.Job is not { Active: false } job || app.Imports.Snapshot().Any(x => x.Active && x.ProjectPath.Equals(job.ProjectPath, StringComparison.OrdinalIgnoreCase))) return;
        var source = job.Source;
        var original = job.Plan.Projects.Single().RecoveryOriginalSource ?? source.UnityPackagePath;
        if (!source.EmptyProject && !File.Exists(source.UnityPackagePath))
        {
            var chosen = await ui.PickFileAsync(".unitypackage"); if (chosen is null) return;
            if (!await ui.ConfirmAsync("Continue import", UiText.Text("Use this package to add missing files? Existing files stay unchanged.") + "\n" + Path.GetFileName(chosen), "Use package")) return;
            source = source with { UnityPackagePath = chosen };
        }
        var destination = await Task.Run(() => new ImportDestinationInspector(app.Projects).InspectAsync(job.ProjectPath, original, lifetime.Token, source.EmptyProject));
        if (destination.Kind is ImportDestinationKind.New or ImportDestinationKind.Partial)
        {
            if (source.EmptyProject)
            {
                if (!await BeginRetryNewProjectAsync(job, destination.Kind == ImportDestinationKind.Partial)) return;
                return;
            }
            if (!await ui.ConfirmAsync("Create a new copy", "Create a separate project? The existing folder stays unchanged.", "New copy")) return;
            if (!await NavigateAsync("Create")) return;
            StageImportFiles([source.UnityPackagePath]);
            var name = job.Name + " (copy)"; var index = 2;
            while (Directory.Exists(Path.Combine(ProjectsRoot, name))) name = job.Name + " (copy " + index++ + ")";
            ProjectName = name; BuildTarget = source.BuildTarget;
            creationOverrides = new(job.Plan.Projects.Single().DesiredPolicy);
            if (SelectedImportDraft is { } draft) draft.Packages = new(creationOverrides);
            RefreshCreationPackages(); return;
        }
        if (!await FindEditorAsync(true)) return;
        source = source with { UnityEditorPath = config.UnityEditorPath };
        foreach (var platform in source.PublishingPlatforms.Append(source.BuildTarget).Distinct())
            if (!UnityProjectDriver.HasBuildSupport(source.UnityEditorPath, platform)) throw new IOException("Unity build support is missing for " + platform);
        var project = job.Plan.Projects.Single();
        var planned = await Task.Run(() => app.PlanResumeAsync(source, project.DesiredPolicy, config.Copy(), project.PresetName,
            source.ExtraImports.Concat(project.ExtraImports).Where(x => !x.Equals(original, StringComparison.OrdinalIgnoreCase) && !x.Equals(job.Source.UnityPackagePath, StringComparison.OrdinalIgnoreCase)), lifetime.Token, original));
        var plan = new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [planned]);
        if (source.EmptyProject || await ui.ReviewAsync(plan)) { await app.Imports.EnqueueAsync(plan, RequestedParallelism, source, lifetime.Token); await NavigateAsync(source.EmptyProject ? "Projects" : "Create"); }
    });
    private async Task<bool> BeginRetryNewProjectAsync(ImportJob job, bool partial)
    {
        if (partial && !await ui.ConfirmAsync("Create a new copy", "Create a separate project? The existing folder stays unchanged.", "New copy")) return false;
        await BeginNewProjectCoreAsync();
        if (CurrentPage != "NewProject") return false;
        var name = job.Name; var index = 2;
        while (Directory.Exists(Path.Combine(ProjectsRoot, name)) || File.Exists(Path.Combine(ProjectsRoot, name))) name = job.Name + " (" + index++ + ")";
        NewProjectName = name; NewProjectPlatform = job.Source.BuildTarget;
        presetWorking = PackagePresence.Selection(job.Plan.Projects.Single().DesiredPolicy);
        RenderPreset(); RefreshLibraryRows();
        return true;
    }
    public async Task RestoreHistoryBackupAsync(UiHistoryEntry entry) => await Guard(async () =>
    {
        var project = entry.ProjectPath; var file = entry.BackupFile;
        if (project.Length == 0 || file.Length == 0) return;
        ProjectRules.EnsureContained(Path.Combine(project, ".vrcsetup", "backups"), file);
        var backup = await AtomicFile.ReadJsonAsync<BackupRecord>(file, lifetime.Token) ?? throw new IOException("Backup record not found.");
        if (!await ui.ConfirmAsync("Restore " + entry.Name, "Restore the backup from this operation? Current files are kept in a recovery folder.\n" + project, "Restore backup")) return;
        using var lease = await app.Projects.AcquireAsync(project, lifetime.Token);
        if (app.Projects.IsBusy(project)) throw new IOException("Close Unity or wait for the active import before restoring.");
        if (!await app.Projects.RestoreAsync(project, backup, new SynchronizationProgress<OperationEvent>(context, AppendActivity), lifetime.Token)) throw new IOException("Restore could not complete; inspect the logs.");
        entry.Health = "Backup restored. Check the project before resuming its import.";
    });
    [RelayCommand] private async Task OpenProjectsFolder() => await IndependentAction(() =>
    {
        var folder = string.IsNullOrWhiteSpace(ProjectsRoot) ? config.UnityProjectsRoot : ProjectRules.NormalizePath(ProjectsRoot);
        if (!Directory.Exists(folder)) throw new IOException("Choose an available projects folder in Settings.");
        OpenPath(folder); return Task.CompletedTask;
    });
}

public sealed class UiHistoryEntry : ObservableObject
{
    private ImportJob? job;
    private OperationReport? report;
    private bool expanded;
    public UiHistoryEntry(ImportJob value, OperationReport? outcome = null) { job = value; report = outcome; }
    public UiHistoryEntry(OperationReport value) => report = value;
    public string Id => job?.Plan.Id ?? report!.Id;
    public ImportJob? Job => job;
    public string ProjectPath => job?.ProjectPath ?? (report?.Results.Length == 1 ? report.Results[0].Path : "");
    public string BackupFile => report?.Results.FirstOrDefault(x => x.Path.Equals(ProjectPath, StringComparison.OrdinalIgnoreCase))?.BackupPath is { Length: > 0 } folder ? Path.Combine(folder, "backup.json") : "";
    public Visibility RestoreVisibility => !Checking && job?.Active != true && File.Exists(BackupFile) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImportActionsVisibility => job is { Active: false } && (!job.Source.EmptyProject || job.Recoverable) ? Visibility.Visible : Visibility.Collapsed;
    public string RepairLabel => job?.Source.EmptyProject == true ? "Retry project creation" : job?.Recoverable == true ? "Continue import" : "Complete missing files";
    public Visibility ProjectActionsVisibility => job?.Active != true && (ProjectPath.Length > 0 || report?.Results.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
    public string StateGlyph => State is "Failed" or "Partially applied" or "Not applied" ? "⚠ " : "";
    public string StateCaption => StateGlyph + UiText.Text(State);
    private string health = "";
    public string Health { get => health; set { if (SetProperty(ref health, value)) OnPropertyChanged(nameof(HealthVisibility)); } }
    public Visibility HealthVisibility => Health.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    private bool checking;
    public bool Checking { get => checking; set { if (SetProperty(ref checking, value)) { OnPropertyChanged(nameof(CanCheck)); OnPropertyChanged(nameof(RestoreVisibility)); } } }
    public bool CanCheck => !Checking;
    public bool Expanded { get => expanded; set => SetProperty(ref expanded, value); }
    private ProjectPlan[] Projects => job?.Plan.Projects ?? report?.Plan?.Projects ?? [];
    public bool IsImport => job is not null ? !job.Source.EmptyProject : Projects.Any(x => x.Creation is { EmptyProject: false } || x.RecoverySource is { EmptyProject: false } || x.ExtraImports.Length > 0);
    public DateTime Timestamp => job?.Plan.CreatedUtc ?? report!.CompletedUtc;
    public string Date => Timestamp.ToLocalTime().ToString("g");
    public string Name => job?.Name ?? (report!.Results.Length == 1 ? report.Results[0].Project : string.Format(UiText.Text("{0} projects"), report.Results.Length));
    public string State => job?.State is { } state ? state == "Queued" ? "Starting…" : state
        : report!.Success ? "Completed" : report.Cancelled ? "Cancelled"
        : report.Results.Length > 0 && report.Results.All(x => x.Status == OperationStatus.Skipped) ? "Not applied"
        : report.Results.Any(x => x.Status is OperationStatus.Completed or OperationStatus.CompletedWithWarnings) ? "Partially applied" : "Failed";
    public string Summary => job?.Source.EmptyProject == true ? string.Join(" · ", new[] { Platform, UiText.Text("Creating project"), string.Format(UiText.Text("{0} packages"), Projects.Sum(x => x.DesiredPolicy.Count)) })
        : IsImport ? string.Join(" · ", new[] { Platform, Projects.FirstOrDefault()?.PresetName is null or "My default packages" ? UiText.Text("Default") : Projects[0].PresetName, string.Format(UiText.Text("{0} packages"), Projects.Sum(x => x.DesiredPolicy.Count)) })
        : Projects.Length == 0 ? UiText.Text("Project operation") : string.Format(UiText.Text("{0} changes · {1}/{2} projects completed"), Projects.SelectMany(x => x.Changes).Count(x => x.Kind != ChangeKind.Keep), report!.Results.Count(x => x.Status is OperationStatus.Completed or OperationStatus.CompletedWithWarnings or OperationStatus.Unchanged), Projects.Length);
    private string Platform => (job?.Source.BuildTarget ?? Projects.Select(x => x.Creation ?? x.RecoverySource).FirstOrDefault(x => x is not null)?.BuildTarget) switch { "Android" => "Android / Quest", "iOS" => "iOS", "StandaloneWindows64" => "Windows", _ => "" };
    public string Paths => string.Join(Environment.NewLine, job is not null ? new[] { job.ProjectPath, job.Source.UnityPackagePath } : report!.Results.Select(x => x.Path));
    public string Packages => !IsImport ? "" : string.Join(Environment.NewLine, Projects.SelectMany(x => x.Changes).Where(x => x.Kind != ChangeKind.Remove).DistinctBy(x => x.Id).Select(x => PackageCatalog.FriendlyName(x.Id) + "  " + (x.ResolvedVersion ?? x.RequestedVersion)));
    public string Changes => IsImport || report?.Plan is not { } plan ? "" : string.Join(Environment.NewLine + Environment.NewLine,
        PackageReview.Group(plan).Select(group => (group.Projects.Length == Projects.Length ? "All projects" : string.Join(", ", group.Projects.Select(x => x.Name))) + Environment.NewLine
            + string.Join(Environment.NewLine, group.Changes.Select(x => $"{x.Kind} · {PackageCatalog.FriendlyName(x.Id)} · {x.InstalledVersion ?? "—"} → {x.ResolvedVersion ?? "removed"}"))));
    public string Result => job is not null ? job.Active || job.State is "Completed" or "Recovered" ? UiText.Text(job.Message) : job.Source.EmptyProject ? UserErrors.Message(new IOException(job.Message)) : UiText.Text("The import did not finish. Check the project or continue the import.")
        : string.Join(Environment.NewLine, report!.Results.Select(x => x.Project + ": " + UiText.Text(x.Status switch
        {
            OperationStatus.Completed => "Completed",
            OperationStatus.Unchanged => "Packages are already up to date",
            OperationStatus.CompletedWithWarnings => "Completed. Check the details in the log.",
            _ when x.Message.Contains("open in Unity", StringComparison.OrdinalIgnoreCase) => "Close Unity, then try again.",
            OperationStatus.Cancelled => "Cancelled",
            _ => "Not completed. Open the project to review it, or try again."
        })));
    public string LogFile => job?.LogFile ?? report!.Results.Select(x => x.LogFile).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    public bool HasLog => LogFile.Length > 0;
    public Visibility PackagesVisibility => Packages.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChangesVisibility => Changes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResultVisibility => Result.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public void Update(UiHistoryEntry value)
    {
        if (job == value.job && report == value.report) return;
        job = value.job; report = value.report; OnPropertyChanged(string.Empty);
    }
}
