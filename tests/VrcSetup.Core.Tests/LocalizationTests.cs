using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("system", "it-IT", "it")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("en", "it-IT", "en")]
    [InlineData("it", "en-US", "it")]
    public void SystemLanguageAndManualOverride(string choice, string system, string expected) => Assert.Equal(expected, UiText.Resolve(choice, system));
    [Fact] public void LabelsTranslateButPackageNamesAndVersionsRemainUnchanged()
    {
        Assert.Equal("Progetti", UiText.Text("Projects", "it"));
        Assert.Equal("Projects", UiText.Text("Projects", "en"));
        Assert.Equal("com.vrcfury.vrcfury", UiText.Text("com.vrcfury.vrcfury", "it"));
        Assert.Equal("3.10.5", UiText.Text("3.10.5", "it"));
        Assert.Equal("Gestisci 4 progetti", UiText.Text("Manage 4 projects", "it"));
    }
}
