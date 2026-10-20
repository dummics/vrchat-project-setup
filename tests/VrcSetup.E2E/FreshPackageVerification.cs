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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var resolved = new Dictionary<string, string>();
        foreach (var id in config.VpmPackages.Keys)
        {
            var versions = await catalog.GetVersionsAsync(id, timeout.Token);
            if (versions.Length == 0) throw new IOException("Fresh profile could not resolve " + id);
            resolved[id] = versions.FirstOrDefault(x => !x.Contains('-')) ?? versions[0];
            Console.WriteLine(PackageCatalog.FriendlyName(id) + ": " + resolved[id]);
        }
        // A non-executable editor fixture makes any accidental Unity launch fail.
        // This checks real package installation on a host with no Unity editor.
        var editor = Path.Combine(evidence, "editor-fixture", "Unity.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!); await File.WriteAllTextAsync(editor, "Not an executable", timeout.Token);
        var projects = Path.Combine(evidence, "projects");
        var project = Path.Combine(projects, "New project [PC] & città");
        config.UnityEditorPath = editor; config.UnityProjectsRoot = projects;
        var workspace = new ProjectWorkspace(paths);
        var driver = new UnityProjectDriver(paths, runner, workspace);
        var request = new CreationRequest("", "New project", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var plan = await new ProjectPlanner(workspace, catalog).PlanAsync(project, config.VpmPackages, config, exact: true, creation: request, cancellationToken: timeout.Token);
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new("fresh-native-project", DateTime.UtcNow, [plan]), cancellationToken: timeout.Token);
        if (!report.Success) throw new IOException("Native VPM creation failed: " + string.Join("; ", report.Results.Select(x => x.Message)));
        var snapshot = await workspace.ReadAsync(project, timeout.Token);
        foreach (var change in plan.Changes)
            if (snapshot.InstalledVersions.GetValueOrDefault(change.Id) != change.ResolvedVersion) throw new IOException("Installed package version mismatch: " + change.Id);
        if (Directory.Exists(paths.Logs) && Directory.EnumerateFiles(paths.Logs, "unity-*.log").Any()) throw new IOException("Empty creation unexpectedly started Unity.");
        var state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(project, ".vrcsetup", "state.json"), timeout.Token);
        if (state?.Completed != true) throw new IOException("New project was left incomplete.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "fresh-packages.json"),
            new { Passed = true, BundledTool = true, Resolved = resolved, NativeProjectCompleted = true,
                UnityStarted = false, FixtureProjectCreated = true, ExistingProjectsChanged = false });
        Console.WriteLine("PASS: all default packages installed and new project completed without starting Unity.");
    }
}
