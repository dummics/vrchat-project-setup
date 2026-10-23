using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<RunningOperationRow> RunningOperations { get; } = [];
    private void RefreshRunningOperations()
    {
        var rows = new List<RunningOperationRow>();
        if (IsCreatingProject) rows.Add(new(UiText.Text("Creating project"), NewProjectName, UiText.Text("Preparing")));
        if (IsApplyingPackages) rows.Add(new(UiText.Text("Applying changes"), ApplyProject, string.Join(" · ", new[] { UiText.Text(ApplyPhase), ApplyPackage, ApplyPackageVersion }.Where(x => x.Length > 0))));
        foreach (var job in app.Imports.Snapshot().Where(x => x.Active || x.Source.EmptyProject && x.Recoverable && !x.HiddenFromImports))
        {
            var creation = job.Source.EmptyProject;
            var step = new UiImportRow(job).Step;
            rows.Add(new(UiText.Text(creation ? job.Active ? "Creating project" : "Project creation stopped" : "Importing assets"), job.Name,
                job.Active ? UiText.Text(step) : UserErrors.Message(new IOException(job.Message)), job.Id, creation && job.Recoverable));
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
    private void ObserveCreationResults(ImportJob[] jobs)
    {
        var refreshProjects = false;
        foreach (var job in jobs.Where(x => x.Source.EmptyProject))
        {
            if (observedCreations.TryGetValue(job.Id, out var old) && old is "Queued" or "Running" or "Cancelling" && !job.Active)
            {
                if (!job.Recoverable) ShowSavedFeedback(string.Format(UiText.Text("Project created: {0}"), job.Name));
                refreshProjects = true;
            }
            observedCreations[job.Id] = job.State;
        }
        if (refreshProjects && CurrentPage == "Projects") _ = RefreshProjectsCommand.ExecuteAsync(null);
    }
    public Task DismissCreationAsync(string id) => IndependentAction(() => app.Imports.DismissAsync(id, lifetime.Token));
    public async Task RetryCreationAsync(string id)
    {
        if (app.Imports.Snapshot().FirstOrDefault(x => x.Id == id && x.Source.EmptyProject && x.Recoverable) is not { } job) return;
        await RepairHistoryImportAsync(new(job));
        if (app.Imports.Snapshot().Any(x => x.Active && x.ProjectPath.Equals(job.ProjectPath, StringComparison.OrdinalIgnoreCase)))
            await DismissCreationAsync(id);
    }
}

public sealed record RunningOperationRow(string Kind, string Name, string Detail, string Id = "", bool Recoverable = false)
{
    public string Caption => Name.Length > 0 ? Name : Kind;
    public string ShortState => Name.Length > 0 ? Kind : "";
    public Visibility ActionsVisibility => Recoverable ? Visibility.Visible : Visibility.Collapsed;
}
