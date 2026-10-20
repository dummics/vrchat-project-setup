namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<bool> ResolvePresetChangesAsync(string? name = null)
    {
        if (!PresetDirty) return true;
        var choice = await ui.ResolveChangesAsync("Unsaved packages", editingNewProject ? "Keep editing" : "Save and leave");
        if (choice is null) return false;
        if (choice == "Save")
        {
            if (editingNewProject) return false;
            if (editingImportPreset)
            {
                StoreImportSelection(name ?? EditingPreset, new(presetWorking));
                presetBaseline = new(presetWorking); RenderPreset();
            }
            else await SaveEditedPresetCoreAsync(name ?? EditingPreset);
        }
        return choice is "Save" or "Discard";
    }
    private async Task<bool> ResolveWorkspaceChangesAsync()
    {
        if (!WorkspaceDirty || WorkspaceApplying) return true;
        var choice = await ui.ResolveChangesAsync("Unsaved changes", "Apply and leave");
        if (choice == "Discard") return true;
        if (choice != "Save") return false;
        try { await ApplyChangesCoreAsync(); }
        catch (Exception error)
        {
            app.Diagnostics.Write("Apply before leaving", error.Message, error);
            ErrorMessage = VrcSetup.Core.UserErrors.Message(error);
            return false;
        }
        return !WorkspaceDirty;
    }
}
