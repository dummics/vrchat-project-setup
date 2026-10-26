using System.Text.Json.Nodes;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class UnityProjectDriver
{
    public async Task<ToolResult> ApplyPackagePlanAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken token)
    {
        if (workspace.IsBusy(plan.Path)) throw new IOException("Close Unity for this project before applying changes.");
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
            if (change.Kind == ChangeKind.Keep)
                dependencies[change.Id] = new JsonObject { ["version"] = change.ResolvedVersion };
            if (change.Kind != ChangeKind.Keep)
                progress?.Report(new(DateTime.UtcNow, "Packages", $"{change.Kind}: {change.Id} @ {change.ResolvedVersion}", Project: plan.Path) { Package = change });
        }
        manifest["dependencies"] = dependencies;
        // Commit one coherent requested set. In particular, a subsequent resolve
        // must not resurrect a shader package replaced by the selected version.
        await AtomicFile.WriteJsonAsync(file, manifest, token);
        foreach (var change in plan.Changes.Where(x => x.Kind == ChangeKind.Remove))
        {
            var fresh = await workspace.ReadAsync(plan.Path, token);
            if (!PackagePresence.Contains(fresh, change.Id)) continue;
            var removed = await ChangePackageAsync(plan.Path, change, progress, token);
            if (ProjectRules.ToolFailed(removed)) return removed;
        }
        foreach (var change in plan.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Update))
        {
            // A general resolve considers a legacy provider sufficient. Explicit
            // installs tell VPM which side of a replacement the user selected.
            var installed = await ChangePackageAsync(plan.Path, change with { RequestedVersion = change.ResolvedVersion }, progress, token);
            if (ProjectRules.ToolFailed(installed, change.Id)) return installed;
        }
        return await ResolveAsync(plan.Path, progress, token);
    }
}
