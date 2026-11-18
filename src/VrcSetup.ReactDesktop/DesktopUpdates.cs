using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal sealed class DesktopUpdates(VrcSetupApplication engine, bool enabled)
{
    private readonly AutomaticUpdates automatic = new(engine.Paths);
    private readonly SemaphoreSlim gate = new(1);
    private ApplicationRelease? available;

    internal async Task RunInBackgroundAsync(CancellationToken token)
    {
        if (!enabled) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), token);
            while (!token.IsCancellationRequested)
            {
                try { await CheckAsync(manual: false, token); }
                catch (Exception error) when (IsRecoverable(error) && !token.IsCancellationRequested)
                { engine.Diagnostics.Write("React automatic updates", error.Message, error); }
                await Task.Delay(TimeSpan.FromMinutes(15), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async Task<UpdateView> StatusAsync(CancellationToken token)
    {
        var config = await engine.Configuration.LoadAsync(token);
        var state = enabled ? await automatic.ReadAsync(token) : new();
        var offered = available is { } release && Allows(release.Version, config) ? release.Version : null;
        var ready = enabled && state.Ready is { } prepared && IsNewerAllowed(prepared.Version, config)
            ? prepared.Version : null;
        return new(ApplicationUpdates.CurrentVersion, enabled, enabled && automatic.Installed, offered, ready);
    }

    internal async Task<UpdateView> CheckAsync(bool manual, CancellationToken token)
    {
        if (!enabled) return await StatusAsync(token);
        await gate.WaitAsync(token);
        try
        {
            var config = await engine.Configuration.LoadAsync(token);
            if (!manual && (!config.CheckUpdatesOnStartup || engine.Imports.HasActiveJobs))
                return await StatusAsync(token);
            using var lease = automatic.TryAcquire();
            if (lease is null) return await StatusAsync(token);
            var state = await automatic.ReadAsync(token);
            if (!manual && !automatic.Due(state)) return await StatusAsync(token);
            await automatic.ReserveCheckAsync(state, token);
            using var client = NewClient();
            var updates = new ApplicationUpdates(client, cacheDirectory: Path.Combine(engine.Paths.Home, "updates"));
            available = await updates.CheckAsync(ApplicationUpdates.CurrentVersion, config.IncludeBetaUpdates,
                token, forceRefresh: manual, includeAlpha: config.IncludeAlphaUpdates);
            if (!manual && automatic.Installed && config.AutomaticUpdates && !engine.Imports.HasActiveJobs &&
                available is { } release && state.LastAttemptVersion != release.Version && state.Ready?.Version != release.Version)
                await PrepareCoreAsync(updates, state, release, manual: false, token);
            return await StatusAsync(token);
        }
        finally { gate.Release(); }
    }

    internal async Task<UpdateView> PrepareAsync(string? version, CancellationToken token)
    {
        if (!enabled || !automatic.Installed) throw new IOException(UiText.Text("Updating requires an installed copy of the app."));
        await gate.WaitAsync(token);
        try
        {
            if (engine.Imports.HasActiveJobs) throw new IOException(UiText.Text("Wait for the imports to finish."));
            if (available is not { } release || release.Version != version)
                throw new IOException(UiText.Text("Check for updates again."));
            using var lease = automatic.TryAcquire() ?? throw new IOException(UiText.Text("An update is already being prepared."));
            var state = await automatic.ReadAsync(token);
            using var client = NewClient();
            await PrepareCoreAsync(new ApplicationUpdates(client), state, release, manual: true, token);
            return await StatusAsync(token);
        }
        finally { gate.Release(); }
    }

    private async Task PrepareCoreAsync(ApplicationUpdates updates, AutomaticUpdateState state, ApplicationRelease release,
        bool manual, CancellationToken token)
    {
        if (!await StillAllowedAsync(release.Version, manual, token)) return;
        if (!await updates.SupportsPrefetchAsync(release, token))
        {
            if (manual) throw new IOException(UiText.Text("This update cannot be prepared automatically."));
            return;
        }
        var folder = Path.Combine(engine.Paths.Home, "updates", release.Version);
        var installer = await updates.DownloadAsync(release, folder, token: token);
        if (engine.Imports.HasActiveJobs || !await StillAllowedAsync(release.Version, manual, token)) return;
        var receipt = Path.Combine(folder, "prefetch-result.json");
        File.Delete(receipt);
        using var process = Launch(installer, ["--prefetch", "--parent-pid", Environment.ProcessId.ToString(), "--result", receipt]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(true);
            throw new IOException(UiText.Text("Preparing the update timed out."));
        }
        if (process.ExitCode != 0 || !File.Exists(receipt))
            throw new IOException(UiText.Text("Could not prepare the update. Retry from Settings."));
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(receipt, token));
        if (!result.RootElement.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True)
            throw new IOException(UiText.Text("Could not prepare the update. Retry from Settings."));
        if (await StillAllowedAsync(release.Version, manual, token))
            await automatic.SetReadyAsync(state, release.Version, installer, token);
    }

    internal async Task<bool> StartReadyAsync(bool manual, CancellationToken token)
    {
        if (!enabled || !automatic.Installed || engine.Imports.HasActiveJobs) return false;
        var journal = Path.Combine(engine.Paths.Home, "imports.json");
        if (File.Exists(journal))
        {
            if (new FileInfo(journal).Length > 32 * 1024 * 1024) return false;
            if ((await AtomicFile.ReadJsonAsync<ImportJob[]>(journal, token))?.Any(x => x is null || x.Active) == true)
                return false;
        }
        var executable = Environment.ProcessPath;
        if (executable is null || !IsOnlyInstance(executable)) return false;
        var config = await engine.Configuration.LoadAsync(token);
        var ready = await automatic.TakeReadyAsync(ApplicationUpdates.CurrentVersion,
            manual || config.CheckUpdatesOnStartup && config.AutomaticUpdates,
            config.IncludeBetaUpdates, token, config.IncludeAlphaUpdates);
        if (ready is null) return false;
        using var process = Launch(ready.Installer, ["--silent", "--wait-for-pid", Environment.ProcessId.ToString(),
            "--result", Path.Combine(engine.Paths.Home, "updates", "last-result.json"), "--launch"]);
        return true;
    }

    private async Task<bool> StillAllowedAsync(string version, bool manual, CancellationToken token)
    {
        var config = await engine.Configuration.LoadAsync(token);
        return (manual || config.CheckUpdatesOnStartup && config.AutomaticUpdates) && Allows(version, config);
    }

    private Process Launch(string installer, string[] args)
    {
        var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var argument in args.Concat(["--root", engine.Paths.PayloadRoot, "--home", engine.Paths.Home]))
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException(UiText.Text("Could not start the updater."));
    }

    private static bool IsOnlyInstance(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try { if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return false; }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
            }
        }
        return true;
    }

    private HttpClient NewClient()
    {
        var client = engine.UpdateHttpClientFactory?.Invoke() ?? new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(15);
        client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        return client;
    }

    private static bool Allows(string version, AppConfiguration config) =>
        ApplicationUpdates.AllowsChannel(new SemanticVersion(version), config.IncludeBetaUpdates, config.IncludeAlphaUpdates);

    private static bool IsNewerAllowed(string version, AppConfiguration config)
    {
        try
        {
            var candidate = new SemanticVersion(version);
            return candidate.CompareTo(new(ApplicationUpdates.CurrentVersion)) > 0 &&
                ApplicationUpdates.AllowsChannel(candidate, config.IncludeBetaUpdates, config.IncludeAlphaUpdates);
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return false; }
    }

    internal static bool IsRecoverable(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or
            System.ComponentModel.Win32Exception or ArgumentException or OperationCanceledException;

    internal sealed record UpdateView(string Current, bool Enabled, bool Installed, string? Available, string? Ready);
}
