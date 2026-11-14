using VrcSetup.Core;
using VrcSetup.Infrastructure;

internal static class PackageOverwriteVerification
{
    public static async Task RunAsync(string editor, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a fresh overwrite fixture.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var token = timeout.Token;
        var paths = new AppPaths(Path.Combine(evidence, "home"));
        var workspace = new ProjectWorkspace(paths);
        var driver = new UnityProjectDriver(paths, new ToolRunner(new DiagnosticLog(paths)), workspace, registerProjects: false);
        var project = Path.Combine(evidence, "P");
        var creation = new CreationRequest("", "Overwrite fixture", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        await driver.CreateAsync(creation, null, token);
        const string id = "com.fixture.editor", guid = "54af3d77651d4290a9d9fb05d77d897e";
        var package = Path.Combine(project, "Packages", id);
        Directory.CreateDirectory(package);
        await AtomicFile.WriteJsonAsync(Path.Combine(package, "package.json"), new { name = id, version = "1.0.0", displayName = "Fixture editor" }, token);
        await AtomicFile.WriteJsonAsync(Path.Combine(package, "FixtureEditor.asmdef"), new { name = "FixtureEditor", includePlatforms = new[] { "Editor" } }, token);
        var script = Path.Combine(package, "PublishedTool.cs");
        await File.WriteAllTextAsync(script, "public class PublishedTool {}\n", token);
        await File.WriteAllTextAsync(script + ".meta", "fileFormatVersion: 2\nguid: " + guid + "\n", token);
        var locked = new Dictionary<string, object> { [id] = new { version = "1.0.0" } };
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), new { dependencies = locked, locked }, token);
        var before = await driver.CapturePackageContentsAsync(project, token);
        var archive = Path.Combine(evidence, "Legacy.unitypackage");
        const string legacy = "public class LegacyTool {}\n";
        CompilationImportVerification.Archive(archive, "Assets/Legacy/PublishedTool.cs", legacy, guid);
        var plan = new ProjectPlan
        {
            Name = creation.Name, Path = project, ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [],
            UnityEditorPath = editor, Creation = creation with { EmptyProject = false, UnityPackagePath = archive, SourceIdentity = await PackageSourceIdentity.ReadAsync(archive, token) }
        };
        await driver.ImportAsync(plan, null, token);
        var after = await driver.CapturePackageContentsAsync(project, token);
        if (await File.ReadAllTextAsync(script, token) != legacy || before[id] == after[id] ||
            (await workspace.ReadAsync(project, token)).InstalledVersions[id] != "1.0.0")
            throw new IOException("Shared-GUID import did not reproduce a detectable package overwrite with unchanged metadata.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "package-overwrite.json"), new
        { Passed = true, SharedGuidOverwrotePackage = true, FingerprintChanged = true, DeclaredVersionStayedUnchanged = true, PersonalProjectsChanged = false }, token);
        Console.WriteLine("PASS: Unity overwrote an embedded package through a shared GUID; content verification detected it while the version stayed unchanged.");
    }
}
