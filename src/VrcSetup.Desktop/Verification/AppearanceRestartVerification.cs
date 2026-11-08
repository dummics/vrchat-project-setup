using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VrcSetup.Infrastructure;
using Windows.UI;

namespace VrcSetup_Desktop.Verification;

// Two separate, offscreen processes share only this fixture's configuration.
internal static class AppearanceRestartVerification
{
    public static async Task RunAsync(MainWindow window, string output, string stage)
    {
        var page = window.Page;
        await page.Initialized;
        var vm = page.ViewModel;
        if (stage == "save")
        {
            vm.CurrentPage = "Settings";
            await Task.Delay(100);
            var editor = (AppearanceEditor)page.FindName("AppearanceEditor");
            ((ColorPicker)editor.FindName("CanvasPicker")).Color = Color.FromArgb(255, 23, 33, 43);
            ((ColorPicker)editor.FindName("SurfacePicker")).Color = Color.FromArgb(255, 38, 53, 66);
            ((ColorPicker)editor.FindName("AccentPicker")).Color = Color.FromArgb(255, 104, 202, 182);
            vm.Theme = "Dark";
            await vm.SaveSettingsCommand.ExecuteAsync(null);
            var saved = await vm.Application.Configuration.LoadAsync();
            if (vm.ErrorMessage.Length != 0 || saved.ColorPalette != "Custom" || saved.CanvasColor != "#17212B" || saved.SurfaceColor != "#263542" || saved.AccentColor != "#68CAB6" || saved.Theme != "Dark")
                throw new IOException("Custom appearance was not saved through Settings.");
        }
        else if (stage == "verify")
        {
            if (vm.ColorPalette != "Custom" || vm.CanvasColor != "#17212B" || vm.SurfaceColor != "#263542" || vm.AccentColor != "#68CAB6" || vm.Theme != "Dark")
                throw new IOException("The saved appearance did not survive restarting the process.");
            var resources = Application.Current.Resources.MergedDictionaries.Single(x => x.Source?.OriginalString.Contains("DesignResources.xaml", StringComparison.OrdinalIgnoreCase) == true);
            var dark = (ResourceDictionary)resources.ThemeDictionaries["Default"];
            if (((SolidColorBrush)dark["AppCanvasBrush"]).Color != Color.FromArgb(255, 23, 33, 43) || vm.ErrorMessage.Length != 0)
                throw new IOException("The saved palette was not applied at startup.");
            vm.CurrentPage = "Settings";
            await Task.Delay(100);
            var editor = (AppearanceEditor)page.FindName("AppearanceEditor");
            if (((ColorPicker)editor.FindName("AccentPicker")).Color != Color.FromArgb(255, 104, 202, 182) || vm.ErrorMessage.Length != 0)
                throw new IOException("The appearance editor did not restore the saved color.");
        }
        else throw new ArgumentException("Expected save or verify.");
        await AtomicFile.WriteJsonAsync(Path.Combine(output, "appearance-" + stage + ".json"), new { Passed = true, Stage = stage, ProcessId = Environment.ProcessId });
    }
}
