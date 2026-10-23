using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class AppPaths
{
    public string Home { get; }
    public string Resources { get; }
    public string Tools { get; }
    public string ConfigFile => Path.Combine(Home, "config.json");
    public string Cache => Path.Combine(Home, "cache");
    public string Logs => Path.Combine(Home, "logs");
    public string Locks => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrcSetup", "locks");
    public string? SourceRoot { get; }
    public string PayloadRoot { get; }
    public bool AllowLegacyImport { get; }
    public static AppPaths ForInstallation(string? home, string payloadRoot) => new(
        home ?? Environment.GetEnvironmentVariable("VRCSETUP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrcSetup"), payloadRoot);

    public AppPaths(string? home = null, string? baseDirectory = null)
    {
        var basePath = baseDirectory ?? AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(basePath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VrcSetup.slnx"))) { SourceRoot = directory.FullName; break; }
        }
        PayloadRoot = FindPayloadRoot(basePath);
        var explicitHome = home ?? Environment.GetEnvironmentVariable("VRCSETUP_HOME");
        AllowLegacyImport = string.IsNullOrWhiteSpace(explicitHome);
        var portable = File.Exists(Path.Combine(PayloadRoot, ".vrcsetup-portable"));
        string? installedHome = null;
        var installation = Path.Combine(PayloadRoot, ".vrcsetup-install.json");
        if (File.Exists(installation))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(installation));
                if (doc.RootElement.TryGetProperty("Home", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } homePath && Path.IsPathFullyQualified(homePath)) installedHome = homePath;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        Home = ProjectRules.NormalizePath(explicitHome ?? installedHome ?? (portable ? Path.Combine(PayloadRoot, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrcSetup")));
        Resources = new[] { Path.Combine(basePath, "Resources"), Path.Combine(PayloadRoot, "app", "cli", "Resources"), Path.Combine(SourceRoot ?? PayloadRoot, "src", "VrcSetup.Infrastructure", "Resources") }.FirstOrDefault(Directory.Exists) ?? Path.Combine(basePath, "Resources");
        Tools = Path.Combine(basePath, "Tools");
    }

    private static string FindPayloadRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, ".vrcsetup-payload.json"))) return directory.FullName;
        return start;
    }
    public string BridgePath => File.Exists(Path.Combine(Resources, "VrcSetupPostImport.cs")) ? Path.Combine(Resources, "VrcSetupPostImport.cs") : Path.Combine(SourceRoot ?? PayloadRoot, "src", "VrcSetup.UnityBridge", "VrcSetupPostImport.cs");
    public string? VrcGetPath => new[] { Path.Combine(Tools, "vrc-get.exe"), Path.Combine(SourceRoot ?? PayloadRoot, "src", "VrcSetup.Infrastructure", "Tools", "vrc-get.exe") }.FirstOrDefault(File.Exists);
    public string? FindLegacyConfiguration() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup", "setup-scripts", "config", "vrcsetup.json"),
        Path.Combine(SourceRoot ?? PayloadRoot, "setup-scripts", "config", "vrcsetup.json")
    }.FirstOrDefault(File.Exists);
}

public static class AtomicFile
{
    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(file, value, JsonDefaults.Options, cancellationToken); await file.FlushAsync(cancellationToken); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken);
    }
    public static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing";
    public static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(100, cancellationToken); }
        }
    }
    public static string PathKey(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProjectRules.NormalizePath(path).ToUpperInvariant())));
}

