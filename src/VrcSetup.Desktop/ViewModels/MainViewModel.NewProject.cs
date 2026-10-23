using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool editingNewProject;
    [ObservableProperty] public partial bool IsCreatingProject { get; set; }
    partial void OnIsCreatingProjectChanged(bool value) => NotifyApplicationStatus();
    internal Func<OperationPlan, Task>? EnqueueNewProjectForVerification { get; set; }
    [ObservableProperty] public partial string NewProjectName { get; set; } = "";
    [ObservableProperty] public partial string NewProjectPlatform { get; set; } = "StandaloneWindows64";
    public DisplayOption[] NewProjectPlatforms => [new("StandaloneWindows64", "Windows"), new("Android", "Android / Quest"), new("iOS", "iOS")];
    public Visibility NewProjectVisibility => editingNewProject ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresetCarouselVisibility => editingNewProject ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NewProjectSavePresetVisibility => editingNewProject && PresetDirty ? Visibility.Visible : Visibility.Collapsed;
    public async Task BeginNewProjectAsync() => await IndependentAction(async () =>
    {
        if (!await NavigateAsync("NewProject")) return;
        editingNewProject = true; editingImportPreset = true;
        NewProjectName = ""; LoadEditingPreset(); LibraryFilter = ""; FavoritesOnly = false;
        NotifyPackageContext();
    });
    private void NotifyPackageContext()
    {
        OnPropertyChanged(nameof(NewProjectVisibility)); OnPropertyChanged(nameof(UseImportPresetVisibility));
        OnPropertyChanged(nameof(PresetCarouselVisibility)); OnPropertyChanged(nameof(NewProjectSavePresetVisibility));
        OnPropertyChanged(nameof(PresetOptionsVisibility)); OnPropertyChanged(nameof(IsCustomPreset)); OnPropertyChanged(nameof(PresetChangesVisibility));
    }
    [RelayCommand] private async Task CreateConfiguredProject()
    {
        if (IsCreatingProject) return;
        IsCreatingProject = true;
        try { await CreateEmptyProjectAsync(NewProjectName.Trim(), EditingPreset, NewProjectPlatform, new(presetWorking)); }
        finally { IsCreatingProject = false; }
    }
    public async Task CreateEmptyProjectAsync(string name, string? preset, string platform, Dictionary<string, string>? packages = null) => await IndependentAction(async () =>
    {
        var request = workspaceRequest; var page = CurrentPage;
        ProjectRules.ValidateName(name);
        if (string.IsNullOrWhiteSpace(ProjectsRoot)) throw new IOException("Choose the projects folder in Settings first.");
        var target = Path.Combine(ProjectRules.NormalizePath(ProjectsRoot), name);
        var selection = new Dictionary<string, string>(packages ?? (preset is null || preset == DefaultPresetKey ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(preset) ?? []));
        if (File.Exists(target)) throw new IOException("A file already uses this project name. Choose another name.");
        if (Directory.Exists(target))
        {
            if (!File.Exists(Path.Combine(target, "ProjectSettings", "ProjectVersion.txt")))
                throw new IOException("This name is already used by another folder. Choose another project name.");
            if (ProjectApplyActive([target]) || ProjectImportsActive([target])) return;
            if (!await ui.ConfirmAsync("Project already exists", "This project will not be recreated. You can open its package list with these choices ready to apply. Changing package versions may affect compatibility. Nothing changes until you press Apply changes.", "Open package changes")) return;
            if (IsInstallingUpdate || ProjectApplyActive([target]) || ProjectImportsActive([target])) return;
            if (!await NavigateAsync("Projects")) return;
            var existing = await app.Projects.ReadAsync(target, lifetime.Token);
            await OpenProjectsAsync([existing]);
            if (CurrentPage == "Workspace" && session?.MatchesProjects([existing]) == true)
                foreach (var package in selection) StageWorkspaceVersion(package.Key, package.Value);
            return;
        }
        if (!await FindEditorAsync(true)) return;
        var choices = config.Copy(); choices.UnityEditorPath = UnityEditorPath; choices.UnityProjectsRoot = ProjectsRoot;
        if (packages is not null)
        {
            if (preset is null || preset == DefaultPresetKey) choices.VpmPackages = new(packages);
            else choices.PackagePresets[preset] = new(packages);
        }
        var plan = await Task.Run(() => app.PlanEmptyProjectAsync(name, choices, preset == DefaultPresetKey ? null : preset, platform, lifetime.Token));
        if (ProjectApplyActive(plan.Projects.Select(x => x.Path))) return;
        if (IsInstallingUpdate || ProjectApplyActive(plan.Projects.Select(x => x.Path))) return;
        if (EnqueueNewProjectForVerification is { } enqueue) await enqueue(plan);
        else await app.Imports.EnqueueAsync(plan, RequestedParallelism, token: lifetime.Token);
        if (request == workspaceRequest && page == CurrentPage)
        {
            presetBaseline = new(presetWorking); RenderPreset();
            await NavigateAsync("Activity"); ShowSavedFeedback("Project creation started");
        }
    });
}
