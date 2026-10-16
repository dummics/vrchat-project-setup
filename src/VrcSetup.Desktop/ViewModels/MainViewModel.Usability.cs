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
    public Visibility PresetChangesVisibility => PresetDirty && !BrowsingProjectPackages ? Visibility.Visible : Visibility.Collapsed;
    public string PresetSaveContext => "Unsaved changes · " + PresetLabel;
    public Visibility ActivityEmptyVisibility => Activity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool HasSelectedProjects => SelectedProjects.Count > 0;
    public void NotifyProjectSelection() => OnPropertyChanged(nameof(HasSelectedProjects));
    public bool IsErrorVisible => ErrorMessage.Length > 0;
    public Visibility WorkVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeveloperVisibility => DeveloperMode ? Visibility.Visible : Visibility.Collapsed;
    public string PackageFileLabel => string.IsNullOrWhiteSpace(PackageFile) ? "Drop a .unitypackage here" : Path.GetFileName(PackageFile);
    public string DestinationPreview => string.IsNullOrWhiteSpace(ProjectsRoot) ? "Choose a projects folder in Settings" : Path.Combine(ProjectsRoot, string.IsNullOrWhiteSpace(ProjectName) ? "Project name" : ProjectName);
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(WorkVisibility)); }
    partial void OnDeveloperModeChanged(bool value) { OnPropertyChanged(nameof(DeveloperVisibility)); }
    partial void OnProjectNameChanged(string value) => OnPropertyChanged(nameof(DestinationPreview));
    partial void OnProjectsRootChanged(string value) => OnPropertyChanged(nameof(DestinationPreview));
    partial void OnLibraryFilterChanged(string value) { RefreshLibraryRows(); _ = SearchLibraryAsync(value); }
    partial void OnFavoritesOnlyChanged(bool value) => RefreshLibraryRows();
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
        Registry.FirstOrDefault(x => x.Id == id)?.DisplayName ?? VrcSetup.Infrastructure.PackageCatalog.FriendlyName(id), version, required, config.FavoritePackages.Contains(id, StringComparer.OrdinalIgnoreCase), BrowsingProjectPackages ? browserIncluded.Contains(id) : presetWorking.ContainsKey(id), BrowsingProjectPackages);
    private void RenderPreset()
    {
        var required = ProjectRules.Foundation(presetWorking, config);
        Replace(PresetPackages, presetWorking.Select(x => MakeRow(x.Key, x.Value, required.Contains(x.Key))).OrderBy(x => x.Id == ProjectRules.BaseSdk ? 0 : ProjectRules.SdkIds.Contains(x.Id) ? 1 : x.Id == ProjectRules.Resolver ? 2 : x.Required ? 3 : 4).ThenBy(x => x.Name));
    }
    private void RefreshLibraryRows()
    {
        browserIncluded = session?.Rows().Where(x => x.Included).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        Replace(LibraryPackages, Registry.Where(x =>
        (!FavoritesOnly || config.FavoritePackages.Contains(x.Id, StringComparer.OrdinalIgnoreCase)) && (x.Id + " " + x.DisplayName).Contains(LibraryFilter, StringComparison.OrdinalIgnoreCase))
        .Select(x => MakeRow(x.Id, x.SelectedVersion)).OrderByDescending(x => x.Favorite).ThenBy(x => x.Name));
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
        await app.Configuration.SaveAsync(config);
        presetSwitching = true;
        try { if (!Presets.Contains(name)) Presets.Add(name); EditingPreset = name; if (SelectedPreset.Equals(old, StringComparison.OrdinalIgnoreCase)) SelectedPreset = name; }
        finally { presetSwitching = false; }
        LoadPresetNames(); creationOverrides = overrides;
    }
    [RelayCommand] private async Task SaveEditedPreset() => await Guard(async () =>
    {
        var name = EditingPreset; var saved = new Dictionary<string, string>(presetWorking);
        if (name == DefaultPresetKey) config.VpmPackages = new(saved); else config.PackagePresets[name] = new(saved);
        await app.Configuration.SaveAsync(config);
        if (EditingPreset == name && saved.Count == presetWorking.Count && saved.All(x => presetWorking.GetValueOrDefault(x.Key) == x.Value)) PresetDirty = false;
        await LoadRegistry(); Status = (name == DefaultPresetKey ? "Default" : name) + " saved";
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
        if (BrowsingProjectPackages) { session?.AddDefinitions(Registry); if (session?.Removed.Contains(row.Id) == true) ToggleWorkspacePackage(session.Rows().First(x => x.Id == row.Id)); else StageWorkspaceVersion(row.Id, row.Version); }
        else if (!presetWorking.ContainsKey(row.Id)) { presetWorking[row.Id] = row.Version; PresetDirty = true; RenderPreset(); RefreshLibraryRows(); }
    }
    [RelayCommand] private async Task TogglePackageFavorite(UiPackageRow? row) => await Guard(async () =>
    {
        if (row is null) return;
        if (!config.FavoritePackages.Remove(row.Id)) config.FavoritePackages.Add(row.Id);
        await app.Configuration.SaveAsync(config); RenderPreset(); RefreshLibraryRows();
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

public sealed record UiPackageRow(string Id, string Name, string Version, bool Required, bool Favorite, bool Included, bool ProjectContext = false)
{
    public bool CanAdd => !Included;
    public Visibility AddVisibility => Included ? Visibility.Collapsed : Visibility.Visible;
    public Visibility IncludedVisibility => Included ? Visibility.Visible : Visibility.Collapsed;
    public bool CanRemove => !Required;
    public string VersionLabel => Version == "latest" ? "Latest" : Version;
    public string AddLabel => Included ? "Added" : ProjectContext ? "Add to project" : "Add to preset";
    public string IncludedLabel => ProjectContext ? "✓ In project" : "✓ In preset";
    public string FavoriteAction => Favorite ? "Remove favorite" : "Add to favorites";
    public Visibility FavoriteVisibility => Favorite ? Visibility.Visible : Visibility.Collapsed;
    public string StarGlyph => Favorite ? "\uE735" : "\uE734";
    public SolidColorBrush StarBrush => new(Favorite ? Microsoft.UI.Colors.Gold : Microsoft.UI.Colors.Gray);
    public Visibility RemoveVisibility => Required ? Visibility.Collapsed : Visibility.Visible;
}
public sealed record UiActivityEntry(string Time, string Phase, string Message);
