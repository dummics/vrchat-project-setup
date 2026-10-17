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
        var imported = jobs.Select(x => new UiHistoryEntry(x, savedOperations.FirstOrDefault(r => r.Id == x.Plan.Id))).ToList();
        var ids = jobs.Select(x => x.Plan.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = savedOperations.Where(x => !ids.Contains(x.Id)).Select(x => new UiHistoryEntry(x)).ToArray();
        imported.AddRange(others.Where(x => x.IsImport));
        Reconcile(ImportHistory, imported.OrderByDescending(x => x.Timestamp).ToArray());
        Reconcile(OperationHistory, others.Where(x => !x.IsImport).OrderByDescending(x => x.Timestamp).ToArray());
        OnPropertyChanged(nameof(ImportHistoryEmptyVisibility)); OnPropertyChanged(nameof(OperationHistoryEmptyVisibility));
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
    public async Task OpenHistoryLogAsync(UiHistoryEntry entry) => await Guard(() =>
    {
        if (File.Exists(entry.LogFile)) { ProjectRules.EnsureContained(app.Paths.Logs, entry.LogFile); OpenPath(entry.LogFile); }
        else OpenLogsCommand.Execute(null);
        return Task.CompletedTask;
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
                var destination = await new ImportDestinationInspector(app.Projects).InspectAsync(job.ProjectPath, owner, lifetime.Token);
                if (destination.Kind is not (ImportDestinationKind.Completed or ImportDestinationKind.Recoverable)) return destination.Message;
                var project = await app.Projects.ReadAsync(job.ProjectPath, lifetime.Token);
                var missing = job.Plan.Projects.Single().DesiredPolicy.Keys.Where(id => !project.InstalledVersions.ContainsKey(id)).ToArray();
                var marker = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(job.ProjectPath, ".vrcsetup", "state.json"), lifetime.Token);
                var steps = string.Join(", ", (marker?.Steps ?? []).Where(x => x.Value).Select(x => x.Key));
                return (missing.Length == 0 ? "Selected VPM packages are installed." : "Missing installed packages: " + string.Join(", ", missing.Select(PackageCatalog.FriendlyName)) + ".")
                    + "\n" + (marker?.Completed == true ? "Import marked complete." : "Import unfinished.")
                    + (steps.Length > 0 ? " Finished steps: " + steps + "." : "")
                    + (File.Exists(job.Source.UnityPackagePath) ? "\nReimport checks for missing assets and preserves existing files." : "\nOriginal package unavailable. Select it again to resume.");
            }, lifetime.Token);
        }
        finally { entry.Checking = false; }
    });
    public async Task RepairHistoryImportAsync(UiHistoryEntry entry) => await Guard(async () =>
    {
        if (entry.Job is not { Active: false } job || app.Imports.Snapshot().Any(x => x.Active && x.ProjectPath.Equals(job.ProjectPath, StringComparison.OrdinalIgnoreCase))) return;
        var source = job.Source;
        var original = job.Plan.Projects.Single().RecoveryOriginalSource ?? source.UnityPackagePath;
        if (!File.Exists(source.UnityPackagePath))
        {
            var chosen = await ui.PickFileAsync(".unitypackage"); if (chosen is null) return;
            if (!await ui.ConfirmAsync("Resume " + job.Name, "Use this file to add missing assets to the existing project? Existing files are preserved.\n" + chosen, "Use package")) return;
            source = source with { UnityPackagePath = chosen };
        }
        var destination = await Task.Run(() => new ImportDestinationInspector(app.Projects).InspectAsync(job.ProjectPath, original, lifetime.Token));
        if (destination.Kind is ImportDestinationKind.New or ImportDestinationKind.Partial)
        {
            if (!await ui.ConfirmAsync("Create a new copy", destination.Message + "\nKeep the existing folder and prepare a new project?", "New copy")) return;
            if (!await NavigateAsync("Create")) return;
            StageImportFiles([source.UnityPackagePath]);
            var name = job.Name + " (copy)"; var index = 2;
            while (Directory.Exists(Path.Combine(ProjectsRoot, name))) name = job.Name + " (copy " + index++ + ")";
            ProjectName = name; BuildTarget = source.BuildTarget;
            creationOverrides = new(job.Plan.Projects.Single().DesiredPolicy);
            if (SelectedImportDraft is { } draft) draft.Packages = new(creationOverrides);
            RefreshCreationPackages(); return;
        }
        UnityProjectDriver.ValidateEditor(source.UnityEditorPath);
        foreach (var platform in source.PublishingPlatforms.Append(source.BuildTarget).Distinct())
            if (!UnityProjectDriver.HasBuildSupport(source.UnityEditorPath, platform)) throw new IOException("Unity build support is missing for " + platform);
        var project = job.Plan.Projects.Single();
        var planned = await Task.Run(() => app.PlanResumeAsync(source, project.DesiredPolicy, config.Copy(), project.PresetName,
            source.ExtraImports.Concat(project.ExtraImports).Where(x => !x.Equals(original, StringComparison.OrdinalIgnoreCase) && !x.Equals(job.Source.UnityPackagePath, StringComparison.OrdinalIgnoreCase)), lifetime.Token, original));
        var plan = new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [planned]);
        if (await ui.ReviewAsync(plan)) { await app.Imports.EnqueueAsync(plan, RequestedParallelism, source, lifetime.Token); await NavigateAsync("Create"); }
    });
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
    [RelayCommand] private async Task OpenProjectsFolder() => await Guard(() =>
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
    public Visibility ImportActionsVisibility => job is { Active: false } ? Visibility.Visible : Visibility.Collapsed;
    public string RepairLabel => job?.Recoverable == true ? "Resume import" : "Reimport missing";
    private string health = "";
    public string Health { get => health; set { if (SetProperty(ref health, value)) OnPropertyChanged(nameof(HealthVisibility)); } }
    public Visibility HealthVisibility => Health.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    private bool checking;
    public bool Checking { get => checking; set { if (SetProperty(ref checking, value)) { OnPropertyChanged(nameof(CanCheck)); OnPropertyChanged(nameof(RestoreVisibility)); } } }
    public bool CanCheck => !Checking;
    public bool Expanded { get => expanded; set => SetProperty(ref expanded, value); }
    private ProjectPlan[] Projects => job?.Plan.Projects ?? report?.Plan?.Projects ?? [];
    public bool IsImport => job is not null || Projects.Any(x => x.Creation is not null || x.RecoverySource is not null || x.ExtraImports.Length > 0);
    public DateTime Timestamp => job?.Plan.CreatedUtc ?? report!.CompletedUtc;
    public string Date => Timestamp.ToLocalTime().ToString("g");
    public string Name => job?.Name ?? (report!.Results.Length == 1 ? report.Results[0].Project : $"{report.Results.Length} projects");
    public string State => job?.State is { } state ? state == "Queued" ? "Starting…" : state : report!.Success ? "Completed" : report.Cancelled ? "Cancelled" : "Failed";
    public string Summary => IsImport ? string.Join(" · ", new[] { Platform, Projects.FirstOrDefault()?.PresetName is null or "My default packages" ? "Default" : Projects[0].PresetName, $"{Projects.Sum(x => x.DesiredPolicy.Count)} packages" })
        : Projects.Length == 0 ? "Project operation" : $"{Projects.SelectMany(x => x.Changes).Count(x => x.Kind != ChangeKind.Keep)} package changes";
    private string Platform => (job?.Source.BuildTarget ?? Projects.Select(x => x.Creation ?? x.RecoverySource).FirstOrDefault(x => x is not null)?.BuildTarget) switch { "Android" => "Android / Quest", "iOS" => "iOS", "StandaloneWindows64" => "Windows", _ => "" };
    public string Paths => string.Join(Environment.NewLine, job is not null ? new[] { job.ProjectPath, job.Source.UnityPackagePath } : report!.Results.Select(x => x.Path));
    public string Packages => !IsImport ? "" : string.Join(Environment.NewLine, Projects.SelectMany(x => x.Changes).Where(x => x.Kind != ChangeKind.Remove).DistinctBy(x => x.Id).Select(x => PackageCatalog.FriendlyName(x.Id) + "  " + (x.ResolvedVersion ?? x.RequestedVersion)));
    public string Changes => IsImport ? "" : string.Join(Environment.NewLine, Projects.SelectMany(x => x.Changes).Where(x => x.Kind != ChangeKind.Keep).Select(x => $"{x.Kind} · {PackageCatalog.FriendlyName(x.Id)} · {x.InstalledVersion ?? "—"} → {x.ResolvedVersion ?? "removed"}"));
    public string Result => job?.Message ?? string.Join(Environment.NewLine, report!.Results.Where(x => x.Message.Length > 0).Select(x => x.Project + ": " + x.Message));
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
