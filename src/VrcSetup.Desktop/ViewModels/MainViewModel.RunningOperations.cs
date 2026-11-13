using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<RunningOperationRow> RunningOperations { get; } = [];
    internal static ImportJob[] CurrentRunningImports(IEnumerable<ImportJob> jobs) => jobs
        .GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase)
        .Select(group => ImportQueue.CurrentForProject(group, group.Key)!).Where(x => x.Active || x.Source.EmptyProject && (x.Recoverable || x.DiscardRequested)).ToArray();
    private void RefreshRunningOperations()
    {
        var rows = new List<RunningOperationRow>();
        if (IsCreatingProject) rows.Add(new(UiText.Text("Creating project"), NewProjectName, UiText.Text("Preparing")));
        if (IsApplyingPackages) rows.Add(new(UiText.Text("Applying changes"), ApplyProject, string.Join(" · ", new[] { UiText.Text(ApplyPhase), ApplyPackage, ApplyPackageVersion }.Where(x => x.Length > 0))));
        foreach (var job in CurrentRunningImports(app.Imports.Snapshot()))
        {
            var creation = job.Source.EmptyProject;
            var step = new UiImportRow(job).Step;
            rows.Add(new(UiText.Text(creation ? job.DiscardRequested ? "Cancellation needs finishing" : job.Active ? "Creating project" : "Project creation stopped" : "Import"), job.Name,
                job.Active ? UiText.Text(step) : UserErrors.Message(new IOException(job.Message)), job.Id, creation && job.Recoverable,
                creation && !job.DiscardCompleted && (job.DiscardRequested || job.Source.CreationId is not null || CanCancelWithoutFolder(job.ProjectPath)), job.DiscardRequested));
        }
        if (IsDownloadingUpdate || IsCheckingUpdate || UpdateFailed)
            rows.Add(new(UiText.Text("Application update"), "", UiText.Text(UpdateStatus)));
        if (IsPreparingImport) rows.Add(new(UiText.Text("Preparing"), ProjectName, ""));
        if (IsInspectingPackage) rows.Add(new(UiText.Text("Checking packages…"), "", ""));
        if (IsSearchingPackages) rows.Add(new(UiText.Text("Searching packages…"), "", ""));
        if (rows.Count == 0) rows.Add(new(UiText.Text("No operations in progress"), "", ""));
        // Update in place: queue timers must not rebuild/flicker the open flyout.
        while (RunningOperations.Count > rows.Count) RunningOperations.RemoveAt(RunningOperations.Count - 1);
        for (var i = 0; i < rows.Count; i++)
            if (i >= RunningOperations.Count) RunningOperations.Add(rows[i]);
            else if (RunningOperations[i] != rows[i]) RunningOperations[i] = rows[i];
    }
    [RelayCommand] private async Task ViewActivity() => await NavigateAsync("Activity");

    private readonly Dictionary<string, string> observedCreations = [];
    private bool projectRefreshPending;
    private bool projectRefreshRunning;
    internal Task PendingProjectRefresh { get; private set; } = Task.CompletedTask;
    internal void ObserveCreationResults(ImportJob[] jobs)
    {
        var refreshProjects = false;
        foreach (var job in jobs)
        {
            if (observedCreations.TryGetValue(job.Id, out var old) && old is "Queued" or "Running" or "Cancelling" or "Pausing" && !job.Active)
            {
                if (job.Source.EmptyProject && !job.DiscardCompleted && job.State is "Completed" or "Recovered")
                    ShowSavedFeedback(string.Format(UiText.Text("Project created: {0}"), job.Name));
                refreshProjects = true;
            }
            observedCreations[job.Id] = job.State;
        }
        if (refreshProjects)
        {
            projectRefreshPending = true;
            StartPendingProjectRefresh();
        }
    }
    private void StartPendingProjectRefresh()
    {
        if (!projectRefreshPending || projectRefreshRunning || CurrentPage != "Projects" || IsClosingApp) return;
        projectRefreshRunning = true;
        PendingProjectRefresh = DrainPendingProjectRefreshAsync();
    }
    private async Task DrainPendingProjectRefreshAsync()
    {
        try
        {
            while (projectRefreshPending && CurrentPage == "Projects" && !IsClosingApp)
            {
                // A manual refresh may already be running. Wait for it, then rescan:
                // its snapshot may predate the import completion we observed.
                if (RefreshProjectsCommand.IsRunning && RefreshProjectsCommand.ExecutionTask is { } existing)
                    await existing;
                if (CurrentPage != "Projects" || IsClosingApp) break;
                projectRefreshPending = false;
                await RefreshProjectsCommand.ExecuteAsync(null);
            }
        }
        catch (Exception error) { app.Diagnostics.Write("Project refresh after import", error.Message, error); }
        finally { projectRefreshRunning = false; }
    }
    public async Task RetryCreationAsync(string id)
    {
        if (app.Imports.Snapshot().FirstOrDefault(x => x.Id == id && x.Source.EmptyProject && x.Recoverable) is not { } job) return;
        await RepairHistoryImportAsync(new(job));
    }
    public async Task CancelCreationAsync(string id)
    {
        if (app.Imports.Snapshot().FirstOrDefault(x => x.Id == id && x.Source.EmptyProject && !x.DiscardCompleted) is not { } job) return;
        await DiscardImportAsync(new(job));
    }
}

public sealed record RunningOperationRow(string Kind, string Name, string Detail, string Id = "", bool Recoverable = false, bool CanDiscard = false, bool DiscardPending = false)
{
    public string Caption => Name.Length > 0 ? Name : Kind;
    public string ShortState => Name.Length > 0 ? Kind : "";
    public Visibility ActionsVisibility => Recoverable || CanDiscard ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => Recoverable && !DiscardPending ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DiscardVisibility => CanDiscard ? Visibility.Visible : Visibility.Collapsed;
    public string DiscardLabel => DiscardPending ? "Finish cancellation" : "Cancel creation";
}
