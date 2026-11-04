using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace VrcSetup_Desktop.Verification;

// Uses the smoke fixture's isolated settings. Never changes Windows appearance.
internal static class AppearanceVerification
{
    internal static async Task RunAsync(MainPage page, Action<bool, string> require)
    {
        var previousPage = page.ViewModel.CurrentPage;
        try
        {
            var resources = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries.Single(x => x.Source?.OriginalString.Contains("DesignResources.xaml", StringComparison.OrdinalIgnoreCase) == true);
            var dark = (Microsoft.UI.Xaml.ResourceDictionary)resources.ThemeDictionaries["Default"];
            require(((Microsoft.UI.Xaml.Media.SolidColorBrush)dark["AppCanvasBrush"]).Color == Color.FromArgb(255, 23, 33, 43), "Saved custom palette is applied at startup before opening Settings");
            page.ViewModel.CurrentPage = "Settings";
            await Task.Delay(30);
            await VerifyAsync(page, require);
            // Restore the normal smoke palette before collecting the other screens.
            var editor = (AppearanceEditor)page.FindName("AppearanceEditor");
            ((ComboBox)editor.FindName("PaletteSelector")).SelectedValue = "Mint";
            await Task.Delay(30);
            await page.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
            require(page.ViewModel.ColorPalette == "Mint" && page.ViewModel.ErrorMessage.Length == 0, "Appearance fixture returns to the built-in palette for the remaining screens");
        }
        finally { page.ViewModel.CurrentPage = previousPage; }
    }

    private static async Task VerifyAsync(MainPage page, Action<bool, string> require)
    {
        var vm = page.ViewModel;
        var original = (vm.ColorPalette, vm.CanvasColor, vm.SurfaceColor, vm.AccentColor, vm.Theme);
        var editor = (AppearanceEditor)page.FindName("AppearanceEditor");
        var palette = (ComboBox)editor.FindName("PaletteSelector");
        var canvas = (ColorPicker)editor.FindName("CanvasPicker");
        var surface = (ColorPicker)editor.FindName("SurfacePicker");
        var accent = (ColorPicker)editor.FindName("AccentPicker");
        var resources = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries.Single(x => x.Source?.OriginalString.Contains("DesignResources.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var highContrast = (Microsoft.UI.Xaml.ResourceDictionary)resources.ThemeDictionaries["HighContrast"];
        var protectedKeys = new[] { "AppCanvasBrush", "AppTextBrush", "AccentFillColorDefaultBrush", "TextOnAccentFillColorPrimaryBrush" };
        var contrastColors = protectedKeys.ToDictionary(key => key, key => ((Microsoft.UI.Xaml.Media.SolidColorBrush)highContrast[key]).Color);
        try
        {
            foreach (var theme in new[] { "Dark", "Light", "System" })
            {
                vm.Theme = theme;
                foreach (var name in new[] { "Ocean", "Violet", "Mint", "Ocean" })
                {
                    palette.SelectedValue = name;
                    await Task.Delay(30);
                    require(vm.ColorPalette == name, $"Appearance selects {name} in {theme}");
                    require(vm.AccentColor == (name == "Ocean" ? "#70B9F2" : name == "Violet" ? "#C49AF0" : "#50CDBB"), "Built-in palette updates its accent");
                }
                canvas.Color = Color.FromArgb(255, 240, 220, 200);
                surface.Color = Color.FromArgb(255, 12, 34, 56);
                accent.Color = Color.FromArgb(255, 0, 0, 0);
                require(vm.ColorPalette == "Custom" && vm.CanvasColor == "#F0DCC8" && vm.SurfaceColor == "#0C2238" && vm.AccentColor == "#000000", "Color pickers update custom settings without feedback corruption");
                accent.Color = Color.FromArgb(255, 255, 255, 255);
                await Task.Delay(30);
                require(vm.AccentColor == "#FFFFFF", "Repeated extreme accent changes remain usable");
                require(vm.ErrorMessage.Length == 0, "Appearance updates do not hide a failed resource update behind error recovery");
            }
            vm.CanvasColor = "invalid";
            vm.SurfaceColor = "#GGGGGG";
            vm.AccentColor = "";
            await Task.Delay(30);
            require(canvas.Color == Color.FromArgb(255, 37, 39, 42) && surface.Color == Color.FromArgb(255, 48, 51, 56) && accent.Color == Color.FromArgb(255, 80, 205, 187), "Invalid appearance values render with safe defaults");
            require(vm.CanvasColor == "invalid" && vm.SurfaceColor == "#GGGGGG" && vm.AccentColor == "", "Fallback preview does not overwrite settings through picker events");
            require(vm.ErrorMessage.Length == 0, "Invalid appearance values do not cause a preview error");
            require(protectedKeys.All(key => ((Microsoft.UI.Xaml.Media.SolidColorBrush)highContrast[key]).Color == contrastColors[key]), "Custom palettes leave the system contrast resources unchanged");
            vm.ColorPalette = "Legacy palette";
            vm.CanvasColor = null!;
            vm.SurfaceColor = null!;
            vm.AccentColor = null!;
            await Task.Delay(30);
            require(canvas.Color == Color.FromArgb(255, 37, 39, 42) && surface.Color == Color.FromArgb(255, 48, 51, 56) && accent.Color == Color.FromArgb(255, 80, 205, 187), "Null legacy color values use the same safe preview defaults");
            require(vm.ErrorMessage.Length == 0 && vm.CanvasColor is null && vm.SurfaceColor is null && vm.AccentColor is null, "Legacy fallback leaves stored user choices intact without an error");
        }
        finally { vm.DiscardSettingsCommand.Execute(null); }
        require((vm.ColorPalette, vm.CanvasColor, vm.SurfaceColor, vm.AccentColor, vm.Theme) == original, "Discard restores saved appearance atomically");
        require(!vm.IsLoadingSettings && !vm.SettingsDirty, "Appearance discard leaves settings ready for further edits");
        var scanned = false;
        void ObserveScan(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.IsLoadingProjects) && vm.IsLoadingProjects) scanned = true;
        }
        vm.PropertyChanged += ObserveScan;
        try
        {
            vm.ColorPalette = "Custom";
            vm.AccentColor = "#123456";
            await vm.SaveSettingsCommand.ExecuteAsync(null);
            var saved = await vm.Application.Configuration.LoadAsync();
            require(saved.AccentColor == "#123456" && saved.ColorPalette == "Custom" && !vm.SettingsDirty, "Custom appearance is persisted by the normal save action");
            require(!scanned && vm.ErrorMessage.Length == 0, "Saving appearance avoids project scanning and completes without errors");
        }
        finally
        {
            vm.PropertyChanged -= ObserveScan;
            vm.ColorPalette = original.ColorPalette;
            vm.CanvasColor = original.CanvasColor;
            vm.SurfaceColor = original.SurfaceColor;
            vm.AccentColor = original.AccentColor;
            vm.Theme = original.Theme;
            await vm.SaveSettingsCommand.ExecuteAsync(null);
        }
    }
}
