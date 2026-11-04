using System.Text.Json;
using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PortablePresetTests
{
    [Fact] public void SharingPreservesVersionChoicesWithoutMachineSettings()
    {
        var original = new PortablePreset("Avatar [PC] & café", new() { [ProjectRules.BaseSdk] = "latest", ["com.llealloo.audiolink"] = "3.0.0-beta.2" });
        var json = JsonSerializer.Serialize(original, JsonDefaults.Options);
        var imported = PortablePreset.Read(json);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal("latest", imported.Packages[ProjectRules.BaseSdk]);
        Assert.Equal("3.0.0-beta.2", imported.Packages["com.llealloo.audiolink"]);
        Assert.DoesNotContain("UnityEditorPath", json); Assert.DoesNotContain("UnityProjectsRoot", json);
    }
    [Fact] public void DuplicateNamesCreateNewPresetsIncludingReservedDefault()
    {
        var preset = new PortablePreset("Default", new() { [ProjectRules.BaseSdk] = "latest" });
        Assert.Equal("Default (3)", preset.AvailableName(["default (2)"]));
        Assert.Equal("Avatar (2)", (preset with { Name = "Avatar" }).AvailableName(["AVATAR"]));
        var longName = new string('a', 80);
        Assert.Equal(80, (preset with { Name = longName }).AvailableName([longName]).Length);
    }
    [Theory]
    [InlineData("../package", "latest")]
    [InlineData("package", "../../evil")]
    [InlineData("package", "1.2.3\nrun")]
    [InlineData("package", "")]
    public void InvalidPackageChoicesAreRejected(string id, string version)
        => Assert.Throws<InvalidDataException>(() => new PortablePreset("Test", new() { [id] = version }).Validate());
    [Fact] public void UnsupportedAndOversizedFilesNeverBecomePresets()
    {
        Assert.Throws<InvalidDataException>(() => PortablePreset.Read(JsonSerializer.Serialize(new AppConfiguration(), JsonDefaults.Options)));
        Assert.Throws<InvalidDataException>(() => new PortablePreset("Test", new() { ["package"] = "latest" }) { Version = 2 }.Validate());
        Assert.Throws<InvalidDataException>(() => PortablePreset.Read(new string(' ', PortablePreset.MaxBytes + 1)));
        Assert.Throws<InvalidDataException>(() => PortablePreset.Read("{\"Name\":\"Test\",\"Packages\":null}"));
    }
    [Fact] public void ErrorsGiveAnActionWithoutLeakingExceptionDetails()
    {
        var text = UserErrors.Message(new HttpRequestException("System.Net sensitive endpoint"));
        Assert.DoesNotContain("System.Net", text); Assert.DoesNotContain("sensitive", text);
        Assert.DoesNotContain("private path", UserErrors.Message(new IOException("private path")));
        Assert.NotEqual(text, UserErrors.Message(new UnauthorizedAccessException("private path")));
    }
    [Fact] public void BusyProjectsGiveTheUserAConcreteRecoveryAction()
    {
        var text = UserErrors.Message(new ProjectsInUseException(["Avatar PC", "Avatar Quest"]));
        Assert.Contains("Avatar PC", text); Assert.Contains("Avatar Quest", text);
        Assert.DoesNotContain("Try again, or contact support", text);
        Assert.DoesNotContain("IOException", text);
    }
}
