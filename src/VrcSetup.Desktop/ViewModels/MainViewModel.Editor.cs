using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private AppConfiguration persistedConfiguration = new();
    public bool LibraryIsList => config.PackageLibraryView == "list";
    public Visibility LibraryListVisibility => LibraryIsList ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LibraryGridVisibility => LibraryIsList ? Visibility.Collapsed : Visibility.Visible;
    public string LibraryLayoutAction => LibraryIsList ? "Grid view" : "List view";
    public string LibraryLayoutGlyph => LibraryIsList ? "\uE80A" : "\uE8FD";
    [RelayCommand] private async Task ToggleLibraryLayout() => await Guard(async () =>
    {
        config.PackageLibraryView = LibraryIsList ? "grid" : "list";
        NotifyLibraryLayout(); await PersistConfigurationAsync();
    });
    private void NotifyLibraryLayout()
    {
        OnPropertyChanged(nameof(LibraryIsList)); OnPropertyChanged(nameof(LibraryListVisibility));
        OnPropertyChanged(nameof(LibraryGridVisibility)); OnPropertyChanged(nameof(LibraryLayoutAction)); OnPropertyChanged(nameof(LibraryLayoutGlyph));
    }
    public string EditorVersionLabel => "Unity " + UnityEditorDiscovery.PinnedVersion;
    public bool EditorReady => UnityEditorDiscovery.IsPinned(UnityEditorPath);
    public string EditorStatus => EditorReady ? "Ready" : "Editor not found";
    public string EditorPathLabel => string.IsNullOrWhiteSpace(UnityEditorPath) ? "Find the installed VRChat editor" : UnityEditorPath;
    public Visibility EditorSetupVisibility => EditorReady ? Visibility.Collapsed : Visibility.Visible;
    partial void OnUnityEditorPathChanged(string value)
    {
        OnPropertyChanged(nameof(EditorReady)); OnPropertyChanged(nameof(EditorStatus));
        OnPropertyChanged(nameof(EditorPathLabel)); OnPropertyChanged(nameof(EditorSetupVisibility));
    }
    private async Task PersistConfigurationAsync()
    {
        config = await app.Configuration.MergeAsync(persistedConfiguration, config, lifetime.Token);
        persistedConfiguration = config.Copy();
        NotifyLibraryLayout();
    }
    private async Task SetEditorAsync(string editor)
    {
        if (!UnityEditorDiscovery.IsPinned(editor)) throw new IOException("Choose Unity " + UnityEditorDiscovery.PinnedVersion + ". The selected executable is a different version or is unavailable.");
        config.UnityEditorPath = ProjectRules.NormalizePath(editor);
        await PersistConfigurationAsync(); UnityEditorPath = config.UnityEditorPath;
        Status = "Unity editor saved";
    }
    private async Task<bool> FindEditorAsync(bool prompt)
    {
        if (UnityEditorDiscovery.IsPinned(UnityEditorPath))
        {
            if (config.UnityEditorPath != UnityEditorPath) await SetEditorAsync(UnityEditorPath);
            return true;
        }
        var found = await Task.Run(() => UnityEditorDiscovery.FindEditors().FirstOrDefault(x => x.Version == UnityEditorDiscovery.PinnedVersion).Path, lifetime.Token);
        if (found is not null) { await SetEditorAsync(found); return true; }
        if (!prompt) return false;
        if (!await ui.ConfirmAsync("Set up Unity " + UnityEditorDiscovery.PinnedVersion, "The VRChat editor was not found. Select Unity.exe, or install this version with Unity Hub first.", "Locate Unity.exe")) return false;
        if (await ui.PickFileAsync(".exe") is not { } file) return false;
        await SetEditorAsync(file); return true;
    }
    [RelayCommand] private async Task FindEditor() => await Guard(async () => { await FindEditorAsync(true); });
    [RelayCommand] private void OpenUnityDownload() => OpenPath("https://creators.vrchat.com/sdk/upgrade/current-unity-version/");
}
