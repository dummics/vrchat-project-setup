using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

// Real Unity bridge proof in a newly created fixture. No VCC repository or
// package registration, user settings, existing projects or avatar files touched.
internal static class UnityImportVerification
{
    public static async Task RunAsync(string editor, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a new evidence directory.");
        UnityProjectDriver.ValidateEditor(editor);
        var project = Path.Combine(evidence, "Project [PC] & città");
        var paths = new AppPaths(Path.Combine(evidence, "home"));
        var runner = new ToolRunner(new DiagnosticLog(paths));
        var driver = new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var creation = new CreationRequest("", "Fixture", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await driver.CreateAsync(creation, null, timeout.Token);
        watch.Stop(); var creationSeconds = watch.Elapsed.TotalSeconds;
        var usedTemplate = !Directory.Exists(paths.Logs) || !Directory.EnumerateFiles(paths.Logs, "unity-create-*.log").Any();
        Console.WriteLine($"PASS: project created in {creationSeconds:F2}s; installed template used: {usedTemplate}.");
        var plan = new ProjectPlan
        {
            Path = project, Name = "Fixture", ExpectedFingerprint = "fixture", DesiredPolicy = new Dictionary<string, string>(),
            Changes = [], UnityEditorPath = editor, Creation = creation
        };
        await driver.ImportAsync(plan, null, timeout.Token);
        if (Directory.Exists(paths.Logs) && Directory.EnumerateFiles(paths.Logs, "unity-*.log").Any()) throw new IOException("Empty creation unexpectedly started Unity.");
        Console.WriteLine("PASS: empty project needs no Unity import session.");
        await driver.ImportAsync(plan with { Creation = null, RecoverySource = creation }, null, timeout.Token);
        if (Directory.Exists(paths.Logs) && Directory.EnumerateFiles(paths.Logs, "unity-*.log").Any()) throw new IOException("Empty recovery unexpectedly started Unity.");
        Console.WriteLine("PASS: existing empty project recovery needs no Unity import session.");
        var archive = Path.Combine(evidence, "Probe.unitypackage");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar))
        {
            Add("Assets/VrcSetupBridgeProbe.txt", "import completed");
            Add("Assets/VrcSetupBridgeProbe.cs", "public sealed class VrcSetupBridgeProbe {}\n");
            void Add(string name, string content)
            {
                var guid = Guid.NewGuid().ToString("N");
                Entry(guid + "/pathname", name); Entry(guid + "/asset", content);
                Entry(guid + "/asset.meta", "fileFormatVersion: 2\nguid: " + guid + "\n");
            }
            void Entry(string name, string content)
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = stream });
            }
        }
        await driver.ImportAsync(plan with { Creation = creation with { ExtraImports = [archive] } }, null, timeout.Token);
        if (!File.Exists(Path.Combine(project, "Assets", "VrcSetupBridgeProbe.txt")) ||
            !File.Exists(Path.Combine(project, "Library", "ScriptAssemblies", "Assembly-CSharp.dll")))
            throw new IOException("Imported asset or recompiled assembly missing.");
        // Exercise the bridge fallback itself with a valid empty plan: it must
        // settle, whereas a missing/malformed package list must remain invalid.
        var queue = Path.Combine(project, ".vrcsetup", "import-plan.json");
        await AtomicFile.WriteJsonAsync(queue, new { packages = Array.Empty<string>() }, timeout.Token);
        var log = Path.Combine(evidence, "empty-queue.log");
        var result = await runner.RunAsync(editor, ["-projectPath", project, "-batchmode", "-executeMethod", "VrcSetupPostImport.RunImport", "-vrcsetupImportPlan", queue, "-logFile", log], cancellationToken: timeout.Token, logFile: log);
        var output = await File.ReadAllTextAsync(log, timeout.Token);
        if (result.ExitCode != 0 || !output.Contains("[vrc-setup] Post-import settle complete"))
            throw new IOException("Bridge rejected the valid empty queue.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "unity-import.json"), new
        {
            Passed = true, CreationSeconds = creationSeconds, InstalledTemplateUsed = usedTemplate,
            EmptyCreationNoUnity = true, ArchiveImported = true, ScriptRecompiled = true,
            EmptyQueueFallbackSettled = true, EmptyProjectRecoveryNoUnity = true, ExistingProjectsChanged = false
        }, timeout.Token);
        Console.WriteLine("PASS: archive imported, scripts recompiled and an empty queue safely settled.");
    }
}
