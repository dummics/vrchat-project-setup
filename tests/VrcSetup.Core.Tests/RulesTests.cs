using System.Text.Json;
using VrcSetup.Cli;
using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class RulesTests
{
    [Fact] public void SavedUnityPackageNameIsSuggestedOnlyWhenEnabled()
    {
        var package = Path.GetFullPath("Avatar.unitypackage"); var config = new AppConfiguration();
        config.SavedProjectNames[package] = "My creator name";
        Assert.Equal("My creator name", ProjectRules.SuggestName(package, config));
        config.Naming.RememberUnityPackageNames = false;
        Assert.Equal("Avatar", ProjectRules.SuggestName(package, config));
    }
    [Theory]
    [InlineData("CON")][InlineData("CON.avatar")][InlineData("NUL")][InlineData("COM1")][InlineData("..")][InlineData("folder/name")][InlineData("file:")][InlineData("ends.")][InlineData("ends ")][InlineData("")]
    public void InvalidWindowsNameIsRejected(string name) => Assert.Throws<ArgumentException>(() => ProjectRules.ValidateName(name));
    [Theory][InlineData("Avatar [PC] & café")][InlineData("My Quest avatar")][InlineData("com10")]
    public void OrdinarySpecialCharactersAreAllowed(string name) => ProjectRules.ValidateName(name);
    [Theory][InlineData("Conquest.unitypackage", "StandaloneWindows64")][InlineData("Avatar_Quest.unitypackage", "Android")][InlineData("Quest 3", "Android")][InlineData("Android.package", "Android")]
    public void PlatformSuggestionMatchesWholeWord(string name, string expected) => Assert.Equal(expected, ProjectRules.SuggestBuildTarget(name));
    [Fact] public void SemanticVersionOrderingIncludesNumericPrerelease()
    {
        Assert.Equal(["1.10.0", "1.9.0", "1.9.0-beta.10", "1.9.0-beta.2", "1.9.0-alpha", "0.1.29", "0.1.9"], SemanticVersion.Sort(["0.1.9", "1.9.0-beta.2", "1.9.0-alpha", "1.10.0", "0.1.29", "1.9.0-beta.10", "1.9.0"]));
    }
    [Fact] public void StableListExcludesPrerelease() => Assert.Equal(["1.0.0"], SemanticVersion.Sort(["1.0.0-beta", "1.0.0"], false));
    [Fact] public void WorldFoundationDoesNotPromoteAvatarSdk()
    {
        var required = ProjectRules.Foundation(new Dictionary<string, string> { [ProjectRules.WorldSdk] = "3.10.5" }, new());
        Assert.Contains(ProjectRules.BaseSdk, required); Assert.Contains(ProjectRules.Resolver, required);
        Assert.DoesNotContain(ProjectRules.WorldSdk, required); Assert.DoesNotContain(ProjectRules.AvatarSdk, required);
    }
    [Fact] public void OverlayKeepsCustomAndExactDoesNot()
    {
        Dictionary<string, string> current = new() { ["custom"] = "1.0.0", ["tool"] = "1.0.0" };
        Dictionary<string, string> selected = new() { ["tool"] = "2.0.0" };
        Assert.Equal("1.0.0", ProjectRules.Merge(current, selected)["custom"]);
        Assert.DoesNotContain("custom", ProjectRules.Merge(current, selected, true).Keys);
        Assert.Equal("1.0.0", current["tool"]);
    }
    [Theory]
    [InlineData("{\"VpmPackages\":[\"tool\"]}", "latest")]
    [InlineData("{\"VpmPackages\":{\"tool\":\"1.0.0\"}}", "1.0.0")]
    [InlineData("{\"VpmPackages\":{\"tool\":{\"version\":\"1.0.0\"}}}", "1.0.0")]
    public void LegacyConfigurationsRemainReadable(string json, string version) => Assert.Equal(version, JsonSerializer.Deserialize<AppConfiguration>(json, JsonDefaults.Options)!.VpmPackages["tool"]);
    [Fact] public void ClonePreservesRevisionAndSeparatesWorkingSet()
    {
        var config = new AppConfiguration { Revision = "one", VpmPackages = new() { ["tool"] = "1.0.0" } };
        var copy = config.Copy(); copy.VpmPackages["tool"] = "2.0.0";
        Assert.Equal("one", copy.Revision); Assert.Equal("1.0.0", config.VpmPackages["tool"]);
    }
    [Theory][InlineData("-Json")][InlineData("--json")][InlineData("-JSON")]
    public void PowerShellFlagSpellingRemainsCompatible(string flag) => Assert.True(new CommandLine(["projects", flag]).Has("json"));
    [Fact] public void PositionalPathsAndArrayOptionsKeepArgumentBoundaries()
    {
        var command = new CommandLine(["create", "C:\\Avatar [PC] & café.unitypackage", "-Name", "My Avatar", "-Package", "tool@1.0.0", "other@latest", "-DryRun"]);
        Assert.Equal("My Avatar", command.Value("name")); Assert.Equal(2, command.All("package").Length); Assert.True(command.DryRun);
        Assert.Equal("C:\\Avatar [PC] & café.unitypackage", command.Positionals[1]);
    }
    [Theory][InlineData("[ERR] Could not get match for tool")][InlineData("[ERR] Failed to resolve tool")]
    public void ExitZeroDoesNotHideResolverFailure(string output) => Assert.True(ProjectRules.ToolFailed(new(0, output, ""), "tool"));
    [Fact] public void ConfirmedAddDoesNotHideUnrelatedErrors()
    {
        Assert.True(ProjectRules.ToolFailed(new(0, "Added package tool\n[ERR] Could not get match for dependency", ""), "tool"));
        Assert.False(ProjectRules.ToolFailed(new(0, "Added package tool\n[ERR] Failed to remove package legacy", ""), "tool"));
    }
    [Fact] public void EditorRetainsMixedVersionsUntilAnExplicitChoice()
    {
        var project1 = new ProjectSnapshot { Name = "PC", Path = Path.GetFullPath("PC"), Packages = new() { ["tool"] = "1.0.0" }, InstalledVersions = new() { ["tool"] = "1.0.0" } };
        var project2 = project1 with { Name = "Quest", Path = Path.GetFullPath("Quest"), Packages = new() { ["tool"] = "2.0.0" }, InstalledVersions = new() { ["tool"] = "2.0.0" } };
        var session = new PackageEditSession([project1, project2], new(), []);
        Assert.Empty(session.Selected); Assert.Equal("Mixed", session.Rows().Single(x => x.Id == "tool").InstalledVersion);
        session.SetVersion("tool", "latest"); session.SetTargets("tool", [project2.Path]);
        Assert.Equal([project2.Path], session.Targets["tool"]); session.Reset(); Assert.Empty(session.Selected);
    }
    [Fact] public void RequiredPackagesCannotBeRemovedInEditor()
    {
        var project = new ProjectSnapshot { Path = Path.GetFullPath("avatar"), Name = "avatar", Packages = new() { [ProjectRules.BaseSdk] = "3.10.5" } };
        var session = new PackageEditSession([project], new(), []);
        Assert.Throws<InvalidOperationException>(() => session.Toggle(ProjectRules.BaseSdk));
    }
    [Fact] public void AddingANewSdkDoesNotLoseTheSelectedComponent()
    {
        var project = new ProjectSnapshot { Path = Path.GetFullPath("avatar"), Name = "avatar", Packages = new() { [ProjectRules.BaseSdk] = "3.10.5", [ProjectRules.AvatarSdk] = "3.10.5" } };
        var session = new PackageEditSession([project], new(), []); session.SetVersion(ProjectRules.WorldSdk, "3.10.6");
        Assert.Equal("3.10.6", session.Selected[ProjectRules.WorldSdk]);
    }
}
