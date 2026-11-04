namespace VrcSetup.Core;

public enum InstallationChange { New, Update, Reinstall, Downgrade }

public static class InstallationVersions
{
    public static InstallationChange Compare(string? installed, string candidate)
    {
        var next = new SemanticVersion(candidate);
        if (installed is null) return InstallationChange.New;
        var current = new SemanticVersion(installed);
        if (current.Major == 2 && current.PreRelease.FirstOrDefault() == "preview" && next.Major == 0 && next.Minor >= 2)
            return InstallationChange.Update;
        var comparison = next.CompareTo(current);
        return comparison > 0 ? InstallationChange.Update : comparison < 0 ? InstallationChange.Downgrade : InstallationChange.Reinstall;
    }
}
