using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

// Replays previously captured local observations, never copies avatar archives.
// This validates attribution changes, not every dependency of those avatars.
internal static class DetectorCorpusVerification
{
    public static async Task RunAsync(string snapshots, string evidence)
    {
        snapshots = Path.GetFullPath(snapshots); evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a new evidence directory.");
        var service = new PackageInspectionService(new AppPaths(Path.Combine(evidence, "home")));
        var index = await service.LoadIndexAsync();
        var results = new List<object>();
        foreach (var (name, file) in new[] { ("Trixie", "deep.json"), ("Lulla PC", "lulla-pc.json"), ("Lulla Quest", "lulla-quest.json") })
        {
            var path = Path.Combine(snapshots, file);
            var bytes = await File.ReadAllBytesAsync(path);
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement.TryGetProperty("Inspection", out var nested) ? nested : json.RootElement;
            var baseline = root.Deserialize<PackageInspectionReport>(JsonDefaults.Options) ?? throw new IOException("Invalid corpus snapshot: " + file);
            if (!baseline.Observation.ArchiveComplete || !baseline.Observation.TextComplete)
                throw new IOException("Incomplete corpus observation: " + file);
            var watch = Stopwatch.StartNew();
            var matched = DependencyAdvisor.Match(baseline.Observation, index);
            watch.Stop();
            var before = baseline.Suggestions.Select(x => x.Id).Order(StringComparer.Ordinal).ToArray();
            var after = matched.Suggestions.Select(x => x.Id).Order(StringComparer.Ordinal).ToArray();
            if (!before.SequenceEqual(after)) throw new IOException("Review an unexpected package-list change before accepting this corpus: " + name);
            DependencyEvidence[] targetedPublisherProof = [];
            if (name == "Lulla PC")
            {
                const string legacy = "104003ab15a9f7a47b6ef0597225e771";
                var hint = matched.Suggestions.Single(x => x.Id == "com.poiyomi.toon");
                // The full report retains only a bounded evidence sample per
                // package. Validate this material separately instead of assuming
                // its legacy shader must fit among that sample's first entries.
                var material = baseline.Observation.Assets.First(x => x.References.Any(r => r.Guid == legacy && r.Kind == "shader"));
                var target = DependencyAdvisor.Match(baseline.Observation with { Assets = [material] }, index).Suggestions.Single(x => x.Id == "com.poiyomi.toon");
                targetedPublisherProof = target.Evidence.Where(x => x.Guid == legacy).ToArray();
                if (hint.VersionConstraint is not null || target.VersionConstraint is not null || !targetedPublisherProof.Any(x => x.ObservedVersions.Contains("9.2"))
                    || matched.UnresolvedReferences >= baseline.UnresolvedReferences)
                    throw new IOException("Publisher attribution did not resolve the known PC shader without inventing a version requirement.");
            }
            results.Add(new
            {
                Sample = name, SnapshotFile = file, SnapshotSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                BeforeUnresolved = baseline.UnresolvedReferences, AfterUnresolved = matched.UnresolvedReferences,
                MatchMilliseconds = watch.Elapsed.TotalMilliseconds, matched.Scope,
                TargetedPublisherProof = targetedPublisherProof.Select(x => new { x.Guid, x.ObservedVersions, x.SignatureSources }).ToArray(),
                Packages = matched.Suggestions.Select(x => new { x.Id, x.Confidence, x.VersionConstraint,
                    PublisherEvidence = x.Evidence.Where(e => e.SignatureSources.Any(s => s.StartsWith("publisher:", StringComparison.Ordinal)))
                        .Select(e => new { e.Guid, e.ObservedVersions, e.SignatureSources }).ToArray() }).ToArray()
            });
            Console.WriteLine($"PASS: {name}; unresolved {baseline.UnresolvedReferences} -> {matched.UnresolvedReferences}; package choices unchanged.");
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "detector-corpus.json"), new
        {
            Passed = true, Kind = "Replay of captured observations, not fresh archive scans or complete creator ground truth", IndexRevision = index.Revision,
            Results = results, AvatarArchivesCopied = false, PersonalProjectsChanged = false
        });
    }
}
