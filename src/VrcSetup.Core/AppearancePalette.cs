namespace VrcSetup.Core;

public sealed record AppearancePalette(string Canvas, string Surface, string Accent)
{
    public static IReadOnlyDictionary<string, AppearancePalette> Presets { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, AppearancePalette>(
            new Dictionary<string, AppearancePalette>(StringComparer.OrdinalIgnoreCase)
            {
                ["Orange"] = new("#181A1D", "#292D33", "#FFAD66"),
                ["Mint"] = new("#25272A", "#303338", "#50CDBB"),
                ["Ocean"] = new("#202A35", "#2A3848", "#70B9F2"),
                ["Violet"] = new("#2A2532", "#393141", "#C49AF0")
            });
}
