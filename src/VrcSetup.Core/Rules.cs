using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace VrcSetup.Core;

public static partial class ProjectRules
{
    public const string BaseSdk = "com.vrchat.base";
    public const string AvatarSdk = "com.vrchat.avatars";
    public const string WorldSdk = "com.vrchat.worlds";
    public const string Resolver = "com.vrchat.core.vpm-resolver";
    public const string EasyLogin = "dev.foxscore.easy-login";
    public static readonly string[] BuildTargets = ["StandaloneWindows64", "Android", "iOS"];
    public static readonly string[] SdkIds = [BaseSdk, AvatarSdk, WorldSdk];

    public static string NormalizePath(string value, string? basePath = null)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A path is required.");
        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"', '\''));
        value = Regex.Replace(value, @"`(?=[\s&()\[\]{}$;,])", "");
        var full = Path.GetFullPath(value, basePath ?? Environment.CurrentDirectory);
        return full.Length > (Path.GetPathRoot(full)?.Length ?? 0) ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    public static void ValidateName(string name)
    {
        if (NameError(name) is { } error) throw new UserInputException("ProjectName", error);
    }
    public static string? NameError(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name for the project.";
        if (name is "." or ".." || Path.IsPathRooted(name) ||
            name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl) || name.EndsWith('.') || name.EndsWith(' '))
            return "Use a project name without these characters: < > : \" / \\ | ? * or a final dot.";
        if (Regex.IsMatch(name.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            return "This name is reserved by Windows. Choose another project name.";
        return null;
    }

    public static string SuggestName(string package, AppConfiguration config)
    {
        if (config.Naming.RememberUnityPackageNames && config.SavedProjectNames.TryGetValue(NormalizePath(package), out var remembered) && !string.IsNullOrWhiteSpace(remembered)) return remembered;
        var name = Path.GetFileNameWithoutExtension(package);
        foreach (var pattern in config.Naming.RegexRemovePatterns)
            name = Regex.Replace(name, pattern, "", RegexOptions.None, TimeSpan.FromSeconds(1));
        return config.Naming.DefaultPrefix + name.Trim() + config.Naming.DefaultSuffix;
    }

    public static string SuggestBuildTarget(string name) => Regex.IsMatch(name, @"(^|[^a-zA-Z])(Quest|Android)([^a-zA-Z]|$)", RegexOptions.IgnoreCase) ? "Android" : "StandaloneWindows64";
    public static void ValidatePackageId(string id)
    {
        if (!Regex.IsMatch(id, @"^[a-zA-Z0-9][a-zA-Z0-9._-]*$")) throw new ArgumentException($"Invalid package ID: {id}");
    }
    public static (string Id, string Version) ParseSpec(string spec)
    {
        var split = spec.LastIndexOf('@');
        var id = split < 0 ? spec.Trim() : spec[..split].Trim();
        var version = split < 0 ? "latest" : spec[(split + 1)..].Trim();
        ValidatePackageId(id);
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("A package version is required after @.");
        return (id, version);
    }

    public static string[] Foundation(IReadOnlyDictionary<string, string> packages, AppConfiguration config)
    {
        // Protect shared infrastructure; Avatars and Worlds are selectable workloads.
        var ids = new HashSet<string>(config.RequiredPackages.Where(x => !SdkIds.Contains(x)), StringComparer.OrdinalIgnoreCase) { BaseSdk, Resolver };
        return ids.OrderBy(x => x == BaseSdk ? 0 : x == AvatarSdk || x == WorldSdk ? 1 : x == Resolver ? 2 : 3).ToArray();
    }
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> selected, bool exact = false)
    {
        var desired = new Dictionary<string, string>(exact ? new Dictionary<string, string>() : current, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in selected) desired[entry.Key] = entry.Value;
        return desired;
    }
    public static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> values) => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));
    public static string FileIdentity(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing";
    }

    public static void EnsureContained(string root, string target)
    {
        root = NormalizePath(root);
        target = NormalizePath(target);
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Destination must be inside {root}.");
        var directory = new DirectoryInfo(target);
        while (directory is not null && directory.FullName.Length >= root.Length)
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Operation refused on a linked directory.");
            directory = directory.Parent;
        }
    }

    public static bool ToolFailed(ToolResult result, string? expectedPackage = null)
    {
        if (result.ExitCode != 0) return true;
        // Absence of legacy packages is harmless only when the requested add is confirmed.
        var output = result.Output;
        if (expectedPackage is not null && Regex.IsMatch(output, @"(?im)Added\s+package\s+" + Regex.Escape(expectedPackage) + @"\b"))
            output = Regex.Replace(output, @"(?im)^.*Failed to remove package [a-zA-Z0-9._-]+.*$", "");
        return Regex.IsMatch(output, @"(?im)\b(ERR|ERROR)\b.*(could not|not found|failed|exception)|^.*\b(failed to (add|install|resolve|download))\b");
    }
}

