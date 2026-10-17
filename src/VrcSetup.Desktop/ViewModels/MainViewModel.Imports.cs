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
    private const int RequestedParallelism = int.MaxValue;
    public Visibility ResourceSummaryVisibility => HasActiveImports ? Visibility.Visible : Visibility.Collapsed;
    public string ResourceSummary
    {
        get
        {
            var resource = app.Imports.Resources;
            var running = ImportJobs.Count(x => x.Job.State is "Running" or "Cancelling"); var waiting = ImportJobs.Count(x => x.Job.State == "Queued");
            var usage = resource is null ? "Checking resources…" : $"RAM {resource.TotalGiB - resource.AvailableGiB:F1} / {resource.TotalGiB:F0} GB · CPU {(resource.CpuPercent is { } cpu ? cpu.ToString("F0") + "%" : "…")}";
            return usage + $" · {running} running · {waiting} queued";
        }
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MemoryWarningVisibility))] public partial string MemoryWarning { get; set; } = "";
    public Visibility MemoryWarningVisibility => MemoryWarning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    private SystemResourceUsage? lastResource;
    private int memoryHighSamples;
    public ObservableCollection<UiImportDraft> ImportDrafts { get; } = [];
    [ObservableProperty] public partial UiImportDraft? SelectedImportDraft { get; set; }
    private bool switchingImportDraft;
    public Visibility DraftListVisibility => ImportDrafts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    private void NotifyDrafts() { OnPropertyChanged(nameof(DraftListVisibility)); OnPropertyChanged(nameof(PackageFileLabel)); }
    partial void OnSelectedImportDraftChanged(UiImportDraft? value)
    {
        if (switchingImportDraft || value is null) return;
        switchingImportDraft = true;
        try { PackageFile = value.File; ProjectName = value.Name; BuildTarget = value.Platform; creationOverrides = value.Packages; }
        finally { switchingImportDraft = false; }
        NotifyDrafts();
    }
    public void StageImportFiles(IEnumerable<string> selected)
    {
        foreach (var path in selected.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) continue;
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
    public bool ImportsIdle => !HasActiveImports;
    public Visibility ImportsEmptyVisibility => ImportJobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    private int importUpdateQueued;
    private void ImportsChanged(ImportJob[] _)
    {
        if (Interlocked.Exchange(ref importUpdateQueued, 1) != 0) return;
        context.Post(_ =>
        {
            Interlocked.Exchange(ref importUpdateQueued, 0);
            var jobs = app.Imports.Snapshot().OrderByDescending(x => x.Active).ToArray(); var ids = jobs.Select(x => x.Id).ToHashSet();
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
            OnPropertyChanged(nameof(HasActiveImports)); OnPropertyChanged(nameof(ImportsIdle)); OnPropertyChanged(nameof(ImportsEmptyVisibility));
            OnPropertyChanged(nameof(ResourceSummary));
            OnPropertyChanged(nameof(ResourceSummaryVisibility));
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
        var choices = config.Copy(); var preset = SelectedPreset == DefaultPresetKey ? null : SelectedPreset;
        var recipe = SelectedRecipe == "No recipe" ? null : SelectedRecipe;
        var inputFile = PackageFile; var inputName = ProjectName; var inputPlatform = BuildTarget;
        var inputPackages = creationOverrides;
        var frozen = ImportDrafts.Select(x => (Draft: x, Revision: x.Revision, x.File, x.Name, x.Platform,
            Packages: x.Packages is null ? null : new Dictionary<string, string>(x.Packages))).ToArray();
        var plans = new List<ProjectPlan>();
        if (frozen.Length == 0)
        {
            var file = inputFile; var name = inputName; var platform = inputPlatform; var packages = inputPackages is null ? null : new Dictionary<string, string>(inputPackages);
            var single = await Task.Run(() => app.PlanCreationAsync(file, choices, name, preset, packages, platform, [platform], recipe, lifetime.Token));
            plans.AddRange(single.Projects);
        }
        else foreach (var item in frozen)
            plans.AddRange((await Task.Run(() => app.PlanCreationAsync(item.File, choices, item.Name, preset, item.Packages, item.Platform, [item.Platform], recipe, lifetime.Token))).Projects);
        if (plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
            throw new ArgumentException("Two files would create the same project name. Select each file and give it a different name.");
        var reviewed = new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
        if (!await ui.ReviewAsync(reviewed)) return;
        await app.Imports.EnqueueAsync(reviewed, RequestedParallelism, token: lifetime.Token);
        foreach (var item in frozen.Where(x => x.Draft.Revision == x.Revision)) ImportDrafts.Remove(item.Draft);
        if (!ImportDrafts.Contains(SelectedImportDraft!)) SelectedImportDraft = ImportDrafts.FirstOrDefault();
        if (frozen.Length > 0 && ImportDrafts.Count == 0) { PackageFile = ""; ProjectName = ""; }
        else if (frozen.Length == 0 && ImportDrafts.Count == 0 && PackageFile == inputFile && ProjectName == inputName && BuildTarget == inputPlatform && ReferenceEquals(creationOverrides, inputPackages) && SelectedPreset == (preset ?? DefaultPresetKey) && SelectedRecipe == (recipe ?? "No recipe"))
        { PackageFile = ""; ProjectName = ""; }
        NotifyDrafts();
    }
    public void CancelImport(UiImportRow row) => app.Imports.Cancel(row.Id);
    public void CancelAllImports() => app.Imports.CancelAll();
    public Task FinishImportsAsync() => app.Imports.DisposeAsync().AsTask();
    public void OpenImportLog(UiImportRow row)
    { if (File.Exists(row.Job.LogFile)) OpenPath(row.Job.LogFile); else OpenLogsCommand.Execute(null); }
    public async Task RecoverImportAsync(UiImportRow row) => await Guard(async () =>
    {
        var plan = await Task.Run(() => app.Imports.RecoveryPlanAsync(row.Id, config.Copy(), lifetime.Token));
        if (await ui.ReviewAsync(plan)) await app.Imports.EnqueueAsync(plan, RequestedParallelism, row.Job.Source, lifetime.Token);
    });
    public async Task RefreshImportLogAsync(UiImportRow row, bool force = false)
    {
        if (row.LoadingLog || !row.DetailsOpen || !row.LiveLog && !force) return;
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
    [RelayCommand] private async Task ClearImportHistory() => await Guard(async () => await app.Imports.ClearCompletedAsync(lifetime.Token));
    [RelayCommand] private async Task ClearImportCache() => await Guard(async () =>
    {
        if (!await ui.ConfirmAsync("Clear analysis cache", "Remove cached UnityPackage analysis? Package signatures, projects, logs and recovery history are preserved.", "Clear cache")) return;
        var count = await app.Inspection.ClearObservationCacheAsync(lifetime.Token);
        Status = $"Cleared {count} cached analyses";
    });
}

public sealed class UiImportRow(ImportJob job) : ObservableObject
{
    private bool detailsOpen;
    private bool liveLog = true;
    private string logText = "";
    public bool DetailsOpen { get => detailsOpen; set => SetProperty(ref detailsOpen, value); }
    public bool LiveLog { get => liveLog; set => SetProperty(ref liveLog, value); }
    public string LogText { get => logText; set => SetProperty(ref logText, value); }
    public bool LoadingLog { get; set; }
    public string LogStamp { get; set; } = "";
    public ImportJob Job { get; private set; } = job;
    public string Id => Job.Id;
    public string Name => Job.Name;
    public string Path => Job.ProjectPath;
    public string Step => Job.State == "Running" ? Job.Step : Job.State;
    public string Message => Job.Message.Length > 240 ? Job.Message[..240] + "…" : Job.Message;
    public int Progress => Job.Progress;
    public bool CanCancel => Job.Active && Job.State != "Cancelling";
    public Visibility CancelVisibility => Job.Active ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RecoverVisibility => Job.Recoverable ? Visibility.Visible : Visibility.Collapsed;
    public void Update(ImportJob value) { if (Job == value) return; Job = value; OnPropertyChanged(string.Empty); }
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
}
