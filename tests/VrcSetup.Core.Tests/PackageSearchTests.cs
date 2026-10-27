using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageSearchTests
{
    [Theory]
    [InlineData("limb", "com.nanochip.avatarlimbscaling", "Avatar Limb Scaling", true)]
    [InlineData("audio link", "com.llealloo.audiolink", "AudioLink", true)]
    [InlineData("AUDIO LINK", "com.llealloo.audiolink", "AudioLink", true)]
    [InlineData("llealloo audio", "com.llealloo.audiolink", "AudioLink", true)]
    [InlineData("limb missing", "com.nanochip.avatarlimbscaling", "Avatar Limb Scaling", false)]
    [InlineData("  ", "example.tool", "Tool", true)]
    public void MatchesWordsAcrossNameAndId(string query, string id, string name, bool expected) =>
        Assert.Equal(expected, PackageSearch.Matches(query, id, name));

    [Fact] public void ProjectSearchFindsIncludedPackagesWithMultipleWords()
    {
        var project = new ProjectSnapshot { Path = Path.GetFullPath("fixture"), Name = "Fixture",
            Packages = new() { ["com.llealloo.audiolink"] = "2.0.0" }, InstalledVersions = new() { ["com.llealloo.audiolink"] = "2.0.0" } };
        var session = new PackageEditSession([project], new(), [new("com.llealloo.audiolink", "AudioLink", "2.0.0")]);
        var row = Assert.Single(session.Rows("audio link"));
        Assert.True(row.Included); Assert.Equal("com.llealloo.audiolink", row.Id);
    }
}
