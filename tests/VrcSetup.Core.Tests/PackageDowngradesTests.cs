using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageDowngradesTests
{
    [Theory]
    [InlineData("tool", "2.1.0", "1.9.0", VersionChangeRisk.RiskyDowngrade)]
    [InlineData("tool", "2.1.0", "2.0.9", VersionChangeRisk.Downgrade)]
    [InlineData("com.vrchat.avatars", "3.8.0", "3.7.0", VersionChangeRisk.RiskyDowngrade)]
    [InlineData("com.vrchat.base", "3.8.0", "3.7.9", VersionChangeRisk.RiskyDowngrade)]
    [InlineData("tool", "1.0.0", "1.0.0-beta.1", VersionChangeRisk.Downgrade)]
    [InlineData("tool", "1.0.0-beta.1", "1.0.0", VersionChangeRisk.None)]
    [InlineData("tool", "1.0.0", "latest", VersionChangeRisk.Unknown)]
    [InlineData("tool", "Mixed", "1.0.0", VersionChangeRisk.Unknown)]
    public void ClassifiesKnownVersionsWithoutGuessingUnresolvedPolicies(string id, string before, string after, VersionChangeRisk expected)
        => Assert.Equal(expected, PackageDowngrades.Classify(id, before, after));

    [Fact]
    public void BulkConfirmationUsesResolvedVersionOfEachProject()
    {
        ProjectPlan Project(string name, string before, string after) => new()
        {
            Path = name, Name = name, ExpectedFingerprint = "fixture", DesiredPolicy = new Dictionary<string, string>(),
            Changes = [new("tool", before, "latest", after, ChangeKind.Update)]
        };
        Assert.True(PackageDowngrades.RequiresConfirmation(new OperationPlan("test", DateTime.UtcNow, [Project("PC", "1.0.0", "1.1.0"), Project("Quest", "2.0.0", "1.1.0")])));
        Assert.False(PackageDowngrades.RequiresConfirmation(new OperationPlan("test", DateTime.UtcNow, [Project("PC", "1.2.0", "1.1.0")])));
    }

    [Fact]
    public void UnknownInstalledSdkVersionRequiresReviewButFreshInstallDoesNot()
    {
        Assert.True(PackageDowngrades.RequiresConfirmation(new PackageChange(ProjectRules.BaseSdk, "latest", "3.7.0", "3.7.0", ChangeKind.Update)));
        Assert.False(PackageDowngrades.RequiresConfirmation(new PackageChange(ProjectRules.BaseSdk, null, "3.7.0", "3.7.0", ChangeKind.Add)));
    }

    [Fact]
    public void SelectingAvatarSdkAlsoStagesBaseSdkAtTheSameVersion()
    {
        var project = new ProjectSnapshot { Name = "test", Path = Path.GetFullPath("test"), Packages = new() { [ProjectRules.AvatarSdk] = "3.8.0", [ProjectRules.BaseSdk] = "3.8.0" } };
        var session = new PackageEditSession([project], new(), []);
        session.SetVersion(ProjectRules.AvatarSdk, "3.7.0");
        Assert.Equal("3.7.0", session.Selected[ProjectRules.AvatarSdk]);
        Assert.Equal("3.7.0", session.Selected[ProjectRules.BaseSdk]);
    }
}
