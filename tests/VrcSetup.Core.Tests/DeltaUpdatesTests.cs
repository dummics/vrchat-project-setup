using System.Security.Cryptography;
using System.Text;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class DeltaUpdatesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VrcSetup-delta-test-" + Guid.NewGuid().ToString("N"));
    private string Basis => Path.Combine(root, "installed");
    private string Target => Path.Combine(root, "extracted");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private async Task PrepareAsync()
    {
        foreach (var folder in new[] { Basis, Target }) Directory.CreateDirectory(folder);
        var files = new Dictionary<string, string> { ["app/cli/vrcsetup.exe"] = Hash("cli"), ["app/desktop/VrcSetup.Desktop.exe"] = Hash("old"), ["runtime.dll"] = Hash("runtime") };
        foreach (var (name, value) in new[] { ("app/cli/vrcsetup.exe", "cli"), ("app/desktop/VrcSetup.Desktop.exe", "old"), ("runtime.dll", "runtime") })
        { var path = Path.Combine(Basis, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, value); }
        await AtomicFile.WriteJsonAsync(Path.Combine(Basis, ".vrcsetup-payload.json"), new PayloadManifest("0.2.0-beta.6", files));
        await AtomicFile.WriteJsonAsync(Path.Combine(Basis, ".vrcsetup-install.json"), new InstallationRecord("0.2.0-beta.6", Basis, DateTime.UtcNow, null));
        var target = new Dictionary<string, string>(files) { ["app/desktop/VrcSetup.Desktop.exe"] = Hash("new") };
        var reuse = new Dictionary<string, string>(files); reuse.Remove("app/desktop/VrcSetup.Desktop.exe");
        await AtomicFile.WriteJsonAsync(Path.Combine(Target, ".vrcsetup-payload.json"), new PayloadManifest("0.2.0-beta.7", target));
        await AtomicFile.WriteJsonAsync(Path.Combine(Target, DeltaUpdates.ManifestName), new DeltaManifest("0.2.0-beta.6", reuse));
        Directory.CreateDirectory(Path.Combine(Target, "app/desktop"));
        await File.WriteAllTextAsync(Path.Combine(Target, "app/desktop/VrcSetup.Desktop.exe"), "new");
    }
    [Fact] public async Task ReconstructsExactTargetAndPreservesBase()
    {
        await PrepareAsync(); Assert.True(await DeltaUpdates.CanUseAsync(Basis, "0.2.0-beta.6"));
        await DeltaUpdates.ReconstructAsync(Target, Basis);
        await new InstallationManager(AppPaths.ForInstallation(null, Target)).ValidatePayloadAsync(Target);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(Basis, "app/desktop/VrcSetup.Desktop.exe")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(Target, "app/desktop/VrcSetup.Desktop.exe")));
    }
    [Fact] public async Task ChangedBaseFallsBackBeforeDownloadAndCannotBeReused()
    {
        await PrepareAsync(); await File.WriteAllTextAsync(Path.Combine(Basis, "runtime.dll"), "changed");
        Assert.False(await DeltaUpdates.CanUseAsync(Basis, "0.2.0-beta.6"));
        await Assert.ThrowsAsync<IOException>(() => DeltaUpdates.ReconstructAsync(Target, Basis));
        Assert.Equal("changed", await File.ReadAllTextAsync(Path.Combine(Basis, "runtime.dll")));
    }
    [Fact] public async Task WrongVersionAndManifestCannotBypassValidation()
    {
        await PrepareAsync(); Assert.False(await DeltaUpdates.CanUseAsync(Basis, "0.2.0-beta.5"));
        await AtomicFile.WriteJsonAsync(Path.Combine(Target, DeltaUpdates.ManifestName), new DeltaManifest("0.2.0-beta.6", new() { ["../outside"] = Hash("runtime") }));
        await Assert.ThrowsAsync<IOException>(() => DeltaUpdates.ReconstructAsync(Target, Basis));
        Assert.False(File.Exists(Path.Combine(root, "outside")));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
