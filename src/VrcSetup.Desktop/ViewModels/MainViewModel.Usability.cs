using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private const string DefaultPresetKey = "My default packages";
    private Dictionary<string, string> presetWorking = new(StringComparer.OrdinalIgnoreCase);
    private bool presetSwitching;
    public ObservableCollection<UiPackageRow> PresetPackages { get; } = [];
    public ObservableCollection<UiPackageRow> LibraryPackages { get; } = [];
    private HashSet<string>? libraryFavoriteSnapshot;
    private HashSet<string> personalPackageIds = new(StringComparer.OrdinalIgnoreCase);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PresetLabel)), NotifyPropertyChangedFor(nameof(IsCustomPreset)), NotifyPropertyChangedFor(nameof(PresetSaveContext))] public partial string EditingPreset { get; set; } = DefaultPresetKey;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PresetChangesVisibility))] public partial bool PresetDirty { get; set; }
    [ObservableProperty] public partial string LibraryFilter { get; set; } = "";
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsErrorVisible))] public partial string ErrorMessage { get; set; } = "";
    [ObservableProperty] public partial bool DeveloperMode { get; set; }
    [ObservableProperty] public partial bool VerboseDiagnostics { get; set; }
    [ObservableProperty] public partial bool TraceTools { get; set; }
    [ObservableProperty] public partial bool IsLoadingProjects { get; set; }
    [ObservableProperty] public partial Visibility EmptyProjectsVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial string EmptyProjectsMessage { get; set; } = "";
    public string PresetLabel => EditingPreset == DefaultPresetKey ? "Default" : EditingPreset;
    public bool IsCustomPreset => EditingPreset != DefaultPresetKey;
    public bool HasMultiplePresets => Presets.Count > 1;
    public Visibility PresetCatalogVisibility => LibraryPackages.Count > 0 || CatalogNotice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresetIncludedVisibility => PresetPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    [RelayCommand] private void PreviousPreset() => CyclePreset(-1);
    [RelayCommand] private void NextPreset() => CyclePreset(1);
    private void CyclePreset(int direction)
    {
        if (IsBusy || Presets.Count < 2) return;
        EditingPreset = Presets[(Presets.IndexOf(EditingPreset) + direction + Presets.Count) % Presets.Count];
    }
    public Visibility PresetChangesVisibility => PresetDirty && !BrowsingProjectPackages ? Visibility.Visible : Visibility.Collapsed;
    public string PresetSaveContext => "Unsaved changes · " + PresetLabel;
    public Visibility ActivityEmptyVisibility => Activity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool HasSelectedProjects => SelectedProjects.Count > 0;
    public Visibility ManageSelectionVisibility => SelectedProjects.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
    public string ManageSelectionLabel => SelectedProjects.Count > 0 ? $"Manage {SelectedProjects.Count} " + (SelectedProjects.Count == 1 ? "project" : "projects") : "Manage selected projects";
    public void NotifyProjectSelection() { OnPropertyChanged(nameof(HasSelectedProjects)); OnPropertyChanged(nameof(ManageSelectionLabel)); OnPropertyChanged(nameof(ManageSelectionVisibility)); }
    public bool IsErrorVisible => ErrorMessage.Length > 0;
    public Visibility DeveloperVisibility => DeveloperMode ? Visibility.Visible : Visibility.Collapsed;
    public string PackageFileLabel => ImportDrafts.Count > 1 ? $"{ImportDrafts.Count} packages selected" : string.IsNullOrWhiteSpace(PackageFile) ? "Drop .unitypackages here" : Path.GetFileName(PackageFile);
    public string DestinationPreview => string.IsNullOrWhiteSpace(ProjectsRoot) ? "Choose a projects folder in Settings" : Path.Combine(ProjectsRoot, string.IsNullOrWhiteSpace(ProjectName) ? "Project name" : ProjectName);
    partial void OnDeveloperModeChanged(bool value) { OnPropertyChanged(nameof(DeveloperVisibility)); }
    partial void OnProjectNameChanged(string value)
    { OnPropertyChanged(nameof(DestinationPreview)); if (!switchingImportDraft && SelectedImportDraft is { } draft) draft.Name = value; }
    partial void OnBuildTargetChanged(string value)
    { if (!switchingImportDraft && SelectedImportDraft is { } draft) draft.Platform = value; }
    partial void OnProjectsRootChanged(string value) => OnPropertyChanged(nameof(DestinationPreview));
    partial void OnLibraryFilterChanged(string value) { libraryFavoriteSnapshot = null; onlineMatches.Clear(); RefreshLibraryRows(); RenderPreset(); _ = SearchLibraryAsync(value ?? ""); }
    partial void OnFavoritesOnlyChanged(bool value) { libraryFavoriteSnapshot = null; RefreshLibraryRows(); RenderPreset(); }
    partial void OnCurrentPageChanged(string value)
    {
        BrowsingProjectPackages = value == "Workspace" && session is not null;
        if (value == "Activity") _ = RefreshSavedHistoryAsync();
        if (value is "Presets" or "Workspace") { libraryFavoriteSnapshot = null; LibraryPackages.Clear(); RefreshLibraryRows(); }
    }
    partial void OnEditingPresetChanged(string oldValue, string newValue)
    {
        if (presetSwitching) return;
        if (!Presets.Contains(newValue)) { presetSwitching = true; EditingPreset = Presets.Contains(oldValue) ? oldValue : DefaultPresetKey; presetSwitching = false; return; }
        _ = Guard(async () =>
        {
            if (PresetDirty && !await ui.ConfirmAsync("Unsaved packages", "Discard changes to " + (oldValue == DefaultPresetKey ? "Default" : oldValue) + "?", "Discard"))
            { presetSwitching = true; EditingPreset = oldValue; presetSwitching = false; return; }
            LoadEditingPreset();
        });
    }
    private void LoadEditingPreset()
    {
        presetWorking = new(EditingPreset == DefaultPresetKey ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(EditingPreset) ?? []);
        foreach (var id in ProjectRules.Foundation(presetWorking, config)) presetWorking.TryAdd(id, "latest");
        PresetDirty = false; RenderPreset(); RefreshLibraryRows();
    }
    private UiPackageRow MakeRow(string id, string version, bool required = false) => new(id,
        Registry.FirstOrDefault(x => x.Id == id)?.DisplayName is { Length: > 0 } name && !name.Equals(id, StringComparison.OrdinalIgnoreCase) ? name : VrcSetup.Infrastructure.PackageCatalog.FriendlyName(id), version, required, config.FavoritePackages.Contains(id, StringComparer.OrdinalIgnoreCase), BrowsingProjectPackages ? browserIncluded.Contains(id) : presetWorking.ContainsKey(id), BrowsingProjectPackages);
    private void RenderPreset()
    {
        var required = ProjectRules.Foundation(presetWorking, config);
        var tokens = (LibraryFilter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var desired = presetWorking.Select(x => MakeRow(x.Key, x.Value, required.Contains(x.Key)))
            .Where(x => tokens.All(t => (x.Name + " " + x.Id).Contains(t, StringComparison.OrdinalIgnoreCase)) && (!FavoritesOnly || (libraryFavoriteSnapshot ?? config.FavoritePackages.ToHashSet(StringComparer.OrdinalIgnoreCase)).Contains(x.Id)))
            .OrderBy(x => x.Id == ProjectRules.Resolver ? 0 : x.Id == ProjectRules.BaseSdk ? 1 : ProjectRules.SdkIds.Contains(x.Id) ? 2 : x.Required ? 3 : 4).ThenBy(x => x.Name).ToArray();
        var ids = desired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = PresetPackages.Count - 1; i >= 0; i--) if (!ids.Contains(PresetPackages[i].Id)) PresetPackages.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++)
        {
            var next = desired[i]; var current = PresetPackages.FirstOrDefault(x => x.Id.Equals(next.Id, StringComparison.OrdinalIgnoreCase));
            if (current is null) PresetPackages.Insert(i, next);
            else
            {
                var index = PresetPackages.IndexOf(current);
                if (current.Name != next.Name || current.Required != next.Required) PresetPackages[index] = next;
                else { current.UpdateVersion(next.Version); current.Favorite = next.Favorite; }
                if (index != i) PresetPackages.Move(index, i);
            }
        }
        OnPropertyChanged(nameof(PresetIncludedVisibility));
    }
    private void RefreshLibraryRows()
    {
        browserIncluded = session?.Rows().Where(x => x.Included).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var tokens = (LibraryFilter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        libraryFavoriteSnapshot = config.FavoritePackages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desired = Registry.Where(x => (BrowsingProjectPackages ? !projectCatalogBaseline.Contains(x.Id) : !presetWorking.ContainsKey(x.Id)) && (tokens.Length > 0 || personalPackageIds.Contains(x.Id) ||
            config.FavoritePackages.Contains(x.Id, StringComparer.OrdinalIgnoreCase) || presetWorking.ContainsKey(x.Id) ||
            BrowsingProjectPackages && browserIncluded.Contains(x.Id)) &&
        (!FavoritesOnly || libraryFavoriteSnapshot.Contains(x.Id)) && (onlineMatches.Contains(x.Id) || tokens.All(token => (x.Id + " " + x.DisplayName).Contains(token, StringComparison.OrdinalIgnoreCase))))
        .Select(x => MakeRow(x.Id, "latest")).OrderByDescending(x => libraryFavoriteSnapshot.Contains(x.Id)).ThenBy(x => x.Name).ToArray();
        var ids = desired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = LibraryPackages.Count - 1; index >= 0; index--) if (!ids.Contains(LibraryPackages[index].Id)) LibraryPackages.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
        {
            var next = desired[index];
            var existing = LibraryPackages.FirstOrDefault(x => x.Id.Equals(next.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) LibraryPackages.Add(next);
            else if (existing.Name != next.Name || existing.Required != next.Required || existing.ProjectContext != next.ProjectContext) LibraryPackages[LibraryPackages.IndexOf(existing)] = next;
            else { existing.UpdateVersion(next.Version); existing.UpdateIncluded(next.Included); existing.Favorite = next.Favorite; }
            var position = LibraryPackages.IndexOf(LibraryPackages.First(x => x.Id.Equals(next.Id, StringComparison.OrdinalIgnoreCase)));
            if (position != index) LibraryPackages.Move(position, index);
        }
        OnPropertyChanged(nameof(PresetCatalogVisibility));
    }
    private void UpdateProjectState()
    {
        EmptyProjectsVisibility = !IsLoadingProjects && Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyProjectsMessage = ProjectFilter.Length > 0 ? "No projects match your search." : string.IsNullOrWhiteSpace(ProjectsRoot) ? "Choose a projects folder in Settings, or add an existing project." : !Directory.Exists(ProjectsRoot) ? "Projects folder is unavailable: " + ProjectsRoot + ". Reconnect the drive or choose another folder." : "No Unity projects found in " + ProjectsRoot;
    }
    [RelayCommand] private void DismissError() => ErrorMessage = "";
    [RelayCommand] private async Task OpenProjectPackages(ProjectSnapshot? project) => await Guard(async () =>
    { if (project is not null) await OpenProjectsAsync([project]); });
    [RelayCommand] private async Task RenamePreset() => await Guard(async () =>
    {
        if (!IsCustomPreset) return;
        var name = await ui.TextAsync("Rename preset", EditingPreset); if (name is not null) await RenameEditingPresetAsync(name);
    });
    internal async Task RenameEditingPresetAsync(string name)
    {
        var old = EditingPreset; name = name.Trim();
        var overrides = creationOverrides;
        if (!IsCustomPreset || string.IsNullOrWhiteSpace(name) || name == old) return;
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.Equals(DefaultPresetKey, StringComparison.OrdinalIgnoreCase) || config.PackagePresets.Keys.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Choose a different preset name.");
        config.PackagePresets[name] = config.PackagePresets[old]; config.PackagePresets.Remove(old);
        foreach (var key in config.CreationRecipes.Where(x => x.Value.Preset.Equals(old, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToArray()) config.CreationRecipes[key] = config.CreationRecipes[key] with { Preset = name };
        await PersistConfigurationAsync();
        presetSwitching = true;
        try { if (!Presets.Contains(name)) Presets.Add(name); EditingPreset = name; if (SelectedPreset.Equals(old, StringComparison.OrdinalIgnoreCase)) SelectedPreset = name; }
        finally { presetSwitching = false; }
        LoadPresetNames(); creationOverrides = overrides;
    }
    [RelayCommand] private async Task SaveEditedPreset() => await Guard(async () =>
    {
        var name = EditingPreset; var saved = new Dictionary<string, string>(presetWorking);
        if (name == DefaultPresetKey) config.VpmPackages = new(saved); else config.PackagePresets[name] = new(saved);
        await PersistConfigurationAsync();
        await app.Catalog.RememberAsync(Registry.Where(x => saved.ContainsKey(x.Id)), lifetime.Token, preserveSelections: true);
        if (EditingPreset == name && saved.Count == presetWorking.Count && saved.All(x => presetWorking.GetValueOrDefault(x.Key) == x.Value)) PresetDirty = false;
        await LoadRegistry(); ShowSavedFeedback("Preset saved"); Status = (name == DefaultPresetKey ? "Default" : name) + " saved";
    });
    [RelayCommand] private void DiscardPresetChanges() => LoadEditingPreset();
    [RelayCommand] private async Task ChangePresetVersion(UiPackageRow? row) => await Guard(async () =>
    {
        if (row is null) return;
        var version = await ui.VersionAsync(row.Id, await app.Catalog.GetVersionsAsync(row.Id), row.Version); if (version is null) return;
        var snapshot = new ProjectSnapshot { Path = Path.GetFullPath("preset"), Name = PresetLabel, Packages = new(presetWorking) };
        var edit = new PackageEditSession([snapshot], config, Registry.ToArray()); edit.SetVersion(row.Id, version);
        presetWorking = ProjectRules.Merge(presetWorking, edit.Selected); PresetDirty = true; RenderPreset();
    });
    [RelayCommand] private void RemovePresetPackage(UiPackageRow? row)
    {
        if (row is null || ProjectRules.Foundation(presetWorking, config).Contains(row.Id)) return;
        presetWorking.Remove(row.Id); PresetDirty = true; RenderPreset(); RefreshLibraryRows();
    }
    [RelayCommand] private void AddLibraryPackage(UiPackageRow? row)
    {
        if (row is null || IsBusy) return;
        if (BrowsingProjectPackages) { session?.AddDefinitions(Registry); if (session is not null && (row.Included || session.Removed.Contains(row.Id))) ToggleWorkspacePackage(session.Rows().First(x => x.Id == row.Id)); else StageWorkspaceVersion(row.Id, "latest"); }
        else if (!presetWorking.ContainsKey(row.Id)) { presetWorking[row.Id] = "latest"; PresetDirty = true; RenderPreset(); RefreshLibraryRows(); }
    }
    [RelayCommand] private async Task TogglePackageFavorite(UiPackageRow? row) => await Guard(async () =>
    {
        if (row is null) return;
        if (!config.FavoritePackages.Remove(row.Id)) config.FavoritePackages.Add(row.Id);
        await PersistConfigurationAsync();
        var favorite = config.FavoritePackages.Contains(row.Id, StringComparer.OrdinalIgnoreCase);
        if (favorite && Registry.FirstOrDefault(x => x.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase)) is { } definition)
            await app.Catalog.RememberAsync([definition], lifetime.Token);
        foreach (var visible in LibraryPackages.Concat(PresetPackages).Where(x => x.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase))) visible.Favorite = favorite;
        RefreshLibraryRows(); RenderPreset();
    });
    [RelayCommand] private void OpenDiagnostics()
    { Directory.CreateDirectory(app.Paths.Logs); OpenPath(app.Paths.Logs); }
    public async Task ReloadConfigurationAsync()
    {
        config = await app.Configuration.LoadAsync(); app.ConfigureDiagnostics(config);
        LoadSettings(); LoadPresetNames(); LoadEditingPreset(); await LoadRegistry(); UpdateProjectState();
    }
    [RelayCommand] private async Task ReloadSettings() => await Guard(async () =>
    {
        if (PresetDirty && !await ui.ConfirmAsync("Unsaved preset", "Discard changes and reload settings?", "Discard")) return;
        await ReloadConfigurationAsync(); if (!string.IsNullOrWhiteSpace(config.UnityProjectsRoot)) await RefreshProjectsCore();
    });
}

