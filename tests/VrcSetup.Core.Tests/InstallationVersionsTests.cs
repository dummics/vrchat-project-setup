using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class InstallationVersionsTests
{
    [Theory]
    [InlineData(null, "0.2.0-beta.16", InstallationChange.New)]
    [InlineData("0.2.0-beta.15", "0.2.0-beta.16", InstallationChange.Update)]
    [InlineData("0.2.0-beta.16", "0.2.0-beta.16", InstallationChange.Reinstall)]
    [InlineData("0.2.0-beta.17", "0.2.0-beta.16", InstallationChange.Downgrade)]
    [InlineData("0.2.0", "0.2.0-beta.16", InstallationChange.Downgrade)]
    [InlineData("0.2.0-beta.16", "0.2.0", InstallationChange.Update)]
    [InlineData("1.0.0", "0.2.0-beta.16", InstallationChange.Downgrade)]
    [InlineData("2.0.0-preview.13", "0.2.0-beta.16", InstallationChange.Update)]
    [InlineData("0.4.0+build.99", "0.4.0+build.100", InstallationChange.Update)]
    [InlineData("0.4.0+build.100", "0.4.0+build.99", InstallationChange.Downgrade)]
    [InlineData("0.4.0+build.100", "0.4.0+build.100", InstallationChange.Reinstall)]
    [InlineData("0.4.0-alpha.3+build.500", "0.4.0", InstallationChange.Update)]
    public void ManualInstallDistinguishesReinstallDowngradeAndLegacyMigration(string? current, string next, InstallationChange expected)
        => Assert.Equal(expected, InstallationVersions.Compare(current, next));

    [Theory]
    [InlineData("0.4.0+build.120.sha", "0.4.0+build.120")]
    [InlineData("0.4.0-alpha.2+build.120", "0.4.0-alpha.2+build.120")]
    [InlineData("0.3.7+001122", "0.3.7")]
    public void DisplayIdentityPreservesBuildAndRemovesCommitMetadata(string value, string expected)
        => Assert.Equal(expected, ApplicationBuild.FromAssemblyVersion(value));
}
