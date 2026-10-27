namespace VrcSetup.Core;

public static class PackageSearch
{
    // Match each word independently so "audio link" also finds "AudioLink".
    public static bool Matches(string? query, string id, string name)
    {
        var text = id + " " + name;
        return (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
