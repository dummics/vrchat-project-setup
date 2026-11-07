using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public Task DiscardImportAsync(UiImportRow row) => IndependentAction(async () =>
    {
        var job = ImportQueue.CurrentForProject(app.Imports.Snapshot(), row.Job.ProjectPath);
        if (job is null || job.Id != row.Id || job.DiscardCompleted) return;
        var missingDestination = ProjectLocationAbsent(job.ProjectPath);
        if (job.Source.CreationId is null && !missingDestination)
        {
            ApplicationNotice = UiText.Text("This project cannot be removed automatically. Your files were kept. Open the project folder or contact support.");
            return;
        }
        var question = missingDestination ? "Cancel this request? No project folder exists at its recorded location, so no folder will be removed."
            : "Cancel this import and remove the project created for it, including its files?";
        if (!await ui.ConfirmAsync("Cancel import", UiText.Text(question) + "\n" + job.Name,
            missingDestination ? "Cancel request" : "Cancel and remove project")) return;
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
            await app.Imports.FinishDiscardAsync(job.Id, (current, token) => missingDestination
                ? ConfirmNoProjectFolderAsync(current, token)
                : Task.Run(() => cleanup.RemoveAsync(current, projectsRoot, token), token), lifetime.Token, noProjectFolder: missingDestination);
            ApplicationNotice = UiText.Text(missingDestination ? "Request cancelled. No project folder was removed." : "Import cancelled.");
            try { await RefreshProjectsCore(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { app.Diagnostics.Write("Project list refresh", error.Message, error); }
        }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        {
            ApplicationNotice = UiText.Text(app.Imports.Snapshot().Any(x => x.Id == job.Id && x.DiscardRequested)
                ? "Stopping is taking longer. Your cancellation request is saved."
                : "The cancellation request was not saved. Try again.");
        }
    });

    internal static bool ProjectLocationAbsent(string path)
    {
        var parent = Path.GetDirectoryName(ProjectRules.NormalizePath(path));
        if (parent is null || !Directory.Exists(parent))
            throw new IOException("The project location is unavailable. Reconnect its drive before cancelling.");
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    internal static bool CanCancelWithoutFolder(string path)
    {
        try { return ProjectLocationAbsent(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal static Task ConfirmNoProjectFolderAsync(ImportJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ProjectLocationAbsent(job.ProjectPath))
            throw new IOException("A project folder appeared. Review the cancellation again before continuing.");
        return Task.CompletedTask;
    }
}
