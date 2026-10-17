namespace VrcSetup.Core;

public sealed record InspectionOptions
{
    public bool Deep { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
    public long ExpandedBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public int EntryTextBytes { get; init; } = 4 * 1024 * 1024;
    public long TotalTextBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxEntries { get; init; } = 100_000;
    public int MaxAssets { get; init; } = 50_000;
    public int MaxReferences { get; init; } = 250_000;
    public static InspectionOptions Extended => new() { Deep = true, Timeout = TimeSpan.FromSeconds(45), ExpandedBytes = 8L * 1024 * 1024 * 1024, EntryTextBytes = 16 * 1024 * 1024, TotalTextBytes = 256L * 1024 * 1024, MaxEntries = 200_000, MaxAssets = 100_000, MaxReferences = 500_000 };
}
public sealed record AssetReference(string Guid, string Kind);
public sealed record ObservedManifest(string Name, string Version, Dictionary<string, string> Dependencies);
public sealed record InspectedAsset(string Guid, string Path, AssetReference[] References, string[] Hints, bool TextPartial, ObservedManifest? Manifest = null);
public sealed record ArchiveObservation(int Schema, long Length, long ModifiedTicks, bool ArchiveComplete, bool TextComplete, string[] Limitations, InspectedAsset[] Assets, double ScanMilliseconds);
public sealed record PackageSignature(string Guid, string PackageId, string Version, string Kind, string Source);
public sealed record SignatureSnapshot(string Revision, PackageSignature[] Signatures);
public enum DependencyConfidence { Declared, Detected, Possible, Included }
public sealed record DependencyEvidence(string Asset, string Kind, string? Guid = null);
public sealed record DependencySuggestion(string Id, DependencyConfidence Confidence, string? VersionConstraint, DependencyEvidence[] Evidence);
public sealed record PackageInspectionReport(ArchiveObservation Observation, string IndexRevision, string Scope, int UnresolvedReferences, DependencySuggestion[] Suggestions, bool FromCache);
public interface IUnityPackageInspector
{
    Task<ArchiveObservation> ReadAsync(string path, InspectionOptions options, CancellationToken token);
}

public static class DependencyAdvisor
{
    public static string? SuggestedVersion(DependencySuggestion suggestion)
    {
        if (suggestion.Confidence == DependencyConfidence.Detected) return "latest";
        if (suggestion.Confidence != DependencyConfidence.Declared || string.IsNullOrWhiteSpace(suggestion.VersionConstraint) || suggestion.VersionConstraint.Length > 128) return null;
        try { _ = new SemanticVersion(suggestion.VersionConstraint); return suggestion.VersionConstraint; }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return null; } // A range must not be silently converted to latest.
    }
    public static PackageInspectionReport Match(ArchiveObservation observation, SignatureSnapshot snapshot, bool cached = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var byGuid = snapshot.Signatures.GroupBy(x => x.Guid, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var internalIds = observation.Assets.Select(x => x.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets = observation.Assets.ToDictionary(x => x.Guid, StringComparer.OrdinalIgnoreCase);
        var included = observation.Assets.Where(x => x.Manifest is not null).Select(x => x.Manifest!.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = observation.Assets.Where(x => (x.Path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || x.Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) && !Optional(x.Path) &&
            x.References.Any(r => r.Kind == "script" && byGuid.GetValueOrDefault(r.Guid)?.Any(s => s.PackageId == ProjectRules.AvatarSdk) == true)).Take(9).ToArray();
        var manyRoots = roots.Length > 8;
        if (manyRoots) roots = []; // Avoid O(roots * graph) work and unsupported certainty.
        HashSet<string> Reach(InspectedAsset root)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var stack = new Stack<string>(); stack.Push(root.Guid);
            while (stack.TryPop(out var id)) { token.ThrowIfCancellationRequested(); if (visited.Add(id) && assets.TryGetValue(id, out var node)) foreach (var edge in node.References) if (internalIds.Contains(edge.Guid)) stack.Push(edge.Guid); }
            return visited;
        }
        var reachable = roots.Select(Reach).ToArray();
        var sharedPackages = reachable.Select(set => set.SelectMany(id => assets[id].References)
            .Where(r => r.Kind is "script" or "shader" && !internalIds.Contains(r.Guid))
            .SelectMany(r => byGuid.GetValueOrDefault(r.Guid) ?? []).Select(x => x.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
        var common = sharedPackages.FirstOrDefault() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sharedPackages.Skip(1)) common.IntersectWith(set);
        var evidence = new Dictionary<string, List<DependencyEvidence>>(StringComparer.OrdinalIgnoreCase);
        var confidences = new Dictionary<string, DependencyConfidence>(StringComparer.OrdinalIgnoreCase);
        var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string id, DependencyConfidence confidence, DependencyEvidence proof, string? version = null)
        {
            if (!evidence.TryGetValue(id, out var list)) evidence[id] = list = [];
            if (list.Count < 12 && !list.Contains(proof)) list.Add(proof);
            if (!confidences.TryGetValue(id, out var old) || confidence < old) confidences[id] = confidence;
            if (version is not null) constraints[id] = constraints.TryGetValue(id, out var previous) && previous != version ? previous + " & " + version : version;
        }
        // The same GUID can belong to several packages; never pick one arbitrarily.
        foreach (var asset in observation.Assets)
        {
            token.ThrowIfCancellationRequested();
            if (asset.Manifest is { } manifest)
            {
                Add(manifest.Name, DependencyConfidence.Included, new(asset.Path, "manifest"), manifest.Version);
                foreach (var dep in manifest.Dependencies)
                    Add(dep.Key, Optional(asset.Path) ? DependencyConfidence.Possible : DependencyConfidence.Declared, new(asset.Path, "declared dependency"), dep.Value);
            }
            foreach (var reference in asset.References)
            {
                if (internalIds.Contains(reference.Guid) || BuiltIn(reference.Guid)) continue;
                var signatures = byGuid.GetValueOrDefault(reference.Guid);
                if (signatures is null) { unresolved.Add(reference.Guid); continue; }
                var owners = signatures.Select(x => x.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var pertinent = !Optional(asset.Path) && (reachable.Length == 0 ? new[] { ".prefab", ".unity", ".mat", ".asset" }.Any(x => asset.Path.EndsWith(x, StringComparison.OrdinalIgnoreCase)) : reachable.Any(x => x.Contains(asset.Guid)));
                foreach (var owner in owners)
                {
                    var confidence = !manyRoots && observation.ArchiveComplete && pertinent && owners.Length == 1 && (reachable.Length < 2 || common.Contains(owner)) && reference.Kind is "script" or "shader" ? DependencyConfidence.Detected : DependencyConfidence.Possible;
                    Add(owner, confidence, new(asset.Path, owners.Length > 1 ? "ambiguous GUID" : reference.Kind, reference.Guid));
                }
            }
            foreach (var hint in asset.Hints) Add(hint, DependencyConfidence.Possible, new(asset.Path, "text hint"));
        }
        foreach (var package in included) confidences[package] = DependencyConfidence.Included;
        var suggestions = evidence.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => new DependencySuggestion(x.Key, confidences[x.Key], constraints.GetValueOrDefault(x.Key), x.Value.ToArray())).ToArray();
        return new(observation, snapshot.Revision, manyRoots ? "Multiple avatar candidates (choose a smaller package)" : roots.Length == 1 ? roots[0].Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? "Avatar scene" : "Avatar prefab" : roots.Length > 1 ? "Shared avatar references" : "Whole archive", unresolved.Count, suggestions, cached);
    }
    private static bool Optional(string path) => new[] { "/Samples/", "/Samples~/", "/Examples/", "/Demo/", "/Editor/" }.Any(x => path.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool BuiltIn(string guid) => guid.StartsWith("0000000000000000", StringComparison.OrdinalIgnoreCase);
}