public sealed partial class SemanticVersion : IComparable<SemanticVersion>
{
    public string Original { get; }
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string[] PreRelease { get; }
    public bool IsPrerelease => PreRelease.Length > 0;
    public SemanticVersion(string value)
    {
        var match = Regex.Match(value, @"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
        if (!match.Success) throw new ArgumentException($"Invalid semantic version: {value}");
        Original = value; Major = int.Parse(match.Groups[1].Value);
        Minor = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        Patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        PreRelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
    }
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        foreach (var pair in new[] { (Major, other.Major), (Minor, other.Minor), (Patch, other.Patch) })
            if (pair.Item1.CompareTo(pair.Item2) is var comparison && comparison != 0) return comparison;
        if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        for (var i = 0; i < Math.Min(PreRelease.Length, other.PreRelease.Length); i++)
        {
            var leftNumeric = long.TryParse(PreRelease[i], out var left);
            var rightNumeric = long.TryParse(other.PreRelease[i], out var right);
            var comparison = leftNumeric && rightNumeric ? left.CompareTo(right) : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : string.CompareOrdinal(PreRelease[i], other.PreRelease[i]);
            if (comparison != 0) return comparison;
        }
        return PreRelease.Length.CompareTo(other.PreRelease.Length);
    }
    public static string[] Sort(IEnumerable<string> versions, bool includePrerelease = true) => versions.Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(x => { try { return new SemanticVersion(x); } catch (Exception error) when (error is ArgumentException or OverflowException) { return null; } })
        .Where(x => x is not null && (includePrerelease || !x.IsPrerelease)).OrderDescending().Select(x => x!.Original).ToArray();
}

