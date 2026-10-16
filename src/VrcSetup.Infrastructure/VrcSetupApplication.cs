using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// The composition root is shared; neither client has its own planner or executor.
public sealed class VrcSetupApplication
{
    public AppPaths Paths { get; }
    public ConfigurationStore Configuration { get; }
    public ToolRunner Tools { get; }
    public DiagnosticLog Diagnostics { get; }
    public PackageCatalog Catalog { get; }
    public ProjectWorkspace Projects { get; }
    public ProjectPlanner Planner { get; }
    public ProjectExecutor Executor { get; }
    public UnityProjectDriver Unity { get; }
    private readonly string[] diagnosticFlags;
    public bool AllowBackgroundCatalogRefresh => !diagnosticFlags.Contains("--no-catalog-refresh");
    public VrcSetupApplication(string? home = null, string[]? diagnosticFlags = null)
    {
        this.diagnosticFlags = diagnosticFlags ?? [];
        Paths = new(home); Configuration = new(Paths); Diagnostics = new(Paths); Tools = new(Diagnostics); Projects = new(Paths, Diagnostics);
        Catalog = new(Paths, Tools, Configuration); Unity = new(Paths, Tools, Projects);
        Projects.Resolve = Unity.ResolveAsync;
        Planner = new(Projects, Catalog); Executor = new(Projects, Unity);
    }
    public void ConfigureDiagnostics(AppConfiguration config)
    {
        Diagnostics.Configure(config.Diagnostics);
        Diagnostics.DeveloperMode |= diagnosticFlags.Contains("--developer");
        Diagnostics.Verbose |= diagnosticFlags.Contains("--verbose");
        Diagnostics.TraceTools |= diagnosticFlags.Contains("--trace-tools");
    }
    public async Task<OperationReport> ApplyAsync(OperationPlan plan, IProgress<OperationEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        using var log = new OperationLog(Paths, plan.Id, progress);
        var report = await Executor.ApplyAsync(plan, log, cancellationToken);
        foreach (var result in report.Results.Where(x => x.Status is OperationStatus.Completed or OperationStatus.CompletedWithWarnings))
        {
            var creation = plan.Projects.First(x => x.Path == result.Path).Creation;
            if (creation is null) continue;
            try
            {
                var config = await Configuration.LoadAsync(CancellationToken.None);
                config.LastProjectName = creation.Name; config.LastUnityPackagePath = creation.UnityPackagePath;
                if (config.Naming.RememberUnityPackageNames) config.SavedProjectNames[creation.UnityPackagePath] = creation.Name;
                await Configuration.SaveAsync(config, CancellationToken.None);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Report(new(DateTime.UtcNow, "Settings", "Project ready; recent-name settings could not be saved: " + error.Message, EventSeverity.Warning, result.Path));
                report = report with { Results = report.Results.Select(x => x.Path == result.Path ? x with { Status = OperationStatus.CompletedWithWarnings, Message = x.Message + " Recent-name preferences were not saved." } : x).ToArray() };
            }
        }
        var reportFile = Path.Combine(Paths.Logs, "operation-" + plan.Id + ".json");
        report = report with { Results = report.Results.Select(x => x with { LogFile = log.Path }).ToArray(), ReportFile = reportFile };
        await AtomicFile.WriteJsonAsync(reportFile, report, CancellationToken.None);
        return report;
    }
    public string[] ExtraImports(AppConfiguration config, string? main = null)
    {
        if (string.IsNullOrWhiteSpace(config.UnityPackagesFolder)) return [];
        var folder = ProjectRules.NormalizePath(config.UnityPackagesFolder, Paths.SourceRoot ?? Paths.PayloadRoot);
        if (!Directory.Exists(folder)) throw new IOException("Extra imports folder does not exist: " + folder);
        return Directory.EnumerateFiles(folder, "*.unitypackage").Order(StringComparer.OrdinalIgnoreCase).Where(x => main is null || !ProjectRules.NormalizePath(x).Equals(ProjectRules.NormalizePath(main), StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public async Task<OperationPlan> PlanCreationAsync(string package, AppConfiguration config, string? name = null, string? preset = null,
        IReadOnlyDictionary<string, string>? packages = null, string? buildTarget = null, string[]? publishingPlatforms = null, string? recipeName = null, CancellationToken cancellationToken = default)
    {
        package = ProjectRules.NormalizePath(package);
        var recipe = recipeName is null ? null : config.CreationRecipes.GetValueOrDefault(recipeName) ?? throw new ArgumentException("Creation recipe not found.");
        name ??= ProjectRules.SuggestName(package, config);
        if (recipe is not null) name = recipe.NamePrefix + name + recipe.NameSuffix;
        ProjectRules.ValidateName(name);
        if (string.IsNullOrWhiteSpace(config.UnityProjectsRoot)) throw new IOException("Choose the projects folder in Settings first.");
        preset ??= recipe?.Preset;
        var selected = packages is not null ? new Dictionary<string, string>(packages) : recipe?.Packages.Count > 0 ? new(recipe.Packages) :
            !string.IsNullOrWhiteSpace(preset) ? new(config.PackagePresets.GetValueOrDefault(preset) ?? throw new ArgumentException("Preset not found.")) : new(config.VpmPackages);
        buildTarget ??= recipe?.BuildTarget ?? ProjectRules.SuggestBuildTarget(package);
        publishingPlatforms ??= recipe?.PublishingPlatforms ?? [buildTarget];
        UnityProjectDriver.ValidateEditor(config.UnityEditorPath);
        foreach (var target in publishingPlatforms.Append(buildTarget).Distinct())
            if (!UnityProjectDriver.HasBuildSupport(config.UnityEditorPath, target)) throw new IOException($"Unity build support is missing for {target}. Install it in Unity Hub before importing.");
        var imports = ExtraImports(config, package).Concat(recipe?.ExtraImports ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targetPath = Path.Combine(ProjectRules.NormalizePath(config.UnityProjectsRoot), name);
        var request = new CreationRequest(package, name, targetPath, config.UnityEditorPath, buildTarget, publishingPlatforms, imports);
        var project = await Planner.PlanAsync(targetPath, selected, config, exact: true, preset: preset ?? "My default packages", creation: request, cancellationToken: cancellationToken);
        return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [project]);
    }
}
