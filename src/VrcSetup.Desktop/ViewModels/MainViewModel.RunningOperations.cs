using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<RunningOperationRow> RunningOperations { get; } = [];
    private void RefreshRunningOperations()
    {
        var rows = new List<RunningOperationRow>();
        if (IsCreatingProject) rows.Add(new(UiText.Text("Creating project"), NewProjectName, UiText.Text("Preparing")));
        if (IsApplyingPackages) rows.Add(new(UiText.Text("Applying changes"), ApplyProject, string.Join(" · ", new[] { UiText.Text(ApplyPhase), ApplyPackage, ApplyPackageVersion }.Where(x => x.Length > 0))));
        foreach (var job in app.Imports.Snapshot().Where(x => x.Active))
        {
            var creation = job.Source.EmptyProject;
            var step = new UiImportRow(job).Step;
            rows.Add(new(UiText.Text(creation ? "Creating project" : "Importing assets"), job.Name, UiText.Text(step) + (job.Progress > 0 ? $" · {job.Progress:F0}%" : "")));
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
}

public sealed record RunningOperationRow(string Kind, string Name, string Detail);
