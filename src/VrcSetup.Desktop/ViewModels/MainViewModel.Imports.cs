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
    [ObservableProperty] public partial int ImportParallelism { get; set; } = 1;
    public int[] ImportParallelismChoices => [1, 2];
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
            var jobs = app.Imports.Snapshot(); var ids = jobs.Select(x => x.Id).ToHashSet();
            for (var index = ImportJobs.Count - 1; index >= 0; index--) if (!ids.Contains(ImportJobs[index].Id)) ImportJobs.RemoveAt(index);
            foreach (var job in jobs)
            {
                var row = ImportJobs.FirstOrDefault(x => x.Id == job.Id);
                if (row is null) ImportJobs.Add(new(job)); else row.Update(job);
            }
            OnPropertyChanged(nameof(HasActiveImports)); OnPropertyChanged(nameof(ImportsIdle)); OnPropertyChanged(nameof(ImportsEmptyVisibility));
        }, null);
    }
    public async Task AddImportFilesAsync(IEnumerable<string> selected) => await Guard(async () =>
    {
        var files = selected.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) return;
        if (files.Length > 50) throw new ArgumentException("Add up to 50 UnityPackages at a time.");
        var choices = config.Copy(); var preset = SelectedPreset == DefaultPresetKey ? null : SelectedPreset;
        var plans = new List<ProjectPlan>();
        foreach (var file in files)
        {
            var platform = ProjectRules.SuggestBuildTarget(file);
            var plan = await Task.Run(() => app.PlanCreationAsync(file, choices, ProjectRules.SuggestName(file, choices),
                preset, buildTarget: platform, publishingPlatforms: [platform], cancellationToken: lifetime.Token));
            plans.AddRange(plan.Projects);
        }
        if (plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
            throw new ArgumentException("Two files would create the same project name. Import them separately using Create to choose distinct names.");
        var reviewed = new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
        if (!await ui.ReviewAsync(reviewed)) return;
        await app.Imports.EnqueueAsync(reviewed, ImportParallelism, token: lifetime.Token);
        CurrentPage = "Imports";
    });
    public void CancelImport(UiImportRow row) => app.Imports.Cancel(row.Id);
    public void CancelAllImports() => app.Imports.CancelAll();
    public Task FinishImportsAsync() => app.Imports.DisposeAsync().AsTask();
    public void OpenImportLog(UiImportRow row)
    { if (File.Exists(row.Job.LogFile)) OpenPath(row.Job.LogFile); else OpenLogsCommand.Execute(null); }
    public async Task RecoverImportAsync(UiImportRow row) => await Guard(async () =>
    {
        var plan = await Task.Run(() => app.Imports.RecoveryPlanAsync(row.Id, config.Copy(), lifetime.Token));
        if (await ui.ReviewAsync(plan)) await app.Imports.EnqueueAsync(plan, ImportParallelism, row.Job.Source, lifetime.Token);
    });
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
