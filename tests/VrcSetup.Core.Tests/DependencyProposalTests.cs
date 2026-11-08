using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class DependencyProposalTests
{
    private static DependencySuggestion Hint(string id, DependencyConfidence confidence, string? version = null)
        => new(id, confidence, version, [new("Assets/Avatar.prefab", "fixture evidence")]);
    private static PackageInspectionReport Report(params DependencySuggestion[] suggestions)
        => new(new(2, 0, 0, true, true, [], [], 0), "fixture-index", "Avatar prefab", 0, suggestions, false);

    [Fact] public void AdviceExplainsAdditionsVersionsAndBundledOverlapWithoutMutatingSelection()
    {
        var selection = new Dictionary<string, string> { ["pinned"] = "1.0.0", ["bundled"] = "latest", ["unrelated-tool"] = "2.0.0" };
        var proposals = DependencyAdvisor.Propose(selection, Report(Hint("missing", DependencyConfidence.Detected),
            Hint("pinned", DependencyConfidence.Declared, "3.0.0"), Hint("bundled", DependencyConfidence.Included, "1.0.0")));
        Assert.Equal(3, proposals.Length);
        Assert.Equal("latest", Assert.Single(proposals, x => x.Kind == DependencyProposalKind.Add).ProposedVersion);
        Assert.Equal("3.0.0", Assert.Single(proposals, x => x.Kind == DependencyProposalKind.ReviewVersion).ProposedVersion);
        Assert.Null(Assert.Single(proposals, x => x.Kind == DependencyProposalKind.ReviewBundled).ProposedVersion);
        Assert.Equal("1.0.0", selection["pinned"]); Assert.Equal("latest", selection["bundled"]);
        Assert.Equal(3, selection.Count); Assert.DoesNotContain(proposals, x => x.Id == "unrelated-tool");
    }

    [Fact] public void DeclaredRangeRequiresManualVersionChoiceAndPreservesConstraint()
    {
        var proposal = Assert.Single(DependencyAdvisor.Propose(new Dictionary<string, string>(), Report(Hint("range", DependencyConfidence.Declared, ">=2.0.0 <3.0.0"))));
        Assert.Equal(DependencyProposalKind.Add, proposal.Kind);
        Assert.Null(proposal.ProposedVersion);
        Assert.Equal(">=2.0.0 <3.0.0", proposal.Evidence.VersionConstraint);
    }

    [Theory]
    [InlineData("2.1.0", ">=2.0.0 <3.0.0", false)]
    [InlineData("2.0.0", ">=2.0.0 <3.0.0", false)]
    [InlineData("3.0.0", ">=2.0.0 <3.0.0", true)]
    [InlineData("1.9.9", ">=2.0.0", true)]
    [InlineData("2.0.0", ">2.0.0", true)]
    [InlineData("2.0.0", "=2.0.0", false)]
    [InlineData("latest", ">=2.0.0", true)]
    [InlineData("2.1.0", "^2.0.0", true)]
    [InlineData("2.1.0-beta.1", ">=2.0.0", true)]
    [InlineData("2.1.0", ">=2.0.0 || <1.0.0", true)]
    public void VersionReviewIsSuppressedOnlyForClearlySatisfiedConstraints(string selected, string constraint, bool review)
    {
        var proposals = DependencyAdvisor.Propose(new Dictionary<string, string> { ["tool"] = selected }, Report(Hint("tool", DependencyConfidence.Declared, constraint)));
        Assert.Equal(review, proposals.Any(x => x.Kind == DependencyProposalKind.ReviewVersion));
    }

    [Fact] public void GuidRecognitionDoesNotDemandAnUpgradeOfAnExplicitPin()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string> { ["tool"] = "1.0.0" },
            Report(Hint("tool", DependencyConfidence.Detected))));
    }

    [Fact] public void AmbiguousHintsRequireReviewAndEvidenceFreeHintsAreOmitted()
    {
        var proposals = DependencyAdvisor.Propose(new Dictionary<string, string>(), Report(
            Hint("possible", DependencyConfidence.Possible), new("unsupported", DependencyConfidence.Detected, null, [])));
        var proposal = Assert.Single(proposals);
        Assert.Equal(DependencyProposalKind.ReviewPossible, proposal.Kind);
        Assert.Null(proposal.ProposedVersion);
    }

    [Fact] public void PossibleMatchesAreVisibleWithoutChangingTheSelectedPackages()
    {
        var selection = new Dictionary<string, string> { ["already-selected"] = "1.0.0" };
        var preview = DependencyAdvisor.CombineSelection(selection, Report(
            Hint("possible", DependencyConfidence.Possible), Hint("bundled", DependencyConfidence.Included),
            new("no-evidence", DependencyConfidence.Possible, null, [])));
        var candidate = Assert.Single(preview, x => x.Id == "possible");
        Assert.False(candidate.Selected);
        Assert.Equal("latest", candidate.Version);
        Assert.DoesNotContain(preview, x => x.Id is "bundled" or "no-evidence");
        Assert.Single(selection);
        Assert.Equal("1.0.0", selection["already-selected"]);
    }

    [Fact] public void AvatarAdviceDoesNotReplaceAnExplicitWorldSdkSelection()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string> { [ProjectRules.WorldSdk] = "latest" },
            Report(Hint(ProjectRules.AvatarSdk, DependencyConfidence.Detected), Hint(ProjectRules.WorldSdk, DependencyConfidence.Declared, "1.0.0"))));
    }

    [Fact] public void PreviewDoesNotOfferAvatarSdkAgainstWorldSdkWithDifferentCasing()
    {
        var selection = new Dictionary<string, string> { [ProjectRules.WorldSdk.ToUpperInvariant()] = "latest" };
        var preview = DependencyAdvisor.CombineSelection(selection, Report(Hint(ProjectRules.AvatarSdk.ToUpperInvariant(), DependencyConfidence.Possible)));
        Assert.DoesNotContain(preview, x => x.Id.Equals(ProjectRules.AvatarSdk, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public void EvenCompleteInspectionDoesNotTreatAbsenceAsPermissionToRemove()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string> { ["editor-utility"] = "1.0.0" }, Report()));
    }
}
