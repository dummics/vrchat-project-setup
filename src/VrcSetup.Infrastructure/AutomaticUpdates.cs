using System.Security.Cryptography;
using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed record ReadyApplicationUpdate(string Version, string Installer, string Sha256, string Root, DateTimeOffset PreparedUtc);
public sealed class AutomaticUpdateState
{
    public DateTimeOffset NextCheckUtc { get; set; }
    public string? LastAttemptVersion { get; set; }
    public ReadyApplicationUpdate? Ready { get; set; }
}

// Scheduling is independent of user preferences and cannot overwrite unsaved settings.
public sealed class AutomaticUpdates(AppPaths paths, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private string StateFile => Path.Combine(paths.Home, "updates", "automatic.json");
    public bool Installed => File.Exists(Path.Combine(paths.PayloadRoot, ".vrcsetup-install.json"));
    public IDisposable? TryAcquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        try { return new FileStream(StateFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
    public async Task<AutomaticUpdateState> ReadAsync(CancellationToken token = default)
    {
        try { return JsonSerializer.Deserialize<AutomaticUpdateState>(await File.ReadAllTextAsync(StateFile, token), JsonDefaults.Options) ?? new(); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or JsonException) { return new(); }
    }
    public bool Due(AutomaticUpdateState state) => state.NextCheckUtc <= clock.GetUtcNow() || state.NextCheckUtc > clock.GetUtcNow().AddHours(5);
    public async Task ReserveCheckAsync(AutomaticUpdateState state, CancellationToken token = default)
    {
        state.NextCheckUtc = clock.GetUtcNow().AddHours(4).AddMinutes(Random.Shared.Next(0, 16));
        await SaveAsync(state, token);
    }
    public async Task SetReadyAsync(AutomaticUpdateState state, string version, string installer, CancellationToken token = default)
    {
        if (!Installed || state.LastAttemptVersion == version) return;
        if (!IsExpectedPath(installer, version)) throw new IOException("Invalid prepared update path.");
        await using var input = File.OpenRead(installer);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        state.Ready = new(version, Path.GetFullPath(installer), hash, Path.GetFullPath(paths.PayloadRoot), clock.GetUtcNow());
        await SaveAsync(state, token);
        PruneInstallers(version);
    }
    public async Task<ReadyApplicationUpdate?> TakeReadyAsync(string installedVersion, bool enabled, bool includeBeta, CancellationToken token = default)
    {
        if (!enabled || !Installed) return null;
        using var lease = TryAcquire(); if (lease is null) return null;
        var state = await ReadAsync(token); var ready = state.Ready;
        if (ready is null) return null;
        state.Ready = null; await SaveAsync(state, token);
        try
        {
            var version = new SemanticVersion(ready.Version);
            if (version.CompareTo(new SemanticVersion(installedVersion)) <= 0 || (!includeBeta && version.IsPrerelease)
                || state.LastAttemptVersion == ready.Version || clock.GetUtcNow() - ready.PreparedUtc > TimeSpan.FromHours(48)
                || ready.PreparedUtc > clock.GetUtcNow().AddMinutes(5)
                || !string.Equals(Path.GetFullPath(paths.PayloadRoot), ready.Root, StringComparison.OrdinalIgnoreCase)
                || !IsExpectedPath(ready.Installer, ready.Version)) return null;
            await using var input = File.OpenRead(ready.Installer);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(ready.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            // Consume before handoff. A failed installer reopening this version cannot restart it again.
            state.LastAttemptVersion = ready.Version; await SaveAsync(state, token); return ready;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OverflowException) { return null; }
    }
    public async Task MarkAttemptAsync(string version, CancellationToken token = default)
    { var state = await ReadAsync(token); state.Ready = null; state.LastAttemptVersion = version; await SaveAsync(state, token); }
    private bool IsExpectedPath(string path, string version)
    {
        _ = new SemanticVersion(version);
        if (version != Path.GetFileName(version)) return false;
        var expected = Path.GetFullPath(Path.Combine(paths.Home, "updates", version, $"Install-VrcSetup-{version}-win-x64.exe"));
        return string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase);
    }
    private void PruneInstallers(string keep)
    {
        // Only our exact installer/receipt names, never component archives or unrelated update files.
        var versions = new List<(DirectoryInfo Directory, SemanticVersion Version)>();
        foreach (var directory in new DirectoryInfo(Path.GetDirectoryName(StateFile)!).EnumerateDirectories())
            try { versions.Add((directory, new SemanticVersion(directory.Name))); } catch (Exception error) when (error is ArgumentException or OverflowException) { }
        foreach (var entry in versions.OrderByDescending(x => x.Version).Skip(3).Where(x => x.Directory.Name != keep))
            try
            {
                File.Delete(Path.Combine(entry.Directory.FullName, $"Install-VrcSetup-{entry.Directory.Name}-win-x64.exe"));
                File.Delete(Path.Combine(entry.Directory.FullName, "prefetch-result.json"));
                if (!entry.Directory.EnumerateFileSystemInfos().Any()) entry.Directory.Delete();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private Task SaveAsync(AutomaticUpdateState state, CancellationToken token) => AtomicFile.WriteJsonAsync(StateFile, state, token);
}
