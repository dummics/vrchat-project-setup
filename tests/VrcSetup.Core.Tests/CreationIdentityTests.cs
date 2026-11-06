using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class CreationIdentityTests
{
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
