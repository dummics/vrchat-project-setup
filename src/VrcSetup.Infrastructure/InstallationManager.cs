using System.Diagnostics;
using System.Runtime.InteropServices;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed record PayloadManifest(string Version, Dictionary<string, string> Files);
public sealed record InstallationRecord(string Version, string Root, DateTime InstalledUtc, string? PreviousInstall);

public sealed class InstallationManager(AppPaths paths)
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup");
    private const string Marker = ".vrcsetup-install.json";
    public async Task ValidatePayloadAsync(string root, CancellationToken token = default)
    {
        var manifest = await AtomicFile.ReadJsonAsync<PayloadManifest>(Path.Combine(root, ".vrcsetup-payload.json"), token) ?? throw new IOException("This is not a published VRCSETUP payload. Build a release first.");
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested(); var path = ProjectRules.NormalizePath(file.Key, root); ProjectRules.EnsureContained(root, path);
            if (AtomicFile.Hash(path) != file.Value) throw new IOException("Payload integrity check failed: " + file.Key);
        }
        foreach (var required in new[] { "app/cli/vrcsetup.exe", "app/desktop/VrcSetup.Desktop.exe" })
            if (!File.Exists(Path.Combine(root, required))) throw new IOException("Payload is missing " + required);
    }
    public async Task<InstallationRecord> InstallAsync(string? destination = null, CancellationToken token = default)
    {
        var source = ProjectRules.NormalizePath(paths.PayloadRoot);
        var target = ProjectRules.NormalizePath(destination ?? DefaultRoot);
        var parent = Path.GetDirectoryName(target) ?? throw new IOException("Install destination cannot be a drive root.");
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Use Repair for this installed copy.");
        if (target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Source and install destination must be separate folders.");
        if (Directory.Exists(target) && !File.Exists(Path.Combine(target, Marker)) && !File.Exists(Path.Combine(target, ".vrcsetup-installed"))) throw new IOException("Refusing to replace an unrecognized destination folder.");
        ProjectRules.EnsureContained(parent, target);
        await ValidatePayloadAsync(source, token);
        var manifest = await AtomicFile.ReadJsonAsync<PayloadManifest>(Path.Combine(source, ".vrcsetup-payload.json"), token) ?? throw new IOException("Payload manifest missing.");
        var stage = target + ".stage-" + Guid.NewGuid().ToString("N");
        var previous = Directory.Exists(target) ? target + ".previous-" + Guid.NewGuid().ToString("N") : null;
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var relative in manifest.Files.Keys.Append(".vrcsetup-payload.json"))
            {
                token.ThrowIfCancellationRequested();
                var file = Path.Combine(source, relative); var copy = Path.Combine(stage, relative);
                ProjectRules.EnsureContained(source, file); ProjectRules.EnsureContained(stage, copy);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(file, copy);
            }
            await ValidatePayloadAsync(stage, token);
            if (previous is not null) Directory.Move(target, previous);
            try { Directory.Move(stage, target); }
            catch { if (previous is not null && !Directory.Exists(target)) Directory.Move(previous, target); throw; }
            var record = new InstallationRecord(manifest.Version, target, DateTime.UtcNow, previous);
            await AtomicFile.WriteJsonAsync(Path.Combine(target, Marker), record, token);
            await IntegrateAsync(target, token);
            return record;
        }
        finally
        {
            if (Directory.Exists(stage)) { ProjectRules.EnsureContained(parent, stage); Directory.Delete(stage, true); }
        }
    }
    public async Task RepairAsync(string? target = null, CancellationToken token = default)
    {
        target = ProjectRules.NormalizePath(target ?? DefaultRoot);
        var marker = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(target, Marker), token) ?? throw new IOException("VRCSETUP .NET installation marker not found.");
        if (!marker.Root.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installation marker does not match this folder.");
        await ValidatePayloadAsync(target, token);
        await IntegrateAsync(target, token);
    }
    private static async Task IntegrateAsync(string target, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows installation only.");
        var bin = Path.Combine(target, "bin"); Directory.CreateDirectory(bin);
        await File.WriteAllTextAsync(Path.Combine(bin, "vrcsetup.cmd"), "@echo off\r\n\"%~dp0..\\app\\cli\\vrcsetup.exe\" %*\r\nexit /b %errorlevel%\r\n", token);
        var existingPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        if (!existingPath.Split(';').Any(x => x.TrimEnd('\\').Equals(bin, StringComparison.OrdinalIgnoreCase))) Environment.SetEnvironmentVariable("PATH", string.Join(';', existingPath.Split(';', StringSplitOptions.RemoveEmptyEntries).Append(bin)), EnvironmentVariableTarget.User);
        var shortcuts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "VRChat Project Setup"); Directory.CreateDirectory(shortcuts);
        await Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows shortcuts only.");
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut support is unavailable.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                Shortcut("VRChat Project Setup", Path.Combine(target, "app", "desktop", "VrcSetup.Desktop.exe"), "");
                Shortcut("Repair VRChat Project Setup", Path.Combine(target, "app", "cli", "vrcsetup.exe"), "repair --root \"" + target + "\"");
                Shortcut("Uninstall VRChat Project Setup", Path.Combine(target, "app", "cli", "vrcsetup.exe"), "uninstall --root \"" + target + "\"");
                void Shortcut(string name, string executable, string arguments)
                {
                    dynamic shortcut = shell.CreateShortcut(Path.Combine(shortcuts, name + ".lnk"));
                    try { shortcut.TargetPath = executable; shortcut.Arguments = arguments; shortcut.WorkingDirectory = target; shortcut.IconLocation = executable; shortcut.Save(); }
                    finally { Marshal.FinalReleaseComObject(shortcut); }
                }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }, token);
    }
    public async Task UninstallAsync(string? target, bool confirmed, CancellationToken token = default)
    {
        if (!confirmed) throw new InvalidOperationException("Uninstall requires explicit confirmation.");
        target = ProjectRules.NormalizePath(target ?? DefaultRoot);
        var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(target, Marker), token) ?? throw new IOException("Uninstall refused: this is not a recognized .NET installation.");
        if (!record.Root.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installation marker does not match the target.");
        var parent = Path.GetDirectoryName(target)!; ProjectRules.EnsureContained(parent, target);
        if (File.Exists(Path.Combine(target, "README.md")) && Directory.Exists(Path.Combine(target, "src"))) throw new IOException("Uninstall refused on the source repository.");
        var bin = Path.Combine(target, "bin");
        if (OperatingSystem.IsWindows())
        {
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            Environment.SetEnvironmentVariable("PATH", string.Join(';', userPath.Split(';').Where(x => !x.TrimEnd('\\').Equals(bin, StringComparison.OrdinalIgnoreCase))), EnvironmentVariableTarget.User);
            var shortcuts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "VRChat Project Setup");
            foreach (var name in new[] { "VRChat Project Setup", "Repair VRChat Project Setup", "Uninstall VRChat Project Setup" })
            { var link = Path.Combine(shortcuts, name + ".lnk"); if (File.Exists(link)) File.Delete(link); }
            if (Directory.Exists(shortcuts) && !Directory.EnumerateFileSystemEntries(shortcuts).Any()) Directory.Delete(shortcuts);
        }
        // Rename the installation to detach entrypoints. Config lives outside it and is preserved.
        var detached = target + ".uninstall-" + Guid.NewGuid().ToString("N");
        Directory.Move(target, detached);
        try { Directory.Delete(detached, true); }
        catch (IOException) when (AppContext.BaseDirectory.StartsWith(detached, StringComparison.OrdinalIgnoreCase) || AppContext.BaseDirectory.StartsWith(target, StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Entrypoints removed and settings preserved. Close this process, then remove the detached program folder: " + detached); }
    }
}
