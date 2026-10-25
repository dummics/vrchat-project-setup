using System.Diagnostics;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

// Real packages and Unity compilation, always in a newly allocated disposable project.
internal static class PackageMixVerification
{
    public static async Task RunAsync(string editor, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose unused fixture evidence.");
        Directory.CreateDirectory(evidence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var installedCli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup", "app", "cli");
        var paths = new AppPaths(Path.Combine(evidence, "home"), installedCli);
        var tool = Path.Combine(installedCli, "Tools", "vrc-get.exe");
        if (!File.Exists(tool)) throw new IOException("Installed package tool not found.");
        var diagnostics = new DiagnosticLog(paths);
        var runner = new ToolRunner(diagnostics, tool);
        var workspace = new ProjectWorkspace(paths, diagnostics);
        var driver = new UnityProjectDriver(paths, runner, workspace, registerProjects: false);
        var store = new ConfigurationStore(paths);
        var config = await store.LoadAsync(timeout.Token);
        config.UnityEditorPath = editor; config.UnityProjectsRoot = evidence;
        var project = Path.Combine(evidence, "Package mix");
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var catalog = new PackageCatalog(paths, runner, store, allowRepositorySetup: false);
        foreach (var id in new[] { ProjectRules.Resolver, ProjectRules.BaseSdk, ProjectRules.AvatarSdk, "com.poiyomi.toon", "com.vrcfury.vrcfury", "nadena.dev.modular-avatar", "com.llealloo.audiolink", "vrchat.blackstartx.gesture-manager" })
        {
            var versions = await catalog.GetVersionsAsync(id, timeout.Token);
            selected[id] = versions.FirstOrDefault(x => !x.Contains('-')) ?? throw new IOException("No stable package in configured VPM sources: " + id);
            Console.WriteLine(id + " @ " + selected[id]);
        }
        var creation = new CreationRequest("", "Package mix", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var plan = await new ProjectPlanner(workspace, catalog).PlanAsync(project, selected, config, exact: true, creation: creation, cancellationToken: timeout.Token);
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [plan]), cancellationToken: timeout.Token);
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "package-install.json"), report);
        if (!report.Success) throw new IOException(string.Join("; ", report.Results.Select(x => x.Message)));
        Directory.CreateDirectory(Path.Combine(project, "Assets", "Editor"));
        await File.WriteAllTextAsync(Path.Combine(project, "Assets", "Editor", "VrcSetupCompileProbe.cs"),
            "public static class VrcSetupCompileProbe { public static void Run() { System.IO.File.WriteAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath, \"../compile-passed.txt\"), \"Compiled\"); UnityEditor.EditorApplication.Exit(0); } }");
        var start = new ProcessStartInfo(editor) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-batchmode", "-nographics", "-projectPath", project, "-executeMethod", "VrcSetupCompileProbe.Run", "-logFile", Path.Combine(evidence, "unity-compile.log") }) start.ArgumentList.Add(arg);
        using var unity = Process.Start(start) ?? throw new IOException("Unity did not start.");
        try { await unity.WaitForExitAsync(timeout.Token); }
        catch { if (!unity.HasExited) unity.Kill(true); throw; } // Only the exact fixture child is owned here.
        using var logStream = new FileStream(Path.Combine(evidence, "unity-compile.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var logReader = new StreamReader(logStream);
        var log = await logReader.ReadToEndAsync(timeout.Token);
        if (unity.ExitCode != 0 || !File.Exists(Path.Combine(project, "compile-passed.txt")) || System.Text.RegularExpressions.Regex.IsMatch(log, @"error CS\d+|Scripts have compiler errors", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new IOException("Unity compile failed; see fixture unity-compile.log.");
        var snapshot = await workspace.ReadAsync(project, timeout.Token);
        foreach (var item in selected)
            if (snapshot.InstalledVersions.GetValueOrDefault(item.Key) != item.Value) throw new IOException("Installed version mismatch: " + item.Key);
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "result.json"), new { Passed = true, Unity = editor, Packages = selected, Installed = snapshot.InstalledVersions, UnityExit = unity.ExitCode });
        Console.WriteLine("PASS: real VPM package installation and Unity compilation.");
    }
}
