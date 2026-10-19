using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool ProjectImportsActive(IEnumerable<string> projects)
    {
        var targets = projects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!app.Imports.Snapshot().Any(job => job.Active && job.Plan.Projects.Any(project => targets.Contains(project.Path)))) return false;
        ErrorMessage = VrcSetup.Core.UiText.Text("Wait for this project's import to finish before changing its packages.");
        return true;
    }
    private bool RequireImportsIdle()
    {
        if (!HasActiveImports) return true;
        ErrorMessage = UiText.Text("Wait for imports to finish before changing the app or its settings.");
        Status = ErrorMessage; return false;
    }
}
