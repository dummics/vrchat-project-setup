using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Text.Json;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string settingsBaseline = "";
    private bool loadingSettings;
    private bool setupMode;
    private static readonly HashSet<string> SettingsProperties = [nameof(UnityEditorPath), nameof(ProjectsRoot), nameof(ExtraImportsFolder), nameof(NamingPrefix), nameof(NamingSuffix), nameof(NamingPatterns), nameof(RememberNames), nameof(Theme), nameof(ManualImportPath), nameof(AutoHideCompletedImports), nameof(CheckUpdatesOnStartup), nameof(IncludeBetaUpdates), nameof(AutomaticUpdateEnabled), nameof(SelectedLanguage), nameof(DeveloperMode), nameof(VerboseDiagnostics), nameof(TraceTools)];
    private string SettingsSnapshot() => JsonSerializer.Serialize(new object[] { UnityEditorPath, ProjectsRoot, ExtraImportsFolder, NamingPrefix, NamingSuffix, NamingPatterns, RememberNames, Theme, ManualImportPath, AutoHideCompletedImports, CheckUpdatesOnStartup, IncludeBetaUpdates, AutomaticUpdateEnabled, SelectedLanguage, DeveloperMode, VerboseDiagnostics, TraceTools });
    public bool SettingsDirty => !loadingSettings && settingsBaseline.Length > 0 && settingsBaseline != SettingsSnapshot();
    public Visibility SettingsChangesVisibility => SettingsDirty || setupMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SetupVisibility => setupMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsOptionsVisibility => setupMode ? Visibility.Collapsed : Visibility.Visible;
    public string SettingsSaveLabel => setupMode ? "Save and continue" : "Save settings";
    public string SettingsTitle => setupMode ? "Get started" : "Settings";
    private void NotifySettingsState()
    {
        OnPropertyChanged(nameof(SettingsDirty)); OnPropertyChanged(nameof(SettingsChangesVisibility));
        OnPropertyChanged(nameof(SetupVisibility)); OnPropertyChanged(nameof(SettingsOptionsVisibility)); OnPropertyChanged(nameof(SettingsSaveLabel));
        OnPropertyChanged(nameof(SettingsTitle));
    }
    private async Task<bool> ResolveSettingsAsync()
    {
        if (!SettingsDirty) return true;
        var choice = await ui.ResolveSettingsAsync();
        if (choice == "Discard") { LoadSettings(); return true; }
        if (choice != "Save") return false;
        try { await SaveSettingsCoreAsync(); return true; }
        catch (Exception error) { app.Diagnostics.Write("Settings", error.Message, error); ErrorMessage = VrcSetup.Core.UserErrors.Message(error); return false; }
    }
    [RelayCommand] private void DiscardSettings() => LoadSettings();
}
