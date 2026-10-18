using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrcSetup.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new PackagePolicyConverter() }
    };
}

// Old configurations used either an array, string values or { "version": ... }.
public sealed class PackagePolicyConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var value in document.RootElement.EnumerateArray()) result[value.GetString()!] = "latest";
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var entry in document.RootElement.EnumerateObject())
                result[entry.Name] = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : entry.Value.GetProperty("version").GetString()!;
        return result;
    }
    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) writer.WriteString(entry.Key, entry.Value);
        writer.WriteEndObject();
    }
}

public sealed class AppConfiguration
{
    public int SchemaVersion { get; set; } = 2;
    public Dictionary<string, string> VpmPackages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DefaultPackages { get; set; } = [];
    public List<string> RequiredPackages { get; set; } = ["com.vrchat.base", "com.vrchat.avatars", "com.vrchat.core.vpm-resolver"];
    public List<string> FavoritePackages { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> PackagePresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CreationRecipe> CreationRecipes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string UnityEditorPath { get; set; } = "";
    public string UnityProjectsRoot { get; set; } = "";
    public string? UnityPackagesFolder { get; set; }
    public NamingSettings Naming { get; set; } = new();
    public Dictionary<string, string> SavedProjectNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string LastProjectName { get; set; } = "";
    public string LastUnityPackagePath { get; set; } = "";
    public string ProjectLibrarySort { get; set; } = "recent";
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "system";
    public string PackageLibraryView { get; set; } = "grid";
    public bool ManualImportPath { get; set; }
    public bool AutoHideCompletedImports { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool AutomaticUpdates { get; set; } = true;
    public bool IncludeBetaUpdates { get; set; } = true;
    public DiagnosticSettings Diagnostics { get; set; } = new();
    [JsonIgnore] public string? Revision { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }
    public AppConfiguration Copy()
    {
        var copy = JsonSerializer.Deserialize<AppConfiguration>(JsonSerializer.Serialize(this, JsonDefaults.Options), JsonDefaults.Options)!;
        copy.Revision = Revision;
        return copy;
    }
}

public sealed class NamingSettings
{
    public string DefaultPrefix { get; set; } = "";
    public string DefaultSuffix { get; set; } = "";
    public List<string> RegexRemovePatterns { get; set; } = [];
    public bool RememberUnityPackageNames { get; set; } = true;
}

public sealed class DiagnosticSettings
{
    public bool DeveloperMode { get; set; }
    public bool Verbose { get; set; }
    public bool TraceTools { get; set; }
}

public sealed record CreationRecipe
{
    public string Preset { get; init; } = "";
    public Dictionary<string, string> Packages { get; init; } = [];
    public string BuildTarget { get; init; } = "StandaloneWindows64";
    public string[] PublishingPlatforms { get; init; } = ["StandaloneWindows64"];
    public string[] ExtraImports { get; init; } = [];
    public string NamePrefix { get; init; } = "";
    public string NameSuffix { get; init; } = "";
}

public sealed record ProjectSnapshot
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public string Kind { get; init; } = "Unity";
    public string UnityVersion { get; init; } = "Unknown";
    public string PackageMetadataSource { get; init; } = "None";
    public string UnityVersionSource { get; init; } = "None";
    public string Status { get; init; } = "Ready";
    public DateTime LastUpdatedUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string Fingerprint { get; init; } = "";
    public Dictionary<string, string> Packages { get; init; } = [];
    public Dictionary<string, string> LockedPackages { get; init; } = [];
    public Dictionary<string, string> InstalledVersions { get; init; } = [];
    public Dictionary<string, string> PackageDisplayNames { get; init; } = [];
    public Dictionary<string, string> Policy { get; init; } = [];
    public string? PresetName { get; init; }
    public bool ProfileModified { get; init; }
    public bool PresetChanged { get; init; }
    public string ProfileStatus => string.Join(" · ", new[] { PresetName, ProfileModified ? "Project packages modified" : null, PresetChanged ? "Preset definition changed" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string ConfigurationFingerprint { get; init; } = "";
    public bool Incomplete { get; init; }
    public int PackageCount => Packages.Count;
}

public sealed record PackageDefinition(string Id, string DisplayName, string LatestVersion, string Description = "")
{
    public string[] Versions { get; init; } = [];
    public string SelectedVersion { get; init; } = "latest";
    public DateTime? UpdatedUtc { get; init; }
}

public enum ChangeKind { Add, Update, Remove, Keep }
public sealed record PackageChange(string Id, string? InstalledVersion, string? RequestedVersion, string? ResolvedVersion, ChangeKind Kind);
public enum OperationStatus { Completed, CompletedWithWarnings, Failed, Cancelled, Skipped, Unchanged }
public enum EventSeverity { Info, Warning, Error }
public sealed record OperationEvent(DateTime Timestamp, string Phase, string Message, EventSeverity Severity = EventSeverity.Info, string? Project = null, string? RawMessage = null);
public sealed record ProjectOutcome(string Project, string Path, OperationStatus Status, string Message, string? LogFile = null, string? BackupPath = null);
public sealed record OperationReport(string Id, DateTime CompletedUtc, ProjectOutcome[] Results, string[] NotProcessed, string? ReportFile = null)
{
    public OperationPlan? Plan { get; init; }
    public bool Success => NotProcessed.Length == 0 && Results.Length > 0 && Results.All(x => x.Status is OperationStatus.Completed or OperationStatus.CompletedWithWarnings or OperationStatus.Unchanged);
    public bool Cancelled => NotProcessed.Length > 0 || Results.Any(x => x.Status == OperationStatus.Cancelled);
}

public sealed record CreationRequest(string UnityPackagePath, string Name, string TargetPath, string UnityEditorPath, string BuildTarget, string[] PublishingPlatforms, string[] ExtraImports, bool EmptyProject = false);
public sealed record ProjectPlan
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string ExpectedFingerprint { get; init; }
    public required IReadOnlyDictionary<string, string> DesiredPolicy { get; init; }
    public required PackageChange[] Changes { get; init; }
    public string? PresetName { get; init; }
    public IReadOnlyDictionary<string, string>? PresetDefinition { get; init; }
    public CreationRequest? Creation { get; init; }
    public CreationRequest? RecoverySource { get; init; }
    public string? RecoveryOriginalSource { get; init; }
    public bool PreparationComplete { get; init; }
    public string[] ExtraImports { get; init; } = [];
    public bool NeedsEasyLoginRepository { get; init; }
    public string[] RequiredPackages { get; init; } = [];
    public string UnityEditorPath { get; init; } = "";
    public IReadOnlyDictionary<string, string> ImportFingerprints { get; init; } = new Dictionary<string, string>();
    public bool HasChanges => Creation is not null || RecoverySource is not null || ExtraImports.Length > 0 || Changes.Any(x => x.Kind != ChangeKind.Keep);
}
public sealed record OperationPlan(string Id, DateTime CreatedUtc, ProjectPlan[] Projects);

public sealed record ToolResult(int ExitCode, string StdOut, string StdErr)
{
    public string Output => string.Join(Environment.NewLine, new[] { StdOut, StdErr }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
public sealed record BackupRecord(string Directory, IReadOnlyDictionary<string, string?> ManifestFiles, string? AssetArchive = null);

public interface IPackageCatalog
{
    Task<PackageDefinition[]> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<string[]> GetVersionsAsync(string packageId, CancellationToken cancellationToken = default);
}

public interface IProjectWorkspace
{
    Task<ProjectSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default);
    bool IsBusy(string path);
    Task<IDisposable> AcquireAsync(string path, CancellationToken cancellationToken);
    Task<BackupRecord> BackupAsync(ProjectPlan plan, CancellationToken cancellationToken);
    Task<bool> RestoreAsync(string project, BackupRecord backup, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task SaveProfileAsync(ProjectPlan plan, CancellationToken cancellationToken);
    Task SetStepAsync(string path, string step, bool done, CancellationToken cancellationToken);
    Task CompleteAsync(string path, CancellationToken cancellationToken);
}

public interface IProjectDriver
{
    Task PrepareAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task CreateAsync(CreationRequest request, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task<ToolResult> ChangePackageAsync(string project, PackageChange change, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task<ToolResult> ResolveAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task ImportAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task SettleAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
    Task<bool> RegisterAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken);
}
