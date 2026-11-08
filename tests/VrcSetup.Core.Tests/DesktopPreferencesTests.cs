using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

// Settings-only fixtures do not need a Unity project, catalog or native window.
public sealed class DesktopPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-preferences-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("name")]
    [InlineData("name-reverse")]
    [InlineData("created")]
    [InlineData("created-reverse")]
    [InlineData("modified")]
    [InlineData("modified-reverse")]
    [InlineData("default")]
    [InlineData("recent")]
    public async Task ProjectSortSurvivesSaveAndReopen(string sort)
    {
        var paths = new AppPaths(root);
        var store = new ConfigurationStore(paths);
        var settings = await store.LoadAsync();
        settings.ProjectLibrarySort = sort;
        await store.SaveAsync(settings);
        Assert.Equal(sort, (await new ConfigurationStore(paths).LoadAsync()).ProjectLibrarySort);
    }

    [Theory]
    [InlineData("Custom", "#123456", "#ABCDEF", "#000000", "Light")]
    [InlineData("Ocean", "#202A35", "#2A3848", "#70B9F2", "Dark")]
    [InlineData("Legacy palette", "invalid", "#GGGGGG", "", "System")]
    public async Task AppearanceValuesSurviveReopenWithoutOverwritingUserChoices(string palette, string canvas, string surface, string accent, string theme)
    {
        var paths = new AppPaths(root);
        var store = new ConfigurationStore(paths);
        var settings = await store.LoadAsync();
        settings.ColorPalette = palette;
        settings.CanvasColor = canvas;
        settings.SurfaceColor = surface;
        settings.AccentColor = accent;
        settings.Theme = theme;
        await store.SaveAsync(settings);
        var reopened = await new ConfigurationStore(paths).LoadAsync();
        Assert.Equal((palette, canvas, surface, accent, theme),
            (reopened.ColorPalette, reopened.CanvasColor, reopened.SurfaceColor, reopened.AccentColor, reopened.Theme));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
