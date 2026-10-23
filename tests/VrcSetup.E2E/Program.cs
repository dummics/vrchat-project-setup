using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
if (args is ["--verify-package-mix", var mixEditor, var mixEvidence])
{
    await PackageMixVerification.RunAsync(mixEditor, mixEvidence);
    return;
}

if (args is ["--verify-unity-import", var importEditor, var importEvidence])
{
    await UnityImportVerification.RunAsync(importEditor, importEvidence);
    return;
}
if (args is ["--verify-public-update", var previousVersion, var expectedVersion, var updateEvidence])
{
    await PublicUpdateVerification.RunAsync(previousVersion, expectedVersion, updateEvidence);
    return;
}
if (args is ["--verify-fresh-packages", var payload, var evidence])
{
    await FreshPackageVerification.RunAsync(payload, evidence);
    return;
}
var arguments = args.Chunk(2).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
var artifacts = ProjectRules.NormalizePath(arguments.GetValueOrDefault("--artifacts") ?? throw new ArgumentException("--artifacts is required."));
var editor = ProjectRules.NormalizePath(arguments.GetValueOrDefault("--unity") ?? throw new ArgumentException("--unity is required."));
Directory.CreateDirectory(artifacts);
var home = Path.Combine(artifacts, "home"); var projects = Path.Combine(artifacts, "projects"); Directory.CreateDirectory(projects);
var app = new VrcSetupApplication(home);
var progress = new ImmediateProgress<OperationEvent>(x => Console.WriteLine($"[{x.Phase}] {x.Message}"));
var checks = new List<object>(); var registered = new List<string>();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(40));
var token = timeout.Token;
try
{
    var package = Path.Combine(artifacts, "Probe [PC] & café.unitypackage");
    CreatePackage(package);
    var config = await app.Configuration.LoadAsync(token);
    config.UnityEditorPath = editor; config.UnityProjectsRoot = projects;
    config.VpmPackages = new() { [ProjectRules.BaseSdk] = "latest", [ProjectRules.AvatarSdk] = "latest", [ProjectRules.Resolver] = "latest" };
    config.PackagePresets["Foundation"] = new(config.VpmPackages); await app.Configuration.SaveAsync(config, token);
    var cli = typeof(VrcSetup.Cli.Program).Assembly.Location;
    var dotnet = ToolRunner.FindExecutable("dotnet") ?? throw new IOException("dotnet not found.");
    async Task<ToolResult> Cli(params string[] command) => await app.Tools.RunAsync(dotnet, new[] { cli, "--home", home }.Concat(command), progress: progress, phase: "CLI", cancellationToken: token, timeout: TimeSpan.FromMinutes(20));
    var dry = await Cli("create", package, "-Name", "CLI Native", "-Preset", "Foundation", "-DryRun");
    Check(dry.ExitCode == 0 && !Directory.Exists(Path.Combine(projects, "CLI Native")), "CLI create dry-run leaves destination absent");
    var create = await Cli("create", package, "-Name", "CLI Native", "-Preset", "Foundation");
    var first = Path.Combine(projects, "CLI Native"); registered.Add(first);
    Check(create.ExitCode == 0, "Real CLI UnityPackage creation", create.Output);
    await VerifyProject(first, app);

    var directPlan = await app.PlanCreationAsync(package, config, "Shared Core Native", "Foundation", cancellationToken: token);
    var direct = await app.ApplyAsync(directPlan, progress, token);
    var second = Path.Combine(projects, "Shared Core Native"); registered.Add(second);
    Check(direct.Success, "Direct shared-engine creation (same client-independent path as GUI)", string.Join("; ", direct.Results.Select(x => x.Message)));
    await VerifyProject(second, app);
    var firstSnapshot = await app.Projects.ReadAsync(first, token); var secondSnapshot = await app.Projects.ReadAsync(second, token);
    Check(firstSnapshot.InstalledVersions.OrderBy(x => x.Key).SequenceEqual(secondSnapshot.InstalledVersions.OrderBy(x => x.Key)), "CLI and direct-engine package results match");
    var listing = await Cli("projects", "-Refresh", "-Json"); Check(listing.ExitCode == 0 && listing.StdOut.TrimStart().StartsWith('['), "CLI project JSON array");
    var versions = await app.Catalog.GetVersionsAsync("gogoloco", token);
    var stable = SemanticVersion.Sort(versions, false).FirstOrDefault() ?? throw new IOException("GoGoLoco versions unavailable.");
    var add = await Cli("packages", "add", first, "gogoloco@" + stable);
    Check(add.ExitCode == 0 && (await app.Projects.ReadAsync(first, token)).InstalledVersions.GetValueOrDefault("gogoloco") == stable, "Real CLI add and metadata verification", add.Output);
    var remove = await Cli("packages", "remove", first, "gogoloco"); Check(remove.ExitCode == 0 && !(await app.Projects.ReadAsync(first, token)).Packages.ContainsKey("gogoloco"), "Real CLI removal", remove.Output);
    var bulk = await app.Planner.PlanBulkAsync([first, second], new Dictionary<string, string> { ["gogoloco"] = stable }, config, cancellationToken: token);
    var bulkReport = await app.ApplyAsync(bulk, progress, token);
    Check(bulkReport.Success && bulkReport.Results.Length == 2, "Real bulk package apply", string.Join("; ", bulkReport.Results.Select(x => x.Message)));
    var save = await Cli("presets", "save", "Real E2E", first); Check(save.ExitCode == 0, "CLI preset saved from installed packages");
    await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "checks.json"), checks, token);
    Console.WriteLine($"PASS: {checks.Count} real E2E checks. Evidence: {artifacts}");
}
catch (Exception error)
{
    checks.Add(new { Name = "Failure", Passed = false, Message = error.ToString() });
    await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "checks.json"), checks, CancellationToken.None);
    Console.Error.WriteLine(error); Environment.ExitCode = 1;
}
finally
{
    foreach (var project in registered)
    {
        try { await app.Tools.VpmAsync(["remove", "project", project], cancellationToken: CancellationToken.None); }
        catch (Exception error) { Console.Error.WriteLine("VCC test registration cleanup: " + error.Message); }
    }
    // Keep disposable projects for evidence inspection; the root removes them after verification.
}

