using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PcCompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VRC test [PC] & città 日本語 " + Guid.NewGuid().ToString("N"));

    [Fact] public async Task FailedSourcePreparationCanBeRetriedWithoutRestartingOrLosingSelections()
    {
        var paths = new AppPaths(Path.Combine(root, "home"));
        var store = new ConfigurationStore(paths);
        var config = await store.LoadAsync();
        config.VpmPackages[ProjectRules.EasyLogin] = "1.1.1";
        await store.SaveAsync(config);
        var attempts = 0; var ready = false;
        var runner = new ToolRunner(bundledPackageTool: "fixture.exe", commandOverride: (_, args, _) =>
        {
            if (args is ["repo", "list"]) return Task.FromResult(new ToolResult(0, "", ""));
            if (args is ["repo", "add", _])
            {
                ready = ++attempts > 1;
                return Task.FromResult(new ToolResult(ready ? 0 : 1, "", ready ? "" : "Network unavailable"));
            }
            return Task.FromResult(new ToolResult(0, ready ? """{"versions":[{"version":"1.1.1"}]}""" : """{"versions":[]}""", ""));
        });
        var catalog = new PackageCatalog(paths, runner, store, Path.Combine(root, "repos"));
        await Assert.ThrowsAsync<IOException>(() => catalog.GetVersionsAsync(ProjectRules.EasyLogin));
        Assert.Equal(["1.1.1"], await catalog.GetVersionsAsync(ProjectRules.EasyLogin));
        Assert.Equal(2, attempts);
        Assert.Equal("1.1.1", (await store.LoadAsync()).VpmPackages[ProjectRules.EasyLogin]);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task UnknownPackageAndDisabledSourceSetupNeverRegisterGuessedRepositories(bool disabled)
    {
        var paths = new AppPaths(Path.Combine(root, "home")); var registered = 0;
        var runner = new ToolRunner(bundledPackageTool: "fixture.exe", commandOverride: (_, args, _) =>
        {
            if (args is ["repo", "add", _]) registered++;
            return Task.FromResult(new ToolResult(0, """{"versions":[]}""", ""));
        });
        var catalog = new PackageCatalog(paths, runner, new ConfigurationStore(paths), Path.Combine(root, "repos"), allowRepositorySetup: !disabled);
        Assert.Empty(await catalog.GetVersionsAsync(disabled ? ProjectRules.EasyLogin : "dev.unknown.package"));
        Assert.Equal(0, registered);
    }

    [Fact] public async Task RememberedProjectVersionsRemainAvailableOfflineWithoutAddingPackagesToDefaults()
    {
        var paths = new AppPaths(Path.Combine(root, "home")); var store = new ConfigurationStore(paths);
        var offline = new ToolRunner(commandOverride: (_, _, _) => Task.FromResult(new ToolResult(1, "", "Offline")));
        var catalog = new PackageCatalog(paths, offline, store, Path.Combine(root, "repos"));
        await catalog.RememberProjectPackagesAsync([new ProjectSnapshot
        {
            Path = Path.Combine(root, "Avatar [Quest]"), Name = "Avatar",
            InstalledVersions = new() { ["dev.example.optional"] = "2.3.4" },
            PackageDisplayNames = new() { ["dev.example.optional"] = "Optional Tool" }
        }]);
        var reopened = new PackageCatalog(paths, offline, new ConfigurationStore(paths), Path.Combine(root, "repos"));
        Assert.Equal(["2.3.4"], await reopened.GetVersionsAsync("dev.example.optional"));
        Assert.DoesNotContain("dev.example.optional", (await store.LoadAsync()).VpmPackages.Keys);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
