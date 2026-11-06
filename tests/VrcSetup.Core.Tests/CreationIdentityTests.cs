using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class CreationIdentityTests
{
    [Fact] public void WindowsEmptyDirectoryRemovalChecksIdentityAndPreservesUnexpectedContents()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-empty-remove-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        try
        {
            var identity = ProjectDirectoryIdentity.TryRead(target)!;
            Assert.NotNull(identity);
            Assert.ThrowsAny<IOException>(() => ProjectDirectoryIdentity.RemoveEmptyDirectory(target, identity with { FileIdLow = identity.FileIdLow ^ 1 }));
            Assert.True(Directory.Exists(target));
            var file = Path.Combine(target, "preserve.txt"); File.WriteAllText(file, "owner data");
            Assert.ThrowsAny<IOException>(() => ProjectDirectoryIdentity.RemoveEmptyDirectory(target, identity));
            Assert.Equal("owner data", File.ReadAllText(file));
            File.Delete(file);
            ProjectDirectoryIdentity.RemoveEmptyDirectory(target, identity);
            Assert.False(Directory.Exists(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void WindowsPathLeasePreventsParentReplacementAndReleasesHandles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-path-lease-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "parent"); var target = Path.Combine(parent, "project");
        Directory.CreateDirectory(target);
        try
        {
            var identity = ProjectDirectoryIdentity.TryRead(target)!;
            Assert.NotNull(identity);
            using (ProjectDirectoryIdentity.HoldPath(target, identity))
            {
                Assert.ThrowsAny<IOException>(() => Directory.Move(parent, parent + "-moved"));
                Assert.ThrowsAny<IOException>(() => Directory.Move(target, target + "-moved"));
            }
            Assert.ThrowsAny<IOException>(() => ProjectDirectoryIdentity.HoldPath(target, identity with { FileIdLow = identity.FileIdLow ^ 1 }));
            Directory.Move(parent, parent + "-moved");
            Assert.True(Directory.Exists(Path.Combine(parent + "-moved", "project")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void ReceiptRequiresMatchingRequestTargetAndPhysicalIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), "receipt-fixture");
        var request = new CreationRequest("avatar.unitypackage", "Avatar", path, "Unity.exe", "StandaloneWindows64", [], [])
            { CreationId = Guid.NewGuid().ToString("N") };
        var identity = new ProjectDirectoryIdentity(1, 2, 3);
        var state = new SetupState { CreationId = request.CreationId, ProjectName = request.Name,
            CreatedTargetPath = path, CreatedDirectoryIdentity = identity };
        Assert.True(ProjectCreationReceipt.Matches(request, state, identity));
        Assert.False(ProjectCreationReceipt.Matches(request, state, null));
        Assert.False(ProjectCreationReceipt.Matches(request, state, identity with { FileIdLow = 4 }));
        Assert.False(ProjectCreationReceipt.Matches(request with { TargetPath = path + "-other" }, state, identity));
        Assert.False(ProjectCreationReceipt.Matches(request with { CreationId = null }, state, identity));
    }

    [Fact] public void WindowsDirectoryIdentitySurvivesRenameButRejectsReplacement()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "vrcsetup-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stage = Path.Combine(root, "stage"); var target = Path.Combine(root, "target");
            Directory.CreateDirectory(stage);
            var identity = ProjectDirectoryIdentity.TryRead(stage);
            Assert.NotNull(identity);
            Directory.Move(stage, target);
            Assert.Equal(identity, ProjectDirectoryIdentity.TryRead(target));
            Directory.CreateDirectory(stage);
            Assert.NotEqual(identity, ProjectDirectoryIdentity.TryRead(stage));
            var file = Path.Combine(root, "file"); File.WriteAllText(file, "fixture");
            Assert.Null(ProjectDirectoryIdentity.TryRead(file));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void LegacyRequestsAndStateDoNotAcquireCreationIdentityOnRead()
    {
        var request = new CreationRequest("avatar.unitypackage", "Avatar", "project", "Unity.exe", "StandaloneWindows64", [], []);
        var serialized = JsonSerializer.Serialize(request, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<CreationRequest>(serialized, JsonDefaults.Options)!;
        var legacy = JsonSerializer.Deserialize<SetupState>("{\"ProjectName\":\"Avatar\",\"SchemaVersion\":2}", JsonDefaults.Options)!;
        Assert.Null(restored.CreationId);
        Assert.Null(legacy.CreationId);
        Assert.Null(legacy.CreatedTargetPath);
    }

    [Fact] public void JournalRoundtripAndRecoveryEditsPreserveCreationIdentity()
    {
        var source = new CreationRequest("avatar.unitypackage", "Avatar", "project", "Unity.exe", "StandaloneWindows64", [], [])
            { CreationId = Guid.NewGuid().ToString("N") };
        var project = new ProjectPlan { Name = source.Name, Path = source.TargetPath, ExpectedFingerprint = "", DesiredPolicy = [], Changes = [], Creation = source };
        var job = new ImportJob("job", new("plan", DateTime.UtcNow, [project]), source, "Paused", "Paused", 10, "", "");
        var restored = JsonSerializer.Deserialize<ImportJob>(JsonSerializer.Serialize(job, JsonDefaults.Options), JsonDefaults.Options)!;
        Assert.Equal(source.CreationId, restored.Source.CreationId);
        Assert.Equal(source.CreationId, restored.Plan.Projects.Single().Creation!.CreationId);
        Assert.Equal(source.CreationId, (restored.Source with { UnityPackagePath = "relocated.unitypackage", UnityEditorPath = "new-editor" }).CreationId);
    }
}
