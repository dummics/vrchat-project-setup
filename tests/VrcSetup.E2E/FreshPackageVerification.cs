using VrcSetup.Core;
using VrcSetup.Infrastructure;

internal static class FreshPackageVerification
{
    public static async Task RunAsync(string payload, string evidence)
    {
        // vrc-get intentionally shares VCC's profile. Run the cold-profile proof
        // only on the disposable hosted runner, never on the owner's profile.
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Fresh package-source verification requires a disposable GitHub runner.");
        Directory.CreateDirectory(evidence);
        var paths = new AppPaths(Path.Combine(evidence, "home"), Path.Combine(Path.GetFullPath(payload), "app", "cli"));
        if (paths.VrcGetPath is not { } tool) throw new IOException("Published package tool is missing.");
        if (!Path.GetFullPath(tool).Equals(Path.GetFullPath(Path.Combine(payload, "app", "cli", "Tools", "vrc-get.exe")), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Fresh verification must use the published tool, not a source-checkout fallback.");
        var runner = new ToolRunner(bundledPackageTool: tool);
        var configStore = new ConfigurationStore(paths); var config = await configStore.LoadAsync();
        var catalog = new PackageCatalog(paths, runner, configStore);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var resolved = new Dictionary<string, string>();
        foreach (var id in config.VpmPackages.Keys)
        {
            var versions = await catalog.GetVersionsAsync(id, timeout.Token);
            if (versions.Length == 0) throw new IOException("Fresh profile could not resolve " + id);
            resolved[id] = versions.FirstOrDefault(x => !x.Contains('-')) ?? versions[0];
            Console.WriteLine(PackageCatalog.FriendlyName(id) + ": " + resolved[id]);
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "fresh-packages.json"),
            new { Passed = true, BundledTool = true, Resolved = resolved, ProjectFilesChanged = false });
    }
}
