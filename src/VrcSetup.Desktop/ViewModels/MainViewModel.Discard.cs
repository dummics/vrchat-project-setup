using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public Task DiscardImportAsync(UiImportRow row) => IndependentAction(async () =>
    {
        var job = ImportQueue.CurrentForProject(app.Imports.Snapshot(), row.Job.ProjectPath);
        if (job is null || job.Id != row.Id || job.DiscardCompleted) return;
        if (job.Source.CreationId is null)
        {
            ApplicationNotice = UiText.Text("This project will be preserved. You can pause its import and continue later.");
            return;
        }
        if (!await ui.ConfirmAsync("Cancel import", UiText.Text("Cancel this import and remove the project created for it, including its files?")
            + "\n" + job.Name, "Cancel and remove project")) return;
        var current = ImportQueue.CurrentForProject(app.Imports.Snapshot(), job.ProjectPath);
        if (IsClosingApp || current is null || current.Id != job.Id || current.DiscardCompleted) return;
        if (!current.DiscardRequested && current.State is "Completed" or "Recovered")
        {
            ApplicationNotice = UiText.Text("The import has finished. The project has been kept.");
            return;
        }
        job = current;
        using var stopWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        stopWait.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await app.Imports.RequestDiscardAsync(job.Id, stopWait.Token);
            await app.Imports.WaitForStoppedAsync(job.Id, stopWait.Token);
            var cleanup = new OwnedProjectDiscard(app.Paths, app.Projects);
            var projectsRoot = ProjectsRoot;
            await app.Imports.FinishDiscardAsync(job.Id,
                (current, token) => Task.Run(() => cleanup.RemoveAsync(current, projectsRoot, token), token), lifetime.Token);
            await RefreshProjectsCore();
            ApplicationNotice = UiText.Text("Import cancelled.");
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            ApplicationNotice = UiText.Text(app.Imports.Snapshot().Any(x => x.Id == job.Id && x.DiscardRequested)
                ? "Stopping is taking longer. Your cancellation request is saved."
                : "The cancellation request was not saved. Try again.");
        }
    });
}
