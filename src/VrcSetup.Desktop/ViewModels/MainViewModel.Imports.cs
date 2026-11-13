using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Microsoft.UI.Xaml;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<UiImportRow> ImportJobs { get; } = [];
    public Task RetryImportHistorySaveAsync(UiImportRow row) => Guard(async () =>
    {
        if (await app.Imports.RetryHistorySaveAsync(row.Id, lifetime.Token))
            ApplicationNotice = UiText.Text("Import status saved.");
    }, allowWhileBusy: true);
    private const int RequestedParallelism = 0; // Adapt to available CPU and memory.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ImportReviewLabel)), NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    public partial bool IsPreparingImport { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ImportErrorVisibility)), NotifyPropertyChangedFor(nameof(ImportSettingsVisibility))]
    public partial string ImportError { get; set; } = "";
    public string ImportReviewLabel => IsPreparingImport ? "Preparing review…" : IsInspectingPackage ? "Checking packages…" : "Review and import";
    public bool CanPrepareImportReview => !string.IsNullOrWhiteSpace(PackageFile) && !IsPreparingImport && !IsInspectingPackage
        && !IsClosingApp && !IsInstallingUpdate && (!IsBusy || IsApplyingPackages);
    public Visibility ImportErrorVisibility => ImportError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImportSettingsVisibility => ImportError.Contains("Settings", StringComparison.OrdinalIgnoreCase) || ImportError.Contains(UiText.Text("Settings"), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ImportDiagnosticVisibility))] public partial bool ImportErrorNeedsDiagnostics { get; set; }
    public Visibility ImportDiagnosticVisibility => ImportErrorNeedsDiagnostics && ImportError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    partial void OnImportErrorChanged(string value) { ImportErrorNeedsDiagnostics = false; OnPropertyChanged(nameof(ImportDiagnosticVisibility)); }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ManualImportPathVisibility))] public partial bool ManualImportPath { get; set; }
    public Visibility ManualImportPathVisibility => ManualImportPath ? Visibility.Visible : Visibility.Collapsed;
    public string ResourceSummary
    {
        get
        {
            var resource = app.Imports.Resources;
            var usage = resource is null ? UiText.Text("Checking resources…") : $"RAM {resource.TotalGiB - resource.AvailableGiB:F1} / {resource.TotalGiB:F0} GB · CPU {(resource.CpuPercent is { } cpu ? cpu.ToString("F0") + "%" : "…")}";
            return usage;
        }
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MemoryWarningVisibility))] public partial string MemoryWarning { get; set; } = "";
    public Visibility MemoryWarningVisibility => MemoryWarning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    private SystemResourceUsage? lastResource;
    private int memoryHighSamples;
    public ObservableCollection<UiImportDraft> ImportDrafts { get; } = [];
    [ObservableProperty] public partial UiImportDraft? SelectedImportDraft { get; set; }
    private bool switchingImportDraft;
    private PackageIdentity? selectionSourceIdentity;
    private void RememberSelectionSource()
    {
        selectionSourceIdentity ??= CreationInspection?.SourceIdentity;
        if (SelectedImportDraft is { } draft) draft.SelectionSourceIdentity = selectionSourceIdentity;
    }
    public Visibility DraftListVisibility => ImportDrafts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    private void NotifyDrafts() { OnPropertyChanged(nameof(DraftListVisibility)); OnPropertyChanged(nameof(PackageFileLabel)); RefreshCreationPackages(); }
    partial void OnSelectedImportDraftChanged(UiImportDraft? value)
    {
        if (switchingImportDraft || value is null) return;
        switchingImportDraft = true;
        try { PackageFile = value.File; ProjectName = value.Name; BuildTarget = value.Platform; creationOverrides = value.Packages; selectionSourceIdentity = value.SelectionSourceIdentity; }
        finally { switchingImportDraft = false; }
        NotifyDrafts();
    }
    public void StageImportFiles(IEnumerable<string> selected)
    {
        if (IsClosingApp) return;
        ImportError = "";
        foreach (var path in selected.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            { ImportError = "Choose a .unitypackage file, not a folder or another file type."; continue; }
            if (ImportDrafts.Any(x => x.File.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            ImportDrafts.Add(new(path, ProjectRules.SuggestName(path, config), ProjectRules.SuggestBuildTarget(path)));
        }
        SelectedImportDraft = ImportDrafts.LastOrDefault(); NotifyDrafts();
    }
    public void RemoveImportDraft(UiImportDraft draft)
    {
        ImportDrafts.Remove(draft);
        if (!ImportDrafts.Contains(SelectedImportDraft!)) SelectedImportDraft = ImportDrafts.FirstOrDefault();
        if (ImportDrafts.Count == 0) { PackageFile = ""; ProjectName = ""; }
        NotifyDrafts();
    }
    public bool HasActiveImports => app.Imports.HasActiveJobs;
    private bool ImportBlocksAutomaticRetry(string path) => app.Imports.Snapshot()
        .Any(job => (job.Active || job.DiscardRequested || job.State == "Paused") && job.Plan.Projects.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
    public bool ImportsIdle => !HasActiveImports;
    internal static ImportJob[] VisibleImportJobs(ImportJob[] snapshot, bool hideCompleted) => snapshot
        .Where(x => !x.Source.EmptyProject || x.HistorySaveFailed || x.CreatorCompanionUnknown)
        .GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase)
        .Select(group => ImportQueue.CurrentForProject(group, group.Key)!)
        .Where(x => !(x.RecoveryNoticeDismissed && x.Recoverable && x.CanDismiss && Directory.Exists(x.ProjectPath))
            && (!x.HiddenFromImports || !x.CanHide) && !(hideCompleted && x.CanHide && !x.CreatorCompanionPending && !x.CreatorCompanionUnknown && !x.UnityCompilationNeedsAttention && x.State is "Completed" or "Recovered"))
        .OrderByDescending(x => x.Plan.CreatedUtc).ToArray();
    private int importUpdateQueued;
    private void ImportsChanged(ImportJob[] _)
    {
        if (Interlocked.Exchange(ref importUpdateQueued, 1) != 0) return;
        context.Post(_ =>
        {
            Interlocked.Exchange(ref importUpdateQueued, 0);
            var snapshot = app.Imports.Snapshot();
            RefreshRegistrationNotices(snapshot);
            ObserveCreationResults(snapshot);
            var jobs = VisibleImportJobs(snapshot, AutoHideCompletedImports); var ids = jobs.Select(x => x.Id).ToHashSet();
            for (var index = ImportJobs.Count - 1; index >= 0; index--) if (!ids.Contains(ImportJobs[index].Id)) ImportJobs.RemoveAt(index);
            foreach (var job in jobs)
            {
                var row = ImportJobs.FirstOrDefault(x => x.Id == job.Id);
                if (row is null) ImportJobs.Add(new(job)); else row.Update(job);
            }
            for (var index = 0; index < jobs.Length; index++)
            {
                var current = ImportJobs.IndexOf(ImportJobs.First(x => x.Id == jobs[index].Id));
                if (current != index) ImportJobs.Move(current, index);
            }
            OnPropertyChanged(nameof(HasActiveImports)); OnPropertyChanged(nameof(ImportsIdle));
            NotifySettingsState();
            OnPropertyChanged(nameof(ResourceSummary));
            OnPropertyChanged(nameof(CanClearFinishedImports));
            OnPropertyChanged(nameof(ImportJobsVisibility)); OnPropertyChanged(nameof(ClearFinishedVisibility));
            if (CurrentPage == "Activity") ReconcileHistory(snapshot);
            var resource = app.Imports.Resources;
            if (resource is not null && !ReferenceEquals(resource, lastResource))
            {
                lastResource = resource;
                memoryHighSamples = resource.MemoryHigh || resource.CpuPercent is >= 95 ? memoryHighSamples + 1 : 0;
                if (HasActiveImports && memoryHighSamples >= 2) MemoryWarning = resource.MemoryHigh ? "Memory is nearly full. Imports continue; cancel an import if needed." : "CPU is busy. Imports may take longer.";
                else if (resource.AvailableGiB > 2 && resource.AvailableCommitGiB > 2 && resource.AvailableGiB / resource.TotalGiB > 0.15 && resource.CpuPercent is < 85) MemoryWarning = "";
            }
            if (!HasActiveImports) MemoryWarning = "";
            foreach (var row in ImportJobs.Where(x => x.DetailsOpen)) _ = RefreshImportLogAsync(row);
        }, null);
    }
    private async Task QueueCreationAsync()
    {
        if (!await FindEditorAsync(true)) return;
        var choices = config.Copy(); var preset = SelectedPreset == DefaultPresetKey ? null : SelectedPreset;
        // Use the settings currently visible in the UI, without saving unrelated edits.
        choices.UnityEditorPath = string.IsNullOrWhiteSpace(UnityEditorPath) ? "" : ProjectRules.NormalizePath(UnityEditorPath);
        choices.UnityProjectsRoot = string.IsNullOrWhiteSpace(ProjectsRoot) ? "" : ProjectRules.NormalizePath(ProjectsRoot);
        var recipe = SelectedRecipe == "No recipe" ? null : SelectedRecipe;
        var inputFile = PackageFile; var inputName = ProjectName; var inputPlatform = BuildTarget;
        var inputPackages = creationOverrides; var inputIdentity = selectionSourceIdentity;
        var frozen = ImportDrafts.Select(x => (Draft: x, Revision: x.Revision, x.File, x.Name, x.Platform,
            Packages: x.Packages is null ? null : new Dictionary<string, string>(x.Packages), Identity: x.SelectionSourceIdentity)).ToArray();
        var plans = new List<ProjectPlan>();
        if (frozen.Length == 0)
        {
            var file = inputFile; var name = inputName; var platform = inputPlatform; var packages = inputPackages is null ? null : new Dictionary<string, string>(inputPackages);
            var single = await Task.Run(() => app.PlanCreationAsync(file, choices, name, preset, packages, platform, [platform], recipe, lifetime.Token));
            if (inputIdentity is not null && single.Projects.Any(x => (x.Creation ?? x.RecoverySource)?.SourceIdentity != inputIdentity))
            { await RefreshChangedPackageAsync(file, null); return; }
            plans.AddRange(single.Projects);
        }
        else foreach (var item in frozen)
        {
            var planned = await Task.Run(() => app.PlanCreationAsync(item.File, choices, item.Name, preset, item.Packages, item.Platform, [item.Platform], recipe, lifetime.Token));
            if (item.Identity is not null && planned.Projects.Any(x => (x.Creation ?? x.RecoverySource)?.SourceIdentity != item.Identity))
            { await RefreshChangedPackageAsync(item.File, item.Draft); return; }
            plans.AddRange(planned.Projects);
        }
        if (plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
            throw new ArgumentException("Two files would create the same project name. Select each file and give it a different name.");
        var reviewed = new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
        if (ProjectApplyActive(plans.Select(x => x.Path)) || !await ui.ReviewAsync(reviewed)) return;
        if (IsInstallingUpdate || ProjectApplyActive(plans.Select(x => x.Path))) return;
        await app.Imports.EnqueueAsync(reviewed, RequestedParallelism, token: lifetime.Token);
        foreach (var item in frozen.Where(x => x.Draft.Revision == x.Revision)) ImportDrafts.Remove(item.Draft);
        if (!ImportDrafts.Contains(SelectedImportDraft!)) SelectedImportDraft = ImportDrafts.FirstOrDefault();
        if (frozen.Length > 0 && ImportDrafts.Count == 0) { PackageFile = ""; ProjectName = ""; }
        else if (frozen.Length == 0 && ImportDrafts.Count == 0 && PackageFile == inputFile && ProjectName == inputName && BuildTarget == inputPlatform && ReferenceEquals(creationOverrides, inputPackages) && SelectedPreset == (preset ?? DefaultPresetKey) && SelectedRecipe == (recipe ?? "No recipe"))
        { PackageFile = ""; ProjectName = ""; }
        NotifyDrafts();
    }
    private async Task RefreshChangedPackageAsync(string file, UiImportDraft? draft)
    {
        if (draft is not null && ImportDrafts.Contains(draft)) SelectedImportDraft = draft;
        if (!await ui.ConfirmAsync("Package changed", "This file changed after you chose packages. Check the suggestions again before importing. Your selection will be kept.", "Check packages")) return;
        await InspectCreationPackageAsync(file);
        if (PackageFile == file && (draft is null || SelectedImportDraft == draft) && CreationInspection?.SourceIdentity is { } identity)
        {
            selectionSourceIdentity = identity;
            if (draft is not null) draft.SelectionSourceIdentity = identity;
            ImportError = "The file changed. Check your package selection, then review the import again.";
        }
    }
    public Task PauseImportAsync(UiImportRow row) => IndependentAction(() => app.Imports.PauseAsync(row.Id, lifetime.Token));
    public void CancelAllImports() => app.Imports.CancelAll();
    public Task FinishImportsAsync() => app.Imports.DisposeAsync().AsTask();
    public void OpenImportLog(UiImportRow row)
    { if (File.Exists(row.Job.LogFile)) RevealLog(row.Job.LogFile); else OpenLogsCommand.Execute(null); }
    public Task RecoverImportAsync(UiImportRow row) => row.Job.HasCompilationBlock || row.Job.UnityCompilationNeedsAttention
        ? Guard(() => LaunchUnityAsync(row.Job.ProjectPath), allowWhileBusy: true)
        : RepairHistoryImportAsync(new(row.Job));
    public async Task RefreshImportLogAsync(UiImportRow row, bool force = false)
    {
        if (row.LoadingLog || !row.DetailsOpen) return;
        row.LoadingLog = true;
        try
        {
            var tail = await app.Imports.ReadLogTailAsync(row.Id, row.LogStamp, lifetime.Token);
            if (tail.Stamp != row.LogStamp || tail.Stamp.Length == 0) { row.LogText = tail.Text; row.LogStamp = tail.Stamp; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { row.LogText = "Log unavailable. Use Open log to inspect it."; }
        finally { row.LoadingLog = false; }
    }
    [RelayCommand] private async Task ClearImportHistory() => await Guard(async () => await app.Imports.ClearFinishedAsync(lifetime.Token));
    internal Task DismissImportAsync(UiImportRow row) => Guard(() => app.Imports.DismissAsync(row.Id, lifetime.Token), allowWhileBusy: true);
    [RelayCommand] private async Task ClearImportCache() => await Guard(async () =>
    {
        if (!RequireImportsIdle()) return;
        if (!await ui.ConfirmAsync("Clear analysis cache", "Remove cached UnityPackage analysis? Package signatures, projects, logs and recovery history are preserved.", "Clear cache")) return;
        if (!RequireImportsIdle()) return;
        var count = await app.Inspection.ClearObservationCacheAsync(lifetime.Token);
        Status = $"Cleared {count} cached analyses";
    });
}

public sealed class UiImportRow(ImportJob job) : ObservableObject
{
    private bool detailsOpen;
    private string logText = "";
    private double logHeight = 105;
    private bool isRegistering;
    public bool IsRegistering { get => isRegistering; set { if (SetProperty(ref isRegistering, value)) OnPropertyChanged(nameof(CanRegister)); } }
    public bool CanRegister => !IsRegistering;
    public double LogHeight { get => logHeight; set => SetProperty(ref logHeight, value); }
    public bool DetailsOpen { get => detailsOpen; set => SetProperty(ref detailsOpen, value); }
    public string LogText
    {
        get => logText;
        set
        {
            if (!SetProperty(ref logText, value)) return;
            OnPropertyChanged(nameof(PreviousLogText)); OnPropertyChanged(nameof(LatestLogText));
            OnPropertyChanged(nameof(TechnicalLogText));
        }
    }
    public string LatestLogText => LogLines().LastOrDefault() ?? "";
    public string TechnicalLogText => string.Join("\n", LogLines().Reverse());
    public string PreviousLogText { get { var lines = LogLines(); return string.Join("\n", lines.Take(Math.Max(0, lines.Length - 1)).Reverse()); } }
    private string[] LogLines() => logText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public bool LoadingLog { get; set; }
    public string LogStamp { get; set; } = "";
    public ImportJob Job { get; private set; } = job;
    public string Id => Job.Id;
    public string Name => Job.Name;
    public string Elapsed
    {
        get
        {
            if (Job.StartedAt is not { } start || !Job.Active && Job.FinishedAt is null) return "—";
            var elapsed = (Job.FinishedAt ?? DateTime.UtcNow) - start;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        }
    }
    public string Path => Job.ProjectPath;
    private string? PhaseLabel => Job.Step switch
    {
        "Create" => "Creating project", "Prepare" or "Preflight" => "Preparing project", "Packages" => "Adding packages",
        "Import" => Job.Plan.Projects.FirstOrDefault()?.RecoverySource is null ? "Importing assets" : "Reimporting assets",
        "Validate" => "Checking project", "Finish" => "Finishing", "ClosingUnity" => "Waiting for Unity to close…", "Starting" => "Starting…", _ => null
    };
    public string LastPhase => Job.Recoverable && PhaseLabel is { } phase
        ? string.Format(UiText.Text("Last step: {0}"), UiText.Text(phase)) : "";
    public Visibility LastPhaseVisibility => LastPhase.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Step => Job.HistorySaveFailed ? "Save needs attention" : Job.CreatorCompanionPending || Job.CreatorCompanionUnknown || Job.UnityCompilationNeedsAttention ? "Completed with a notice" : Job.DiscardRequested ? Job.Active ? "Stopping import…" : "Cancellation needs finishing" : Job.State == "Running" ? PhaseLabel ?? "Working…" : Job.State switch
    {
        "Failed" when Job.HasCompilationBlock => "Script errors in Unity",
        "Queued" => "Waiting in queue", "Interrupted" => "Unexpected interruption",
        "Cancelled" when !Job.DiscardCompleted => "Stopped by you",
        "Pausing" or "Cancelling" => "Stopping safely…", _ => Job.State
    };
    public string Message => Job.HistorySaveFailed ? "The import status could not be saved. Check storage and retry saving before closing the app."
        : Job.UnityCompilationNeedsAttention && (Job.CreatorCompanionPending || Job.CreatorCompanionUnknown) ? "The import finished. Open the project in Unity to fix script errors. Also check that the project appears in Creator Companion."
        : Job.UnityCompilationNeedsAttention ? "The import finished. Open the project in Unity to fix script errors. You do not need to import it again."
        : Job.CreatorCompanionUnknown ? "Your project is ready. Check whether it appears in Creator Companion; if needed, add it there as an existing project. You do not need to import it again."
        : Job.CreatorCompanionPending ? "Your project is ready. It was not added to Creator Companion. Retry here, or add it there as an existing project. You do not need to import it again."
        : Job.DiscardRequested ? "Your cancellation request is saved. The project is preserved until cleanup finishes."
        : Job.DiscardCompleted ? Job.DiscardNoFolder ? "Request cancelled. No project folder was removed." : "The import was cancelled and its created project was removed."
        : Job.State switch
        {
            "Paused" => "The import is paused. Your project is kept. Choose Continue import when you are ready.",
            "Interrupted" => "The previous session ended before the import finished. Your project is kept. You can continue the import.",
            "Cancelled" => "You stopped this import. Your project is kept. You can continue the import.",
            "Failed" when Job.HasCompilationBlock => "Unity stopped because of script errors. Your project is kept. Open it in Unity to check the packages and scripts; importing again may not fix this.",
            "Failed" when Job.Message.Contains("Automatic restore incomplete", StringComparison.OrdinalIgnoreCase) => "The import stopped and the previous files could not be restored automatically. Your recovery copy was kept. Open the project options to review and restore a backup.",
            "Failed" when Job.Message.StartsWith("This package differs from the original.", StringComparison.Ordinal) || Job.Message.StartsWith("UnityPackage changed after review.", StringComparison.Ordinal) => "The UnityPackage changed after it was selected. Choose Try again to select the original file or review the import again.",
            "Failed" when Job.Message.Contains("The project path may be too long.", StringComparison.Ordinal) => "The project was kept. Its path may be too long for Unity. Choose a shorter projects folder in Settings and import again, or open the log for details.",
            "Failed" when Job.Message.Contains("could not import some project files.", StringComparison.Ordinal) => "The project was kept. Unity could not import some files. Open the log, fix the errors in Unity, then try again.",
            "Failed" => "The import could not finish. Your project and recovery information are kept. Choose Try again, or open the log for details.",
            "Pausing" or "Cancelling" => "Waiting for the import to stop safely. Keep the app open until it finishes.",
            "Completed" or "Recovered" => "The project is ready. You can open it from Projects.",
            _ => ""
        };
    public int Progress => Job.Progress;
    public Visibility MessageVisibility => Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool CanPause => !Job.DiscardRequested && Job.Active && Job.State is not ("Cancelling" or "Pausing");
    public string PauseLabel => Job.State is "Cancelling" or "Pausing" ? "Pausing…" : "Pause import";
    public string RetryLabel => Job.HasCompilationBlock || Job.UnityCompilationNeedsAttention ? "Open in Unity" : Job.State is "Interrupted" or "Paused" or "Cancelled" ? "Continue import" : "Try again";
    public Visibility AttentionVisibility => Job.Recoverable || Job.CreatorCompanionPending || Job.CreatorCompanionUnknown || Job.UnityCompilationNeedsAttention || Job.HistorySaveFailed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HistorySaveVisibility => Job.HistorySaveFailed && !Job.Active ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PauseVisibility => Job.Active ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RecoverVisibility => (Job.Recoverable || Job.UnityCompilationNeedsAttention) && !Job.Active && !Job.HistorySaveFailed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DismissVisibility => Job.CanDismiss && (!Job.Recoverable || Directory.Exists(Job.ProjectPath)) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RegistrationVisibility => Job.CreatorCompanionPending && !Job.Active && !Job.HistorySaveFailed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DiscardVisibility => !Job.HistorySaveFailed && !Job.DiscardCompleted && (Job.Source.CreationId is not null || Job.DiscardRequested || MainViewModel.CanCancelWithoutFolder(Job.ProjectPath))
        && (Job.Active || Job.Recoverable || Job.DiscardRequested) ? Visibility.Visible : Visibility.Collapsed;
    public string DiscardLabel => MainViewModel.CanCancelWithoutFolder(Job.ProjectPath) ? "Cancel import" : "Cancel and remove project";
    public void Update(ImportJob value) { OnPropertyChanged(nameof(Elapsed)); if (Job == value) return; Job = value; OnPropertyChanged(string.Empty); }
}

public sealed class UiImportDraft(string file, string name, string platform) : ObservableObject
{
    public string File { get; } = file;
    private string name = name, platform = platform;
    public string FileName => System.IO.Path.GetFileName(File);
    public int Revision { get; private set; }
    public string Name { get => name; set { if (SetProperty(ref name, value)) Revision++; } }
    public string Platform { get => platform; set { if (SetProperty(ref platform, value)) Revision++; } }
    private Dictionary<string, string>? packages;
    public Dictionary<string, string>? Packages { get => packages; set { packages = value; Revision++; } }
    private PackageIdentity? selectionSourceIdentity;
    public PackageIdentity? SelectionSourceIdentity { get => selectionSourceIdentity; set { if (SetProperty(ref selectionSourceIdentity, value)) Revision++; } }
}
