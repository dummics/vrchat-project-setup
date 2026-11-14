namespace VrcSetup.Core;

public enum DependencyProposalKind { Add, ReviewVersion, ReviewBundled, ReviewPossible }

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
                else if (hint.Confidence == DependencyConfidence.Possible)
                    proposals.Add(new(hint.Id, DependencyProposalKind.ReviewPossible, null, null,
                        "This package may be needed. Review it before adding it.", hint));
            }
            else if (hint.Confidence == DependencyConfidence.Declared && hint.VersionConstraint is { Length: > 0 } constraint
                && !ClearlySatisfies(version, constraint))
            {
                proposals.Add(new(hint.Id, DependencyProposalKind.ReviewVersion, version, SuggestedVersion(hint),
                    "Check the selected version against the declared requirement before importing.", hint));
            }
            // A selected package with no matching reference is not a removal candidate:
            // scripts, editor tooling and unobserved variants can still require it.
        }
        return proposals.OrderBy(x => x.Kind).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Only suppress a review when the result is unambiguous. Prereleases,
    // aliases, floating policies and unsupported range syntax remain visible.
    private static bool ClearlySatisfies(string selected, string constraint)
    {
        const string exact = @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(selected, exact) || constraint.Length > 128) return false;
        var parts = constraint.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        try
        {
            var actual = new SemanticVersion(selected);
            foreach (var part in parts)
            {
                var op = part.StartsWith(">=") || part.StartsWith("<=") ? part[..2] :
                    part.StartsWith('>') || part.StartsWith('<') || part.StartsWith('=') ? part[..1] : "";
                var required = part[op.Length..];
                if (!System.Text.RegularExpressions.Regex.IsMatch(required, exact)) return false;
                var comparison = actual.CompareTo(new SemanticVersion(required));
                if (!(op switch { ">=" => comparison >= 0, "<=" => comparison <= 0, ">" => comparison > 0,
                    "<" => comparison < 0, "" or "=" => comparison == 0, _ => false })) return false;
            }
            return true;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return false; }
    }
}
