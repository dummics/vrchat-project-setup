using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AsyncVersionCache versionCache;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? packageSearch;
    private bool navigating;
    [ObservableProperty] public partial bool BrowsingProjectPackages { get; set; }
    [ObservableProperty] public partial bool IsSearchingPackages { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CatalogNoticeVisibility)), NotifyPropertyChangedFor(nameof(PresetCatalogVisibility))] public partial string CatalogNotice { get; set; } = "";
    private HashSet<string> browserIncluded = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> onlineMatches = new(StringComparer.OrdinalIgnoreCase);
    public Visibility CatalogNoticeVisibility => CatalogNotice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProjectCatalogVisibility => BrowsingProjectPackages ? Visibility.Visible : Visibility.Collapsed;
    public bool WorkspaceDirty => session?.HasPendingChanges == true;
    public Visibility WorkspaceChangesVisibility => WorkspaceDirty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SingleProjectLaunchVisibility => session?.Projects.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
    public string WorkspaceChangeContext => session is null ? "" : "Pending changes · " + session.Projects.Length + (session.Projects.Length == 1 ? " project" : " projects");
    partial void OnBrowsingProjectPackagesChanged(bool value) { OnPropertyChanged(nameof(ProjectCatalogVisibility)); OnPropertyChanged(nameof(PresetChangesVisibility)); RefreshLibraryRows(); OnPropertyChanged(nameof(LibraryPendingVisibility)); }
    public string[] CachedVersions(string id) => Registry.FirstOrDefault(x => x.Id == id)?.Versions ?? [];
    public Task<string[]> LoadVersionsAsync(string id) => versionCache.GetAsync(id);
    public string PackageDisplayName(string id)
    {
        var name = Packages.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? Registry.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.DisplayName;
        return string.IsNullOrWhiteSpace(name) || name.Equals(id, StringComparison.OrdinalIgnoreCase) ? VrcSetup.Infrastructure.PackageCatalog.FriendlyName(id) : name;
    }
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!WorkspaceDirty && !PresetDirty) return true;
        return await ui.ConfirmAsync("Unsaved changes", "Discard pending package changes and close?", "Discard and close");
    }
    public async Task<bool> NavigateAsync(string page)
    {
        if (page == CurrentPage) return true;
        if (navigating || IsBusy) return false;
        navigating = true;
        try
        {
            var sameProjectDraft = (page is "Workspace" or "Catalog") && (CurrentPage is "Workspace" or "Catalog");
            if (PresetDirty && page != "Presets")
            {
                if (!await ui.ConfirmAsync("Unsaved preset", "Discard changes to " + PresetLabel + "?", "Discard and leave")) return false;
            }
            if (PresetDirty && page != "Presets") LoadEditingPreset();
            if (!sameProjectDraft && session is not null)
            {
                session.Reset(); session = null; RenderWorkspace(); RefreshLibraryRows();
            }
            BrowsingProjectPackages = false; CurrentPage = page; return true;
        }
        finally { navigating = false; }
    }
    [RelayCommand] private async Task BackToProjects() => await NavigateAsync("Projects");
    [RelayCommand] private void BackToProject() { CurrentPage = "Workspace"; BrowsingProjectPackages = false; }
    [RelayCommand] private void OpenProjectCatalog()
    { if (session is null) return; BrowsingProjectPackages = true; RefreshLibraryRows(); CurrentPage = "Catalog"; }
    [RelayCommand] private void OpenPresetLibrary() { BrowsingProjectPackages = false; RefreshLibraryRows(); CurrentPage = "Presets"; }
    public void StageWorkspaceVersion(string id, string version)
    { if (session is null || IsBusy) return; session.SetVersion(id, version); RenderWorkspace(); RefreshLibraryRows(); }
    public void ToggleWorkspacePackage(PackageWorkspaceRow row)
    {
        if (session is null || IsBusy || row.Required) return;
        if (session.Removed.Remove(row.Id)) session.Selected.Remove(row.Id);
        else if (row.Included && !session.Projects.Any(x => x.Packages.ContainsKey(row.Id))) session.Selected.Remove(row.Id);
        else session.Toggle(row.Id);
        RenderWorkspace(); RefreshLibraryRows();
    }
    public void StagePresetVersion(string id, string version)
    {
        if (IsBusy) return;
        var snapshot = new ProjectSnapshot { Path = Path.GetFullPath("preset"), Name = PresetLabel, Packages = new(presetWorking) };
        var edit = new PackageEditSession([snapshot], config, Registry.ToArray()); edit.SetVersion(id, version);
        presetWorking = ProjectRules.Merge(presetWorking, edit.Selected); PresetDirty = true; RenderPreset(); RefreshLibraryRows();
    }
    private void NotifyWorkspaceChanges()
    { OnPropertyChanged(nameof(WorkspaceFriendlySummary)); OnPropertyChanged(nameof(WorkspaceFriendlyNames)); OnPropertyChanged(nameof(LibraryPendingVisibility)); OnPropertyChanged(nameof(WorkspaceDirty)); OnPropertyChanged(nameof(WorkspaceChangesVisibility)); OnPropertyChanged(nameof(WorkspaceChangeContext)); OnPropertyChanged(nameof(SingleProjectLaunchVisibility)); }
    private async Task SearchLibraryAsync(string query)
    {
        packageSearch?.Cancel(); var source = packageSearch = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = source.Token;
        IsSearchingPackages = false; CatalogNotice = "";
        try
        {
            if (query.Trim().Length < 2) return;
            await Task.Delay(350, token); IsSearchingPackages = true;
            var found = await Task.Run(() => app.Catalog.SearchAsync(query, token), token);
            if (token.IsCancellationRequested || source != packageSearch) return;
            var merged = Registry.Where(x => personalPackageIds.Contains(x.Id) || config.FavoritePackages.Contains(x.Id) || presetWorking.ContainsKey(x.Id) || BrowsingProjectPackages && browserIncluded.Contains(x.Id)).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var item in found) merged[item.Id] = item;
            onlineMatches = found.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Replace(Registry, merged.Values);
            session?.AddDefinitions(found); RefreshLibraryRows();
            CatalogNotice = found.Length == 0 && LibraryPackages.Count == 0 ? "No matching packages" : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (source == packageSearch) { CatalogNotice = "Online search unavailable. Local packages remain available."; app.Diagnostics.Write("Package search", error.Message, error); } }
        finally { if (source == packageSearch) { IsSearchingPackages = false; packageSearch = null; } source.Dispose(); }
    }
}
