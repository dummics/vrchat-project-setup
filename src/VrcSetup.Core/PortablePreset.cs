using System.Text.Json;
using System.Text.RegularExpressions;

namespace VrcSetup.Core;

// Portable by construction: never accepts application paths, credentials or project contents.
public sealed record PortablePreset(string Name, Dictionary<string, string> Packages)
{
    public string Format { get; init; } = "vrcsetup-preset";
    public int Version { get; init; } = 1;
    public const int MaxBytes = 256 * 1024;
    public static PortablePreset Read(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Preset too large.");
        var preset = JsonSerializer.Deserialize<PortablePreset>(json, JsonDefaults.Options) ?? throw new InvalidDataException("Missing preset.");
        preset.Validate(); return preset with { Name = preset.Name.Trim(), Packages = new(preset.Packages, StringComparer.OrdinalIgnoreCase) };
    }
    public void Validate()
    {
        if (Format != "vrcsetup-preset" || Version != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl) || Packages is null || Packages.Count is < 1 or > 1000)
            throw new InvalidDataException("Unsupported preset.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, version) in Packages)
            if (!ids.Add(id) || !Regex.IsMatch(id, @"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,199}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
                version is null || version.Length > 120 || version != "latest" && !Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?(?:\+[a-zA-Z0-9.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new InvalidDataException("Invalid package choice.");
    }
    public string AvailableName(IEnumerable<string> existing)
    {
        var names = existing.Append("Default").Append("My default packages").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = Name.Trim(); var candidate = name;
        for (var n = 2; names.Contains(candidate); n++)
        {
            var suffix = " (" + n + ")";
            candidate = name[..Math.Min(name.Length, 80 - suffix.Length)] + suffix;
        }
        return candidate;
    }
}
