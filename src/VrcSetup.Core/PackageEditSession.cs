namespace VrcSetup.Core;

public sealed record PackageWorkspaceRow(string Id, string Name, string InstalledVersion, string SelectedVersion, string Action, bool Required, bool Included, int TargetCount);

// Presentation-independent staged intent used by both the terminal and Windows editor.
public sealed class PackageEditSession
{
    private readonly AppConfiguration config;
    private readonly Dictionary<string, PackageDefinition> known;
    public ProjectSnapshot[] Projects { get; }
    public Dictionary<string, string> Selected { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Removed { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Exact { get; private set; }
    public string? Preset { get; private set; }
    public string[] ExtraImports { get; set; } = [];
    public bool HasPendingChanges => Exact || Selected.Count > 0 || Removed.Count > 0 || ExtraImports.Length > 0;
    public void AddDefinitions(IEnumerable<PackageDefinition> definitions) { foreach (var definition in definitions) known[definition.Id] = definition; }
    public PackageEditSession(ProjectSnapshot[] projects, AppConfiguration config, IEnumerable<PackageDefinition> definitions)
    {
        if (projects.Length == 0) throw new ArgumentException("Select at least one project.");
        Projects = projects; this.config = config.Copy();
        known = definitions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }
    public bool IsRequired(string id) => Projects.Any(x => ProjectRules.Foundation(x.Packages, config).Contains(id, StringComparer.OrdinalIgnoreCase));
    public PackageWorkspaceRow[] Rows(string query = "")
    {
        var ids = Projects.SelectMany(x => x.Packages.Keys.Concat(ProjectRules.Foundation(x.Packages, config))).Concat(known.Keys).Concat(Selected.Keys).Concat(Removed).Distinct(StringComparer.OrdinalIgnoreCase);
        return ids.Select(id =>
        {
            var installed = Projects.Select(x => x.InstalledVersions.GetValueOrDefault(id) ?? x.LockedPackages.GetValueOrDefault(id) ?? x.Packages.GetValueOrDefault(id) ?? "Not installed").Distinct().ToArray();
            var exists = Projects.Any(x => x.Packages.ContainsKey(id) || IsRequired(id) && x.LockedPackages.ContainsKey(id));
            var included = !Removed.Contains(id) && (Exact ? Selected.ContainsKey(id) || IsRequired(id) : exists || Selected.ContainsKey(id));
            var policies = Projects.Select(x => x.Policy.GetValueOrDefault(id) ?? x.InstalledVersions.GetValueOrDefault(id) ?? x.Packages.GetValueOrDefault(id) ?? "Not installed").Distinct().ToArray();
            var selected = Selected.GetValueOrDefault(id) ?? (policies.Length == 1 ? policies[0] : "Mixed — unchanged");
            var action = !included && exists ? "Will be removed" : Selected.ContainsKey(id) && !exists ? "Will be added" : Selected.ContainsKey(id) ? "Will be changed" : IsRequired(id) ? "Always included" : exists ? "Included" : "Available";
            return new PackageWorkspaceRow(id, known.GetValueOrDefault(id)?.DisplayName ?? id, installed.Length == 1 ? installed[0] : "Mixed", selected, action, IsRequired(id), included, Targets.GetValueOrDefault(id)?.Length ?? Projects.Length);
        }).Where(x => string.IsNullOrWhiteSpace(query) || (x.Id + " " + x.Name).Contains(query, StringComparison.OrdinalIgnoreCase))
          .OrderBy(x => x.Id == ProjectRules.BaseSdk ? 0 : x.Id == ProjectRules.AvatarSdk || x.Id == ProjectRules.WorldSdk ? 1 : x.Id == ProjectRules.Resolver ? 2 : x.Required ? 3 : x.Included || Removed.Contains(x.Id) ? 4 : 5).ThenBy(x => x.Name).ToArray();
    }
    public void SetVersion(string id, string version)
    {
        ProjectRules.ValidatePackageId(id);
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Choose a version or latest.");
        if (ProjectRules.SdkIds.Contains(id))
        {
            var linked = Projects.SelectMany(x => x.Packages.Keys).Concat(Selected.Keys).Append(ProjectRules.BaseSdk).Append(id).Where(ProjectRules.SdkIds.Contains).Distinct();
            foreach (var sdk in linked) { Selected[sdk] = version; Removed.Remove(sdk); }
        }
        else { Selected[id] = version; Removed.Remove(id); }
    }
    public void Toggle(string id)
    {
        var row = Rows().First(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (row.Included)
        {
            if (IsRequired(id)) throw new InvalidOperationException("The VRChat foundation stays included.");
            Selected.Remove(id); Removed.Add(id);
        }
        else SetVersion(id, known.GetValueOrDefault(id)?.SelectedVersion ?? "latest");
    }
    public void ApplyPreset(string name, IReadOnlyDictionary<string, string> packages, bool exact)
    {
        Reset(); Preset = name; Exact = exact;
        foreach (var entry in packages) Selected[entry.Key] = entry.Value;
    }
    public void SetTargets(string id, IEnumerable<string> projects)
    {
        if (Exact) throw new InvalidOperationException("Per-package project groups require Keep custom packages mode.");
        var selected = projects.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected.Any(x => !Projects.Any(p => p.Path.Equals(x, StringComparison.OrdinalIgnoreCase)))) throw new ArgumentException("Project is outside this selection.");
        foreach (var package in ProjectRules.SdkIds.Contains(id) ? ProjectRules.SdkIds.Where(x => Rows().Any(r => r.Id == x)) : new[] { id }) Targets[package] = selected;
    }
    public void Reset() { Selected.Clear(); Removed.Clear(); Targets.Clear(); ExtraImports = []; Exact = false; Preset = null; }
}
