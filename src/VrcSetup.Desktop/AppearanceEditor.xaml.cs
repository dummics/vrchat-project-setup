using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VrcSetup_Desktop;

public sealed partial class AppearanceEditor : UserControl
{
    private bool updatingPalette;
    private bool initialized;
    private ViewModels.MainViewModel? observed;
    private ViewModels.MainViewModel ViewModel => observed!;

    public AppearanceEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => { initialized = true; AttachViewModel(); };
        Unloaded += (_, _) => { initialized = false; DetachViewModel(); };
        DataContextChanged += (_, _) => { if (initialized) AttachViewModel(); };
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        observed = DataContext as ViewModels.MainViewModel;
        if (observed is null) return;
        observed.PropertyChanged += AppearanceChanged;
        ApplyColorPalette();
    }

    private void DetachViewModel()
    {
        if (observed is not null) observed.PropertyChanged -= AppearanceChanged;
        observed = null;
    }

    private void AppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModel.CanvasColor) or nameof(ViewModel.SurfaceColor)
            or nameof(ViewModel.AccentColor) or nameof(ViewModel.IsLoadingSettings))
            ApplyColorPalette();
    }


    private void PaletteSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || observed is null || updatingPalette || ViewModel.IsLoadingSettings || PaletteSelector.SelectedValue is not string palette) return;
        var colors = palette switch
        {
            "Mint" => ("#25272A", "#303338", "#50CDBB"),
            "Ocean" => ("#202A35", "#2A3848", "#70B9F2"),
            "Violet" => ("#2A2532", "#393141", "#C49AF0"),
            _ => ("", "", "")
        };
        if (colors.Item1.Length == 0) return;
        updatingPalette = true;
        try
        {
            ViewModel.CanvasColor = colors.Item1;
            ViewModel.SurfaceColor = colors.Item2;
            ViewModel.AccentColor = colors.Item3;
        }
        finally { updatingPalette = false; }
        ApplyColorPalette();
    }

    private void PaletteColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!initialized || observed is null || updatingPalette || ViewModel.IsLoadingSettings) return;
        var color = args.NewColor;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        updatingPalette = true;
        try
        {
            switch (sender.Tag as string)
            {
                case "Canvas": ViewModel.CanvasColor = hex; break;
                case "Surface": ViewModel.SurfaceColor = hex; break;
                case "Accent": ViewModel.AccentColor = hex; break;
                default: return;
            }
            ViewModel.ColorPalette = "Custom";
        }
        finally { updatingPalette = false; }
        ApplyColorPalette();
    }

    private void ApplyColorPalette()
    {
        if (!initialized || observed is null || updatingPalette || ViewModel is null || ViewModel.IsLoadingSettings) return;
        updatingPalette = true;
        try { ApplyColorPaletteCore(); }
        catch (Exception error)
        {
            ViewModel.Application.Diagnostics.Write("Appearance preview", error.Message, error);
            ViewModel.ErrorNeedsDiagnostics = true;
            ViewModel.ErrorMessage = VrcSetup.Core.UserErrors.Message(error);
        }
        finally { updatingPalette = false; }
    }

    private void ApplyColorPaletteCore()
    {
        var canvas = AppearanceResources.ParseColor(ViewModel.CanvasColor, "#25272A");
        var surface = AppearanceResources.ParseColor(ViewModel.SurfaceColor, "#303338");
        var accent = AppearanceResources.ParseColor(ViewModel.AccentColor, "#50CDBB");
        CanvasPicker.Color = canvas; SurfacePicker.Color = surface; AccentPicker.Color = accent;
        CanvasSwatch.Background = new SolidColorBrush(canvas); SurfaceSwatch.Background = new SolidColorBrush(surface); AccentSwatch.Background = new SolidColorBrush(accent);
    }
}
