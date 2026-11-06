using VrcSetup.Core;
using System.Text.Json;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PartialInitializationTests : IDisposable
{
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
