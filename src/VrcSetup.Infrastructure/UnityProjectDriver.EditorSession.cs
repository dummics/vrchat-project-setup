using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class UnityProjectDriver
{
    public async Task<IAsyncDisposable?> BeginPackageChangesAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken token)
    {
        // Install the small editor hook for future open-editor operations, even
        // when this project is currently closed. Never replace a foreign file.
        var source = Path.Combine(paths.Resources, "VrcSetupPackageSession.cs");
        var destination = Path.Combine(plan.Path, "Assets", "Editor", "VrcSetupPackageSession.cs");
        if (!File.Exists(source)) throw new IOException("Package refresh helper is missing. Repair the application and retry.");
        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
        }
        else if (AtomicFile.Hash(source) != AtomicFile.Hash(destination))
        {
            if (!(await File.ReadAllTextAsync(destination, token)).StartsWith("// VRC Project Setup package refresh coordination.", StringComparison.Ordinal))
                throw new IOException("A different editor script uses the package helper filename. Rename that script and retry.");
            var temporary = destination + ".vrcsetup-tmp";
            try { File.Copy(source, temporary, true); File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var lease = new EditorPackageLease(plan.Path);
        try
        {
            // Write before checking Unity: an editor opened during preparation
            // can acknowledge the same lease before VPM starts.
            await lease.StartAsync(token);
            if (!workspace.IsBusy(plan.Path)) return lease;
            progress?.Report(new(DateTime.UtcNow, "Preparing", "Waiting for Unity to finish preparing scripts.", Project: plan.Path));
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (workspace.IsBusy(plan.Path))
            {
                token.ThrowIfCancellationRequested();
                if (await lease.IsAcknowledgedAsync(token)) return lease;
                if (DateTime.UtcNow >= deadline) throw new IOException("Unity is still preparing scripts. Wait for it to finish, then retry the package changes.");
                await Task.Delay(200, token);
            }
            return lease;
        }
        catch { await lease.DisposeAsync(); throw; }
    }
}

internal sealed class EditorPackageLease(string project) : IAsyncDisposable
{
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly string file = Path.Combine(project, ".vrcsetup", "editor-package-lease.json");
    private readonly string ack = Path.Combine(project, ".vrcsetup", "editor-package-ack.txt");
    private readonly CancellationTokenSource stop = new();
    private Task? heartbeat;
    public async Task StartAsync(CancellationToken token)
    {
        await WriteAsync(token);
        heartbeat = KeepAliveAsync();
    }
    private Task WriteAsync(CancellationToken token) => AtomicFile.WriteJsonAsync(file, new { id, expiresUtcTicks = DateTime.UtcNow.AddSeconds(20).Ticks }, token);
    private async Task KeepAliveAsync()
    {
        try
        {
            while (true) { await Task.Delay(2000, stop.Token); await WriteAsync(stop.Token); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async Task<bool> IsAcknowledgedAsync(CancellationToken token)
    {
        if (heartbeat is { IsFaulted: true }) await heartbeat;
        try { return File.Exists(ack) && await File.ReadAllTextAsync(ack, token) == id; }
        catch (IOException) { return false; }
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try { if (heartbeat is not null) await heartbeat; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Expiry releases the editor if storage became unavailable. */ }
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        stop.Dispose();
    }
}