void Check(bool passed, string name, string? message = null)
{
    checks.Add(new { Name = name, Passed = passed, Message = passed ? "" : message });
    Console.WriteLine((passed ? "PASS: " : "FAIL: ") + name);
    if (!passed) throw new IOException(name + ": " + message);
}
async Task VerifyProject(string project, VrcSetupApplication app)
{
    var state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(project, ".vrcsetup", "state.json"), token);
    Check(state?.Completed == true, "Setup state completed: " + Path.GetFileName(project));
    Check(File.Exists(Path.Combine(project, "Assets", "VrcSetupE2EMarker.txt")), "UnityPackage marker imported: " + Path.GetFileName(project));
    Check(File.Exists(Path.Combine(project, "Library", "ScriptAssemblies", "Assembly-CSharp.dll")), "Domain-reload probe compiled: " + Path.GetFileName(project));
    var snapshot = await app.Projects.ReadAsync(project, token); Check(snapshot.InstalledVersions.ContainsKey(ProjectRules.AvatarSdk), "Embedded SDK installed: " + Path.GetFileName(project));
}
static void CreatePackage(string path)
{
    using var file = File.Create(path); using var gzip = new GZipStream(file, CompressionLevel.Fastest); using var tar = new TarWriter(gzip, TarEntryFormat.Ustar);
    Add("Assets/VrcSetupE2EMarker.txt", "VRCSETUP native .NET import verified.");
    Add("Assets/VrcSetupE2EProbe.cs", "using UnityEngine; public sealed class VrcSetupE2EProbe : MonoBehaviour {}\n");
    void Add(string assetPath, string contents)
    {
        var guid = Guid.NewGuid().ToString("N");
        Entry(guid + "/asset", contents); Entry(guid + "/asset.meta", "fileFormatVersion: 2\nguid: " + guid + "\n"); Entry(guid + "/pathname", assetPath);
    }
    void Entry(string name, string contents)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
        tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = stream });
    }
}
public sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
