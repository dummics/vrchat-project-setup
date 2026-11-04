using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VrcSetup_Desktop;

// Global theme resources must not depend on whether Settings has been opened.
internal static class AppearanceResources
{
    internal static Color ParseColor(string value, string fallback)
    {
        var text = value is { Length: 7 } && value[0] == '#' ? value : fallback;
        return uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : ParseColor(fallback, "#25272A");
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(255,
        (byte)(from.R + (to.R - from.R) * amount), (byte)(from.G + (to.G - from.G) * amount), (byte)(from.B + (to.B - from.B) * amount));
    private static double Luminance(Color color)
    {
        static double Linear(byte channel) { var v = channel / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    internal static void Apply(string canvasValue, string surfaceValue, string accentValue)
    {
        var canvas = ParseColor(canvasValue, "#25272A");
        var surface = ParseColor(surfaceValue, "#303338");
        var accent = ParseColor(accentValue, "#50CDBB");
        // Preserve readable light text in the dark theme, even for bright selections.
        var black = Color.FromArgb(255, 0, 0, 0);
        if (Math.Max(canvas.R, Math.Max(canvas.G, canvas.B)) > 75) canvas = Blend(canvas, black, 1 - 75.0 / Math.Max(canvas.R, Math.Max(canvas.G, canvas.B)));
        if (Math.Max(surface.R, Math.Max(surface.G, surface.B)) > 95) surface = Blend(surface, black, 1 - 95.0 / Math.Max(surface.R, Math.Max(surface.G, surface.B)));
        var dictionary = Application.Current.Resources.MergedDictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("DesignResources.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (dictionary is null) return;
        var dark = (ResourceDictionary)dictionary.ThemeDictionaries["Default"];
        var light = (ResourceDictionary)dictionary.ThemeDictionaries["Light"];
        void Set(ResourceDictionary d, string key, Color value) { if (d.ContainsKey(key) && d[key] is SolidColorBrush brush) brush.Color = value; }
        Set(dark, "AppCanvasBrush", canvas); Set(dark, "AppSurfaceBrush", surface);
        Set(dark, "AppCatalogBrush", Blend(canvas, black, .12)); Set(dark, "AppCatalogCardBrush", Blend(canvas, surface, .45));
        Set(dark, "AppControlBrush", Blend(surface, Color.FromArgb(255, 255, 255, 255), .05));
        var white = Color.FromArgb(255, 255, 255, 255);
        Set(light, "AppCanvasBrush", Blend(canvas, white, .96)); Set(light, "AppSurfaceBrush", Blend(surface, white, .985));
        Set(light, "AppCatalogBrush", Blend(canvas, white, .91)); Set(light, "AppCatalogCardBrush", Blend(surface, white, .96));
        Set(light, "AppControlBrush", Blend(surface, white, .91));
        var brightAccent = accent;
        while (Luminance(brightAccent) < .32) brightAccent = Blend(brightAccent, white, .12);
        var darkAccent = accent;
        while (Luminance(darkAccent) > .13) darkAccent = Blend(darkAccent, black, .12);
        Set(dark, "AppAccentTextBrush", brightAccent); Set(dark, "AppAccentSubtleBrush", Blend(canvas, accent, .18));
        Set(light, "AppAccentTextBrush", darkAccent); Set(light, "AppAccentSubtleBrush", Blend(Color.FromArgb(255, 255, 255, 255), accent, .16));
        foreach (var theme in new[] { dark, light })
        {
            Set(theme, "AccentFillColorDefaultBrush", brightAccent);
            Set(theme, "AccentFillColorSecondaryBrush", Blend(brightAccent, white, .15));
            Set(theme, "AccentFillColorTertiaryBrush", darkAccent);
        }
    }
}
