using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string settingsBaseline = "";
    private bool loadingSettings;
    public bool IsLoadingSettings => loadingSettings;
    private void LoadSettings()
    {
        loadingSettings = true;
        try
        {
            persistedConfiguration = config.Copy();
            NotifyLibraryLayout();
            UnityEditorPath = config.UnityEditorPath; ProjectsRoot = config.UnityProjectsRoot; ExtraImportsFolder = config.UnityPackagesFolder ?? "";
            ManualImportPath = config.ManualImportPath;
            AutoHideCompletedImports = config.AutoHideCompletedImports;
            CheckUpdatesOnStartup = config.CheckUpdatesOnStartup; IncludeBetaUpdates = config.IncludeBetaUpdates;
            AutomaticUpdateEnabled = config.AutomaticUpdates;
            SelectedLanguage = config.Language;
            NamingPrefix = config.Naming.DefaultPrefix; NamingSuffix = config.Naming.DefaultSuffix; NamingPatterns = string.Join(Environment.NewLine, config.Naming.RegexRemovePatterns);
            RememberNames = config.Naming.RememberUnityPackageNames; Theme = config.Theme; ColorPalette = config.ColorPalette; CanvasColor = config.CanvasColor; SurfaceColor = config.SurfaceColor; AccentColor = config.AccentColor; ProjectSort = config.ProjectLibrarySort;
            DeveloperMode = app.Diagnostics.DeveloperMode; VerboseDiagnostics = app.Diagnostics.Verbose; TraceTools = app.Diagnostics.TraceTools;
            settingsBaseline = SettingsSnapshot();
        }
        finally
        {
            loadingSettings = false;
            OnPropertyChanged(nameof(IsLoadingSettings));
        }
        NotifySettingsState();
    }
    private bool setupMode;
    private static readonly HashSet<string> SettingsProperties = [nameof(UnityEditorPath), nameof(ProjectsRoot), nameof(ExtraImportsFolder), nameof(NamingPrefix), nameof(NamingSuffix), nameof(NamingPatterns), nameof(RememberNames), nameof(Theme), nameof(ColorPalette), nameof(CanvasColor), nameof(SurfaceColor), nameof(AccentColor), nameof(ManualImportPath), nameof(AutoHideCompletedImports), nameof(CheckUpdatesOnStartup), nameof(IncludeBetaUpdates), nameof(AutomaticUpdateEnabled), nameof(SelectedLanguage), nameof(DeveloperMode), nameof(VerboseDiagnostics), nameof(TraceTools)];
    private string SettingsSnapshot() => JsonSerializer.Serialize(new object[] { UnityEditorPath, ProjectsRoot, ExtraImportsFolder, NamingPrefix, NamingSuffix, NamingPatterns, RememberNames, Theme, ColorPalette, CanvasColor, SurfaceColor, AccentColor, ManualImportPath, AutoHideCompletedImports, CheckUpdatesOnStartup, IncludeBetaUpdates, AutomaticUpdateEnabled, SelectedLanguage, DeveloperMode, VerboseDiagnostics, TraceTools });
    public bool SettingsDirty => !loadingSettings && settingsBaseline.Length > 0 && settingsBaseline != SettingsSnapshot();
    public Visibility SettingsChangesVisibility => SettingsDirty || setupMode || LanguageRestartVisibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SetupVisibility => setupMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsOptionsVisibility => setupMode ? Visibility.Collapsed : Visibility.Visible;
    public string SettingsSaveLabel => LanguageRestartVisibility == Visibility.Visible && !HasActiveImports && !WorkspaceDirty && !PresetDirty ? "Save and restart" : setupMode ? "Save and continue" : "Save settings";
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
            config.ColorPalette = ColorPalette; config.CanvasColor = CanvasColor; config.SurfaceColor = SurfaceColor; config.AccentColor = AccentColor;
            config.Diagnostics = new() { DeveloperMode = DeveloperMode, Verbose = VerboseDiagnostics, TraceTools = TraceTools }; app.Diagnostics.Configure(config.Diagnostics);
            await PersistConfigurationAsync();
        }
        catch { config = previous; app.Diagnostics.Configure(config.Diagnostics); throw; }
        var wasSetup = setupMode; setupMode = false; settingsBaseline = SettingsSnapshot(); NotifySettingsState(); Status = "Settings saved";
        if (wasSetup) CurrentPage = "Projects";
        if (wasSetup || !string.Equals(previous.UnityProjectsRoot, config.UnityProjectsRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(config.UnityProjectsRoot)) await RefreshProjectsCore();
            else { allProjects = []; FilterProjects(); }
        }
    }
}
