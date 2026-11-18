namespace VrcSetup.Core;

public enum InstallationChange { New, Update, Reinstall, Downgrade }

public static class InstallationVersions
{
    public static InstallationChange Compare(string? installed, string candidate)
    {
        var next = new ApplicationBuild(candidate);
        if (installed is null) return InstallationChange.New;
        var current = new ApplicationBuild(installed);
        if (current.Version.Major == 2 && current.Version.PreRelease.FirstOrDefault() == "preview" && next.Version.Major == 0 && next.Version.Minor >= 2)
            return InstallationChange.Update;
        var comparison = next.CompareTo(current);
        return comparison > 0 ? InstallationChange.Update : comparison < 0 ? InstallationChange.Downgrade : InstallationChange.Reinstall;
    }
}
