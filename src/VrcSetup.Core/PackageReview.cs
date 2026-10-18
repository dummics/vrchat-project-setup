namespace VrcSetup.Core;

public sealed record PackageReviewGroup(ProjectPlan[] Projects, PackageChange[] Changes);

public static class PackageReview
{
    // Group each actual transition by its exact target set, so common changes appear once
    // while different installed/target versions remain separate and attributable.
    public static PackageReviewGroup[] Group(OperationPlan plan)
    {
        var changes = plan.Projects.SelectMany(project => project.Changes.Where(x => x.Kind != ChangeKind.Keep).Select(change => (Project: project, Change: change)))
            .GroupBy(x => (Id: x.Change.Id.ToUpperInvariant(), x.Change.Kind, x.Change.InstalledVersion, x.Change.ResolvedVersion))
            .Select(group => new { Projects = group.Select(x => x.Project).DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray(), Change = group.First().Change }).ToArray();
        return changes.GroupBy(x => string.Join("\0", x.Projects.Select(p => p.Path.ToUpperInvariant())))
            .Select(group => new PackageReviewGroup(group.First().Projects, group.Select(x => x.Change).ToArray()))
            .OrderByDescending(x => x.Projects.Length).ToArray();
    }
}
