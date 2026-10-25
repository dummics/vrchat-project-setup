using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty] public partial int UpdateDownloadPercent { get; set; }
    [ObservableProperty] public partial string ApplicationNotice { get; set; } = "";
    private int noticeSequence;
    private void ShowSavedFeedback(string message)
    {
        ApplicationNotice = UiText.Text(message);
        _ = ClearSavedFeedbackAsync(++noticeSequence);
    }
    private async Task ClearSavedFeedbackAsync(int sequence)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(4), lifetime.Token); if (sequence == noticeSequence) ApplicationNotice = ""; }
        catch (OperationCanceledException) { }
    }
    public bool ApplicationWorking => IsCreatingProject || IsApplyingPackages || IsBusy || IsRefreshing || IsInspectingPackage || IsPreparingImport || IsSearchingPackages
        || HasActiveImports || IsDownloadingUpdate || IsInstallingUpdate || IsCheckingUpdate;
    public bool ApplicationProgressIndeterminate => !IsDownloadingUpdate || UpdateStatus == "Preparing update…";
    public Visibility ApplicationProgressVisibility => ApplicationWorking ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ApplicationStatusVisibility => ApplicationStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ApplicationStopVisibility => IsBusy && !IsApplyingPackages && !IsPreparingImport ? Visibility.Visible : Visibility.Collapsed;
    public bool CanUseUpdateAction => MainInteractionEnabled && !IsBusy && !IsCreatingProject && !IsApplyingPackages && !IsDownloadingUpdate && !IsCheckingUpdate;
    public string ApplicationStatus
    {
        get
        {
            var parts = new List<string>();
            if (IsCreatingProject) parts.Add(UiText.Text("Creating project"));
            if (app.Imports.Snapshot().Any(x => x.Source.EmptyProject && x.Recoverable && !x.HiddenFromImports)) parts.Add(UiText.Text("Project creation stopped"));
            if (UpdateFailed) parts.Add(UiText.Text("Update failed"));
            if (IsInstallingUpdate) parts.Add(UiText.Text("Installing update"));
            else if (IsDownloadingUpdate) parts.Add(UpdateStatus == "Preparing update…" ? UiText.Text("Preparing update…") : UiText.Text("Downloading update") + $" · {UpdateDownloadPercent}%");
            else if (IsCheckingUpdate) parts.Add(UiText.Text("Checking for updates…"));
            if (IsApplyingPackages) parts.Add(UiText.Text("Applying changes"));
            else if (IsPreparingImport) parts.Add(UiText.Text("Preparing"));
            if (HasActiveImports)
            {
                var count = app.Imports.Snapshot().Count(x => x.Active);
                parts.Add(string.Format(UiText.Text("{0} projects in progress"), count));
            }
            if (IsInspectingPackage) parts.Add(UiText.Text("Checking packages…"));
            if (IsSearchingPackages) parts.Add(UiText.Text("Searching packages…"));
            if (MemoryWarning.Length > 0) parts.Add(UiText.Text(app.Imports.Resources?.MemoryHigh == true ? "Memory nearly full" : "CPU busy"));
            if (parts.Count == 0 && (IsBusy || IsRefreshing)) parts.Add(UiText.Text("Working…"));
            if (ApplicationNotice.Length > 0 && !IsBusy && !IsApplyingPackages && !IsDownloadingUpdate && !IsInstallingUpdate) parts.Insert(0, ApplicationNotice);
            return parts.Count > 0 ? string.Join(" · ", parts) : UiText.Text(UpdateSummary);
        }
    }
    private void InitializeApplicationStatus()
    {
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy) or nameof(IsRefreshing) or nameof(IsInspectingPackage) or nameof(IsPreparingImport) or nameof(IsSearchingPackages)
                or nameof(IsApplyingPackages) or nameof(HasActiveImports) or nameof(IsDownloadingUpdate) or nameof(UpdateDownloadPercent)
                or nameof(IsInstallingUpdate) or nameof(UpdateFailed) or nameof(UpdateStatus) or nameof(IsCheckingUpdate) or nameof(MemoryWarning) or nameof(ApplicationNotice))
                NotifyApplicationStatus();
        };
    }
    private void NotifyApplicationStatus()
    {
        RefreshRunningOperations();
        OnPropertyChanged(nameof(ApplicationStatus)); OnPropertyChanged(nameof(ApplicationStatusVisibility));
        OnPropertyChanged(nameof(ApplicationWorking)); OnPropertyChanged(nameof(ApplicationProgressVisibility));
        OnPropertyChanged(nameof(ApplicationProgressIndeterminate)); OnPropertyChanged(nameof(ApplicationStopVisibility));
        OnPropertyChanged(nameof(CanUseUpdateAction));
    }
    [RelayCommand] private async Task OpenApplicationStatus()
    {
        if (UpdateFailed) await ShowUpdateDetails();
        else if (HasActiveImports && !IsApplyingPackages) await NavigateAsync("Create");
        else OpenLogsCommand.Execute(null);
    }
    public Visibility ImportJobsVisibility => ImportJobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ClearFinishedVisibility => CanClearFinishedImports ? Visibility.Visible : Visibility.Collapsed;
}
