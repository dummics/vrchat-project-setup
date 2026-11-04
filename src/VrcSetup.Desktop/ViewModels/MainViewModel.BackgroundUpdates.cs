using System.Diagnostics;
using System.Text.Json;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RunPeriodicUpdateChecksAsync()
    {
        try
        {
            // First paint/import recovery takes precedence. No timer wakes a closed app.
            await Task.Delay(TimeSpan.FromSeconds(45), lifetime.Token);
            while (!lifetime.IsCancellationRequested)
            {
                try { await RunBackgroundUpdateCheckAsync(); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
                { app.Diagnostics.Write("Automatic updates", error.Message, error); }
                await Task.Delay(TimeSpan.FromMinutes(15), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    internal async Task RunBackgroundUpdateCheckAsync()
    {
        if (!CheckUpdatesOnStartup || IsCheckingUpdate || IsDownloadingUpdate || IsInstallingUpdate || IsBusy || HasActiveImports) return;
        var automatic = new AutomaticUpdates(app.Paths);
        using var lease = automatic.TryAcquire(); if (lease is null) return;
        var state = await automatic.ReadAsync(lifetime.Token);
        if (!automatic.Due(state)) return;
        await automatic.ReserveCheckAsync(state, lifetime.Token);
        if (!await CheckApplicationUpdatesAsync(false) || availableUpdate is not { } release) return;
        if (HasActiveImports || !AutomaticUpdateEnabled || !automatic.Installed || state.LastAttemptVersion == release.Version) return;
        if (state.Ready is { } prepared && prepared.Version == release.Version) { UpdateFailed = false; IsUpdateReady = true; return; }
        IsDownloadingUpdate = true; UpdateDownloadPercent = 0;
        try
        {
            using var client = app.UpdateHttpClientFactory?.Invoke() ?? new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(15); client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            var updates = new ApplicationUpdates(client);
            if (!await updates.SupportsPrefetchAsync(release, lifetime.Token)) return;
            var folder = Path.Combine(app.Paths.Home, "updates", release.Version);
            var progress = new SynchronizationProgress<int>(context, value => { UpdateDownloadPercent = value; UpdateStatus = "Downloading update · " + value + "%"; });
            // Always the unified bootstrapper: historical delta EXEs do not implement prefetch.
            var installer = await updates.DownloadAsync(release, folder, progress, lifetime.Token);
            if (!AutomaticUpdateEnabled || !CheckUpdatesOnStartup || (!IncludeBetaUpdates && new VrcSetup.Core.SemanticVersion(release.Version).IsPrerelease)) return;
            UpdateStatus = "Preparing update…";
            if (HasActiveImports) { UpdateStatus = "Update will wait until imports finish."; return; }
            var receipt = Path.Combine(folder, "prefetch-result.json");
            if (File.Exists(receipt)) File.Delete(receipt);
            var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            foreach (var value in new[] { "--prefetch", "--parent-pid", Environment.ProcessId.ToString(), "--root", app.Paths.PayloadRoot, "--home", app.Paths.Home, "--result", receipt }) start.ArgumentList.Add(value);
            using var process = Process.Start(start) ?? throw new IOException("Could not prepare the update.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                // On normal app exit the helper observes its parent, cancels and removes staging itself.
                if (!lifetime.IsCancellationRequested)
                    try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                throw;
            }
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(receipt, lifetime.Token));
            if (process.ExitCode != 0 || !result.RootElement.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True)
                throw new IOException("Update preparation failed; the current installation is preserved.");
            if (!AutomaticUpdateEnabled || !CheckUpdatesOnStartup || (!IncludeBetaUpdates && new VrcSetup.Core.SemanticVersion(release.Version).IsPrerelease)) return;
            await automatic.SetReadyAsync(state, release.Version, installer, lifetime.Token);
            UpdateFailed = false; IsUpdateReady = true; UpdateStatus = "Ready for next launch";
        }
        finally { IsDownloadingUpdate = false; }
    }
}
