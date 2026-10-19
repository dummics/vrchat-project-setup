using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LanguageRestartVisibility))]
    public partial string SelectedLanguage { get; set; } = "system";
    public DisplayOption[] LanguageOptions => [new("system", UiText.Text("System")), new("it", "Italiano"), new("en", "English")];
    public DisplayOption[] ThemeOptions => [new("System", UiText.Text("System")), new("Dark", UiText.Text("Dark")), new("Light", UiText.Text("Light"))];
    public Visibility LanguageRestartVisibility => UiText.Resolve(SelectedLanguage) == UiText.CurrentLanguage ? Visibility.Collapsed : Visibility.Visible;
    [RelayCommand] private async Task RestartForLanguage() => await Guard(async () =>
    {
        if (IsBusy || HasActiveImports || WorkspaceDirty || PresetDirty) { Status = UiText.Text("Finish current work before updating"); return; }
        await SaveSettingsCoreAsync();
        await ui.RestartApplicationAsync();
    });
}
public sealed record DisplayOption(string Id, string Name);
