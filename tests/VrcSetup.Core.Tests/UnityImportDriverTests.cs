using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class UnityImportDriverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-import [PC] & città-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("2.0.0", "1.0.0", "downgrade")]
    [InlineData("2.0.0-beta.3", "1.0.0-beta.2", "downgrade")]
    [InlineData("1.0.0", "2.0.0", "install")]
    [InlineData("1.0.0", "2.0.0-beta.2", "install")]
    public async Task ExactVersionsUseResolverDowngradeForOlderLocks(string current, string requested, string action)
    {
        var project = Path.Combine(root, "Project");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), new
        { dependencies = new Dictionary<string, object> { ["example.tool"] = new { version = current } }, locked = new Dictionary<string, object> { ["example.tool"] = new { version = current } } });
        string[]? command = null;
        var runner = new ToolRunner(bundledPackageTool: "fixture-vrc-get.exe", commandOverride: (_, args, _) =>
        { command = args; return Task.FromResult(new ToolResult(0, "", "")); });
        var paths = new AppPaths(Path.Combine(root, "home"));
        await new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths)).ChangePackageAsync(project,
            new("example.tool", null, requested, requested, ChangeKind.Update), null, default);
        Assert.Equal([action, "example.tool", requested, "-p", project, "-y", "--prerelease"], command!);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task EmptyCreationSettlesWhileActualArchivesUseTheImportQueue(bool emptyProject, bool extra)
    {
        Directory.CreateDirectory(root);
        var editor = Path.Combine(root, "Unity.exe"); await File.WriteAllTextAsync(editor, "fixture, never executed");
        var archive = Path.Combine(root, "Avatar.unitypackage"); await File.WriteAllTextAsync(archive, "fixture, never imported");
        var project = Path.Combine(root, "Project");
        string[]? command = null;
        var runner = new ToolRunner(commandOverride: async (_, args, _) =>
        {
            command = args;
            var log = args[Array.IndexOf(args, "-logFile") + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            await File.WriteAllTextAsync(log, "[vrc-setup] Post-import settle complete, quitting.");
            return new ToolResult(0, "", "");
        });
        var paths = new AppPaths(Path.Combine(root, "home"));
        var driver = new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths));
        var plan = new ProjectPlan
        {
            Path = project, Name = "Fixture", ExpectedFingerprint = "missing", DesiredPolicy = new Dictionary<string, string>(), Changes = [],
            UnityEditorPath = editor,
            Creation = new(emptyProject ? "" : archive, "Fixture", project, editor, "StandaloneWindows64", ["StandaloneWindows64"], extra ? [archive] : [], emptyProject)
        };
        await driver.ImportAsync(plan, null, CancellationToken.None);
        if (emptyProject && !extra)
        {
            Assert.Null(command);
            Assert.False(Directory.Exists(Path.Combine(project, "Assets", "Editor")));
            return;
        }
        var method = command![Array.IndexOf(command, "-executeMethod") + 1];
        Assert.Equal("VrcSetupPostImport.RunImport", method);
        Assert.DoesNotContain("-quit", command);
        Assert.Equal(!(emptyProject && !extra), command.Contains("-vrcsetupImportPlan"));
        Assert.True(File.Exists(Path.Combine(project, "Assets", "Editor", "VrcSetupPostImport.cs")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(project, ".vrcsetup", "import-plan.json")));
        Assert.Equal(emptyProject && !extra ? 0 : 1, json.RootElement.GetProperty("packages").GetArrayLength());
    }

    [Fact] public async Task FailedEmptyProjectCanResumeWithoutAnArchiveAndKeepsExistingAssets()
    {
        var project = Path.Combine(root, "Existing");
        Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "Packages"));
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "manifest.json"), "{\"dependencies\":{}}");
        var asset = Path.Combine(project, "Assets", "keep.txt"); await File.WriteAllTextAsync(asset, "user content");
        await AtomicFile.WriteJsonAsync(Path.Combine(project, ".vrcsetup", "state.json"), new SetupState { ProjectName = "Existing", UnityPackagePath = "", Completed = false });
        var app = new VrcSetupApplication(Path.Combine(root, "home"));
        var source = new CreationRequest("", "Existing", project, "Unity.exe", "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true);
        var plan = await app.PlanResumeAsync(source, new Dictionary<string, string>(), new AppConfiguration(), null, []);
        Assert.NotNull(plan.RecoverySource); Assert.Empty(plan.ExtraImports); Assert.Null(plan.Creation);
        Assert.Equal("user content", await File.ReadAllTextAsync(asset));
        await Assert.ThrowsAsync<IOException>(() => app.PlanResumeAsync(source with { Name = "Other" }, new Dictionary<string, string>(), new AppConfiguration(), null, []));
        var inspector = new ImportDestinationInspector(app.Projects);
        await AtomicFile.WriteJsonAsync(Path.Combine(project, ".vrcsetup", "state.json"), new SetupState { ProjectName = "Existing", UnityPackagePath = Path.Combine(root, "other.unitypackage") });
        Assert.Equal(ImportDestinationKind.DifferentSource, (await inspector.InspectAsync(project, "", emptyProject: true)).Kind);
    }

    [Theory] [InlineData("folder")] [InlineData("renamed")] [InlineData("empty")]
    public async Task ImportPreflightRejectsFoldersRenamedFilesAndEmptyArchives(string kind)
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "Invalid.unitypackage");
        if (kind == "folder") Directory.CreateDirectory(path);
        else if (kind == "renamed") await File.WriteAllTextAsync(path, "This is not a Unity archive");
        else
        {
            using var file = File.Create(path);
            using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
        }
        await Assert.ThrowsAsync<IOException>(() => UnityPackageFormat.ValidateAsync(path));
    }
    [Fact] public async Task ImportPreflightAcceptsARealUnityPackageContainer()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "Valid.unitypackage");
        using (var file = File.Create(path))
        using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
        using (var tar = new System.Formats.Tar.TarWriter(gzip))
        using (var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Assets/Marker.txt")))
            tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, new string('a', 32) + "/pathname") { DataStream = data });
        await UnityPackageFormat.ValidateAsync(path);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
