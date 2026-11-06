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

    [Fact] public void GuidRecognitionDoesNotDemandAnUpgradeOfAnExplicitPin()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string> { ["tool"] = "1.0.0" },
            Report(Hint("tool", DependencyConfidence.Detected))));
    }

    [Fact] public void AmbiguousAndEvidenceFreeHintsDoNotProduceAdditions()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string>(), Report(
            Hint("possible", DependencyConfidence.Possible), new("unsupported", DependencyConfidence.Detected, null, []))));
    }

    [Fact] public void AvatarAdviceDoesNotReplaceAnExplicitWorldSdkSelection()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string> { [ProjectRules.WorldSdk] = "latest" },
            Report(Hint(ProjectRules.AvatarSdk, DependencyConfidence.Detected), Hint(ProjectRules.WorldSdk, DependencyConfidence.Declared, "1.0.0"))));
    }

    [Fact] public void EvenCompleteInspectionDoesNotTreatAbsenceAsPermissionToRemove()
    {
        Assert.Empty(DependencyAdvisor.Propose(new Dictionary<string, string> { ["editor-utility"] = "1.0.0" }, Report()));
    }
}
