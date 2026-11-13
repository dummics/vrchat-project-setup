using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("Importing assets", "Importazione dei contenuti")]
    [InlineData("Open in Unity", "Apri in Unity")]
    [InlineData("Create a new copy", "Crea una nuova copia")]
    [InlineData("Script errors in Unity", "Errori negli script Unity")]
    public void ImportActionsHaveItalianLabels(string key, string translated) => Assert.Equal(translated, UiText.Text(key, "it"));

    [Fact] public void VpmResolverErrorsExplainNextActionWithoutExposingToolOutput()
    {
        var error = new IOException("VPM resolve failed. [ERR] raw internal tool diagnostic");
        var message = UserErrors.Message(error);
        Assert.Equal(UiText.Text("The packages could not be prepared. Check their versions and sources in Packages, then continue. Your project is kept."), message);
        Assert.DoesNotContain("[ERR]", message);
        Assert.True(UserErrors.IsExpected(error));
    }
    [Theory]
    [InlineData("system", "it-IT", "it")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("en", "it-IT", "en")]
    [InlineData("it", "en-US", "it")]
    public void SystemLanguageAndManualOverride(string choice, string system, string expected) => Assert.Equal(expected, UiText.Resolve(choice, system));
    [Fact] public void LabelsTranslateButPackageNamesAndVersionsRemainUnchanged()
    {
        Assert.Equal("Progetti", UiText.Text("Projects", "it"));
        Assert.Equal("Verifica pacchetti…", UiText.Text("Checking packages…", "it"));
        Assert.Equal("Gestisci pacchetti", UiText.Text("Manage packages", "it"));
        Assert.Equal("Verifica risorse…", UiText.Text("Checking resources…", "it"));
        Assert.Equal("Opzioni di importazione", UiText.Text("Import options", "it"));
        Assert.Equal("Avanzamento della fase di importazione", UiText.Text("Import step progress", "it"));
        Assert.Equal("Rimuovi il file selezionato", UiText.Text("Remove selected file", "it"));
        Assert.Equal("Projects", UiText.Text("Projects", "en"));
        Assert.Equal("com.vrcfury.vrcfury", UiText.Text("com.vrcfury.vrcfury", "it"));
        Assert.Equal("3.10.5", UiText.Text("3.10.5", "it"));
        Assert.Equal("Gestisci 4 progetti", UiText.Text("Manage 4 projects", "it"));
    }
}
