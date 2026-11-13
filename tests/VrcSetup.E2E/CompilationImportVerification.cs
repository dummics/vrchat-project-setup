using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

// All assets and errors are generated locally; never uses a personal Unity project.
internal static class CompilationImportVerification
{
    public static async Task RunAsync(string editor, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a new evidence directory.");
        Directory.CreateDirectory(evidence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var paths = new AppPaths(Path.Combine(evidence, "home"));
        var driver = new UnityProjectDriver(paths, new ToolRunner(new DiagnosticLog(paths)), new ProjectWorkspace(paths), registerProjects: false);
        var project = Path.Combine(evidence, "P");
        var creation = new CreationRequest("", "Compilation fixture", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        await driver.CreateAsync(creation, null, token);
        var broken = Path.Combine(evidence, "Broken.unitypackage");
        var followup = Path.Combine(evidence, "Following.unitypackage");
        Archive(broken, "Assets/CompilationProbe.cs", "public class CompilationProbe { THIS_IS_NOT_VALID_CSHARP }\n");
        Archive(followup, "Assets/Following.txt", "Imported after the broken script");
        var plan = new ProjectPlan
        {
            Name = creation.Name, Path = project, ExpectedFingerprint = "fixture", DesiredPolicy = new Dictionary<string, string>(), Changes = [], UnityEditorPath = editor,
            Creation = creation with { EmptyProject = false, UnityPackagePath = broken, SourceIdentity = await PackageSourceIdentity.ReadAsync(broken, token) },
            ExtraImports = [followup]
        };
        var warning = false;
        try { await driver.ImportAsync(plan, null, token); }
        catch (UnityCompilationWarningException) { warning = true; }
        if (!warning || !File.Exists(Path.Combine(project, "Assets", "Following.txt")))
            throw new IOException("The broken-script import did not both import the following archive and report a completed compilation notice.");
        Console.WriteLine("PASS: script errors did not stop the remaining archive; completion has a distinct notice.");
        await File.WriteAllTextAsync(Path.Combine(project, "Assets", "CompilationProbe.cs"), "public class CompilationProbe {}\n", token);
        await driver.SettleAsync(plan, null, token);
        if (!File.Exists(Path.Combine(project, "Library", "ScriptAssemblies", "Assembly-CSharp.dll")))
            throw new IOException("The corrected script was not compiled.");
        Console.WriteLine("PASS: correcting the script settles without importing the archives again.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "compilation-import.json"), new
        {
            Passed = true, LaterArchiveImported = true, CompletedCompilationNotice = true, CorrectedWithoutReimport = true, PersonalProjectsChanged = false
        }, token);
    }

    private static void Archive(string path, string asset, string content)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip, TarEntryFormat.Ustar);
        var guid = Guid.NewGuid().ToString("N");
        Entry(guid + "/pathname", asset);
        Entry(guid + "/asset", content);
        Entry(guid + "/asset.meta", "fileFormatVersion: 2\nguid: " + guid + "\n");
        void Entry(string name, string text)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = stream });
        }
    }
}
