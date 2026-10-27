namespace VrcSetup.Core;

public enum VersionChangeRisk { Unknown, None, Downgrade, RiskyDowngrade }

// Compare actual versions, never the unresolved "latest" policy or a mixed selection.
public static class PackageDowngrades
{
    public static VersionChangeRisk Classify(string id, string? installed, string? target)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(target)) return VersionChangeRisk.Unknown;
        try
        {
            var before = new SemanticVersion(installed);
            var after = new SemanticVersion(target);
            if (after.CompareTo(before) >= 0) return VersionChangeRisk.None;
            return after.Major < before.Major || ProjectRules.SdkIds.Contains(id, StringComparer.OrdinalIgnoreCase)
                ? VersionChangeRisk.RiskyDowngrade : VersionChangeRisk.Downgrade;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return VersionChangeRisk.Unknown; }
    }

    public static bool RequiresConfirmation(PackageChange change)
    {
        if (change.Kind == ChangeKind.Remove) return false;
        var risk = Classify(change.Id, change.InstalledVersion, change.ResolvedVersion);
        return risk == VersionChangeRisk.RiskyDowngrade || risk == VersionChangeRisk.Unknown && change.Kind == ChangeKind.Update && ProjectRules.SdkIds.Contains(change.Id, StringComparer.OrdinalIgnoreCase);
    }
    public static bool RequiresConfirmation(OperationPlan plan) => plan.Projects.Any(project => project.Changes.Any(RequiresConfirmation));
}
