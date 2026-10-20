using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool editingNewProject;
    [ObservableProperty] public partial string NewProjectName { get; set; } = "";
    [ObservableProperty] public partial string NewProjectPlatform { get; set; } = "StandaloneWindows64";
    public DisplayOption[] NewProjectPlatforms => [new("StandaloneWindows64", "Windows"), new("Android", "Android / Quest"), new("iOS", "iOS")];
    public Visibility NewProjectVisibility => editingNewProject ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresetCarouselVisibility => editingNewProject ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NewProjectSavePresetVisibility => editingNewProject && PresetDirty ? Visibility.Visible : Visibility.Collapsed;
    public async Task BeginNewProjectAsync() => await Guard(async () =>
    {
        if (!await NavigateAsync("NewProject")) return;
        editingNewProject = true; editingImportPreset = true;
        LoadEditingPreset(); LibraryFilter = ""; FavoritesOnly = false;
        NotifyPackageContext();
    });
    private void NotifyPackageContext()
    {
        OnPropertyChanged(nameof(NewProjectVisibility)); OnPropertyChanged(nameof(UseImportPresetVisibility));
        OnPropertyChanged(nameof(PresetCarouselVisibility)); OnPropertyChanged(nameof(NewProjectSavePresetVisibility));
        OnPropertyChanged(nameof(PresetOptionsVisibility)); OnPropertyChanged(nameof(IsCustomPreset)); OnPropertyChanged(nameof(PresetChangesVisibility));
    }
    [RelayCommand] private Task CreateConfiguredProject() => CreateEmptyProjectAsync(NewProjectName.Trim(), EditingPreset, NewProjectPlatform, new(presetWorking));
    public async Task CreateEmptyProjectAsync(string name, string? preset, string platform, Dictionary<string, string>? packages = null) => await Guard(async () =>
    {
        ProjectRules.ValidateName(name);
        if (!await FindEditorAsync(true)) return;
        var choices = config.Copy(); choices.UnityEditorPath = UnityEditorPath; choices.UnityProjectsRoot = ProjectsRoot;
        if (packages is not null)
        {
            if (preset is null || preset == DefaultPresetKey) choices.VpmPackages = new(packages);
            else choices.PackagePresets[preset] = new(packages);
        }
        var plan = await Task.Run(() => app.PlanEmptyProjectAsync(name, choices, preset == DefaultPresetKey ? null : preset, platform, lifetime.Token));
        if (!await ui.ReviewAsync(plan)) return;
        await app.Imports.EnqueueAsync(plan, RequestedParallelism, token: lifetime.Token);
        presetBaseline = new(presetWorking);
        await NavigateAsync("Activity");
    });
}
