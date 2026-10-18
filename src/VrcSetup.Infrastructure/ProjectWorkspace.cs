using System.IO.Compression;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class ProjectWorkspace(AppPaths paths, DiagnosticLog? diagnostics = null) : IProjectWorkspace
{
    private static readonly string[] ManifestNames = ["Packages/manifest.json", "Packages/vpm-manifest.json", "Packages/packages-lock.json"];
    private static readonly HashSet<string> Exclusions = new(["Assets", "Library", "Logs", "obj", "Packages", "ProjectSettings", "Temp", "UserSettings", ".git", ".vrcsetup", ".vpm-validation-cache", ".work"], StringComparer.OrdinalIgnoreCase);
    public Func<string, IProgress<OperationEvent>?, CancellationToken, Task<ToolResult>>? Resolve { get; set; }

    public Task<ProjectSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() => Read(path, cancellationToken), cancellationToken);
    public ProjectSnapshot Read(string path, CancellationToken cancellationToken = default)
    {
        path = ProjectRules.NormalizePath(path);
        if (!Directory.Exists(Path.Combine(path, "Assets")) || !Directory.Exists(Path.Combine(path, "Packages"))) throw new IOException($"Not a Unity project: {path}");
        cancellationToken.ThrowIfCancellationRequested();
        var direct = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var locked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var source = "None";
        var status = "No VPM manifest";
        var manifest = Path.Combine(path, "Packages", "vpm-manifest.json");
        if (File.Exists(manifest))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                direct = ReadDependencyMap(document.RootElement, "dependencies"); locked = ReadDependencyMap(document.RootElement, "locked");
                source = "VpmManifest"; status = "Ready";
            }
            catch (JsonException) { status = "Invalid VPM manifest"; }
        }
        foreach (var folder in Directory.EnumerateDirectories(Path.Combine(path, "Packages")))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var packageJson = Path.Combine(folder, "package.json");
            if (!File.Exists(packageJson)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
                if (document.RootElement.TryGetProperty("name", out var name) && document.RootElement.TryGetProperty("version", out var version))
                {
                    var id = name.GetString()!; installed[id] = version.GetString()!;
                    if (document.RootElement.TryGetProperty("displayName", out var display) && display.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(display.GetString())) displayNames[id] = display.GetString()!;
                }
            }
            catch (JsonException) { }
        }
        if (direct.Count == 0 && installed.Count > 0)
        {
            direct = new(installed, StringComparer.OrdinalIgnoreCase); source = "EmbeddedPackages";
            status = status == "Invalid VPM manifest" ? "Recovered (invalid manifest)" : "Recovered";
        }
        var unityVersion = "Unknown";
        var versionSource = "None";
        var projectVersion = Path.Combine(path, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(projectVersion))
        {
            var match = Regex.Match(File.ReadLines(projectVersion).FirstOrDefault() ?? "", @"^m_EditorVersion:\s*(.+)$");
            if (match.Success) { unityVersion = match.Groups[1].Value.Trim(); versionSource = "ProjectVersion"; }
        }
        if (unityVersion == "Unknown")
            foreach (var projectFile in Directory.EnumerateFiles(path, "*.csproj").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var match = Regex.Match(File.ReadAllText(projectFile), @"<UnityVersion>\s*([^<]+)\s*</UnityVersion>");
                if (match.Success) { unityVersion = match.Groups[1].Value.Trim(); versionSource = "GeneratedProject"; break; }
            }
        var updated = Directory.GetLastWriteTimeUtc(path);
        foreach (var file in new[] { manifest, projectVersion }) if (File.Exists(file) && File.GetLastWriteTimeUtc(file) > updated) updated = File.GetLastWriteTimeUtc(file);
        var avatar = direct.ContainsKey(ProjectRules.AvatarSdk); var world = direct.ContainsKey(ProjectRules.WorldSdk);
        var kind = avatar && world ? "Avatar + World" : avatar ? "Avatar" : world ? "World" : direct.ContainsKey(ProjectRules.BaseSdk) ? "VRChat" : "Unity";
        var stateFile = Path.Combine(path, ".vrcsetup", "state.json");
        var incomplete = false;
        if (File.Exists(stateFile))
        {
            try { using var state = JsonDocument.Parse(File.ReadAllText(stateFile)); incomplete = TryProperty(state.RootElement, "completed") is not { ValueKind: JsonValueKind.True }; }
            catch (JsonException) { incomplete = true; }
        }
        string? preset = null; var modified = false; var presetChanged = false;
        var policy = new Dictionary<string, string>(direct, StringComparer.OrdinalIgnoreCase);
        var profileFile = Path.Combine(path, ".vrcsetup", "package-profile.json");
        if (File.Exists(profileFile))
        {
            try
            {
                using var profile = JsonDocument.Parse(File.ReadAllText(profileFile));
                preset = TryProperty(profile.RootElement, "Preset")?.GetString();
                var saved = ReadDependencyMap(profile.RootElement, "Installed");
                var savedPolicy = ReadDependencyMap(profile.RootElement, "Policy");
                var savedManifest = ReadDependencyMap(profile.RootElement, "ManifestPolicy");
                var visibleInstalled = direct.Keys.Concat(saved.Keys.Where(locked.ContainsKey)).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(x => x, x => installed.GetValueOrDefault(x) ?? locked.GetValueOrDefault(x) ?? direct[x], StringComparer.OrdinalIgnoreCase);
                modified = !MapsEqual(saved, visibleInstalled);
                if (savedManifest.Count > 0 && !MapsEqual(savedManifest, direct)) modified = true;
                foreach (var entry in savedPolicy)
                    if (visibleInstalled.TryGetValue(entry.Key, out var actual) && saved.GetValueOrDefault(entry.Key) == actual &&
                        (savedManifest.Count == 0 || savedManifest.GetValueOrDefault(entry.Key) == direct.GetValueOrDefault(entry.Key))) policy[entry.Key] = entry.Value;
                if (File.Exists(paths.ConfigFile))
                {
                    var config = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(paths.ConfigFile), JsonDefaults.Options);
                    var definition = preset is "My default packages" or "My package set" ? config?.VpmPackages : config?.PackagePresets.GetValueOrDefault(preset ?? "");
                    var oldDefinition = ReadDependencyMap(profile.RootElement, "PresetDefinition");
                    presetChanged = definition is not null && oldDefinition.Count > 0 && !MapsEqual(oldDefinition, definition);
                }
            }
            catch (JsonException) { modified = true; }
        }
        return new() { Path = path, Name = Path.GetFileName(path), Kind = kind, UnityVersion = unityVersion, UnityVersionSource = versionSource, PackageMetadataSource = source,
            Status = status, LastUpdatedUtc = updated, Fingerprint = Fingerprint(path), Packages = direct, LockedPackages = locked, InstalledVersions = installed, PackageDisplayNames = displayNames,
            Incomplete = incomplete, PresetName = preset, ProfileModified = modified, PresetChanged = presetChanged, Policy = policy, ConfigurationFingerprint = AtomicFile.Hash(paths.ConfigFile) };
    }

    public static JsonElement? TryProperty(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var item in element.EnumerateObject()) if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase)) return item.Value;
        return null;
    }
    public static Dictionary<string, string> ReadDependencyMap(JsonElement root, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (TryProperty(root, name) is not { ValueKind: JsonValueKind.Object } map) return result;
        foreach (var entry in map.EnumerateObject())
        {
            var value = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : TryProperty(entry.Value, "version")?.GetString();
            if (!string.IsNullOrWhiteSpace(value)) result[entry.Name] = value;
        }
        return result;
    }
    private static bool MapsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) => left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var value) && value == x.Value);
    public static string Fingerprint(string path)
    {
        var files = ManifestNames.Concat(new[] { "ProjectSettings/ProjectVersion.txt", ".vrcsetup/state.json", ".vrcsetup/package-profile.json" }).Select(x => Path.Combine(path, x)).ToList();
        if (Directory.Exists(Path.Combine(path, "Packages"))) files.AddRange(Directory.EnumerateDirectories(Path.Combine(path, "Packages")).Where(x => !new DirectoryInfo(x).Attributes.HasFlag(FileAttributes.ReparsePoint)).Select(x => Path.Combine(x, "package.json")));
        if (!File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"))) files.AddRange(Directory.EnumerateFiles(path, "*.csproj"));
        var text = string.Join("|", files.Order(StringComparer.OrdinalIgnoreCase).Select(x => Path.GetRelativePath(path, x) + ":" + AtomicFile.Hash(x)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public async Task<ProjectSnapshot[]> ScanAsync(string root, string sort = "recent", bool refresh = false, int maxDepth = 2, CancellationToken cancellationToken = default)
    {
        root = ProjectRules.NormalizePath(root);
        if (!Directory.Exists(root)) throw new IOException("Projects folder not found.");
        var cacheFile = Path.Combine(paths.Cache, "projects-" + AtomicFile.PathKey(root) + ".json");
        ProjectSnapshot[] previous = [];
        try { if (!refresh) previous = await AtomicFile.ReadJsonAsync<ProjectSnapshot[]>(cacheFile, cancellationToken) ?? []; } catch (JsonException) { }
        var cache = previous.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0));
        var result = new List<ProjectSnapshot>();
        while (queue.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(item.Path, ".vrcsetup", "state.json")) && (!Directory.Exists(Path.Combine(item.Path, "Assets")) || !Directory.Exists(Path.Combine(item.Path, "Packages"))))
            {
                try
                {
                    var state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(item.Path, ".vrcsetup", "state.json"), cancellationToken);
                    if (state is { Completed: false }) result.Add(new() { Path = item.Path, Name = Path.GetFileName(item.Path), Status = "Incomplete", Incomplete = true, Fingerprint = Fingerprint(item.Path), LastUpdatedUtc = state.LastUpdatedAt });
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                { diagnostics?.Write("Project state", item.Path, error); result.Add(new() { Path = item.Path, Name = Path.GetFileName(item.Path), Status = "Needs attention", Incomplete = true }); }
                continue;
            }
            if (Directory.Exists(Path.Combine(item.Path, "Assets")) && Directory.Exists(Path.Combine(item.Path, "Packages")))
            {
                try
                {
                    var fingerprint = Fingerprint(item.Path);
                    if (cache.TryGetValue(item.Path, out var cached) && cached.Fingerprint == fingerprint && cached.ConfigurationFingerprint == AtomicFile.Hash(paths.ConfigFile))
                        result.Add(cached with { LastUpdatedUtc = new[] { Directory.GetLastWriteTimeUtc(item.Path), cached.LastUpdatedUtc }.Max() });
                    else result.Add(await ReadAsync(item.Path, cancellationToken));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
                { diagnostics?.Write("Project library", item.Path, error); result.Add(new() { Path = item.Path, Name = Path.GetFileName(item.Path), Status = "Needs attention" }); }
                continue;
            }
            if (item.Depth > maxDepth) continue;
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(item.Path))
                    if (!Exclusions.Contains(Path.GetFileName(folder)) && !new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint)) queue.Enqueue((folder, item.Depth + 1));
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            { diagnostics?.Write("Project folder", item.Path, error); }
        }
        await AtomicFile.WriteJsonAsync(cacheFile, result, cancellationToken);
        return sort == "name" ? result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray() : result.OrderByDescending(x => x.LastUpdatedUtc).ThenBy(x => x.Name).ToArray();
    }

    public bool IsBusy(string path)
    {
        var lockFile = Path.Combine(path, "Temp", "UnityLockfile");
        if (File.Exists(lockFile))
        {
            try { using var handle = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
        return GetUnityProcesses(path).Length > 0;
    }
    public static (int Pid, string CommandLine)[] GetUnityProcesses(string project)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<(int, string)>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'Unity.exe'");
        foreach (var process in searcher.Get())
        {
            using (process)
            {
                var command = process["CommandLine"]?.ToString() ?? "";
                var match = Regex.Match(command, "-projectPath\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var path = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                try { if (ProjectRules.NormalizePath(path).Equals(ProjectRules.NormalizePath(project), StringComparison.OrdinalIgnoreCase)) result.Add((Convert.ToInt32(process["ProcessId"]), command)); } catch (ArgumentException) { }
            }
        }
        return result.ToArray();
    }
    public async Task<IDisposable> AcquireAsync(string path, CancellationToken cancellationToken) => await AtomicFile.LockAsync(Path.Combine(paths.Locks, "project-" + AtomicFile.PathKey(path) + ".lock"), cancellationToken, TimeSpan.FromSeconds(1));

    public async Task<BackupRecord> BackupAsync(ProjectPlan plan, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(plan.Path, ".vrcsetup", "backups", "operation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var manifests = new Dictionary<string, string?>();
        foreach (var relative in ManifestNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.Combine(plan.Path, relative); var saved = Path.Combine(folder, Path.GetFileName(file));
            if (File.Exists(file)) { File.Copy(file, saved); manifests[relative] = saved; } else manifests[relative] = null;
        }
        string? assets = null;
        if (plan.Creation is null && (plan.ExtraImports.Length > 0 || plan.NeedsEasyLoginRepository && Directory.Exists(Path.Combine(plan.Path, "Assets", "EASY LOGIN"))))
        {
            assets = Path.Combine(folder, "assets.zip");
            await Task.Run(() => CreateAssetBackup(plan.Path, assets, cancellationToken), cancellationToken);
        }
        var record = new BackupRecord(folder, manifests, assets);
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "backup.json"), record, cancellationToken);
        return record;
    }
    private static void CreateAssetBackup(string project, string archivePath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var root in new[] { "Assets", "ProjectSettings" })
        {
            var pending = new Queue<string>(); pending.Enqueue(Path.Combine(project, root));
            while (pending.TryDequeue(out var folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder)) continue;
                if (new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Cannot back up linked asset folders safely.");
                foreach (var child in Directory.EnumerateDirectories(folder)) pending.Enqueue(child);
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Cannot back up linked asset files safely.");
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(project, file).Replace('\\', '/'), CompressionLevel.Fastest);
                }
            }
        }
    }
    public async Task<bool> RestoreAsync(string project, BackupRecord backup, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        project = ProjectRules.NormalizePath(project);
        ProjectRules.EnsureContained(Path.Combine(project, ".vrcsetup", "backups"), backup.Directory);
        if (IsBusy(project)) return false;
        if (backup.ManifestFiles.Keys.Any(x => !ManifestNames.Contains(x))) throw new IOException("Backup contains an unexpected manifest destination.");
        foreach (var file in backup.ManifestFiles)
        {
            var destination = ProjectRules.NormalizePath(file.Key, project); ProjectRules.EnsureContained(project, destination);
            if (file.Value is not null) { ProjectRules.EnsureContained(backup.Directory, file.Value); if (!File.Exists(file.Value)) throw new IOException("Backup manifest is missing."); }
        }
        var stage = Path.Combine(backup.Directory, "restore-stage-" + Guid.NewGuid().ToString("N"));
        var displaced = Path.Combine(backup.Directory, "before-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage); Directory.CreateDirectory(displaced);
        var swapped = new List<string>();
        var originals = new Dictionary<string, string?>();
        var manifestsMutated = false;
        try
        {
        if (backup.AssetArchive is not null)
        {
            ProjectRules.EnsureContained(backup.Directory, backup.AssetArchive);
            using var archive = ZipFile.OpenRead(backup.AssetArchive);
            // Validate every entry before touching either original tree.
            foreach (var entry in archive.Entries)
            {
                var destination = ProjectRules.NormalizePath(entry.FullName, stage); ProjectRules.EnsureContained(stage, destination);
                var relative = Path.GetRelativePath(stage, destination).Replace('\\', '/');
                if (!relative.StartsWith("Assets/", StringComparison.Ordinal) && !relative.StartsWith("ProjectSettings/", StringComparison.Ordinal)) throw new IOException("Unexpected path in asset backup.");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Linked files are not allowed in an asset backup.");
            }
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ProjectRules.NormalizePath(entry.FullName, stage);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); entry.ExtractToFile(destination, false);
            }
            foreach (var name in new[] { "Assets", "ProjectSettings" }) Directory.CreateDirectory(Path.Combine(stage, name));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (IsBusy(project)) return false;
        foreach (var file in backup.ManifestFiles)
        {
            var destination = Path.Combine(project, file.Key); var original = Path.Combine(displaced, Path.GetFileName(file.Key));
            if (File.Exists(destination)) { File.Copy(destination, original); originals[file.Key] = original; } else originals[file.Key] = null;
        }
        if (backup.AssetArchive is not null)
        {
            // Keep displaced assets until recovery is verified; cancellation cannot remove them.
            foreach (var name in new[] { "Assets", "ProjectSettings" })
            {
                var original = Path.Combine(project, name); ProjectRules.EnsureContained(project, original);
                if (Directory.Exists(original)) Directory.Move(original, Path.Combine(displaced, name));
                swapped.Add(name);
                Directory.Move(Path.Combine(stage, name), original);
            }
        }
        manifestsMutated = true;
        foreach (var file in backup.ManifestFiles)
        {
            var destination = Path.Combine(project, file.Key);
            if (file.Value is null) { if (File.Exists(destination)) File.Delete(destination); }
            else File.Copy(file.Value, destination, true);
        }
        if (Resolve is null) throw new IOException("Recovery resolver is unavailable.");
        var result = await Resolve(project, progress, cancellationToken);
        if (ProjectRules.ToolFailed(result)) throw new IOException("VPM resolve did not complete during recovery.");
        progress?.Report(new(DateTime.UtcNow, "Recovery", "Backup restored. Previous trees preserved at " + displaced, Project: project));
        return true;
        }
        catch
        {
            // Roll back the restore itself. Never delete the displaced original trees.
            foreach (var name in swapped.AsEnumerable().Reverse())
            {
                var destination = Path.Combine(project, name);
                if (Directory.Exists(destination)) Directory.Move(destination, Path.Combine(backup.Directory, "failed-restore-" + name + "-" + Guid.NewGuid().ToString("N")));
                if (Directory.Exists(Path.Combine(displaced, name))) Directory.Move(Path.Combine(displaced, name), destination);
            }
            if (manifestsMutated)
                foreach (var original in originals)
                { var file = Path.Combine(project, original.Key); if (original.Value is null) { if (File.Exists(file)) File.Delete(file); } else File.Copy(original.Value, file, true); }
            throw;
        }
        finally
        {
            // Only the validated extraction stage is disposable; recovery copies remain.
            if (Directory.Exists(stage)) { ProjectRules.EnsureContained(backup.Directory, stage); Directory.Delete(stage, true); }
        }
    }

    public async Task SaveProfileAsync(ProjectPlan plan, CancellationToken cancellationToken)
    {
        var installed = await ReadAsync(plan.Path, cancellationToken);
        var visible = installed.Packages.Keys.Concat(plan.RequiredPackages).Distinct().Where(installed.InstalledVersions.ContainsKey).ToDictionary(x => x, x => installed.InstalledVersions[x]);
        await AtomicFile.WriteJsonAsync(Path.Combine(plan.Path, ".vrcsetup", "package-profile.json"), new
        { Schema = 2, Preset = plan.PresetName ?? "Custom packages", Policy = plan.DesiredPolicy, Installed = visible, ManifestPolicy = installed.Packages, PresetDefinition = plan.PresetDefinition, UpdatedUtc = DateTime.UtcNow }, cancellationToken);
    }
    public async Task SetStepAsync(string path, string step, bool done, CancellationToken cancellationToken)
    {
        var file = Path.Combine(path, ".vrcsetup", "state.json");
        var state = await AtomicFile.ReadJsonAsync<SetupState>(file, cancellationToken) ?? new() { ProjectName = Path.GetFileName(path), StartedAt = DateTime.UtcNow };
        state.Completed = false; state.CompletedAt = null; state.LastUpdatedAt = DateTime.UtcNow; state.Steps[step] = done;
        await AtomicFile.WriteJsonAsync(file, state, cancellationToken);
    }
    public async Task CompleteAsync(string path, CancellationToken cancellationToken)
    {
        var file = Path.Combine(path, ".vrcsetup", "state.json");
        var state = await AtomicFile.ReadJsonAsync<SetupState>(file, cancellationToken) ?? throw new IOException("Setup state missing.");
        // Package-only operations need no create step, but every execution phase must be verified.
        state.Completed = new[] { "nunit", "vpm", "importMain", "importExtras", "finalize" }.All(x => state.Steps.GetValueOrDefault(x));
        if (!state.Completed) throw new IOException("Project has unfinished setup steps.");
        state.CompletedAt = state.LastUpdatedAt = DateTime.UtcNow;
        await AtomicFile.WriteJsonAsync(file, state, cancellationToken);
    }
    public async Task CleanupAsync(string project, string root, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("Explicit confirmation is required to delete an incomplete project.");
        project = ProjectRules.NormalizePath(project); ProjectRules.EnsureContained(root, project);
        using var projectLock = await AcquireAsync(project, cancellationToken);
        if (IsBusy(project)) throw new IOException("Project is open in Unity. Close it before cleanup.");
        var state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(project, ".vrcsetup", "state.json"), cancellationToken);
        if (state is null || state.Completed) throw new IOException("Only a project with an incomplete VRCSETUP state can be cleaned up.");
        Directory.Delete(project, true);
    }
    public async Task CloseUnityAsync(string project, int pid, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("Explicit confirmation is required to close a Unity editor.");
        project = ProjectRules.NormalizePath(project);
        if (!GetUnityProcesses(project).Any(x => x.Pid == pid)) throw new IOException("This PID is not a Unity editor using the selected project.");
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true); await process.WaitForExitAsync(cancellationToken);
    }
}

public sealed class SetupState
{
    public int SchemaVersion { get; set; } = 2;
    public string ProjectName { get; set; } = "";
    public string UnityPackagePath { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Completed { get; set; }
    public Dictionary<string, bool> Steps { get; set; } = [];
}