public sealed class ProjectPlanner(IProjectWorkspace workspace, IPackageCatalog catalog)
{
    public async Task<ProjectPlan> PlanAsync(string project, IReadOnlyDictionary<string, string> selected, AppConfiguration config,
        bool exact = false, IEnumerable<string>? remove = null, string? preset = null, string[]? extraImports = null,
        CreationRequest? creation = null, CancellationToken cancellationToken = default)
    {
        project = ProjectRules.NormalizePath(project);
        var snapshot = creation is null ? await workspace.ReadAsync(project, cancellationToken) : new ProjectSnapshot { Path = project, Name = creation.Name, Status = "New" };
        if (creation is not null)
        {
            ProjectRules.ValidateName(creation.Name);
            ProjectRules.EnsureContained(config.UnityProjectsRoot, project);
            if (Directory.Exists(project) || File.Exists(project)) throw new IOException("The destination already exists. Choose another name.");
            if (creation.EmptyProject ? creation.UnityPackagePath.Length != 0 : !File.Exists(creation.UnityPackagePath) || !creation.UnityPackagePath.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) throw new IOException("UnityPackage not found.");
            foreach (var target in creation.PublishingPlatforms.Append(creation.BuildTarget))
                if (!ProjectRules.BuildTargets.Contains(target)) throw new ArgumentException($"Unsupported platform: {target}");
        }
        selected = PackagePresence.Selection(selected);
        var desired = PackagePresence.Selection(ProjectRules.Merge(snapshot.Policy.Count > 0 ? snapshot.Policy : snapshot.Packages, selected, exact || creation is not null));
        var required = ProjectRules.Foundation(snapshot.Packages.Count > 0 ? snapshot.Packages : desired, config);
        foreach (var id in remove ?? [])
        {
            if (required.Contains(id, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"Cannot remove required package: {id}");
            desired.Remove(id);
        }
        foreach (var id in required)
            if (!desired.ContainsKey(id)) desired[id] = snapshot.InstalledVersions.GetValueOrDefault(id) ?? snapshot.LockedPackages.GetValueOrDefault(id) ?? "latest";
        var sdkSelections = selected.Where(x => ProjectRules.SdkIds.Contains(x.Key)).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sdkSelections.Length > 1) throw new InvalidOperationException("Select one version policy for linked VRChat SDK components.");
        if (sdkSelections.Length == 1)
            foreach (var sdkId in desired.Keys.Where(ProjectRules.SdkIds.Contains).ToArray()) desired[sdkId] = sdkSelections[0];
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sdkIds = desired.Keys.Where(ProjectRules.SdkIds.Contains).ToArray();
        var changedSdk = sdkIds.Any(id => !snapshot.Packages.TryGetValue(id, out var old) || old != desired[id] || desired[id] == "latest");
        if (changedSdk)
        {
            var pinned = sdkIds.Select(id => desired[id]).Where(x => x != "latest").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (pinned.Length > 1) throw new InvalidOperationException("Select the same version for linked VRChat SDK components.");
            HashSet<string>? common = null;
            foreach (var id in sdkIds)
            {
                var versions = await catalog.GetVersionsAsync(id, cancellationToken);
                if (common is null) common = new(versions, StringComparer.OrdinalIgnoreCase); else common.IntersectWith(versions);
            }
            var version = pinned.FirstOrDefault() ?? SemanticVersion.Sort(common ?? [], false).FirstOrDefault();
            if (version is null || !common!.Contains(version)) throw new InvalidOperationException("No compatible version is available for every linked SDK component.");
            foreach (var id in sdkIds) resolved[id] = version;
        }
        foreach (var entry in desired)
        {
            ProjectRules.ValidatePackageId(entry.Key);
            if (resolved.ContainsKey(entry.Key)) continue;
            if (snapshot.Packages.TryGetValue(entry.Key, out var unchanged) && unchanged == entry.Value && entry.Value != "latest")
            {
                // An exact pin is authoritative even if an import replaced the embedded version.
                try { _ = new SemanticVersion(entry.Value); resolved[entry.Key] = entry.Value; }
                catch (ArgumentException) { resolved[entry.Key] = snapshot.InstalledVersions.GetValueOrDefault(entry.Key) ?? snapshot.LockedPackages.GetValueOrDefault(entry.Key) ?? unchanged; }
                continue;
            }
            var versions = await catalog.GetVersionsAsync(entry.Key, cancellationToken);
            var version = entry.Value == "latest" ? SemanticVersion.Sort(versions, false).FirstOrDefault() : entry.Value;
            if (version is null || !versions.Contains(version, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"Package/version not found: {entry.Key}@{entry.Value}");
            resolved[entry.Key] = version;
        }
        var changes = snapshot.Packages.Keys.Union(desired.Keys, StringComparer.OrdinalIgnoreCase).Union(remove ?? [], StringComparer.OrdinalIgnoreCase).Where(id => !PackagePresence.IsGenerated(id)).Select(id =>
        {
            var old = snapshot.InstalledVersions.GetValueOrDefault(id) ?? snapshot.LockedPackages.GetValueOrDefault(id) ?? snapshot.Packages.GetValueOrDefault(id);
            var version = resolved.GetValueOrDefault(id);
            var kind = !desired.ContainsKey(id) ? ChangeKind.Remove : old is null || !snapshot.Packages.ContainsKey(id) ? ChangeKind.Add : snapshot.InstalledVersions.GetValueOrDefault(id) == version ? ChangeKind.Keep : ChangeKind.Update;
            return new PackageChange(id, old, desired.GetValueOrDefault(id), version, kind);
        }).OrderBy(x => x.Id == ProjectRules.BaseSdk ? 0 : ProjectRules.SdkIds.Contains(x.Id) ? 1 : x.Id == ProjectRules.Resolver ? 2 : 3).ThenBy(x => x.Id).ToArray();
        foreach (var import in (extraImports ?? []).Concat(creation?.ExtraImports ?? []))
            if (!File.Exists(import) || !import.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) throw new IOException($"Extra UnityPackage not found: {import}");
        return new ProjectPlan
        {
            Path = project, Name = snapshot.Name, ExpectedFingerprint = snapshot.Fingerprint, DesiredPolicy = ProjectRules.Freeze(desired), Changes = changes,
            PresetName = preset, PresetDefinition = selected.Count > 0 ? ProjectRules.Freeze(selected) : null, Creation = creation,
            ExtraImports = extraImports ?? [], RequiredPackages = required,
            UnityEditorPath = creation?.UnityEditorPath ?? config.UnityEditorPath,
            ImportFingerprints = ProjectRules.Freeze((extraImports ?? []).Concat(creation?.ExtraImports ?? []).Concat(creation is null || creation.EmptyProject ? [] : new[] { creation.UnityPackagePath }).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(x => x, ProjectRules.FileIdentity)),
            NeedsEasyLoginRepository = desired.ContainsKey(ProjectRules.EasyLogin)
        };
    }

    public async Task<OperationPlan> PlanBulkAsync(IEnumerable<string> projects, IReadOnlyDictionary<string, string> selected, AppConfiguration config,
        bool exact = false, IEnumerable<string>? remove = null, IReadOnlyDictionary<string, string[]>? packageTargets = null,
        string? preset = null, string[]? extraImports = null, CancellationToken cancellationToken = default)
    {
        var plans = new List<ProjectPlan>();
        foreach (var path in projects.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var choices = selected.Where(x => exact || packageTargets is null || !packageTargets.TryGetValue(x.Key, out var targets) || targets.Contains(path, StringComparer.OrdinalIgnoreCase)).ToDictionary();
            var removals = (remove ?? []).Where(id => packageTargets is null || !packageTargets.TryGetValue(id, out var targets) || targets.Contains(path, StringComparer.OrdinalIgnoreCase));
            plans.Add(await PlanAsync(path, choices, config, exact, removals, preset, extraImports, cancellationToken: cancellationToken));
        }
        if (plans.Count == 0) throw new ArgumentException("Select at least one project.");
        return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
    }
}
