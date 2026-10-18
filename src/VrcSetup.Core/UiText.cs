using System.Globalization;
using System.Text.Json;

namespace VrcSetup.Core;

public static class UiText
{
    private static readonly Lazy<Dictionary<string, string>> Italian = new(() =>
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("VrcSetup.Core.Resources.it.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });
    public static string Language { get; set; } = "system";
    public static string Resolve(string? choice, string? system = null) => choice is "it" or "en" ? choice : (system ?? CultureInfo.CurrentUICulture.Name).StartsWith("it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";
    public static string CurrentLanguage => Resolve(Language);
    public static string Text(string? source, string? language = null)
    {
        if (string.IsNullOrEmpty(source) || Resolve(language ?? Language) != "it") return source ?? "";
        if (Italian.Value.TryGetValue(source, out var translated)) return translated;
        var manage = System.Text.RegularExpressions.Regex.Match(source, @"^Manage (\d+) projects?$");
        if (manage.Success) return "Gestisci " + manage.Groups[1].Value + " progetti";
        var projects = System.Text.RegularExpressions.Regex.Match(source, @"^(\d+) projects?$");
        if (projects.Success) return projects.Groups[1].Value + (projects.Groups[1].Value == "1" ? " progetto" : " progetti");
        var packages = System.Text.RegularExpressions.Regex.Match(source, @"^(\d+) packages?( detected)?(.*)$");
        if (packages.Success) return packages.Groups[1].Value + " pacchetti" + (packages.Groups[2].Success ? " rilevati" : "") + packages.Groups[3].Value;
        foreach (var pair in Italian.Value.Where(x => x.Key.EndsWith("…") || x.Key.EndsWith(" · ") || x.Key.EndsWith(": ") || x.Key.EndsWith(" (")))
            if (source.StartsWith(pair.Key, StringComparison.Ordinal)) return pair.Value + Text(source[pair.Key.Length..], "it");
        return source;
    }
    public static void LoadPreference(string path)
    {
        try { if (File.Exists(path)) Language = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(path), JsonDefaults.Options)?.Language ?? "system"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { Language = "system"; }
    }
}
