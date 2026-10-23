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
    Task<string?> SaveFileAsync(string name, string extension) => Task.FromResult<string?>(null);
    Task<string?> ResolveSettingsAsync() => Task.FromResult<string?>(null);
    async Task<string?> ResolveChangesAsync(string title, string action) =>
        await ConfirmAsync(title, "Discard pending changes?", "Discard and leave") ? "Discard" : null;
    Task<string?> PickFolderAsync();
    Task<bool> ConfirmAsync(string title, string content, string action);
    Task<bool> ReviewAsync(OperationPlan plan);
    Task<string?> TextAsync(string title, string initial = "");
    Task<string?> ChooseAsync(string title, string[] items);
    Task<string[]?> ChooseManyAsync(string title, string[] items);
    Task<string?> VersionAsync(string package, string[] versions, string current);
    Task<string[]?> ChooseProjectsAsync(string title, ProjectSnapshot[] projects, string[] selected);
    Task ShowErrorAsync(string message);
    Task ShowUpdateInfoAsync(string details) => ShowErrorAsync(details);
    Task ShowContactAsync() => ShowErrorAsync("Email: contact@dummics.com · Discord: dummics");
    Task<bool> RestartApplicationAsync() => Task.FromResult(false);
    Task<string?> PromptRecoveryAsync(string name, bool canResume) => Task.FromResult<string?>(null);
    void CloseApplication();
    Task<bool> InstallApplicationUpdateAsync(string installer, string result) => Task.FromResult(false);
    Task<Dictionary<string, string>?> EditPackagesAsync(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions, PackageInspectionReport? inspection = null);
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
    private readonly SemaphoreSlim actionGate = new(1, 1);
    public ObservableCollection<ProjectSnapshot> Projects { get; } = [];
    public ObservableCollection<PackageWorkspaceRow> Packages { get; } = [];
    public ObservableCollection<PackageDefinition> Registry { get; } = [];
    public ObservableCollection<string> Presets { get; } = [DefaultPresetKey];
    public ObservableCollection<string> Recipes { get; } = [];
    public ObservableCollection<UiActivityEntry> Activity { get; } = [];
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
    [ObservableProperty] public partial string Theme { get; set; } = "Dark";
    [ObservableProperty] public partial string LastLog { get; set; } = "";
    public string[] PlatformChoices => ProjectRules.BuildTargets;
    public PlatformOption[] PlatformOptions => [new("Windows", "StandaloneWindows64"), new("Android / Quest", "Android"), new("iOS", "iOS")];
    public string[] ThemeChoices => ["System", "Light", "Dark"];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProjectNameSortLabel)), NotifyPropertyChangedFor(nameof(ProjectModifiedSortLabel)), NotifyPropertyChangedFor(nameof(ProjectCreatedSortLabel))] public partial string ProjectSort { get; set; } = "recent";
    public VrcSetupApplication Application => app;

    public MainViewModel(VrcSetupApplication app, IDesktopInteraction interaction)
    { this.app = app; ui = interaction; context = SynchronizationContext.Current ?? new(); versionCache = new(app.Catalog.GetVersionsAsync, lifetime.Token); app.Imports.ConfigureParallelism(int.MaxValue); app.Imports.Changed += ImportsChanged; InitializeApplicationStatus();
        PropertyChanged += (_, e) => { if (!loadingSettings && e.PropertyName is { } name && SettingsProperties.Contains(name)) NotifySettingsState(); };
    }
    public async Task InitializeAsync()
    {
        await Guard(async () =>
        {
            config = await app.Configuration.LoadAsync(); app.ConfigureDiagnostics(config); LoadSettings(); LoadPresetNames();
            if (app.AllowEditorDiscovery) await FindEditorAsync(false);
            try { await LoadRegistry(); } catch (Exception error) { app.Diagnostics.Write("Package library", error.Message, error); AppendActivity(new(DateTime.UtcNow, "Packages", "Saved package library could not load: " + error.Message, EventSeverity.Warning)); }
            LoadEditingPreset();
            try { await app.Imports.InitializeAsync(lifetime.Token); }
            catch (Exception error) { app.Diagnostics.Write("Import history", error.Message, error); AppendActivity(new(DateTime.UtcNow, "Imports", "Import queue unavailable: " + error.Message, EventSeverity.Warning)); }
            if (!string.IsNullOrWhiteSpace(config.UnityProjectsRoot) && Directory.Exists(config.UnityProjectsRoot)) await RefreshProjectsCore();
            UpdateProjectState();
            setupMode = string.IsNullOrWhiteSpace(config.UnityEditorPath) || string.IsNullOrWhiteSpace(config.UnityProjectsRoot) || app.AllowEditorDiscovery && !EditorReady;
            if (setupMode) CurrentPage = "Settings";
            settingsBaseline = SettingsSnapshot(); NotifySettingsState();
            if (config.Diagnostics.Verbose && app.Configuration.ImportedFrom is { } imported) AppendActivity(new(DateTime.UtcNow, "Migration", "Settings imported: " + imported));
        });
        if (ErrorMessage.Length == 0) await PromptInterruptedImportAsync();
        if (app.AllowBackgroundUpdateChecks) { await ReadPreviousUpdateResultAsync(); _ = RunPeriodicUpdateChecksAsync(); }
    }
    private void LoadSettings()
    {
        loadingSettings = true;
        persistedConfiguration = config.Copy();
        NotifyLibraryLayout();
        UnityEditorPath = config.UnityEditorPath; ProjectsRoot = config.UnityProjectsRoot; ExtraImportsFolder = config.UnityPackagesFolder ?? "";
        ManualImportPath = config.ManualImportPath;
        AutoHideCompletedImports = config.AutoHideCompletedImports;
        CheckUpdatesOnStartup = config.CheckUpdatesOnStartup; IncludeBetaUpdates = config.IncludeBetaUpdates;
        AutomaticUpdateEnabled = config.AutomaticUpdates;
        SelectedLanguage = config.Language;
        NamingPrefix = config.Naming.DefaultPrefix; NamingSuffix = config.Naming.DefaultSuffix; NamingPatterns = string.Join(Environment.NewLine, config.Naming.RegexRemovePatterns);
        RememberNames = config.Naming.RememberUnityPackageNames; Theme = config.Theme; ProjectSort = config.ProjectLibrarySort;
        DeveloperMode = app.Diagnostics.DeveloperMode; VerboseDiagnostics = app.Diagnostics.Verbose; TraceTools = app.Diagnostics.TraceTools;
        settingsBaseline = SettingsSnapshot(); loadingSettings = false; NotifySettingsState();
    }
    private void LoadPresetNames()
    {
        var names = new[] { DefaultPresetKey }.Concat(config.PackagePresets.Keys.Order()).ToArray();
        var editing = names.Contains(EditingPreset) ? EditingPreset : DefaultPresetKey;
        var selected = names.Contains(SelectedPreset) ? SelectedPreset : DefaultPresetKey;
        presetSwitching = true;
        foreach (var name in names.Where(x => !Presets.Contains(x))) Presets.Add(name);
        foreach (var name in Presets.Where(x => !names.Contains(x)).ToArray()) Presets.Remove(name);
        EditingPreset = editing; SelectedPreset = selected;
        presetSwitching = false;
        var recipeNames = new[] { "No recipe" }.Concat(config.CreationRecipes.Keys.Order()).ToArray();
        var recipe = recipeNames.Contains(SelectedRecipe) ? SelectedRecipe : "No recipe";
        foreach (var name in recipeNames.Where(x => !Recipes.Contains(x))) Recipes.Add(name);
        foreach (var name in Recipes.Where(x => !recipeNames.Contains(x)).ToArray()) Recipes.Remove(name);
        SelectedRecipe = recipe;
        OnPropertyChanged(nameof(EditingPreset)); OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(HasMultiplePresets));
    }
    private async Task LoadRegistry()
    {
        var known = await app.Catalog.KnownAsync(config, session?.Projects.Select(x => (IReadOnlyDictionary<string, string>)x.Packages));
        personalPackageIds = known.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Replace(Registry, known); RefreshLibraryRows(); RefreshCreationPackages();
    }
    partial void OnProjectFilterChanged(string value) => FilterProjects();
    partial void OnPackageFilterChanged(string value) { RenderWorkspace(); if (CurrentPage == "Workspace") LibraryFilter = value; }
    partial void OnPackageFileChanged(string value)
    {
        if (!switchingImportDraft && ImportDrafts.Count > 0 && SelectedImportDraft?.File != value)
        {
            switchingImportDraft = true;
            ImportDrafts.Clear(); SelectedImportDraft = null;
            switchingImportDraft = false; NotifyDrafts();
        }
        OnPropertyChanged(nameof(PackageFileLabel));
        creationOverrides = null;
        _ = InspectCreationPackageAsync(value);
        RefreshCreationPackages();
        if (string.IsNullOrWhiteSpace(value)) return;
        ProjectName = ProjectRules.SuggestName(value, config); BuildTarget = ProjectRules.SuggestBuildTarget(value);
        PublishAndroid = BuildTarget == "Android"; PublishWindows = !PublishAndroid;
    }
    partial void OnSelectedPresetChanged(string value)
    {
        creationOverrides = null;
        if (!acceptingImportSelection) foreach (var draft in ImportDrafts) draft.Packages = null;
        RefreshCreationPackages();
    }
    partial void OnSelectedRecipeChanged(string value)
    {
        foreach (var draft in ImportDrafts) draft.Packages = null;
        if (string.IsNullOrWhiteSpace(value) || !config.CreationRecipes.TryGetValue(value, out var recipe)) { creationOverrides = null; RefreshCreationPackages(); return; }
        SelectedPreset = string.IsNullOrWhiteSpace(recipe.Preset) || !config.PackagePresets.ContainsKey(recipe.Preset) ? "My default packages" : recipe.Preset;
        creationOverrides = recipe.Packages.Count > 0 ? new(recipe.Packages) : null;
        if (SelectedImportDraft is { } selected) selected.Packages = creationOverrides is null ? null : new(creationOverrides);
        BuildTarget = recipe.BuildTarget; PublishWindows = recipe.PublishingPlatforms.Contains("StandaloneWindows64"); PublishAndroid = recipe.PublishingPlatforms.Contains("Android"); PublishIos = recipe.PublishingPlatforms.Contains("iOS");
        RefreshCreationPackages();
    }
    private void FilterProjects() { Replace(Projects, ProjectOrdering.Apply(allProjects.Where(x => (x.Name + " " + x.Kind + " " + x.UnityVersion).Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase)), ProjectSort)); UpdateProjectState(); }
    private void RenderWorkspace()
    {
        var selected = SelectedPackage?.Id;
        var desired = session?.Rows(PackageFilter).Where(x => x.Action != "Available").ToArray() ?? [];
        var ids = desired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = Packages.Count - 1; i >= 0; i--) if (!ids.Contains(Packages[i].Id)) Packages.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            var next = desired[i]; var current = Packages.FirstOrDefault(x => x.Id.Equals(next.Id, StringComparison.OrdinalIgnoreCase));
            if (current is null) Packages.Insert(i, next);
            else
            {
                var position = Packages.IndexOf(current);
                if (current != next) Packages[position] = next;
                if (position != i) Packages.Move(position, i);
            }
        }
        SelectedPackage = Packages.FirstOrDefault(x => x.Id == selected) ?? Packages.FirstOrDefault();
        NotifyWorkspaceChanges();
    }
    [RelayCommand] private async Task RefreshProjects() => await Guard(RefreshProjectsCore, allowWhileBusy: true);
    private async Task RefreshProjectsCore()
    {
        IsLoadingProjects = true; ErrorMessage = "";
        try
        {
            var root = config.UnityProjectsRoot; var sort = ProjectSort;
            var scanned = await Task.Run(() => app.Projects.ScanAsync(root, sort, true, cancellationToken: lifetime.Token), lifetime.Token);
            if (root != config.UnityProjectsRoot || sort != ProjectSort) return;
            allProjects = scanned; FilterProjects(); Status = $"{allProjects.Length} projects";
            try { await app.Catalog.RememberProjectPackagesAsync(allProjects, lifetime.Token); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { app.Diagnostics.Write("Project package index", error.Message, error); }
        }
        catch when (!Directory.Exists(config.UnityProjectsRoot)) { allProjects = []; FilterProjects(); throw; }
        finally { IsLoadingProjects = false; UpdateProjectState(); }
    }
    [RelayCommand] private async Task ChangeSort()
    {
        await Guard(async () => { config.ProjectLibrarySort = ProjectSort; await PersistConfigurationAsync(); FilterProjects(); });
    }
    public async Task CycleProjectSortAsync(string column)
    { ProjectSort = ProjectOrdering.Cycle(ProjectSort == "recent" ? "modified" : ProjectSort, column); await ChangeSortCommand.ExecuteAsync(null); OnPropertyChanged(nameof(ProjectNameSortLabel)); OnPropertyChanged(nameof(ProjectModifiedSortLabel)); OnPropertyChanged(nameof(ProjectCreatedSortLabel)); }
    private string SortLabel(string label, string column) => UiText.Text(label) + (ProjectSort == column || column == "modified" && ProjectSort == "recent" ? " ↓" : ProjectSort == column + "-reverse" ? " ↑" : "");
    public string ProjectNameSortLabel => SortLabel("Name", "name");
    public string ProjectModifiedSortLabel => SortLabel("Modified", "modified");
    public string ProjectCreatedSortLabel => SortLabel("Created", "created");
    [RelayCommand] private async Task PickExistingProject()
    {
        var folder = await ui.PickFolderAsync(); if (folder is null) return;
        await Guard(async () => await OpenProjectsAsync([await app.Projects.ReadAsync(folder)]));
    }
    [RelayCommand] private async Task OpenSelectedProjects() => await IndependentAction(async () =>
    {
        if (SelectedProjects.Count == 0) throw new InvalidOperationException("Select one or more projects.");
        await OpenProjectsAsync(SelectedProjects.ToArray());
    });
    public async Task OpenProjectsAsync(ProjectSnapshot[] projects, bool reload = false)
    {
        if (IsApplyingPackages && !(reload && ReferenceEquals(session, applyingSession)) && applyingSession?.Projects.Any(x => projects.Any(y => x.Path.Equals(y.Path, StringComparison.OrdinalIgnoreCase))) == true)
        { Status = "These projects are being updated. You can manage other projects in the meantime."; return; }
        if (!reload && CurrentPage == "Workspace" && session?.MatchesProjects(projects) == true)
        { BrowsingProjectPackages = true; RenderWorkspace(); RefreshLibraryRows(); CurrentPage = "Workspace"; return; }
        var request = ++workspaceRequest; var page = CurrentPage; var previousSession = session;
        if (session?.HasPendingChanges == true && !ReferenceEquals(session, applyingSession) && !await ui.ConfirmAsync("Pending changes", "Discard staged package changes?", "Discard")) return;
        var fresh = new List<ProjectSnapshot>(); foreach (var project in projects) fresh.Add(await app.Projects.ReadAsync(project.Path));
        var choices = config.Copy();
        var known = await Task.Run(() => app.Catalog.KnownAsync(choices, fresh.Select(x => (IReadOnlyDictionary<string, string>)x.Packages)), lifetime.Token);
        try { await app.Catalog.RememberAsync(known, preserveSelections: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { app.Diagnostics.Write("Package library cache", error.Message, error); }
        if (request != workspaceRequest || page != CurrentPage || !ReferenceEquals(session, previousSession)) return;
        session = new(fresh.ToArray(), config, known); Replace(Registry, known);
        projectCatalogBaseline = session.Rows().Where(x => x.Included).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        personalPackageIds = known.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        WorkspaceProfile = string.Join(Environment.NewLine, fresh.Where(x => x.ProfileStatus.Length > 0).Select(x => (fresh.Count > 1 ? x.Name + ": " : "") + x.ProfileStatus));
        SelectedProjects.Clear(); NotifyProjectSelection();
        WorkspaceTitle = projects.Length == 1 ? projects[0].Name : $"{projects.Length} projects"; PackageFilter = ""; LibraryFilter = ""; FavoritesOnly = false; BrowsingProjectPackages = true; RenderWorkspace(); RefreshLibraryRows(); CurrentPage = "Workspace";
    }
    [RelayCommand] private async Task TogglePackage() => await Guard(async () =>
    { if (session is null || SelectedPackage is null) return; ToggleWorkspacePackage(SelectedPackage); await Task.CompletedTask; });
    [RelayCommand] private async Task ChooseVersion() => await Guard(async () =>
    {
        if (session is null || SelectedPackage is null) return;
        var id = SelectedPackage.Id; var version = await ui.VersionAsync(id, await app.Catalog.GetVersionsAsync(id), SelectedPackage.SelectedVersion);
        if (version is not null) { session.SetVersion(id, version); RenderWorkspace(); }
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
    [RelayCommand] private async Task ApplyChanges() => await Guard(ApplyChangesCoreAsync, serialize: false);
    private async Task ApplyChangesCoreAsync()
    {
        if (session is null || IsApplyingPackages) return;
        var source = session;
        if (ProjectImportsActive(source.Projects.Select(x => x.Path))) return;
        // Freeze planning inputs before the first await; navigation may detach this editor.
        var selected = new Dictionary<string, string>(source.Selected);
        var removed = source.Removed.ToArray();
        var targets = source.Targets.ToDictionary(x => x.Key, x => x.Value.ToArray());
        var exact = source.Exact; var preset = source.Preset; var extraImports = source.ExtraImports.ToArray();
        var choices = config.Copy();
        applyingSession = source;
        using var cancellation = new CancellationTokenSource(); operation = cancellation;
        IsApplyingPackages = true; IsBusy = true; CanCancelApply = true;
        ApplyProject = ""; ApplyPosition = ""; ApplyPackage = ""; ApplyPackageVersion = ""; ApplyPhase = "Preparing";
        try
        {
            var busy = extraImports.Length == 0 ? [] : await Task.Run(() => source.Projects.Where(x => app.Projects.IsBusy(x.Path)).Select(x => x.Name).ToArray(), cancellation.Token);
            if (busy.Length > 0) throw new ProjectsInUseException(busy);
            var plan = await Task.Run(() => app.Planner.PlanBulkAsync(source.Projects.Select(x => x.Path), selected, choices, exact, removed, targets, preset, extraImports, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            if (BeforePackageApplyForVerification is { } ready) await ready(plan);
            cancellation.Token.ThrowIfCancellationRequested();
            if (ProjectImportsActive(plan.Projects.Select(x => x.Path))) return;
            applyingProjects = plan.Projects; ApplyPhase = "Preparing";
            var report = await Run(plan, cancellation);
            if (report.Success)
            {
                source.Reset();
                if (ReferenceEquals(session, source) && CurrentPage == "Workspace")
                {
                    // Reload only the editor that started this operation, never a different page.
                    await OpenProjectsAsync(source.Projects, reload: true);
                }
            }
            else { Status = "Some changes were not saved. Pending choices kept; review and retry."; ErrorMessage = report.Results.FirstOrDefault(x => x.Status is OperationStatus.Failed or OperationStatus.Cancelled or OperationStatus.Skipped)?.Message ?? Status; }
        }
        finally { operation = null; IsBusy = false; IsApplyingPackages = false; CanCancelApply = false; applyingProjects = []; applyingSession = null; }
    }
    [RelayCommand] private async Task PickPackage()
    { if (await ui.PickFileAsync(".unitypackage") is { } file) { PackageFile = file; CurrentPage = "Create"; } }
    [RelayCommand] private async Task CustomizeCreationPackages() => await Guard(async () =>
    {
        var working = CurrentCreationSelection();
        if (!await NavigateAsync("ImportPackages")) return;
        presetSwitching = true;
        try { EditingPreset = SelectedPreset; LoadEditingPreset(); }
        finally { presetSwitching = false; }
        editingImportPreset = true;
        presetWorking = working; LibraryFilter = ""; FavoritesOnly = false; RenderPreset(); RefreshLibraryRows();
        OnPropertyChanged(nameof(UseImportPresetVisibility)); OnPropertyChanged(nameof(PresetOptionsVisibility)); OnPropertyChanged(nameof(IsCustomPreset)); OnPropertyChanged(nameof(PresetChangesVisibility));
    });
    private Dictionary<string, string>? creationOverrides;
    [RelayCommand] private async Task CreateProject()
    {
        ImportError = "";
        if (ImportDrafts.Count == 0 && string.IsNullOrWhiteSpace(PackageFile)) { ImportError = "Choose a UnityPackage. For an empty project, use Projects → New project."; return; }
        if (IsInstallingUpdate || IsBusy && !IsApplyingPackages) { ImportError = "Wait for the current operation to finish, then try again."; return; }
        IsPreparingImport = true;
        try
        {
            await actionGate.WaitAsync(lifetime.Token);
            try { await QueueCreationAsync(); }
            finally { actionGate.Release(); }
        }
        catch (OperationCanceledException) { ImportError = "Import preparation cancelled. Your files are still selected."; }
        catch (Exception error)
        {
            app.Diagnostics.Write("Import review", error.Message, error);
            ImportError = error.Message;
            AppendActivity(new(DateTime.UtcNow, "Error", error.Message, EventSeverity.Error));
        }
        finally { IsPreparingImport = false; }
    }
    private async Task<OperationReport> Run(OperationPlan plan, CancellationTokenSource? suppliedCancellation = null)
    {
        using var ownedCancellation = suppliedCancellation is null ? new CancellationTokenSource() : null;
        var cancellation = suppliedCancellation ?? ownedCancellation!; operation = cancellation; IsBusy = true;
        try
        {
            var progress = new SynchronizationProgress<OperationEvent>(context, value => { AppendActivity(value); UpdateApplyProgress(value); });
            var report = await Task.Run(() => app.ApplyAsync(plan, progress, cancellation.Token));
            LastLog = report.Results.FirstOrDefault()?.LogFile ?? "";
            lastReport = report.ReportFile ?? "";
            foreach (var result in report.Results) AppendActivity(new(DateTime.UtcNow, result.Status.ToString(), result.Project + ": " + result.Message, report.Success ? EventSeverity.Info : EventSeverity.Warning));
            Status = report.Success ? "Completed" : "Recovery available";
            return report;
        }
        finally { if (!IsApplyingPackages) { operation = null; IsBusy = false; } }
    }
    [RelayCommand] private void CancelOperation() { operation?.Cancel(); refresh?.Cancel(); }
    public void AppendActivity(OperationEvent value)
    {
        Activity.Add(new($"{value.Timestamp.ToLocalTime():HH:mm}", value.Phase, value.Message));
        while (Activity.Count > 300) Activity.RemoveAt(0);
        OnPropertyChanged(nameof(ActivityEmptyVisibility));
        Status = value.Phase;
    }
    [RelayCommand] private async Task SavePreset() => await IndependentAction(async () =>
    {
        var name = await ui.TextAsync("Save preset", session?.Preset ?? ""); if (string.IsNullOrWhiteSpace(name)) return;
        var current = session?.Projects[0].Packages ?? config.VpmPackages;
        var selected = session is null ? new Dictionary<string, string>(current) : ProjectRules.Merge(current, session.Selected, session.Exact);
        if (session is not null) foreach (var id in session.Removed) selected.Remove(id);
        config.PackagePresets[name] = selected; await PersistConfigurationAsync(); LoadPresetNames();
    });
    [RelayCommand] private async Task EditPreset() => await Guard(async () =>
    {
        var name = await ui.ChooseAsync("Edit packages", Presets.ToArray()); if (name is null) return;
        var source = name == "My default packages" ? config.VpmPackages : config.PackagePresets[name];
        var result = await EditPackageSet(new(source), name); if (result is null) return;
        if (name == "My default packages") config.VpmPackages = result; else config.PackagePresets[name] = result;
        await PersistConfigurationAsync(); await LoadRegistry();
    });
    [RelayCommand] private async Task CreatePreset() => await IndependentAction(async () =>
    {
        var name = await ui.TextAsync("New preset"); if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.Equals(DefaultPresetKey, StringComparison.OrdinalIgnoreCase) || config.PackagePresets.Keys.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Choose a different preset name.");
        if (PresetDirty && !editingNewProject && !await ui.ConfirmAsync("Unsaved packages", "Discard changes to " + PresetLabel + "?", "Discard")) return;
        config.PackagePresets[name] = new(presetWorking.Count > 0 ? presetWorking : config.VpmPackages); await PersistConfigurationAsync(); LoadPresetNames(); presetSwitching = true; EditingPreset = name; presetSwitching = false; LoadEditingPreset();
    });
    [RelayCommand] private async Task DeletePreset() => await IndependentAction(async () =>
    {
        var name = EditingPreset; if (name == DefaultPresetKey) { Status = "Default cannot be deleted"; return; }
        if (await ui.ConfirmAsync("Delete preset", name, "Delete")) { config.PackagePresets.Remove(name); await PersistConfigurationAsync(); LoadPresetNames(); presetSwitching = true; EditingPreset = DefaultPresetKey; presetSwitching = false; LoadEditingPreset(); }
    });
    private async Task<Dictionary<string, string>?> EditPackageSet(Dictionary<string, string> working, string title)
    {
        return await ui.EditPackagesAsync(title, working, config, await app.Catalog.KnownAsync(config, [working]), app.Catalog.SearchAsync, app.Catalog.GetVersionsAsync);
    }
    [RelayCommand] private async Task FavoriteDefinition() => await Guard(async () =>
    {
        var id = SelectedDefinition?.Id ?? SelectedPackage?.Id; if (id is null) return;
        if (!config.FavoritePackages.Remove(id)) config.FavoritePackages.Add(id);
        await PersistConfigurationAsync(); Status = "Favorites saved";
    });
    [RelayCommand] private async Task RegistryToPreset() => await Guard(async () =>
    {
        if (SelectedDefinition is null) return;
        var name = await ui.ChooseAsync("Add to preset", Presets.ToArray()); if (name is null) return;
        var set = name == "My default packages" ? config.VpmPackages : config.PackagePresets[name];
        set[SelectedDefinition.Id] = await ui.VersionAsync(SelectedDefinition.Id, await app.Catalog.GetVersionsAsync(SelectedDefinition.Id), SelectedDefinition.SelectedVersion) ?? SelectedDefinition.SelectedVersion;
        await PersistConfigurationAsync(); await LoadRegistry();
    });
    [RelayCommand] private async Task RefreshCatalog()
    {
        if (IsRefreshing || !app.AllowBackgroundCatalogRefresh) return;
        using var cancellation = new CancellationTokenSource(); refresh = cancellation; IsRefreshing = true;
        try { await app.Catalog.RefreshAsync(Registry.Where(x => x.UpdatedUtc is null || x.UpdatedUtc < DateTime.UtcNow.AddHours(-6)).Take(12).ToArray(), new SynchronizationProgress<OperationEvent>(context, value => { if (DeveloperMode) AppendActivity(value); }), cancellation.Token); await LoadRegistry(); }
        catch (OperationCanceledException) { Status = "Refresh stopped; saved definitions kept"; }
        catch (Exception error) { app.Diagnostics.Write("Package refresh", error.Message, error); if (DeveloperMode) AppendActivity(new(DateTime.UtcNow, "Packages", error.Message, EventSeverity.Warning)); }
        finally { IsRefreshing = false; refresh = null; }
    }
    [RelayCommand] private async Task SaveRecipe() => await Guard(async () =>
    {
        var name = await ui.TextAsync("Save template"); if (string.IsNullOrWhiteSpace(name)) return;
        config.CreationRecipes[name] = new() { Preset = SelectedPreset == "My default packages" ? "" : SelectedPreset,
            Packages = creationOverrides ?? new(SelectedPreset == "My default packages" ? config.VpmPackages : config.PackagePresets[SelectedPreset]), BuildTarget = BuildTarget, PublishingPlatforms = [BuildTarget], ExtraImports = app.ExtraImports(config) };
        await PersistConfigurationAsync(); LoadPresetNames();
    });
    [RelayCommand] private async Task DeleteRecipe() => await Guard(async () =>
    {
        var name = await ui.ChooseAsync("Delete creation recipe", config.CreationRecipes.Keys.ToArray()); if (name is null) return;
        if (await ui.ConfirmAsync("Delete recipe", name, "Delete")) { config.CreationRecipes.Remove(name); await PersistConfigurationAsync(); LoadPresetNames(); }
    });
    [RelayCommand] private async Task SaveSettings() => await Guard(async () =>
    {
        var restart = LanguageRestartVisibility == Microsoft.UI.Xaml.Visibility.Visible && !HasActiveImports && !WorkspaceDirty && !PresetDirty;
        await SaveSettingsCoreAsync();
        if (restart) await ui.RestartApplicationAsync();
        else if (LanguageRestartVisibility == Microsoft.UI.Xaml.Visibility.Visible) Status = "Language saved. It will apply the next time you open the app.";
    });
    private async Task SaveSettingsCoreAsync()
    {
        if (!RequireImportsIdle()) throw new InvalidOperationException("Imports are in progress.");
        // Validate the entire form before changing the working configuration.
        if (!string.IsNullOrWhiteSpace(UnityEditorPath) && !UnityEditorPath.Equals(config.UnityEditorPath, StringComparison.OrdinalIgnoreCase))
            UnityProjectDriver.ValidateEditor(ProjectRules.NormalizePath(UnityEditorPath));
        if (setupMode && (!EditorReady || string.IsNullOrWhiteSpace(ProjectsRoot))) throw new InvalidOperationException("Choose Unity and a projects folder to continue.");
        var patterns = NamingPatterns.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns) _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        _ = string.IsNullOrWhiteSpace(ProjectsRoot) ? "" : ProjectRules.NormalizePath(ProjectsRoot);
        _ = string.IsNullOrWhiteSpace(ExtraImportsFolder) ? "" : ProjectRules.NormalizePath(ExtraImportsFolder);
        var previous = config.Copy();
        try
        {
        config.ManualImportPath = ManualImportPath;
        config.AutoHideCompletedImports = AutoHideCompletedImports;
        config.CheckUpdatesOnStartup = CheckUpdatesOnStartup; config.IncludeBetaUpdates = IncludeBetaUpdates;
        config.AutomaticUpdates = AutomaticUpdateEnabled;
        config.Language = SelectedLanguage;
        config.UnityEditorPath = string.IsNullOrWhiteSpace(UnityEditorPath) ? "" : ProjectRules.NormalizePath(UnityEditorPath);
        config.UnityProjectsRoot = string.IsNullOrWhiteSpace(ProjectsRoot) ? "" : ProjectRules.NormalizePath(ProjectsRoot);
        config.UnityPackagesFolder = string.IsNullOrWhiteSpace(ExtraImportsFolder) ? null : ProjectRules.NormalizePath(ExtraImportsFolder);
        config.Naming = new() { DefaultPrefix = NamingPrefix, DefaultSuffix = NamingSuffix, RegexRemovePatterns = patterns.ToList(), RememberUnityPackageNames = RememberNames }; config.Theme = Theme;
        config.Diagnostics = new() { DeveloperMode = DeveloperMode, Verbose = VerboseDiagnostics, TraceTools = TraceTools }; app.Diagnostics.Configure(config.Diagnostics);
        await PersistConfigurationAsync();
        }
        catch { config = previous; app.Diagnostics.Configure(config.Diagnostics); throw; }
        var wasSetup = setupMode; setupMode = false; settingsBaseline = SettingsSnapshot(); NotifySettingsState(); Status = "Settings saved";
        if (wasSetup) CurrentPage = "Projects";
        if (!string.IsNullOrWhiteSpace(config.UnityProjectsRoot)) await RefreshProjectsCore();
    }
    [RelayCommand] private async Task PickEditor() => await Guard(async () =>
    { if (await ui.PickFileAsync(".exe") is { } file) await SetEditorAsync(file); });
    [RelayCommand] private async Task PickProjectsRoot() => await Guard(async () =>
    {
        if (await ui.PickFolderAsync() is not { } folder) return; ProjectsRoot = folder;
        if (CurrentPage == "Projects") { config.UnityProjectsRoot = folder; await PersistConfigurationAsync(); await RefreshProjectsCore(); }
    });
    [RelayCommand] private async Task PickExtraFolder() { if (await ui.PickFolderAsync() is { } folder) ExtraImportsFolder = folder; }
    [RelayCommand] private async Task ResetSettings() => await Guard(async () =>
    {
        if (!RequireImportsIdle()) return;
        if (!await ui.ConfirmAsync("Reset settings", "Reset defaults, presets and paths? Your Unity projects are preserved.", "Reset")) return;
        if (!RequireImportsIdle()) return;
        await app.Configuration.ResetAsync(); await ReloadConfigurationAsync();
    });
    [RelayCommand] private async Task RepairInstallation() => await Guard(async () =>
    {
        if (!RequireImportsIdle()) return;
        await new InstallationManager(app.Paths).RepairAsync(InstallationRoot()); Status = "Installation and shortcuts verified";
    });
    [RelayCommand] private async Task UninstallApplication() => await Guard(async () =>
    {
        if (!RequireImportsIdle()) return;
        var root = InstallationRoot();
        if (!await ui.ConfirmAsync("Uninstall VRCSETUP", root + "\nSettings and Unity projects will be preserved.", "Uninstall")) return;
        if (!RequireImportsIdle()) return;
        await new InstallationManager(app.Paths).UninstallAsync(root, true); ui.CloseApplication();
    });
    private string InstallationRoot() => File.Exists(Path.Combine(app.Paths.PayloadRoot, ".vrcsetup-install.json")) ? app.Paths.PayloadRoot : InstallationManager.DefaultRoot;
    [RelayCommand] private async Task ImportSettings() => await Guard(async () =>
    {
        if (!RequireImportsIdle()) return;
        if (!await ResolveSettingsAsync()) return;
        if (PresetDirty && !await ui.ConfirmAsync("Unsaved preset", "Discard changes before importing settings?", "Discard")) return;
        if (await ui.PickFileAsync(".json") is { } file && await ui.ConfirmAsync("Restore settings", "This replaces your settings and presets. Your Unity projects stay unchanged.", "Restore") && RequireImportsIdle()) { await app.Configuration.ImportAsync(file); await ReloadConfigurationAsync(); }
    });
    [RelayCommand] private void OpenLogs()
    {
        Directory.CreateDirectory(app.Paths.Logs);
        if (SupportLogs.LatestError(app.Paths) is { } file) RevealLog(file); else OpenPath(app.Paths.Logs);
    }
    [RelayCommand] private void OpenLastLog() { if (File.Exists(LastLog)) RevealLog(LastLog); }
    [RelayCommand] private void OpenReport() { if (File.Exists(lastReport)) OpenPath(lastReport); }
    [RelayCommand] private void OpenProjectFolder() { if (session is not null) OpenPath(session.Projects[0].Path); }
    [RelayCommand] private async Task OpenUnity() => await Guard(async () =>
    {
        if (session?.Projects.Length != 1) return;
        await LaunchUnityAsync(session.Projects.Single().Path);
    });
    [RelayCommand] private async Task LaunchProject(ProjectSnapshot? project) => await Guard(async () =>
    {
        if (project is not null) await LaunchUnityAsync(project.Path);
    });
    private async Task LaunchUnityAsync(string project)
    {
        if (!Directory.Exists(project)) throw new IOException("Project folder is unavailable: " + project);
        if (!await FindEditorAsync(true)) return;
        if (app.Projects.IsBusy(project)) { Status = "Project is already open in Unity"; return; }
        var start = new ProcessStartInfo(config.UnityEditorPath) { UseShellExecute = false }; start.ArgumentList.Add("-projectPath"); start.ArgumentList.Add(project); Process.Start(start); await Task.CompletedTask;
    }
    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private async Task Guard(Func<Task> action, bool allowWhileBusy = false, bool serialize = true)
    {
        if (IsInstallingUpdate || IsBusy && !allowWhileBusy) { Status = "Wait for the current operation to finish, then try again."; return; }
        var area = CurrentPage;
        if (serialize) await actionGate.WaitAsync(lifetime.Token);
        try
        {
            if (IsInstallingUpdate || IsBusy && !allowWhileBusy) { Status = "Wait for the current operation to finish, then try again."; return; }
            ErrorMessage = ""; await action();
        }
        catch (OperationCanceledException) { Status = "Cancelled"; }
        catch (Exception error) { app.Diagnostics.Write(area, error.Message, error); ErrorMessage = UserErrors.Message(error); Status = "Action failed"; AppendActivity(new(DateTime.UtcNow, "Error", DeveloperMode ? error.ToString() : ErrorMessage, EventSeverity.Error)); }
        finally { if (serialize) actionGate.Release(); }
    }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items) { collection.Clear(); foreach (var item in items) collection.Add(item); }
    private Task IndependentAction(Func<Task> action) => Guard(action, allowWhileBusy: true);
    public void Dispose()
    {
        operation?.Cancel(); refresh?.Cancel(); packageSearch?.Cancel(); inspectionRequest?.Cancel(); lifetime.Cancel();
        app.Imports.Changed -= ImportsChanged;
        _ = app.Imports.DisposeAsync().AsTask().ContinueWith(task => { if (task.Exception is { } error) app.Diagnostics.Write("Closing imports", error.Message, error); }, TaskScheduler.Default);
    }
}

public sealed class SynchronizationProgress<T>(SynchronizationContext context, Action<T> callback) : IProgress<T>
{ public void Report(T value) => context.Post(_ => callback(value), null); }
public sealed record PlatformOption(string Label, string Value);
