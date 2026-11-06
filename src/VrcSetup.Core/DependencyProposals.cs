namespace VrcSetup.Core;

public enum DependencyProposalKind { Add, ReviewVersion, ReviewBundled }

// Advice refers to the caller's working selection, never authorizes a mutation.
public sealed record DependencyProposal(string Id, DependencyProposalKind Kind,
    string? SelectedVersion, string? ProposedVersion, string Reason, DependencySuggestion Evidence);

public static partial class DependencyAdvisor
{
    public static DependencyProposal[] Propose(IReadOnlyDictionary<string, string> selection, PackageInspectionReport inspection)
    {
        var selected = new Dictionary<string, string>(selection, StringComparer.OrdinalIgnoreCase);
        var proposals = new List<DependencyProposal>();
        foreach (var hint in inspection.Suggestions.Where(x => IsAvatarCandidate(x.Id)))
        {
            if (hint.Evidence.Length == 0) continue;
            if (!selected.TryGetValue(hint.Id, out var version))
            {
                if (hint.Id.Equals(ProjectRules.AvatarSdk, StringComparison.OrdinalIgnoreCase) && selected.ContainsKey(ProjectRules.WorldSdk)) continue;
                if (hint.Confidence is DependencyConfidence.Declared or DependencyConfidence.Detected)
                    proposals.Add(new(hint.Id, DependencyProposalKind.Add, null, SuggestedVersion(hint),
                        SuggestedVersion(hint) is null ? "Choose a version satisfying the declared requirement." : "Referenced package is not in your selection.", hint));
            }
            else if (hint.Confidence == DependencyConfidence.Included)
            {
                proposals.Add(new(hint.Id, DependencyProposalKind.ReviewBundled, version, null,
                    "Package metadata is bundled in this file. Review whether external installation is needed; do not remove dependencies automatically.", hint));
            }
            else if (hint.Confidence == DependencyConfidence.Declared && hint.VersionConstraint is { Length: > 0 } constraint
                && !string.Equals(version, constraint, StringComparison.OrdinalIgnoreCase))
            {
                proposals.Add(new(hint.Id, DependencyProposalKind.ReviewVersion, version, SuggestedVersion(hint),
                    "Check the selected version against the declared requirement before importing.", hint));
            }
            // A selected package with no matching reference is not a removal candidate:
            // scripts, editor tooling and unobserved variants can still require it.
        }
        return proposals.OrderBy(x => x.Kind).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
