using System.Text.Json.Nodes;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class UnityProjectDriver
{
    public Task<ToolResult> ReinstallPackagesAsync(string project, string[] ids, IProgress<OperationEvent>? progress, CancellationToken token) =>
        runner.VpmAsync(["reinstall", "packages", "-p", project, .. ids], project, progress, token);

    public async Task<ToolResult> ApplyPackagePlanAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken token)
    {
        var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var before = await workspace.ReadAsync(plan.Path, token);
        var repairs = plan.Changes.Where(x => x.Kind == ChangeKind.Update &&
            before.LockedPackages.GetValueOrDefault(x.Id) == x.ResolvedVersion && !PackagePresence.Matches(x, before)).ToArray();
        if (repairs.Length > 0)
        {
            // Installing the same locked version can return "nothing to do"
            // without restoring files. Resolve while the roots still exist.
            var resolved = await ResolveAsync(plan.Path, progress, token);
            if (ProjectRules.ToolFailed(resolved)) return resolved;
            var repaired = await workspace.ReadAsync(plan.Path, token);
            foreach (var change in repairs.Where(x => PackagePresence.Matches(x, repaired))) restored.Add(change.Id);
        }
        // vrc-get removes declared roots. Removing them from the manifest first
        // makes the CLI report "not installed" and leaves replacement half done.
        foreach (var change in plan.Changes.Where(x => x.Kind == ChangeKind.Remove))
        {
            var fresh = await workspace.ReadAsync(plan.Path, token);
            if (!PackagePresence.Contains(fresh, change.Id)) continue;
            var removed = await ChangePackageAsync(plan.Path, change, progress, token);
            if (ProjectRules.ToolFailed(removed)) return removed;
        }
        var file = Path.Combine(plan.Path, "Packages", "vpm-manifest.json");
        var manifest = File.Exists(file) ? JsonNode.Parse(await File.ReadAllTextAsync(file, token))?.AsObject() : new JsonObject();
        if (manifest is null) throw new IOException("The project package manifest is invalid.");
        var dependencies = manifest["dependencies"]?.AsObject() ?? new JsonObject();
        foreach (var id in dependencies.Select(x => x.Key).Where(x => !PackagePresence.IsGenerated(x)).ToArray()) dependencies.Remove(id);
        foreach (var change in plan.Changes.Where(x => x.Kind != ChangeKind.Remove))
        {
            if (string.IsNullOrWhiteSpace(change.ResolvedVersion)) throw new IOException("A package version is missing from the plan.");
            // The CLI treats an already-declared target version as a no-op even
            // when it is not locked/installed. Let explicit installs declare
            // changed targets; publish only retained roots before those calls.
            if (change.Kind == ChangeKind.Keep || restored.Contains(change.Id))
                dependencies[change.Id] = new JsonObject { ["version"] = change.ResolvedVersion };
            if (change.Kind != ChangeKind.Keep)
                progress?.Report(new(DateTime.UtcNow, "Packages", $"{change.Kind}: {change.Id} @ {change.ResolvedVersion}", Project: plan.Path) { Package = change });
        }
        manifest["dependencies"] = dependencies;
        // Commit one coherent requested set. In particular, a subsequent resolve
        // must not resurrect a shader package replaced by the selected version.
        await AtomicFile.WriteJsonAsync(file, manifest, token);
        foreach (var change in plan.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update))
        {
            if (restored.Contains(change.Id)) continue;
            // A general resolve considers a legacy provider sufficient. Explicit
            // installs tell VPM which side of a replacement the user selected.
            var installed = await ChangePackageAsync(plan.Path, change with { RequestedVersion = change.ResolvedVersion }, progress, token);
            if (ProjectRules.ToolFailed(installed, change.Id)) return installed;
        }
        return await ResolveAsync(plan.Path, progress, token);
    }
}
