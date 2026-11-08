using VrcSetup.Core;
using VrcSetup.Infrastructure;

// This changes the VCC project list. It must only run on a disposable hosted runner.
internal static class HostedVccVerification
{
    public static async Task RunAsync(string evidence)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("VCC registration verification requires a disposable GitHub-hosted runner.");
        var runnerTemp = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new IOException("Missing runner temp directory."));
        evidence = Path.GetFullPath(evidence);
        if (!evidence.StartsWith(Path.TrimEndingDirectorySeparator(runnerTemp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(evidence))
            throw new IOException("Choose a new fixture directory inside the hosted runner temp directory.");
        var project = Path.Combine(evidence, "VccProbe");
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1\n");
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "manifest.json"), "{\"dependencies\":{}}\n");
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), "{\"dependencies\":{},\"locked\":{}}\n");
        var paths = new AppPaths(Path.Combine(evidence, "home"));
        var runner = new ToolRunner(new DiagnosticLog(paths));
        var driver = new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var progress = new ImmediateProgress<OperationEvent>(item => Console.WriteLine($"[{item.Phase}] {item.Message} {item.RawMessage}"));
        if (await driver.RegisterAsync(project, progress, timeout.Token)) throw new IOException("VCC accepted an invalid fixture as a successful registration.");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        if (!await driver.RegisterAsync(project, progress, timeout.Token)) throw new IOException("VCC registration failed on the hosted fixture.");
        var listed = await runner.VpmAsync(["list", "projects"], cancellationToken: timeout.Token);
        if (listed.ExitCode != 0 || !listed.Output.Contains("VccProbe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("VCC did not list the registered hosted fixture: " + listed.Output);
        Console.WriteLine("PASS: the real VPM CLI registered and listed an isolated project.");
    }
}