public sealed class ConfigurationStore(AppPaths paths)
{
    public string? ImportedFrom { get; private set; }
    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var initializationLock = await AtomicFile.LockAsync(Path.Combine(paths.Home, "initialize.lock"), cancellationToken);
        if (File.Exists(paths.ConfigFile))
        {
            var config = await AtomicFile.ReadJsonAsync<AppConfiguration>(paths.ConfigFile, cancellationToken) ?? throw new IOException("Configuration is empty.");
            if (config.SchemaVersion < 2)
            {
                var migration = Path.Combine(paths.Home, "migration"); Directory.CreateDirectory(migration);
                var snapshot = Path.Combine(migration, "config-schema-" + config.SchemaVersion + "-" + AtomicFile.Hash(paths.ConfigFile) + ".json");
                if (!File.Exists(snapshot)) File.Copy(paths.ConfigFile, snapshot);
            }
            Normalize(config); config.Revision = AtomicFile.Hash(paths.ConfigFile); return config;
        }
        var legacy = paths.AllowLegacyImport ? paths.FindLegacyConfiguration() : null;
        var defaults = Path.Combine(paths.Resources, "vrcsetup.defaults");
        var initial = await AtomicFile.ReadJsonAsync<AppConfiguration>(legacy ?? defaults, cancellationToken) ?? new AppConfiguration
        {
            VpmPackages = new() { [ProjectRules.BaseSdk] = "latest", [ProjectRules.AvatarSdk] = "latest", [ProjectRules.Resolver] = "latest" }
        };
        if (legacy is not null)
        {
            var legacyRoot = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(legacy)!)!.FullName)!.FullName;
            if (!string.IsNullOrWhiteSpace(initial.UnityPackagesFolder)) initial.UnityPackagesFolder = ProjectRules.NormalizePath(initial.UnityPackagesFolder, legacyRoot);
            if (!string.IsNullOrWhiteSpace(initial.UnityEditorPath)) initial.UnityEditorPath = ProjectRules.NormalizePath(initial.UnityEditorPath, legacyRoot);
            if (!string.IsNullOrWhiteSpace(initial.UnityProjectsRoot)) initial.UnityProjectsRoot = ProjectRules.NormalizePath(initial.UnityProjectsRoot, legacyRoot);
            Directory.CreateDirectory(Path.Combine(paths.Home, "migration"));
            var legacyBackup = Path.Combine(paths.Home, "migration", "legacy-config.json");
            if (!File.Exists(legacyBackup)) File.Copy(legacy, legacyBackup);
            else if (AtomicFile.Hash(legacyBackup) != AtomicFile.Hash(legacy)) File.Copy(legacy, Path.Combine(paths.Home, "migration", "legacy-config-" + Guid.NewGuid().ToString("N") + ".json"));
            ImportedFrom = legacy;
        }
        Normalize(initial);
        await SaveAsync(initial, cancellationToken);
        return initial;
    }
    public async Task SaveAsync(AppConfiguration config, CancellationToken cancellationToken = default)
    {
        using var configLock = await AtomicFile.LockAsync(Path.Combine(paths.Home, "config.lock"), cancellationToken);
        var current = AtomicFile.Hash(paths.ConfigFile);
        if (config.Revision is not null && config.Revision != current) throw new IOException("Settings changed in another window. Reload before saving.");
        if (config.Revision is null && current != "missing") throw new IOException("Settings already exist. Reload before saving.");
        Normalize(config);
        await AtomicFile.WriteJsonAsync(paths.ConfigFile, config, cancellationToken);
        config.Revision = AtomicFile.Hash(paths.ConfigFile);
    }
    public async Task<AppConfiguration> MergeAsync(AppConfiguration baseline, AppConfiguration edited, CancellationToken cancellationToken = default)
    {
        using var configLock = await AtomicFile.LockAsync(Path.Combine(paths.Home, "config.lock"), cancellationToken);
        var current = await AtomicFile.ReadJsonAsync<AppConfiguration>(paths.ConfigFile, cancellationToken) ?? throw new IOException("Settings are unavailable. Reload before saving.");
        var original = JsonSerializer.SerializeToNode(baseline, JsonDefaults.Options)!.AsObject();
        var changes = JsonSerializer.SerializeToNode(edited, JsonDefaults.Options)!.AsObject();
        var merged = JsonSerializer.SerializeToNode(current, JsonDefaults.Options)!.AsObject();
        foreach (var item in changes)
        {
            if (JsonNode.DeepEquals(original[item.Key], item.Value)) continue;
            if (!JsonNode.DeepEquals(merged[item.Key], original[item.Key]) && !JsonNode.DeepEquals(merged[item.Key], item.Value))
                throw new IOException("Setting changed in another window: " + item.Key + ". Reload before saving.");
            merged[item.Key] = item.Value?.DeepClone();
        }
        var saved = merged.Deserialize<AppConfiguration>(JsonDefaults.Options)!;
        Normalize(saved); await AtomicFile.WriteJsonAsync(paths.ConfigFile, saved, cancellationToken);
        saved.Revision = AtomicFile.Hash(paths.ConfigFile); return saved;
    }
    public async Task<AppConfiguration> ImportAsync(string source, CancellationToken cancellationToken = default)
    {
        source = ProjectRules.NormalizePath(source);
        var imported = await AtomicFile.ReadJsonAsync<AppConfiguration>(source, cancellationToken) ?? throw new IOException("Configuration not found.");
        Directory.CreateDirectory(Path.Combine(paths.Home, "migration"));
        if (File.Exists(paths.ConfigFile)) File.Copy(paths.ConfigFile, Path.Combine(paths.Home, "migration", $"config-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
        imported.Revision = AtomicFile.Hash(paths.ConfigFile);
        if (!string.IsNullOrWhiteSpace(imported.UnityPackagesFolder)) imported.UnityPackagesFolder = ProjectRules.NormalizePath(imported.UnityPackagesFolder, ResolveLegacyBase(source));
        if (!string.IsNullOrWhiteSpace(imported.UnityEditorPath)) imported.UnityEditorPath = ProjectRules.NormalizePath(imported.UnityEditorPath, ResolveLegacyBase(source));
        if (!string.IsNullOrWhiteSpace(imported.UnityProjectsRoot)) imported.UnityProjectsRoot = ProjectRules.NormalizePath(imported.UnityProjectsRoot, ResolveLegacyBase(source));
        await SaveAsync(imported, cancellationToken);
        ImportedFrom = source;
        return imported;
    }
    public async Task<AppConfiguration> ResetAsync(CancellationToken cancellationToken = default)
    {
        var defaults = await AtomicFile.ReadJsonAsync<AppConfiguration>(Path.Combine(paths.Resources, "vrcsetup.defaults"), cancellationToken) ?? new();
        defaults.Revision = AtomicFile.Hash(paths.ConfigFile);
        if (File.Exists(paths.ConfigFile))
        {
            Directory.CreateDirectory(Path.Combine(paths.Home, "migration"));
            File.Copy(paths.ConfigFile, Path.Combine(paths.Home, "migration", "before-reset-" + Guid.NewGuid().ToString("N") + ".json"));
        }
        await SaveAsync(defaults, cancellationToken); return defaults;
    }
    private static string ResolveLegacyBase(string source) => Path.GetFileName(Path.GetDirectoryName(source)) == "config" && Path.GetFileName(Directory.GetParent(Path.GetDirectoryName(source)!)!.FullName) == "setup-scripts"
        ? Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(source)!)!.FullName)!.FullName : Path.GetDirectoryName(source)!;

    private static void Normalize(AppConfiguration config)
    {
        if (config.SchemaVersion > 2) throw new IOException("These settings were saved by a newer app. Update the app before saving changes.");
        if (config.PackagePresets?.Any(x => x.Value is null) == true) throw new IOException("A saved preset is incomplete. The original settings have been preserved.");
        config.SchemaVersion = 2;
        config.VpmPackages = new(config.VpmPackages ?? [], StringComparer.OrdinalIgnoreCase);
        config.PackagePresets = new(config.PackagePresets ?? [], StringComparer.OrdinalIgnoreCase);
        config.CreationRecipes = new(config.CreationRecipes ?? [], StringComparer.OrdinalIgnoreCase);
        config.SavedProjectNames = new(config.SavedProjectNames ?? [], StringComparer.OrdinalIgnoreCase);
        config.Naming ??= new(); config.Naming.RegexRemovePatterns ??= [];
        config.Diagnostics ??= new();
        config.RequiredPackages ??= [ProjectRules.BaseSdk, ProjectRules.AvatarSdk, ProjectRules.Resolver];
        config.FavoritePackages ??= []; config.DefaultPackages ??= [];
        config.ProjectLibrarySort = config.ProjectLibrarySort == "name" ? "name" : "recent";
        config.Language = config.Language is "it" or "en" ? config.Language : "system";
    }
}

public sealed class OperationLog : IProgress<OperationEvent>, IDisposable
{
    private readonly StreamWriter writer;
    private readonly object gate = new();
    private readonly IProgress<OperationEvent>? target;
    public string Path { get; }
    public OperationLog(AppPaths paths, string id, IProgress<OperationEvent>? target = null)
    {
        Directory.CreateDirectory(paths.Logs);
        Path = System.IO.Path.Combine(paths.Logs, $"vrcsetup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{id}.log");
        writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        this.target = target;
    }
    public void Report(OperationEvent value)
    {
        lock (gate) writer.WriteLine($"{value.Timestamp:O} [{value.Severity}] [{value.Phase}] {value.Project} {value.RawMessage ?? value.Message}");
        target?.Report(value);
    }
    public void Dispose() { lock (gate) writer.Dispose(); }
}
