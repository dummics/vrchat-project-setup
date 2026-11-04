using VrcSetup.Core;
using VrcSetup.Infrastructure;

internal static class PackageMigrationVerification
{
    // Real public VPM packages only. Never launches Unity or signs into Patreon.
    public static async Task RunAsync(string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a fresh migration fixture.");
        Directory.CreateDirectory(evidence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)); var token = timeout.Token;
        var cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup", "app", "cli");
        var paths = new AppPaths(Path.Combine(evidence, "home"), cli);
        var runner = new ToolRunner(new DiagnosticLog(paths), paths.VrcGetPath);
        var workspace = new ProjectWorkspace(paths);
        var driver = new UnityProjectDriver(paths, runner, workspace, registerProjects: false);
        var config = new AppConfiguration();
        var catalog = new PackageCatalog(paths, runner, new ConfigurationStore(paths), allowRepositorySetup: false);
        var planner = new ProjectPlanner(workspace, catalog); var executor = new ProjectExecutor(workspace, driver);
        var project = Path.Combine(evidence, "Shader migration");
        Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "Packages")); Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1", token);
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "manifest.json"), new { dependencies = new Dictionary<string, string>() }, token);
        var checks = new List<string>();
        async Task Apply(string name, Dictionary<string, string> selected, string[]? remove = null)
        {
            var plan = await planner.PlanAsync(project, selected, config, remove: remove, cancellationToken: token);
            await AtomicFile.WriteJsonAsync(Path.Combine(evidence, name + "-plan.json"), plan, token);
            var report = await executor.ApplyAsync(new(name, DateTime.UtcNow, [plan]), new ImmediateProgress<OperationEvent>(e => Console.WriteLine($"[{e.Phase}] {e.Message}")), token);
            await AtomicFile.WriteJsonAsync(Path.Combine(evidence, name + ".json"), report, token);
            if (!report.Success) throw new IOException(name + ": " + string.Join("; ", report.Results.Select(x => x.Message)));
            checks.Add(name); Console.WriteLine("PASS: " + name);
        }
        await Apply("install-toon", new() { ["com.poiyomi.toon"] = "10.0.27" });
        await Apply("switch-to-pro", new() { ["com.poiyomi.pro"] = "11.0.38" });
        var pro = await workspace.ReadAsync(project, token);
        if (pro.InstalledVersions.GetValueOrDefault("com.poiyomi.pro") != "11.0.38" || PackagePresence.Contains(pro, "com.poiyomi.toon")) throw new IOException("Pro did not replace Toon.");
        await Apply("remove-already-absent", new(), ["com.poiyomi.toon"]);
        await Apply("downgrade-pro", new() { ["com.poiyomi.pro"] = "10.0.11" });
        var older = await workspace.ReadAsync(project, token);
        if (older.InstalledVersions.GetValueOrDefault("com.poiyomi.pro") != "10.0.11") throw new IOException("Pro downgrade was not preserved.");
        await Apply("switch-back-to-toon", new() { ["com.poiyomi.toon"] = "10.0.27" });
        var toon = await workspace.ReadAsync(project, token);
        if (PackagePresence.Contains(toon, "com.poiyomi.pro") || toon.InstalledVersions.GetValueOrDefault("com.poiyomi.toon") != "10.0.27") throw new IOException("Toon did not replace Pro.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "migration.json"), new { Passed = true, Checks = checks, UnityLaunched = false, PatreonAuthenticated = false, CreatorCompanionRegistration = false }, token);
    }
}
