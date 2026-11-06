using VrcSetup.Core;
using System.Text.Json;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PartialInitializationTests : IDisposable
{
    [Fact] public async Task CreationPersistsRecoveryInputsOutsideImportJournal()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "created");
        var editor = Path.Combine(root, "Unity.exe");
        File.Copy(typeof(VrcSetup.TestFixtures.EditorMetadataMarker).Assembly.Location, editor);
        var source = new CreationRequest(Path.Combine(root, "source.unitypackage"), "Avatar", target, editor,
            "StandaloneWindows64", ["StandaloneWindows64"], ["extra.unitypackage"])
        { CreationId = Guid.NewGuid().ToString("N"), SourceIdentity = new PackageIdentity(42, new string('A', 64)), RecoveryPolicy = new() { ["com.example.test"] = "1.2.3" } };
        var paths = new AppPaths(Path.Combine(root, "home"));
        var runner = new ToolRunner(commandOverride: (_, _, _) =>
        {
            Directory.CreateDirectory(Path.Combine(target, "Assets"));
            Directory.CreateDirectory(Path.Combine(target, "Packages"));
            return Task.FromResult(new ToolResult(0, "", ""));
        });
        await new UnityProjectDriver(paths, runner, new ProjectWorkspace(paths)).CreateAsync(source, null, default);
        var saved = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(target, ".vrcsetup", "state.json"));
        Assert.NotNull(saved?.CreationRequest);
        Assert.True(source.MatchesInputs(saved.CreationRequest));
        Assert.True(ProjectCreationReceipt.Matches(saved.CreationRequest, saved, ProjectDirectoryIdentity.TryRead(target)));
        Assert.False(File.Exists(Path.Combine(paths.Home, "imports.json")));
        var recovered = await OrphanImportRecovery.ReadAsync(new ProjectWorkspace(paths), target);
        Assert.NotNull(recovered);
        Assert.True(source.MatchesInputs(recovered));
        saved.CreationRequest = saved.CreationRequest with { SourceIdentity = null };
        await AtomicFile.WriteJsonAsync(Path.Combine(target, ".vrcsetup", "state.json"), saved);
        Assert.Null(await OrphanImportRecovery.ReadAsync(new ProjectWorkspace(paths), target));
        saved.CreationRequest = source;
        saved.CreationRequest = saved.CreationRequest with { CreationId = Guid.NewGuid().ToString("N") };
        await AtomicFile.WriteJsonAsync(Path.Combine(target, ".vrcsetup", "state.json"), saved);
        await Assert.ThrowsAsync<IOException>(() => OrphanImportRecovery.ReadAsync(new ProjectWorkspace(paths), target));
    }
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-partial-init-" + Guid.NewGuid().ToString("N"));

    [Theory, InlineData(true), InlineData(false)]
    public async Task ReceiptOrReviewedFileMismatchPreservesPartialProject(bool wrongReceipt)
    {
        if (!OperatingSystem.IsWindows()) return;
        var paths = new AppPaths(Path.Combine(root, "home"));
        var target = Path.Combine(root, "project");
        Directory.CreateDirectory(Path.Combine(target, "Packages"));
        var manifest = Path.Combine(target, "Packages", "manifest.json");
        const string contents = "{\"dependencies\":{\"example.package\":\"1.2.3\"}}";
        await File.WriteAllTextAsync(manifest, contents);
        var source = new CreationRequest("avatar.unitypackage", "Avatar", target, "", "StandaloneWindows64", [], [])
            { CreationId = Guid.NewGuid().ToString("N") };
        var identity = ProjectDirectoryIdentity.TryRead(target)!;
        Assert.NotNull(identity);
        await AtomicFile.WriteJsonAsync(Path.Combine(target, ".vrcsetup", "state.json"), new SetupState
        { ProjectName = source.Name, CreationId = wrongReceipt ? Guid.NewGuid().ToString("N") : source.CreationId,
            CreatedTargetPath = target, CreatedDirectoryIdentity = identity });
        var plan = new ProjectPlan { Name = source.Name, Path = target, ExpectedFingerprint = ProjectWorkspace.Fingerprint(target), DesiredPolicy = new Dictionary<string, string>(), Changes = [],
            RecoverySource = source, Initialization = new(source, identity.Key, new Dictionary<string, string>
            { ["Packages/manifest.json"] = "changed-after-review", ["ProjectSettings/ProjectVersion.txt"] = "" }) };
        var driver = new UnityProjectDriver(paths, new ToolRunner(commandOverride: (_, _, _) => throw new InvalidOperationException("No process may start.")), new ProjectWorkspace(paths));
        await Assert.ThrowsAnyAsync<IOException>(() => driver.InitializePartialAsync(plan, null, default));
        Assert.Equal(contents, await File.ReadAllTextAsync(manifest));
        Assert.False(Directory.Exists(Path.Combine(target, "ProjectSettings")));
        Assert.False(Directory.Exists(Path.Combine(target, "Assets")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory, InlineData(true), InlineData(false)] public async Task InitializationAddsOnlyMissingScaffoldAndNeverStartsTheEditor(bool existingManifest)
    {
        if (!OperatingSystem.IsWindows()) return;
        var paths = new AppPaths(Path.Combine(root, "home")); var target = Path.Combine(root, "project");
        Directory.CreateDirectory(target);
        var manifest = Path.Combine(target, "Packages", "manifest.json");
        const string policy = "{\"dependencies\":{\"example.pinned\":\"1.2.3\"}}";
        if (existingManifest) { Directory.CreateDirectory(Path.GetDirectoryName(manifest)!); await File.WriteAllTextAsync(manifest, policy); }
        var editor = Path.Combine(root, "Unity.exe");
        File.Copy(typeof(VrcSetup.TestFixtures.EditorMetadataMarker).Assembly.Location, editor);
        var source = new CreationRequest("avatar.unitypackage", "Avatar", target, editor, "StandaloneWindows64", [], []) { CreationId = Guid.NewGuid().ToString("N") };
        var identity = ProjectDirectoryIdentity.TryRead(target)!;
        Assert.NotNull(identity);
        await AtomicFile.WriteJsonAsync(Path.Combine(target, ".vrcsetup", "state.json"), new SetupState
        { ProjectName = source.Name, CreationId = source.CreationId, CreatedTargetPath = target, CreatedDirectoryIdentity = identity });
        var plan = new ProjectPlan { Name = source.Name, Path = target, ExpectedFingerprint = ProjectWorkspace.Fingerprint(target), DesiredPolicy = new Dictionary<string, string>(), Changes = [],
            RecoverySource = source, Initialization = new(source, identity.Key, new Dictionary<string, string>
            { ["Packages/manifest.json"] = existingManifest ? AtomicFile.Hash(manifest) : "", ["ProjectSettings/ProjectVersion.txt"] = "" }) };
        var driver = new UnityProjectDriver(paths, new ToolRunner(commandOverride: (_, _, _) => throw new InvalidOperationException("The editor must never start.")), new ProjectWorkspace(paths));
        plan = JsonSerializer.Deserialize<ProjectPlan>(JsonSerializer.Serialize(plan, JsonDefaults.Options), JsonDefaults.Options)!;
        await driver.InitializePartialAsync(plan, null, default);
        Assert.Equal(existingManifest ? policy : "{\"dependencies\":{}}", await File.ReadAllTextAsync(manifest));
        Assert.Equal("m_EditorVersion: 2022.3.22f1\n", await File.ReadAllTextAsync(Path.Combine(target, "ProjectSettings", "ProjectVersion.txt")));
        Assert.True(Directory.Exists(Path.Combine(target, "Assets")));
        Assert.True((await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(target, ".vrcsetup", "state.json")))!.CreationId == source.CreationId);
    }
}