public sealed class UiPackageRow(string id, string name, string version, bool required, bool favorite, bool included, bool projectContext = false) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    private string versionValue = version;
    public string Version => versionValue;
    internal void UpdateVersion(string value)
    { if (SetProperty(ref versionValue, value, nameof(Version))) OnPropertyChanged(nameof(VersionLabel)); }
    public bool Required { get; } = required;
    private bool includedValue = included;
    public bool Included => includedValue;
    internal void UpdateIncluded(bool value)
    {
        if (!SetProperty(ref includedValue, value, nameof(Included))) return;
        foreach (var property in new[] { nameof(CanAdd), nameof(AddVisibility), nameof(IncludedVisibility), nameof(AddLabel), nameof(IncludedLabel) }) OnPropertyChanged(property);
    }
    public bool ProjectContext { get; } = projectContext;
    private bool favoriteValue = favorite;
    public bool Favorite
    {
        get => favoriteValue;
        set
        {
            if (!SetProperty(ref favoriteValue, value)) return;
            OnPropertyChanged(nameof(FavoriteAction)); OnPropertyChanged(nameof(FavoriteVisibility));
            OnPropertyChanged(nameof(StarGlyph)); OnPropertyChanged(nameof(StarBrush));
        }
    }
    public bool CanAdd => !Included;
    public Visibility AddVisibility => !Included || ProjectContext ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IncludedVisibility => Included && !ProjectContext ? Visibility.Visible : Visibility.Collapsed;
    public bool CanRemove => !Required;
    public string VersionLabel => Version == "latest" ? "Latest" : Version;
    public string AddLabel => Included ? "Added" : ProjectContext ? "Add to project" : "Add";
    public string IncludedLabel => ProjectContext ? "✓ In project" : "✓ In preset";
    public string FavoriteAction => Favorite ? "Remove favorite" : "Add to favorites";
    public Visibility FavoriteVisibility => Favorite ? Visibility.Visible : Visibility.Collapsed;
    public string StarGlyph => Favorite ? "\uE735" : "\uE734";
    public SolidColorBrush StarBrush => new(Favorite ? Microsoft.UI.Colors.Gold : Microsoft.UI.Colors.Gray);
    public Visibility RemoveVisibility => Required ? Visibility.Collapsed : Visibility.Visible;
}
public sealed record UiActivityEntry(string Time, string Phase, string Message);
