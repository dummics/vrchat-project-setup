using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool editingImportPreset;
    private bool acceptingImportSelection;
    public Visibility UseImportPresetVisibility => editingImportPreset ? Visibility.Visible : Visibility.Collapsed;
    [RelayCommand] private async Task UsePresetForImport() => await Guard(async () =>
    {
        var selection = new Dictionary<string, string>(presetWorking); var preset = EditingPreset;
        var previousPreset = SelectedPreset == DefaultPresetKey ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(SelectedPreset) ?? config.VpmPackages;
        acceptingImportSelection = true;
        try
        {
            if (!await NavigateAsync("Create")) return;
            if (preset != SelectedPreset)
                foreach (var other in ImportDrafts.Where(x => x != SelectedImportDraft)) other.Packages ??= new(previousPreset);
            SelectedPreset = preset; creationOverrides = selection;
            if (SelectedImportDraft is { } draft) draft.Packages = new(selection);
            RefreshCreationPackages();
        }
        finally { acceptingImportSelection = false; }
    });
}
