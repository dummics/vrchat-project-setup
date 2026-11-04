using CommunityToolkit.Mvvm.Input;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand] private async Task ExportPreset() => await Guard(async () =>
    {
        var preset = new PortablePreset(UiText.Text(PresetLabel), new(presetWorking)); preset.Validate();
        var filename = string.Concat(preset.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (await ui.SaveFileAsync(filename, ".json") is not { } path) return;
        await AtomicFile.WriteJsonAsync(path, preset); ShowSavedFeedback("Preset exported");
    });
    [RelayCommand] private async Task ImportPreset() => await Guard(async () =>
    {
        if (await ui.PickFileAsync(".json") is not { } path) return;
        if (new FileInfo(path).Length > PortablePreset.MaxBytes) throw new InvalidDataException("Preset too large.");
        var preset = PortablePreset.Read(await File.ReadAllTextAsync(path, lifetime.Token));
        var name = preset.AvailableName(config.PackagePresets.Keys);
        if (!await ui.ConfirmAsync("Import preset", string.Format(UiText.Text("{0} · {1} packages. This adds a new preset; your existing presets stay unchanged."), name, preset.Packages.Count), "Import")) return;
        if (PresetDirty && !await ui.ConfirmAsync("Unsaved preset", "Discard changes before importing a preset?", "Discard")) return;
        config.PackagePresets[name] = new(preset.Packages);
        try { await PersistConfigurationAsync(); }
        catch { config.PackagePresets.Remove(name); throw; }
        LoadPresetNames(); presetSwitching = true;
        try { EditingPreset = name; LoadEditingPreset(); }
        finally { presetSwitching = false; }
        ShowSavedFeedback("Preset imported");
    });
    [RelayCommand] private void ClearPackageFilters() { FavoritesOnly = false; LibraryFilter = ""; }
}
