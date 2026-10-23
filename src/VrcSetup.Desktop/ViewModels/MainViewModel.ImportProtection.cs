using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool ProjectApplyActive(IEnumerable<string> projects)
    {
        if (!IsApplyingPackages || applyingSession is null) return false;
        var targets = projects.Select(x => ProjectRules.NormalizePath(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!applyingSession.Projects.Any(x => targets.Contains(ProjectRules.NormalizePath(x.Path)))) return false;
        ErrorMessage = UiText.Text("These projects are being updated. You can manage other projects in the meantime.");
        return true;
    }
    private bool ProjectImportsActive(IEnumerable<string> projects)
    {
        var targets = projects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!app.Imports.Snapshot().Any(job => job.Active && job.Plan.Projects.Any(project => targets.Contains(project.Path)))) return false;
        ErrorMessage = VrcSetup.Core.UiText.Text("Wait for this project's import to finish before changing its packages.");
        return true;
    }
    private bool RequireImportsIdle()
    {
        if (IsBusy || IsCreatingProject || IsApplyingPackages || IsInstallingUpdate)
        {
            ErrorMessage = UiText.Text("Wait for the current operation to finish, then try again.");
            Status = ErrorMessage; return false;
        }
        if (!HasActiveImports) return true;
        ErrorMessage = UiText.Text("Wait for imports to finish before changing the app or its settings.");
        Status = ErrorMessage; return false;
    }
}
