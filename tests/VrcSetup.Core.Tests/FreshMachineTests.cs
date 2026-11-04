using System.IO.Compression;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class FreshMachineTests : IDisposable
{
    [Theory][InlineData(true)][InlineData(false)]
    public async Task BundledRemovalDeclinesVpmProposalBeforeApplyingAndBlocksConflicts(bool conflict)
    {
        var commands = new List<string[]>();
        var runner = new ToolRunner(bundledPackageTool: "bundled-vpm.exe", commandOverride: (_, args, _) =>
        {
            commands.Add(args);
            return Task.FromResult(args.Contains("-y") ? new ToolResult(0, "Removed", "") : new ToolResult(1,
                (conflict ? "**Those changes conflicts with the following packages**\n" : "") + "Do you want to apply those changes? [y/n]", ""));
        });
        var result = await runner.VpmAsync(["remove", "package", "dependency", "-p", root]);
        Assert.Equal(conflict ? 1 : 2, commands.Count); Assert.DoesNotContain("-y", commands[0]);
        Assert.Equal(conflict ? 1 : 0, result.ExitCode);
        if (conflict) Assert.Contains("Package removal blocked: dependency", result.Output);
    }
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-fresh-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task SelectedPackageWithMissingMetadataPreparesItsSourceBeforeResolvingAgain()
    {
        var paths = new AppPaths(Path.Combine(root, "home")); bool ready = false; int added = 0;
        var runner = new ToolRunner(bundledPackageTool: "bundled-vpm.exe", commandOverride: (_, args, _) =>
        {
            if (args is ["repo", "list"]) return Task.FromResult(new ToolResult(0, "", ""));
            if (args is ["repo", "add", "https://foxscore.dev/vpm/index.json"])
            { ready = true; added++; return Task.FromResult(new ToolResult(0, "Added", "")); }
            return Task.FromResult(new ToolResult(0, ready ? """{"versions":[{"version":"1.2.3"}]}""" : """{"versions":[]}""", ""));
        });
        var catalog = new PackageCatalog(paths, runner, new ConfigurationStore(paths), Path.Combine(root, "empty-repos"));
        Assert.Equal(["1.2.3"], await catalog.GetVersionsAsync(ProjectRules.EasyLogin));
        Assert.Equal(["1.2.3"], await catalog.GetVersionsAsync(ProjectRules.EasyLogin));
        Assert.Equal(1, added);
    }
    [Fact] public async Task FreshDefaultsAreMinimalAndExistingChoicesRemainIntact()
    {
        var paths = new AppPaths(Path.Combine(root, "home")); var store = new ConfigurationStore(paths);
        var fresh = await store.LoadAsync();
        Assert.False(fresh.IncludeBetaUpdates);
        Assert.Equal(5, fresh.VpmPackages.Count);
        Assert.Contains("com.poiyomi.toon", fresh.VpmPackages.Keys); Assert.Contains("com.vrcfury.vrcfury", fresh.VpmPackages.Keys);
        Assert.DoesNotContain(ProjectRules.EasyLogin, fresh.VpmPackages.Keys);
        fresh.VpmPackages[ProjectRules.EasyLogin] = "1.1.1"; fresh.FavoritePackages.Add(ProjectRules.EasyLogin);
        fresh.IncludeBetaUpdates = true;
        await store.SaveAsync(fresh);
        var reopened = await new ConfigurationStore(paths).LoadAsync();
        Assert.Equal("1.1.1", reopened.VpmPackages[ProjectRules.EasyLogin]); Assert.Contains(ProjectRules.EasyLogin, reopened.FavoritePackages);
        Assert.True(reopened.IncludeBetaUpdates);
    }
    [Theory]
    [InlineData(null, true)] [InlineData(true, true)] [InlineData(false, false)]
    public async Task ExistingImplicitAndExplicitUpdateChannelsSurviveUpgradeAndMerge(bool? previous, bool expected)
    {
        var paths = new AppPaths(Path.Combine(root, "channel"));
        Directory.CreateDirectory(paths.Home);
        var setting = previous is null ? "" : ",\"IncludeBetaUpdates\":" + (previous.Value ? "true" : "false");
        await File.WriteAllTextAsync(paths.ConfigFile, "{\"SchemaVersion\":2" + setting + "}");
        var store = new ConfigurationStore(paths);
        var loaded = await store.LoadAsync(); Assert.Equal(expected, loaded.IncludeBetaUpdates);
        var edited = loaded.Copy(); edited.Theme = "Light";
        var merged = await store.MergeAsync(loaded, edited);
        Assert.Equal(expected, merged.IncludeBetaUpdates);
        Assert.Equal(expected, (await store.LoadAsync()).IncludeBetaUpdates);
    }
    [Fact] public async Task ExistingProjectPackagesAreIndexedWithoutJoiningTheDefaultPreset()
    {
        var paths = new AppPaths(Path.Combine(root, "home")); var store = new ConfigurationStore(paths);
        var catalog = new PackageCatalog(paths, new ToolRunner(), store, Path.Combine(root, "empty-repos"));
        await catalog.RememberProjectPackagesAsync([new ProjectSnapshot
        {
            Path = Path.Combine(root, "project"), Name = "Fixture",
            InstalledVersions = new() { ["dev.example.tool"] = "2.3.4" },
            PackageDisplayNames = new() { ["dev.example.tool"] = "Existing Tool" }
        }]);
        var known = (await catalog.KnownAsync()).Single(x => x.Id == "dev.example.tool");
        Assert.Equal("Existing Tool", known.DisplayName); Assert.Contains("2.3.4", known.Versions);
        Assert.DoesNotContain("dev.example.tool", (await store.LoadAsync()).VpmPackages.Keys);
    }
    [Fact] public async Task PackageCommandsUseBundledExecutableAndPreservePinnedVersionAndPath()
    {
        string? executable = null; string[]? command = null;
        var runner = new ToolRunner(bundledPackageTool: "bundled-vpm.exe", commandOverride: (exe, args, _) =>
        { executable = exe; command = args; return Task.FromResult(new ToolResult(0, "", "")); });
        var project = Path.Combine(root, "Avatar [PC] & café");
        await runner.VpmAsync(["add", "package", "dev.example.tool@1.2.3", "-p", project], project);
        Assert.Equal("bundled-vpm.exe", executable);
        Assert.Equal(["install", "dev.example.tool", "1.2.3", "-p", project, "-y", "--prerelease"], command!);
        await runner.VpmAsync(["add", "package", "dev.example.tool", "-p", project], project);
        Assert.Equal(["install", "dev.example.tool", "-p", project, "-y"], command!);
        Assert.Equal(["remove", "tool", "-p", project, "-y"], ToolRunner.PackageToolArguments(["remove", "package", "tool", "-p", project])!);
        Assert.Equal(["resolve", "-p", project], ToolRunner.PackageToolArguments(["resolve", "project", project])!);
    }
    [Fact] public void SecondDesktopCannotAcquireTheSameHomeAndCanReopenAfterExit()
    {
        var home = Path.Combine(root, "home");
        using (var first = DesktopInstanceLease.TryAcquire(home, out _))
        {
            Assert.NotNull(first);
            Assert.Null(DesktopInstanceLease.TryAcquire(home, out var owner)); Assert.Equal(Environment.ProcessId, owner);
            using var independent = DesktopInstanceLease.TryAcquire(Path.Combine(root, "portable"), out _);
            Assert.NotNull(independent);
        }
        using var reopened = DesktopInstanceLease.TryAcquire(home, out _); Assert.NotNull(reopened);
    }
    [Fact] public async Task SharedLogArchiveIncludesUpdateFailuresAndExcludesSettings()
    {
        var paths = new AppPaths(Path.Combine(root, "home")); Directory.CreateDirectory(paths.Logs);
        var diagnostics = new DiagnosticLog(paths); diagnostics.Write("Fixture", "Failure", new IOException("Synthetic error"));
        await File.WriteAllTextAsync(paths.ConfigFile, "private settings are excluded");
        var update = Path.Combine(paths.Home, "updates", "components"); Directory.CreateDirectory(update);
        await File.WriteAllTextAsync(Path.Combine(update, "last-error.log"), "Synthetic update failure");
        var zipPath = Path.Combine(root, "logs.zip");
        await SupportLogs.ExportAsync(paths, zipPath);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, x => x.FullName.StartsWith("App/VRC-Project-Setup-Errors-"));
        Assert.Contains(zip.Entries, x => x.FullName == "Update/last-error.log");
        Assert.DoesNotContain(zip.Entries, x => x.FullName.Contains("config", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(diagnostics.FilePath, SupportLogs.LatestError(paths));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
