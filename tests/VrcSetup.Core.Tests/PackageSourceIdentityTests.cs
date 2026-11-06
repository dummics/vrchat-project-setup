using VrcSetup.Infrastructure;
using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageSourceIdentityTests
{
    [Fact] public async Task ResumeRejectsDifferentSourceBeforeInspectingOrChangingProject()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vrcsetup-source-resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(directory, "original.unitypackage");
            var replacement = Path.Combine(directory, "replacement.unitypackage");
            await File.WriteAllTextAsync(original, "original"); await File.WriteAllTextAsync(replacement, "replaced");
            var target = Path.Combine(directory, "not-created");
            var source = new CreationRequest(replacement, "Avatar", target, "", "StandaloneWindows64", [], [])
                { SourceIdentity = await PackageSourceIdentity.ReadAsync(original) };
            var app = new VrcSetupApplication(Path.Combine(directory, "home"), ["--no-vcc-registration"]);
            var error = await Assert.ThrowsAsync<IOException>(() => app.PlanResumeAsync(source, new Dictionary<string, string>(), new AppConfiguration(), null, []));
            Assert.Contains("differs from the original", error.Message);
            Assert.False(Directory.Exists(target));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public async Task IdentityFollowsContentsAcrossRenameAndRejectsSameLengthReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vrcsetup-source-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "original.unitypackage");
            var moved = Path.Combine(directory, "moved.unitypackage");
            await File.WriteAllTextAsync(first, "original");
            var identity = await PackageSourceIdentity.ReadAsync(first);
            File.Move(first, moved);
            Assert.Equal(identity, await PackageSourceIdentity.ReadAsync(moved));
            await using (await PackageSourceIdentity.OpenVerifiedAsync(moved, identity))
            {
                if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.WriteAllText(moved, "replaced"));
            }
            await File.WriteAllTextAsync(moved, "replaced");
            var replacement = await PackageSourceIdentity.ReadAsync(moved);
            Assert.Equal(identity.Length, replacement.Length);
            Assert.NotEqual(identity.Sha256, replacement.Sha256);
            await Assert.ThrowsAsync<IOException>(() => PackageSourceIdentity.OpenVerifiedAsync(moved, identity));
        }
        finally { Directory.Delete(directory, true); }
    }
}
