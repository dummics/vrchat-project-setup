using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private ApplicationRelease? availableUpdate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateLabel))] public partial bool IsCheckingUpdate { get; set; }
    [ObservableProperty] public partial bool CheckUpdatesOnStartup { get; set; } = true;
    [ObservableProperty] public partial bool IncludeBetaUpdates { get; set; }
    [ObservableProperty] public partial bool AutomaticUpdateEnabled { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateLabel))] public partial bool IsUpdateReady { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateLabel))] public partial bool HasApplicationUpdate { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateLabel))] public partial bool IsDownloadingUpdate { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MainInteractionEnabled)), NotifyPropertyChangedFor(nameof(ShellInteractionEnabled))] public partial bool IsInstallingUpdate { get; set; }
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";
    [ObservableProperty] public partial bool UpdateFailed { get; set; }
    private string updateDetails = "";
    public string UpdateSummary => UpdateFailed ? "Update failed" : IsDownloadingUpdate ? UpdateStatus : IsUpdateReady ? "Ready for next launch" : UpdateStatus.StartsWith("Update checks are temporarily paused") ? UpdateStatus : UpdateStatus.StartsWith("Finish") ? "Finish current work before updating" : UpdateStatus.StartsWith("No published") ? "Up to date" : UpdateStatus.StartsWith("Checking") ? "Checking for updates…" : "";
    partial void OnIsUpdateReadyChanged(bool value) { OnPropertyChanged(nameof(UpdateSummary)); OnPropertyChanged(nameof(UpdateSummaryVisibility)); NotifyApplicationStatus(); }
    public Microsoft.UI.Xaml.Visibility UpdateSummaryVisibility => UpdateSummary.Length == 0 ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    partial void OnUpdateStatusChanged(string value) { OnPropertyChanged(nameof(UpdateSummary)); OnPropertyChanged(nameof(UpdateSummaryVisibility)); OnPropertyChanged(nameof(UpdateLabel)); }
    partial void OnUpdateFailedChanged(bool value) { OnPropertyChanged(nameof(UpdateSummary)); OnPropertyChanged(nameof(UpdateSummaryVisibility)); OnPropertyChanged(nameof(UpdateLabel)); }
    partial void OnIncludeBetaUpdatesChanged(bool value)
    {
        availableUpdate = null; HasApplicationUpdate = false; IsUpdateReady = false;
        if (!IsCheckingUpdate && !IsDownloadingUpdate) UpdateStatus = "";
    }
    [RelayCommand] private Task ShowUpdateDetails() => ui.ShowUpdateInfoAsync(UiText.Text(updateDetails.Length > 0 ? updateDetails : UpdateStatus));
    [RelayCommand] private async Task UpdateAction()
    { if (HasApplicationUpdate) await InstallApplicationUpdate(); else await CheckApplicationUpdatesAsync(true); }
    [RelayCommand] private async Task ContactSupport() => await Guard(ui.ShowContactAsync, allowWhileBusy: true, serialize: false);
    public string AppVersion => ApplicationUpdates.CurrentVersion;
    public string UpdateLabel => IsCheckingUpdate ? "Checking for updates…" : UpdateFailed ? "Update failed" : IsDownloadingUpdate ? AppVersion : IsUpdateReady ? "Update ready" : UpdateStatus.StartsWith("Finish", StringComparison.Ordinal) ? "Finish current work" : HasApplicationUpdate ? "Update available" : UpdateStatus.StartsWith("No published", StringComparison.Ordinal) ? "Up to date" : AppVersion;
    [RelayCommand] private Task CheckApplicationUpdates() => CheckApplicationUpdatesAsync(true);
    internal async Task<bool> CheckApplicationUpdatesAsync(bool manual)
    {
        if (IsCheckingUpdate || IsDownloadingUpdate) return false;
        var succeeded = false;
        IsCheckingUpdate = true;
        if (manual) UpdateStatus = "Checking for updates…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var client = app.UpdateHttpClientFactory?.Invoke() ?? new HttpClient();
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            var includeBeta = IncludeBetaUpdates;
            var release = await new ApplicationUpdates(client, cacheDirectory: Path.Combine(app.Paths.Home, "updates")).CheckAsync(AppVersion, includeBeta, timeout.Token, forceRefresh: manual);
            if (includeBeta != IncludeBetaUpdates) { availableUpdate = null; HasApplicationUpdate = false; UpdateStatus = "Update channel changed. Check for updates again."; return false; }
            availableUpdate = release;
            UpdateFailed = false; updateDetails = "";
            HasApplicationUpdate = availableUpdate is not null;
            UpdateStatus = HasApplicationUpdate ? "Version " + availableUpdate!.Version + " is available" : manual ? "No published update available" : "";
            succeeded = true;
        }
        catch (UpdateCheckDeferredException error)
        {
            UpdateFailed = false;
            UpdateStatus = "Update checks are temporarily paused. Please try again later.";
            updateDetails = UpdateStatus;
            app.Diagnostics.Write("Updates", "Next check after " + error.RetryAt.ToString("O"));
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        { if (manual) { updateDetails = "Could not check updates. Please try again later."; UpdateFailed = true; UpdateStatus = "Could not check updates."; } app.Diagnostics.Write("Updates", error.Message, error); }
        finally
        {
            try
            {
                if (!manual && !UpdateStatus.StartsWith("Update checks are temporarily paused")) await ReadPreviousUpdateResultAsync();
            }
            finally { IsCheckingUpdate = false; }
        }
        return succeeded;
    }
    private async Task ReadPreviousUpdateResultAsync()
    {
                try
                {
                    var path = Path.Combine(app.Paths.Home, "updates", "last-result.json");
                    if (File.Exists(path))
                    {
                        using var result = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path, lifetime.Token));
                        if (result.RootElement.TryGetProperty("Success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.False)
                        { UpdateFailed = true; updateDetails = "Could not install the update. Your current installation is unchanged. Please try again later."; UpdateStatus = "Update failed"; }
                    }
                }
                catch (Exception error) when (error is IOException or System.Text.Json.JsonException or OperationCanceledException or UnauthorizedAccessException)
                { app.Diagnostics.Write("Update result", error.Message, error); }
    }
    [RelayCommand] private async Task InstallApplicationUpdate()
    {
        var release = availableUpdate;
        if (release is null || IsDownloadingUpdate) return;
        if (!IncludeBetaUpdates && new VrcSetup.Core.SemanticVersion(release.Version).IsPrerelease)
        { availableUpdate = null; HasApplicationUpdate = false; await CheckApplicationUpdatesAsync(true); return; }
        if (IsBusy || IsCreatingProject || HasActiveImports || WorkspaceDirty || PresetDirty || SettingsDirty)
        { UpdateStatus = "Finish imports and save or revert changes before updating."; return; }
        if (!File.Exists(Path.Combine(app.Paths.PayloadRoot, ".vrcsetup-install.json")))
        { UpdateStatus = "Portable copy: download the new version from Releases."; OpenPath(release.Page.AbsoluteUri); return; }
        IsDownloadingUpdate = true;
        UpdateDownloadPercent = 0;
        var preferFullInstaller = UpdateFailed;
        UpdateFailed = false; updateDetails = "";
        try
        {
            using var client = app.UpdateHttpClientFactory?.Invoke() ?? new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(15); client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            var progress = new SynchronizationProgress<int>(context, value => { if (IsDownloadingUpdate) { UpdateDownloadPercent = value; UpdateStatus = "Downloading update · " + value + "%"; } });
            var folder = Path.Combine(app.Paths.Home, "updates", release.Version);
            var installer = await new ApplicationUpdates(client).DownloadAsync(release, folder, progress, lifetime.Token, preferFullInstaller ? null : app.Paths.PayloadRoot);
            if (!IncludeBetaUpdates && new VrcSetup.Core.SemanticVersion(release.Version).IsPrerelease)
            { UpdateStatus = "Update channel changed. Check for updates again."; return; }
            if (IsBusy || IsCreatingProject || HasActiveImports || WorkspaceDirty || PresetDirty || SettingsDirty)
            { UpdateStatus = "Update downloaded. Finish your work, then choose Install update."; return; }
            UpdateStatus = "Restarting to install update…";
            await new AutomaticUpdates(app.Paths).MarkAttemptAsync(release.Version, lifetime.Token);
            if (!await ui.InstallApplicationUpdateAsync(installer, Path.Combine(app.Paths.Home, "updates", "last-result.json")))
                UpdateStatus = "Update is ready. Finish current work before restarting.";
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { UpdateFailed = true; updateDetails = "Could not download the update. Please try again later. Your current installation is unchanged."; UpdateStatus = "Update failed"; app.Diagnostics.Write("Updates", error.Message, error); }
        finally { IsDownloadingUpdate = false; }
    }
    [RelayCommand] private void ReportBug() => OpenPath(ApplicationUpdates.BugReportUrl);
    [RelayCommand] private void OpenReleases() => OpenPath(ApplicationUpdates.Repository + "/releases");
}
