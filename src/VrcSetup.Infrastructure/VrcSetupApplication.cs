using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// The composition root is shared; neither client has its own planner or executor.
public sealed partial class VrcSetupApplication
{
    public AppPaths Paths { get; }
    public ConfigurationStore Configuration { get; }
    public ToolRunner Tools { get; }
    public DiagnosticLog Diagnostics { get; }
    public PackageCatalog Catalog { get; }
    public PackageInspectionService Inspection { get; }
    public ImportQueue Imports { get; }
    public ProjectWorkspace Projects { get; }
    public ProjectPlanner Planner { get; }
    public ProjectExecutor Executor { get; }
    public UnityProjectDriver Unity { get; }
    public Func<HttpClient>? UpdateHttpClientFactory { get; init; }
    public string? UpdateSmokeSpec { get; init; }
    private readonly string[] diagnosticFlags;
    public bool AllowBackgroundCatalogRefresh => !diagnosticFlags.Contains("--no-catalog-refresh");
    public bool AllowBackgroundUpdateChecks => !diagnosticFlags.Contains("--no-update-check");
    public bool AllowEditorDiscovery => !diagnosticFlags.Contains("--no-editor-discovery");
    public VrcSetupApplication(string? home = null, string[]? diagnosticFlags = null, IUnityPackageInspector? packageInspector = null)
    {
        this.diagnosticFlags = diagnosticFlags ?? [];
        Paths = new(home); Configuration = new(Paths); Diagnostics = new(Paths); Tools = new(Diagnostics, Paths.VrcGetPath); Projects = new(Paths, Diagnostics);
        Catalog = new(Paths, Tools, Configuration, allowRepositorySetup: AllowBackgroundCatalogRefresh); Inspection = new(Paths, packageInspector); Unity = new(Paths, Tools, Projects);
        Projects.Resolve = Unity.ResolveAsync;
        Planner = new(Projects, Catalog); Executor = new(Projects, Unity); Imports = new(this);
    }
    public void ConfigureDiagnostics(AppConfiguration config)
    {
        Diagnostics.Configure(config.Diagnostics);
        Diagnostics.DeveloperMode |= diagnosticFlags.Contains("--developer");
        Diagnostics.Verbose |= diagnosticFlags.Contains("--verbose");
        Diagnostics.TraceTools |= diagnosticFlags.Contains("--trace-tools");
    }
    public async Task<OperationReport> ApplyAsync(OperationPlan plan, IProgress<OperationEvent>? progress = null, CancellationToken cancellationToken = default, bool rememberCreation = true)
    {
        using var log = new OperationLog(Paths, plan.Id, progress);
        var report = await Executor.ApplyAsync(plan, log, cancellationToken);
        foreach (var result in report.Results.Where(x => rememberCreation && (x.Status is OperationStatus.Completed or OperationStatus.CompletedWithWarnings)))
        {
            var creation = plan.Projects.First(x => x.Path == result.Path).Creation;
            if (creation is null || creation.EmptyProject) continue;
            try
            {
                var config = await Configuration.LoadAsync(CancellationToken.None);
                var baseline = config.Copy();
                config.LastProjectName = creation.Name; config.LastUnityPackagePath = creation.UnityPackagePath;
                if (config.Naming.RememberUnityPackageNames) config.SavedProjectNames[creation.UnityPackagePath] = creation.Name;
                await Configuration.MergeAsync(baseline, config, CancellationToken.None);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                log.Report(new(DateTime.UtcNow, "Settings", "Project ready; recent-name settings could not be saved: " + error.Message, EventSeverity.Warning, result.Path));
                report = report with { Results = report.Results.Select(x => x.Path == result.Path ? x with { Status = OperationStatus.CompletedWithWarnings, Message = x.Message + " Recent-name preferences were not saved." } : x).ToArray() };
            }
        }
        var reportFile = Path.Combine(Paths.Logs, "operation-" + plan.Id + ".json");
        report = report with { Results = report.Results.Select(x => x with { LogFile = log.Path }).ToArray(), ReportFile = reportFile, Plan = plan };
        await AtomicFile.WriteJsonAsync(reportFile, report, CancellationToken.None);
        LogRetention.Prune(Paths, reportFile);
        return report;
    }
    public string[] ExtraImports(AppConfiguration config, string? main = null)
    {
        if (string.IsNullOrWhiteSpace(config.UnityPackagesFolder)) return [];
        var folder = ProjectRules.NormalizePath(config.UnityPackagesFolder, Paths.SourceRoot ?? Paths.PayloadRoot);
        if (!Directory.Exists(folder)) throw new IOException("Extra imports folder does not exist: " + folder);
        return Directory.EnumerateFiles(folder, "*.unitypackage").Order(StringComparer.OrdinalIgnoreCase).Where(x => main is null || !ProjectRules.NormalizePath(x).Equals(ProjectRules.NormalizePath(main), StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public async Task<OperationPlan> PlanEmptyProjectAsync(string name, AppConfiguration config, string? preset = null, string buildTarget = "StandaloneWindows64", CancellationToken cancellationToken = default)
    {
        ProjectRules.ValidateName(name);
        if (string.IsNullOrWhiteSpace(config.UnityProjectsRoot)) throw new IOException("Choose the projects folder in Settings first.");
        UnityProjectDriver.ValidateEditor(config.UnityEditorPath);
        if (!ProjectRules.BuildTargets.Contains(buildTarget)) throw new ArgumentException("Unsupported platform.");
        if (!UnityProjectDriver.HasBuildSupport(config.UnityEditorPath, buildTarget)) throw new IOException("Unity build support is missing for " + buildTarget);
        var selected = preset is null ? new Dictionary<string, string>(config.VpmPackages) : new(config.PackagePresets.GetValueOrDefault(preset) ?? throw new ArgumentException("Preset not found."));
        var target = Path.Combine(ProjectRules.NormalizePath(config.UnityProjectsRoot), name);
        var request = new CreationRequest("", name, target, config.UnityEditorPath, buildTarget, [buildTarget], [], EmptyProject: true);
        var project = await Planner.PlanAsync(target, selected, config, exact: true, preset: preset ?? "My default packages", creation: request, cancellationToken: cancellationToken);
        return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [project]);
    }
    public async Task<OperationPlan> PlanCreationAsync(string package, AppConfiguration config, string? name = null, string? preset = null,
        IReadOnlyDictionary<string, string>? packages = null, string? buildTarget = null, string[]? publishingPlatforms = null, string? recipeName = null, CancellationToken cancellationToken = default)
    {
        package = ProjectRules.NormalizePath(package);
        await UnityPackageFormat.ValidateAsync(package, cancellationToken);
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
        var destination = await new ImportDestinationInspector(Projects).InspectAsync(targetPath, package, cancellationToken);
        if (destination.Kind == ImportDestinationKind.Recoverable || destination.CanImportMissing)
            return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [await PlanResumeAsync(request, selected, config, preset, imports, cancellationToken)]);
        if (destination.Kind != ImportDestinationKind.New) throw new IOException(destination.Message);
        var project = await Planner.PlanAsync(targetPath, selected, config, exact: true, preset: preset ?? "My default packages", creation: request, cancellationToken: cancellationToken);
        return new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [project]);
    }
    public async Task<ProjectPlan> PlanResumeAsync(CreationRequest source, IReadOnlyDictionary<string, string> packages, AppConfiguration config, string? preset,
        IEnumerable<string> imports, CancellationToken token = default, string? recordedSource = null)
    {
        // A replacement archive is allowed only after the caller explicitly selected it.
        // Validate ownership against the recorded source, while fingerprinting the selected file.
        var state = await new ImportDestinationInspector(Projects).InspectAsync(source.TargetPath, recordedSource ?? source.UnityPackagePath, token, source.EmptyProject);
        if (state.Kind != ImportDestinationKind.Recoverable && !state.CanImportMissing) throw new IOException(state.Message);
        var marker = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(source.TargetPath, ".vrcsetup", "state.json"), token);
        if (source.EmptyProject && marker?.ProjectName != source.Name) throw new IOException("The project import state does not match this import. Its files have been preserved.");
        var current = await Projects.ReadAsync(source.TargetPath, token);
        var preserved = new Dictionary<string, string>(packages);
        foreach (var entry in packages)
            if ((current.InstalledVersions.GetValueOrDefault(entry.Key) ?? current.LockedPackages.GetValueOrDefault(entry.Key)) is { Length: > 0 } installed) preserved[entry.Key] = installed;
        var missingImports = (source.EmptyProject ? Array.Empty<string>() : [source.UnityPackagePath]).Concat(imports).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan = await Planner.PlanAsync(source.TargetPath, preserved, config, exact: false, preset: preset,
            extraImports: missingImports, cancellationToken: token);
        return plan with { UnityEditorPath = source.UnityEditorPath, RecoverySource = source, RecoveryOriginalSource = recordedSource,
            PreparationComplete = marker?.Steps.GetValueOrDefault("nunit") == true && marker.Steps.GetValueOrDefault("vpm") && plan.Changes.All(x => x.Kind == ChangeKind.Keep) };
    }
}
