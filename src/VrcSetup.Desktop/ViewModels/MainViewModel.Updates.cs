using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private ApplicationRelease? availableUpdate;
    private bool checkingUpdate;
    [ObservableProperty] public partial bool CheckUpdatesOnStartup { get; set; } = true;
    [ObservableProperty] public partial bool IncludeBetaUpdates { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateLabel))] public partial bool HasApplicationUpdate { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateLabel))] public partial bool IsDownloadingUpdate { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MainInteractionEnabled))] public partial bool IsInstallingUpdate { get; set; }
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";
    public string AppVersion => ApplicationUpdates.CurrentVersion;
    public string UpdateLabel => IsDownloadingUpdate ? "Downloading update…" : HasApplicationUpdate ? AppVersion + " · Update available" : AppVersion;
    [RelayCommand] private Task CheckApplicationUpdates() => CheckApplicationUpdatesAsync(true);
    internal async Task CheckApplicationUpdatesAsync(bool manual)
    {
        if (checkingUpdate || IsDownloadingUpdate) return;
        checkingUpdate = true;
        if (manual) UpdateStatus = "Checking for updates…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var client = app.UpdateHttpClientFactory?.Invoke() ?? new HttpClient();
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            availableUpdate = await new ApplicationUpdates(client).CheckAsync(AppVersion, IncludeBetaUpdates, timeout.Token);
            HasApplicationUpdate = availableUpdate is not null;
            UpdateStatus = HasApplicationUpdate ? "Version " + availableUpdate!.Version + " is available" : manual ? "No published update available" : "";
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        { if (manual) UpdateStatus = "Could not check updates. Try again later."; app.Diagnostics.Write("Updates", error.Message); }
        finally
        {
            checkingUpdate = false;
            if (!manual)
            {
                try
                {
                    var path = Path.Combine(app.Paths.Home, "updates", "last-result.json");
                    if (File.Exists(path))
                    {
                        using var result = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path, lifetime.Token));
                        if (result.RootElement.TryGetProperty("Success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.False)
                            UpdateStatus = "The last update did not finish. Your current version is preserved; retry from the version menu.";
                    }
                }
                catch (Exception error) when (error is IOException or System.Text.Json.JsonException or OperationCanceledException or UnauthorizedAccessException)
                { app.Diagnostics.Write("Update result", error.Message); }
            }
        }
    }
    [RelayCommand] private async Task InstallApplicationUpdate()
    {
        if (availableUpdate is null || IsDownloadingUpdate) return;
        if (IsBusy || HasActiveImports || WorkspaceDirty || PresetDirty)
        { UpdateStatus = "Finish imports and save or revert changes before updating."; return; }
        if (!File.Exists(Path.Combine(app.Paths.PayloadRoot, ".vrcsetup-install.json")))
        { UpdateStatus = "Portable copy: download the new version from Releases."; OpenPath(availableUpdate.Page.AbsoluteUri); return; }
        IsDownloadingUpdate = true;
        try
        {
            using var client = app.UpdateHttpClientFactory?.Invoke() ?? new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(15); client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            var progress = new SynchronizationProgress<int>(context, value => { if (IsDownloadingUpdate) UpdateStatus = "Downloading update · " + value + "%"; });
            var folder = Path.Combine(app.Paths.Home, "updates", availableUpdate.Version);
            var installer = await new ApplicationUpdates(client).DownloadAsync(availableUpdate, folder, progress, lifetime.Token);
            if (IsBusy || HasActiveImports || WorkspaceDirty || PresetDirty)
            { UpdateStatus = "Update downloaded. Finish your work, then choose Install update."; return; }
            UpdateStatus = "Restarting to install update…";
            if (!await ui.InstallApplicationUpdateAsync(installer, Path.Combine(app.Paths.Home, "updates", "last-result.json")))
                UpdateStatus = "Update is ready. Finish current work before restarting.";
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { UpdateStatus = "Update could not be installed. Your current installation is preserved."; app.Diagnostics.Write("Updates", error.Message, error); }
        finally { IsDownloadingUpdate = false; }
    }
    [RelayCommand] private void ReportBug() => OpenPath(ApplicationUpdates.BugReportUrl);
    [RelayCommand] private void OpenReleases() => OpenPath(ApplicationUpdates.Repository + "/releases");
}
