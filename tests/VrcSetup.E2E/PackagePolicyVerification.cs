using VrcSetup.Core;
using VrcSetup.Infrastructure;

// Exercises the real reported resolver transition without touching an owner project
// or registering the disposable project with Creator Companion.
internal static class PackagePolicyVerification
{
    public static async Task RunAsync(string editor, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose unused fixture evidence.");
        Directory.CreateDirectory(evidence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var installedCli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup", "app", "cli");
        var paths = new AppPaths(Path.Combine(evidence, "home"), installedCli);
        var runner = new ToolRunner(new DiagnosticLog(paths), paths.VrcGetPath);
        var workspace = new ProjectWorkspace(paths);
        var driver = new UnityProjectDriver(paths, runner, workspace, registerProjects: false);
        var store = new ConfigurationStore(paths);
        var config = await store.LoadAsync(token);
        config.UnityEditorPath = editor; config.UnityProjectsRoot = evidence;
        var catalog = new PackageCatalog(paths, runner, store, allowRepositorySetup: false);
        var project = Path.Combine(evidence, "Policy project");
        await driver.CreateAsync(new("", "Policy project", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true), null, token);
        var selected = new Dictionary<string, string>
        {
            [ProjectRules.BaseSdk] = "latest", [ProjectRules.AvatarSdk] = "latest", [ProjectRules.Resolver] = "latest",
            ["com.nanochip.avatarlimbscaling"] = "1.2.0", ["com.vrcfury.vrcfury"] = "latest"
        };
        var planner = new ProjectPlanner(workspace, catalog);
        var executor = new ProjectExecutor(workspace, driver);
        var plan = await planner.PlanAsync(project, selected, config, cancellationToken: token);
        var automatic = await executor.ApplyAsync(new("automatic", DateTime.UtcNow, [plan]), cancellationToken: token);
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "automatic.json"), automatic, token);
        if (!automatic.Success) throw new IOException(string.Join("; ", automatic.Results.Select(x => x.Message)));
        var resolved = await workspace.ReadAsync(project, token);
        Console.WriteLine("PASS: Automatic accepts resolver-selected VRCFury " + resolved.InstalledVersions["com.vrcfury.vrcfury"]);
        // This is the stable version from the owner's failing operation report.
        // It deliberately remains an E2E fixture, never production policy.
        selected["com.vrcfury.vrcfury"] = "1.1434.0";
        var pinnedPlan = await planner.PlanAsync(project, selected, config, cancellationToken: token);
        var pinned = await executor.ApplyAsync(new("pinned", DateTime.UtcNow, [pinnedPlan]), cancellationToken: token);
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "pinned.json"), pinned, token);
        if (!pinned.Success) throw new IOException(string.Join("; ", pinned.Results.Select(x => x.Message)));
        var final = await workspace.ReadAsync(project, token);
        if (final.InstalledVersions["com.vrcfury.vrcfury"] != "1.1434.0" || final.LockedPackages["com.vrcfury.vrcfury"] != "1.1434.0")
            throw new IOException("Stable downgrade was not preserved.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "result.json"), new { Passed = true, Automatic = resolved.InstalledVersions, Pinned = final.InstalledVersions }, token);
        Console.WriteLine("PASS: explicit stable VRCFury downgrade preserved after dependency resolution.");
    }
}
