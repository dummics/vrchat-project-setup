using System.Globalization;
using System.Text.RegularExpressions;

namespace VrcSetup.Core;

// Build numbers order installer revisions of one app release. VPM still compares SemVer.
public sealed class ApplicationBuild : IComparable<ApplicationBuild>
{
    public SemanticVersion Version { get; }
    public long Number { get; }
    public string Identity { get; }
    public string AssetId => Number == 0 ? Identity : Identity.Replace("+build.", "-build.", StringComparison.Ordinal);

    public ApplicationBuild(string identity)
    {
        var parts = identity.TrimStart('v').Split('+', 2);
        Version = new(parts[0]);
        if (parts.Length == 2)
        {
            var match = Regex.Match(parts[1], @"^build\.([1-9][0-9]*)$");
            if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new ArgumentException("Invalid application build number.", nameof(identity));
            Number = number;
        }
        Identity = identity.TrimStart('v');
    }

    public static string FromAssemblyVersion(string value)
    {
        var match = Regex.Match(value, @"^(?<version>[^+]+)(?:\+build\.(?<build>[1-9][0-9]*)(?:\.|$))?");
        return match.Groups["build"].Success ? match.Groups["version"].Value + "+build." + match.Groups["build"].Value
            : match.Groups["version"].Value;
    }

    public int CompareTo(ApplicationBuild? other)
    {
        if (other is null) return 1;
        var comparison = Version.CompareTo(other.Version);
        return comparison == 0 ? Number.CompareTo(other.Number) : comparison;
    }
}
