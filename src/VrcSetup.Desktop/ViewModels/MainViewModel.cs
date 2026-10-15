using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public interface IDesktopInteraction
{
    Task<string?> PickFileAsync(string extension);
    Task<string?> PickFolderAsync();
    Task<bool> ConfirmAsync(string title, string content, string action);
    Task<bool> ReviewAsync(OperationPlan plan);
    Task<string?> TextAsync(string title, string initial = "");
    Task<string?> ChooseAsync(string title, string[] items);
    Task<string[]?> ChooseManyAsync(string title, string[] items);
    Task<string?> VersionAsync(string package, string[] versions, string current);
    Task<string[]?> ChooseProjectsAsync(string title, ProjectSnapshot[] projects, string[] selected);
    Task ShowErrorAsync(string message);
    void CloseApplication();
    Task<Dictionary<string, string>?> EditPackagesAsync(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions);
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly VrcSetupApplication app;
    private readonly IDesktopInteraction ui;
    private AppConfiguration config = new();
    private ProjectSnapshot[] allProjects = [];
    private PackageEditSession? session;
    private CancellationTokenSource? operation;
    private CancellationTokenSource? refresh;
    private readonly SynchronizationContext context;
    public ObservableCollection<ProjectSnapshot> Projects { get; } = [];
    public ObservableCollection<PackageWorkspaceRow> Packages { get; } = [];
    public ObservableCollection<PackageDefinition> Registry { get; } = [];
    public ObservableCollection<string> Presets { get; } = [];
    public ObservableCollection<string> Recipes { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];
    public ObservableCollection<ProjectSnapshot> SelectedProjects { get; } = [];
    [ObservableProperty] public partial string CurrentPage { get; set; } = "Projects";
    [ObservableProperty] public partial string ProjectFilter { get; set; } = "";
    [ObservableProperty] public partial string PackageFilter { get; set; } = "";
    [ObservableProperty] public partial string PackageFile { get; set; } = "";
    [ObservableProperty] public partial string ProjectName { get; set; } = "";
    [ObservableProperty] public partial string SelectedPreset { get; set; } = "My default packages";
    [ObservableProperty] public partial string SelectedRecipe { get; set; } = "No recipe";
    [ObservableProperty] public partial string BuildTarget { get; set; } = "StandaloneWindows64";
    [ObservableProperty] public partial bool PublishWindows { get; set; } = true;
    [ObservableProperty] public partial bool PublishAndroid { get; set; }
    [ObservableProperty] public partial bool PublishIos { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Ready";
    [ObservableProperty] public partial string WorkspaceTitle { get; set; } = "Packages";
    [ObservableProperty] public partial string WorkspaceProfile { get; set; } = "";
    private string lastReport = "";
    [ObservableProperty] public partial PackageWorkspaceRow? SelectedPackage { get; set; }
    [ObservableProperty] public partial PackageDefinition? SelectedDefinition { get; set; }
    [ObservableProperty] public partial string UnityEditorPath { get; set; } = "";
    [ObservableProperty] public partial string ProjectsRoot { get; set; } = "";
    [ObservableProperty] public partial string ExtraImportsFolder { get; set; } = "";
    [ObservableProperty] public partial string NamingPrefix { get; set; } = "";
    [ObservableProperty] public partial string NamingSuffix { get; set; } = "";
    [ObservableProperty] public partial string NamingPatterns { get; set; } = "";
    [ObservableProperty] public partial bool RememberNames { get; set; } = true;
    [ObservableProperty] public partial string Theme { get; set; } = "System";
    [ObservableProperty] public partial string LastLog { get; set; } = "";
    public string[] PlatformChoices => ProjectRules.BuildTargets;
    public PlatformOption[] PlatformOptions => [new("Windows", "StandaloneWindows64"), new("Android / Quest", "Android"), new("iOS", "iOS")];
    public string[] ThemeChoices => ["System", "Light", "Dark"];
    public string[] SortChoices => ["recent", "name"];
    [ObservableProperty] public partial string ProjectSort { get; set; } = "recent";
    public VrcSetupApplication Application => app;

    public MainViewModel(VrcSetupApplication app, IDesktopInteraction interaction)
    { this.app = app; ui = interaction; context = SynchronizationContext.Current ?? new(); }
    public async Task InitializeAsync()
    {
        await Guard(async () =>
        {
            config = await app.Configuration.LoadAsync(); LoadSettings(); LoadPresetNames();
            await LoadRegistry();
            if (!string.IsNullOrWhiteSpace(config.UnityProjectsRoot) && Directory.Exists(config.UnityProjectsRoot)) await RefreshProjectsCore();
            if (string.IsNullOrWhiteSpace(config.UnityEditorPath) || string.IsNullOrWhiteSpace(config.UnityProjectsRoot)) CurrentPage = "Settings";
            if (app.Configuration.ImportedFrom is { } imported) AppendActivity(new(DateTime.UtcNow, "Migration", "Legacy settings imported; original preserved: " + imported));
        });
    }
    private void LoadSettings()
    {
        UnityEditorPath = config.UnityEditorPath; ProjectsRoot = config.UnityProjectsRoot; ExtraImportsFolder = config.UnityPackagesFolder ?? "";
        NamingPrefix = config.Naming.DefaultPrefix; NamingSuffix = config.Naming.DefaultSuffix; NamingPatterns = string.Join(Environment.NewLine, config.Naming.RegexRemovePatterns);
        RememberNames = config.Naming.RememberUnityPackageNames; Theme = config.Theme; ProjectSort = config.ProjectLibrarySort;
    }
    private void LoadPresetNames()
    {
        Replace(Presets, new[] { "My default packages" }.Concat(config.PackagePresets.Keys.Order()));
        Replace(Recipes, new[] { "No recipe" }.Concat(config.CreationRecipes.Keys.Order()));
        if (!Presets.Contains(SelectedPreset)) SelectedPreset = "My default packages";
    }
    private async Task LoadRegistry() => Replace(Registry, await app.Catalog.KnownAsync(config, session?.Projects.Select(x => (IReadOnlyDictionary<string, string>)x.Packages)));
    partial void OnProjectFilterChanged(string value) => FilterProjects();
    partial void OnPackageFilterChanged(string value) => RenderWorkspace();
    partial void OnPackageFileChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        ProjectName = ProjectRules.SuggestName(value, config); BuildTarget = ProjectRules.SuggestBuildTarget(value);
        PublishAndroid = BuildTarget == "Android"; PublishWindows = !PublishAndroid;
    }
    partial void OnSelectedPresetChanged(string value) => creationOverrides = null;
    partial void OnSelectedRecipeChanged(string value)
    {
        if (!config.CreationRecipes.TryGetValue(value, out var recipe)) { creationOverrides = null; return; }
        SelectedPreset = string.IsNullOrWhiteSpace(recipe.Preset) || !config.PackagePresets.ContainsKey(recipe.Preset) ? "My default packages" : recipe.Preset;
        creationOverrides = recipe.Packages.Count > 0 ? new(recipe.Packages) : null;
        BuildTarget = recipe.BuildTarget; PublishWindows = recipe.PublishingPlatforms.Contains("StandaloneWindows64"); PublishAndroid = recipe.PublishingPlatforms.Contains("Android"); PublishIos = recipe.PublishingPlatforms.Contains("iOS");
    }
    private void FilterProjects() => Replace(Projects, allProjects.Where(x => (x.Name + " " + x.Kind + " " + x.UnityVersion).Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase)));
    private void RenderWorkspace()
    {
        var selected = SelectedPackage?.Id;
        Replace(Packages, session?.Rows(PackageFilter) ?? []);
        SelectedPackage = Packages.FirstOrDefault(x => x.Id == selected) ?? Packages.FirstOrDefault();
    }
    [RelayCommand] private async Task RefreshProjects() => await Guard(RefreshProjectsCore);
    private async Task RefreshProjectsCore()
    {
        allProjects = await app.Projects.ScanAsync(config.UnityProjectsRoot, ProjectSort, true); FilterProjects();
        Status = $"{allProjects.Length} projects";
    }
    [RelayCommand] private async Task ChangeSort()
    {
        await Guard(async () => { config.ProjectLibrarySort = ProjectSort; await app.Configuration.SaveAsync(config); await RefreshProjectsCore(); });
    }
    [RelayCommand] private async Task PickExistingProject()
    {
        var folder = await ui.PickFolderAsync(); if (folder is null) return;
        await Guard(async () => await OpenProjectsAsync([await app.Projects.ReadAsync(folder)]));
    }
    [RelayCommand] private async Task OpenSelectedProjects() => await Guard(async () =>
    {
        if (SelectedProjects.Count == 0) throw new InvalidOperationException("Select one or more projects.");
        await OpenProjectsAsync(SelectedProjects.ToArray());
    });
    public async Task OpenProjectsAsync(ProjectSnapshot[] projects)
    {
        if (session?.HasPendingChanges == true && !await ui.ConfirmAsync("Pending changes", "Discard staged package changes?", "Discard")) return;
        var fresh = new List<ProjectSnapshot>(); foreach (var project in projects) fresh.Add(await app.Projects.ReadAsync(project.Path));
        session = new(fresh.ToArray(), config, await app.Catalog.KnownAsync(config, fresh.Select(x => (IReadOnlyDictionary<string, string>)x.Packages)));
        WorkspaceProfile = string.Join(Environment.NewLine, fresh.Where(x => x.ProfileStatus.Length > 0).Select(x => (fresh.Count > 1 ? x.Name + ": " : "") + x.ProfileStatus));
        WorkspaceTitle = projects.Length == 1 ? projects[0].Name : $"{projects.Length} projects"; PackageFilter = ""; RenderWorkspace(); CurrentPage = "Workspace";
    }
    [RelayCommand] private async Task TogglePackage() => await Guard(async () =>
    { if (session is null || SelectedPackage is null) return; session.Toggle(SelectedPackage.Id); RenderWorkspace(); await Task.CompletedTask; });
    [RelayCommand] private async Task ChooseVersion() => await Guard(async () =>
    {
        if (session is null || SelectedPackage is null) return;
        var id = SelectedPackage.Id; var version = await ui.VersionAsync(id, await app.Catalog.GetVersionsAsync(id), SelectedPackage.SelectedVersion);
        if (version is not null) { session.SetVersion(id, version); RenderWorkspace(); }
    });
    [RelayCommand] private async Task SearchPackages() => await Guard(async () =>
    {
        var query = await ui.TextAsync("Search packages"); if (string.IsNullOrWhiteSpace(query)) return;
        var results = await app.Catalog.SearchAsync(query); if (results.Length == 0) { Status = "No packages found"; return; }
        var choices = await ui.ChooseManyAsync("Add packages", results.Select(x => x.DisplayName + " · " + x.Id).ToArray()); if (choices is null) return;
        foreach (var item in results.Where(x => choices.Contains(x.DisplayName + " · " + x.Id)))
        {
            var version = await ui.VersionAsync(item.Id, await app.Catalog.GetVersionsAsync(item.Id), item.SelectedVersion); if (version is null) continue;
            if (session is not null) session.SetVersion(item.Id, version);
            else await app.Catalog.RememberAsync([item with { SelectedVersion = version }]);
        }
        if (session is not null) RenderWorkspace(); else await LoadRegistry();
    });
    [RelayCommand] private async Task ApplyPreset() => await Guard(async () =>
    {
        if (session is null) return;
        var name = await ui.ChooseAsync("Creator preset", Presets.ToArray()); if (name is null) return;
        var mode = await ui.ChooseAsync("Apply preset", ["Keep custom packages", "Match exactly"]); if (mode is null) return;
        session.ApplyPreset(name, name == "My default packages" ? config.VpmPackages : config.PackagePresets[name], mode == "Match exactly"); RenderWorkspace();
    });
    [RelayCommand] private async Task CopyProjectPackages() => await Guard(async () =>
    {
        if (session is null) return;
        var folder = await ui.PickFolderAsync(); if (folder is null) return;
        var source = await app.Projects.ReadAsync(folder);
        var set = new Dictionary<string, string>(source.Packages.Keys.ToDictionary(x => x, x => source.InstalledVersions.GetValueOrDefault(x) ?? source.LockedPackages.GetValueOrDefault(x) ?? source.Packages[x]));
        var profile = await AtomicFile.ReadJsonAsync<System.Text.Json.Nodes.JsonObject>(Path.Combine(folder, ".vrcsetup", "package-profile.json"));
        if (profile?["Policy"] is { } policy && await ui.ChooseAsync("Copy from reference", ["Installed packages", "Saved policy"]) == "Saved policy") set = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(policy.ToJsonString(), JsonDefaults.Options)!;
        var mode = await ui.ChooseAsync("Apply copied packages", ["Keep custom packages", "Match exactly"]); if (mode is null) return;
        session.ApplyPreset("Reference: " + source.Name, set, mode == "Match exactly"); RenderWorkspace();
    });
    [RelayCommand] private async Task AddFavorites() => await Guard(async () =>
    {
        if (session is null || config.FavoritePackages.Count == 0) return;
        var id = await ui.ChooseAsync("Favorite package", config.FavoritePackages.ToArray()); if (id is null) return;
        session.SetVersion(id, "latest"); RenderWorkspace();
    });
    [RelayCommand] private async Task ChooseTargets() => await Guard(async () =>
    {
        if (session is null || SelectedPackage is null) return;
        var id = SelectedPackage.Id;
        var targets = await ui.ChooseProjectsAsync("Projects for " + SelectedPackage.Name, session.Projects, session.Targets.GetValueOrDefault(id) ?? session.Projects.Select(x => x.Path).ToArray());
        if (targets is not null) { session.SetTargets(id, targets); RenderWorkspace(); }
    });
    [RelayCommand] private async Task AddExtraImports() => await Guard(async () =>
    {
        if (session is null) return;
        var choice = await ui.ChooseAsync("Additional imports", ["Use configured folder", "Choose UnityPackage", "Clear extra imports"]);
        if (choice == "Clear extra imports") session.ExtraImports = [];
        if (choice == "Use configured folder") session.ExtraImports = app.ExtraImports(config);
        if (choice == "Choose UnityPackage" && await ui.PickFileAsync(".unitypackage") is { } file) session.ExtraImports = session.ExtraImports.Append(file).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Status = $"{session.ExtraImports.Length} extra imports queued";
    });
    [RelayCommand] private async Task UndoChanges()
    {
        if (session is not null && await ui.ConfirmAsync("Undo changes", "Discard all staged changes and extra imports?", "Discard")) { session.Reset(); RenderWorkspace(); }
    }
    [RelayCommand] private async Task ApplyChanges() => await Guard(async () =>
    {
        if (session is null) return;
        var plan = await app.Planner.PlanBulkAsync(session.Projects.Select(x => x.Path), session.Selected, config, session.Exact, session.Removed, session.Targets, session.Preset, session.ExtraImports);
        if (!await ui.ReviewAsync(plan)) return;
        var report = await Run(plan);
        if (report.Success) { session.Reset(); await OpenProjectsAsync(session.Projects); }
        else Status = "Some changes were not saved. Pending choices kept; review and retry.";
    });
    [RelayCommand] private async Task PickPackage()
    { if (await ui.PickFileAsync(".unitypackage") is { } file) { PackageFile = file; CurrentPage = "Create"; } }
    [RelayCommand] private async Task CustomizeCreationPackages() => await Guard(async () =>
    {
        var working = new Dictionary<string, string>(SelectedPreset == "My default packages" ? config.VpmPackages : config.PackagePresets[SelectedPreset]);
        if (await EditPackageSet(working, "Packages for this project") is { } edited) creationOverrides = edited;
    });
    private Dictionary<string, string>? creationOverrides;
    [RelayCommand] private async Task CreateProject() => await Guard(async () =>
    {
        var platforms = new List<string>(); if (PublishWindows) platforms.Add("StandaloneWindows64"); if (PublishAndroid) platforms.Add("Android"); if (PublishIos) platforms.Add("iOS");
        if (platforms.Count == 0) throw new InvalidOperationException("Choose at least one publishing platform.");
        var plan = await app.PlanCreationAsync(PackageFile, config, ProjectName, SelectedPreset == "My default packages" ? null : SelectedPreset, creationOverrides, BuildTarget, platforms.ToArray(), SelectedRecipe == "No recipe" ? null : SelectedRecipe);
        if (!await ui.ReviewAsync(plan)) return;
        var report = await Run(plan);
        if (report.Success)
        {
            config = await app.Configuration.LoadAsync(); creationOverrides = null;
            await OpenProjectsAsync([await app.Projects.ReadAsync(plan.Projects[0].Path)]);
        }
    });
    private async Task<OperationReport> Run(OperationPlan plan)
    {
        using var cancellation = new CancellationTokenSource(); operation = cancellation; IsBusy = true;
        try
        {
            var progress = new SynchronizationProgress<OperationEvent>(context, AppendActivity);
            var report = await app.ApplyAsync(plan, progress, cancellation.Token);
            LastLog = report.Results.FirstOrDefault()?.LogFile ?? "";
            lastReport = report.ReportFile ?? "";
            foreach (var result in report.Results) AppendActivity(new(DateTime.UtcNow, result.Status.ToString(), result.Project + ": " + result.Message, report.Success ? EventSeverity.Info : EventSeverity.Warning));
            Status = report.Success ? "Completed" : "Recovery available";
            return report;
        }
        finally { operation = null; IsBusy = false; }
    }
    [RelayCommand] private void CancelOperation() { operation?.Cancel(); refresh?.Cancel(); }
    public void AppendActivity(OperationEvent value)
    {
        Activity.Add($"{value.Timestamp.ToLocalTime():HH:mm:ss}  {value.Phase}  {value.Message}");
        while (Activity.Count > 300) Activity.RemoveAt(0);
        Status = value.Phase;
    }
    [RelayCommand] private async Task SavePreset() => await Guard(async () =>
    {
        var name = await ui.TextAsync("Save preset", session?.Preset ?? ""); if (string.IsNullOrWhiteSpace(name)) return;
        var current = session?.Projects[0].Packages ?? config.VpmPackages;
        var selected = session is null ? new Dictionary<string, string>(current) : ProjectRules.Merge(current, session.Selected, session.Exact);
        if (session is not null) foreach (var id in session.Removed) selected.Remove(id);
        config.PackagePresets[name] = selected; await app.Configuration.SaveAsync(config); LoadPresetNames();
    });
    [RelayCommand] private async Task EditPreset() => await Guard(async () =>
    {
        var name = await ui.ChooseAsync("Edit packages", Presets.ToArray()); if (name is null) return;
        var source = name == "My default packages" ? config.VpmPackages : config.PackagePresets[name];
        var result = await EditPackageSet(new(source), name); if (result is null) return;
        if (name == "My default packages") config.VpmPackages = result; else config.PackagePresets[name] = result;
        await app.Configuration.SaveAsync(config); await LoadRegistry();
    });
    [RelayCommand] private async Task CreatePreset() => await Guard(async () =>
    {
        var name = await ui.TextAsync("New creator preset"); if (string.IsNullOrWhiteSpace(name)) return;
        var result = await EditPackageSet(new(config.VpmPackages), name); if (result is null) return;
        config.PackagePresets[name] = result; await app.Configuration.SaveAsync(config); LoadPresetNames(); await LoadRegistry();
    });
    [RelayCommand] private async Task DeletePreset() => await Guard(async () =>
    {
        var name = await ui.ChooseAsync("Delete preset", config.PackagePresets.Keys.ToArray()); if (name is null) return;
        if (await ui.ConfirmAsync("Delete preset", name, "Delete")) { config.PackagePresets.Remove(name); await app.Configuration.SaveAsync(config); LoadPresetNames(); }
    });
    private async Task<Dictionary<string, string>?> EditPackageSet(Dictionary<string, string> working, string title)
    {
        return await ui.EditPackagesAsync(title, working, config, await app.Catalog.KnownAsync(config, [working]), app.Catalog.SearchAsync, app.Catalog.GetVersionsAsync);
    }
    [RelayCommand] private async Task FavoriteDefinition() => await Guard(async () =>
    {
        var id = SelectedDefinition?.Id ?? SelectedPackage?.Id; if (id is null) return;
        if (!config.FavoritePackages.Remove(id)) config.FavoritePackages.Add(id);
        await app.Configuration.SaveAsync(config); Status = "Favorites saved";
    });
    [RelayCommand] private async Task RegistryToPreset() => await Guard(async () =>
    {
        if (SelectedDefinition is null) return;
        var name = await ui.ChooseAsync("Add to preset", Presets.ToArray()); if (name is null) return;
        var set = name == "My default packages" ? config.VpmPackages : config.PackagePresets[name];
        set[SelectedDefinition.Id] = await ui.VersionAsync(SelectedDefinition.Id, await app.Catalog.GetVersionsAsync(SelectedDefinition.Id), SelectedDefinition.SelectedVersion) ?? SelectedDefinition.SelectedVersion;
        await app.Configuration.SaveAsync(config); await LoadRegistry();
    });
    [RelayCommand] private async Task RefreshCatalog()
    {
        if (IsRefreshing) return;
        using var cancellation = new CancellationTokenSource(); refresh = cancellation; IsRefreshing = true;
        try { await app.Catalog.RefreshAsync(Registry.ToArray(), new SynchronizationProgress<OperationEvent>(context, AppendActivity), cancellation.Token); await LoadRegistry(); }
        catch (OperationCanceledException) { Status = "Refresh stopped; saved definitions kept"; }
        catch (Exception error) { await ui.ShowErrorAsync(error.Message); }
        finally { IsRefreshing = false; refresh = null; }
    }
    [RelayCommand] private async Task SaveRecipe() => await Guard(async () =>
    {
        var name = await ui.TextAsync("Save creation recipe"); if (string.IsNullOrWhiteSpace(name)) return;
        var platforms = new List<string>(); if (PublishWindows) platforms.Add("StandaloneWindows64"); if (PublishAndroid) platforms.Add("Android"); if (PublishIos) platforms.Add("iOS");
        config.CreationRecipes[name] = new() { Preset = SelectedPreset == "My default packages" ? "" : SelectedPreset,
            Packages = creationOverrides ?? new(SelectedPreset == "My default packages" ? config.VpmPackages : config.PackagePresets[SelectedPreset]), BuildTarget = BuildTarget, PublishingPlatforms = platforms.ToArray(), ExtraImports = app.ExtraImports(config) };
        await app.Configuration.SaveAsync(config); LoadPresetNames();
    });
    [RelayCommand] private async Task DeleteRecipe() => await Guard(async () =>
    {
        var name = await ui.ChooseAsync("Delete creation recipe", config.CreationRecipes.Keys.ToArray()); if (name is null) return;
        if (await ui.ConfirmAsync("Delete recipe", name, "Delete")) { config.CreationRecipes.Remove(name); await app.Configuration.SaveAsync(config); LoadPresetNames(); }
    });
    [RelayCommand] private async Task SaveSettings() => await Guard(async () =>
    {
        if (!string.IsNullOrWhiteSpace(UnityEditorPath)) UnityProjectDriver.ValidateEditor(ProjectRules.NormalizePath(UnityEditorPath));
        var patterns = NamingPatterns.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns) _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        config.UnityEditorPath = string.IsNullOrWhiteSpace(UnityEditorPath) ? "" : ProjectRules.NormalizePath(UnityEditorPath);
        config.UnityProjectsRoot = string.IsNullOrWhiteSpace(ProjectsRoot) ? "" : ProjectRules.NormalizePath(ProjectsRoot);
        config.UnityPackagesFolder = string.IsNullOrWhiteSpace(ExtraImportsFolder) ? null : ProjectRules.NormalizePath(ExtraImportsFolder);
        config.Naming = new() { DefaultPrefix = NamingPrefix, DefaultSuffix = NamingSuffix, RegexRemovePatterns = patterns.ToList(), RememberUnityPackageNames = RememberNames }; config.Theme = Theme;
        await app.Configuration.SaveAsync(config); Status = "Settings saved";
    });
    [RelayCommand] private async Task PickEditor()
    {
        var editors = UnityProjectDriver.FindEditors();
        if (editors.Length > 0)
        { var choice = await ui.ChooseAsync("Unity Editor", editors.Select(x => x.Version + " · " + x.Path).Append("Browse").ToArray()); var editor = editors.FirstOrDefault(x => x.Version + " · " + x.Path == choice); if (editor.Path is not null) { UnityEditorPath = editor.Path; return; } }
        if (await ui.PickFileAsync(".exe") is { } file) UnityEditorPath = file;
    }
    [RelayCommand] private async Task PickProjectsRoot() { if (await ui.PickFolderAsync() is { } folder) ProjectsRoot = folder; }
    [RelayCommand] private async Task PickExtraFolder() { if (await ui.PickFolderAsync() is { } folder) ExtraImportsFolder = folder; }
    [RelayCommand] private async Task ResetSettings() => await Guard(async () =>
    {
        if (!await ui.ConfirmAsync("Reset settings", "Reset defaults, presets and paths? Your Unity projects are preserved.", "Reset")) return;
        config = await app.Configuration.ResetAsync(); LoadSettings(); LoadPresetNames(); await LoadRegistry();
    });
    [RelayCommand] private async Task RepairInstallation() => await Guard(async () =>
    {
        await new InstallationManager(app.Paths).RepairAsync(InstallationRoot()); Status = "Installation and shortcuts verified";
    });
    [RelayCommand] private async Task UninstallApplication() => await Guard(async () =>
    {
        var root = InstallationRoot();
        if (!await ui.ConfirmAsync("Uninstall VRCSETUP", root + "\nSettings and Unity projects will be preserved.", "Uninstall")) return;
        await new InstallationManager(app.Paths).UninstallAsync(root, true); ui.CloseApplication();
    });
    private string InstallationRoot() => File.Exists(Path.Combine(app.Paths.PayloadRoot, ".vrcsetup-install.json")) ? app.Paths.PayloadRoot : InstallationManager.DefaultRoot;
    [RelayCommand] private async Task ImportSettings() => await Guard(async () =>
    { if (await ui.PickFileAsync(".json") is { } file) { config = await app.Configuration.ImportAsync(file); LoadSettings(); LoadPresetNames(); await LoadRegistry(); } });
    [RelayCommand] private async Task RecoverProject() => await Guard(async () =>
    {
        var candidates = allProjects.Where(x => x.Incomplete).ToArray();
        var choice = await ui.ChooseAsync("Incomplete project", candidates.Select(x => x.Name + " · " + x.Path).ToArray());
        var project = candidates.FirstOrDefault(x => x.Name + " · " + x.Path == choice); if (project is null) return;
        var action = await ui.ChooseAsync("Recovery", ["Inspect packages / retry", "Reimport original UnityPackage", "Delete incomplete project", "Close a blocking Unity editor", "Open folder"]);
        if (action == "Open folder") OpenPath(project.Path);
        if (action == "Close a blocking Unity editor")
        {
            var processes = ProjectWorkspace.GetUnityProcesses(project.Path);
            var selected = await ui.ChooseAsync("Unity process", processes.Select(x => "PID " + x.Pid + " · " + project.Name).ToArray());
            var process = processes.FirstOrDefault(x => "PID " + x.Pid + " · " + project.Name == selected);
            if (process.Pid > 0 && await ui.ConfirmAsync("Close Unity editor", "Force close PID " + process.Pid + " for " + project.Path + "? Unsaved work may be lost.", "Close Unity")) await app.Projects.CloseUnityAsync(project.Path, process.Pid, true);
        }
        if (action == "Inspect packages / retry") await OpenProjectsAsync([project]);
        if (action == "Reimport original UnityPackage")
        {
            var state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(project.Path, ".vrcsetup", "state.json"));
            if (state is null || !File.Exists(state.UnityPackagePath)) throw new IOException("The original UnityPackage is unavailable; choose it manually in Additional imports.");
            await OpenProjectsAsync([project]); session!.ExtraImports = [state.UnityPackagePath];
            Status = "Original import staged. Review changes before retrying.";
        }
        if (action == "Delete incomplete project" && await ui.ConfirmAsync("Delete incomplete project", project.Path + "\nAll contents will be deleted.", "Delete")) { await app.Projects.CleanupAsync(project.Path, config.UnityProjectsRoot, true); await RefreshProjectsCore(); }
    });
    [RelayCommand] private async Task RestoreBackup() => await Guard(async () =>
    {
        var project = await ui.PickFolderAsync(); if (project is null) return;
        var file = await ui.PickFileAsync(".json"); if (file is null) return;
        var backup = await AtomicFile.ReadJsonAsync<BackupRecord>(file) ?? throw new IOException("Backup record not found.");
        if (!await ui.ConfirmAsync("Restore backup", "Replace the current project state with this backup?\n" + project, "Restore")) return;
        using var lease = await app.Projects.AcquireAsync(project, CancellationToken.None);
        if (!await app.Projects.RestoreAsync(project, backup, new SynchronizationProgress<OperationEvent>(context, AppendActivity), CancellationToken.None)) throw new IOException("Restore could not complete; inspect the logs.");
    });
    [RelayCommand] private void OpenLogs() { Directory.CreateDirectory(app.Paths.Logs); OpenPath(app.Paths.Logs); }
    [RelayCommand] private void OpenLastLog() { if (File.Exists(LastLog)) OpenPath(LastLog); }
    [RelayCommand] private void OpenReport() { if (File.Exists(lastReport)) OpenPath(lastReport); }
    [RelayCommand] private void OpenProjectFolder() { if (session is not null) OpenPath(session.Projects[0].Path); }
    [RelayCommand] private async Task OpenUnity() => await Guard(async () =>
    {
        if (session is null) return;
        var project = session.Projects[0].Path;
        UnityProjectDriver.ValidateEditor(config.UnityEditorPath);
        if (app.Projects.IsBusy(project)) { Status = "Project is already open in Unity"; return; }
        var start = new ProcessStartInfo(config.UnityEditorPath) { UseShellExecute = false }; start.ArgumentList.Add("-projectPath"); start.ArgumentList.Add(project); Process.Start(start); await Task.CompletedTask;
    });
    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private async Task Guard(Func<Task> action)
    {
        if (IsBusy) return;
        try { await action(); }
        catch (OperationCanceledException) { Status = "Cancelled; recovery information preserved"; }
        catch (Exception error) { Status = "Action failed"; AppendActivity(new(DateTime.UtcNow, "Error", error.Message, EventSeverity.Error)); await ui.ShowErrorAsync(error.Message); }
    }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items) { collection.Clear(); foreach (var item in items) collection.Add(item); }
    public void Dispose() { operation?.Cancel(); refresh?.Cancel(); }
}

public sealed class SynchronizationProgress<T>(SynchronizationContext context, Action<T> callback) : IProgress<T>
{ public void Report(T value) => context.Post(_ => callback(value), null); }
public sealed record PlatformOption(string Label, string Value);
