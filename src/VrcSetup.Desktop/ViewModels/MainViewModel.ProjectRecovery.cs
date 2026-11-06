using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ImportJob? RecoverableImportForProject(ProjectSnapshot project) =>
        ImportQueue.CurrentForProject(app.Imports.Snapshot(), project.Path) is { Recoverable: true, Active: false } job ? job : null;

    public Task RecoverProjectImportAsync(ProjectSnapshot project, string expectedImportId)
    {
        var job = RecoverableImportForProject(project);
        if (job is not null && job.Id == expectedImportId) return RepairHistoryImportAsync(new(job));
        Status = "This import changed. Choose it again to continue.";
        return Task.CompletedTask;
    }

    private bool CanRecoverCurrentImport(ImportJob job)
    {
        var current = ImportQueue.CurrentForProject(app.Imports.Snapshot(), job.ProjectPath);
        if (!IsClosingApp && current is { Active: false, DiscardRequested: false } && current.Id == job.Id) return true;
        Status = "This import changed. Choose it again to continue.";
        return false;
    }
}
