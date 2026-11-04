using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PresetMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-preset-migration-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData("""["tool","other"]""", "latest")]
    [InlineData("""{"tool":"1.2.3-beta.4","other":"latest"}""", "1.2.3-beta.4")]
    [InlineData("""{"tool":{"version":"1.2.3-beta.4"},"other":{"Version":"latest"}}""", "1.2.3-beta.4")]
    public async Task ExistingPresetsSurviveUpgradeSaveAndReopen(string policy, string expected)
    {
        var paths = new AppPaths(root); Directory.CreateDirectory(root);
        var original = """{"SchemaVersion":1,"VpmPackages":POLICY,"PackagePresets":{"Creator PC":POLICY,"Quest personalizzato":{}},"FavoritePackages":["tool"],"futureOption":{"enabled":true}}""".Replace("POLICY", policy);
        await File.WriteAllTextAsync(paths.ConfigFile, original);
        var store = new ConfigurationStore(paths);
        var config = await store.LoadAsync();
        Assert.Equal(expected, config.PackagePresets["creator pc"]["TOOL"]);
        Assert.Empty(config.PackagePresets["Quest personalizzato"]);
        Assert.Equal(original, await File.ReadAllTextAsync(paths.ConfigFile));
        await store.LoadAsync();
        Assert.Single(Directory.GetFiles(Path.Combine(root, "migration")));
        config.Theme = "light"; await store.SaveAsync(config);
        var reopened = await new ConfigurationStore(paths).LoadAsync();
        Assert.Equal(2, reopened.SchemaVersion);
        Assert.Equal(expected, reopened.PackagePresets["Creator PC"]["tool"]);
        Assert.Equal("latest", reopened.PackagePresets["Creator PC"]["other"]);
        Assert.Equal(expected, reopened.VpmPackages["tool"]); Assert.Contains("tool", reopened.FavoritePackages);
        Assert.True(reopened.AdditionalSettings!["futureOption"].GetProperty("enabled").GetBoolean());
        Assert.Equal(original, await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(root, "migration")).Single()));
    }
    [Theory]
    [InlineData("""{"SchemaVersion":99,"PackagePresets":{"Keep":{"tool":"1.2.3"}}}""")]
    [InlineData("""{"PackagePresets":{"Keep":null}}""")]
    [InlineData("""{"PackagePresets":{"Keep":true}}""")]
    [InlineData("""{"PackagePresets":{"Keep":{"tool":{}}}}""")]
    public async Task UnsupportedDataIsNeverSilentlyReplaced(string original)
    {
        var paths = new AppPaths(root); Directory.CreateDirectory(root); await File.WriteAllTextAsync(paths.ConfigFile, original);
        await Assert.ThrowsAnyAsync<Exception>(() => new ConfigurationStore(paths).LoadAsync());
        Assert.Equal(original, await File.ReadAllTextAsync(paths.ConfigFile));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
