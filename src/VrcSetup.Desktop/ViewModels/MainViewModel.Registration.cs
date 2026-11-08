using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private HashSet<string> pendingRegistrationPaths = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> unknownRegistrationPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> registrationRetries = [];

    internal void RefreshRegistrationNotices(ImportJob[] jobs)
    {
        var next = jobs.GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase)
            .Where(group => ImportQueue.CurrentForProject(group, group.Key) is { CreatorCompanionPending: true, Active: false })
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = jobs.GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase)
            .Where(group => ImportQueue.CurrentForProject(group, group.Key) is { CreatorCompanionUnknown: true, Active: false, HiddenFromImports: false })
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pendingRegistrationPaths.SetEquals(next) && unknownRegistrationPaths.SetEquals(unknown)) return;
        pendingRegistrationPaths = next;
        unknownRegistrationPaths = unknown;
        FilterProjects();
    }

    public async Task RetryCreatorCompanionAsync(UiImportRow row)
    {
        if (!registrationRetries.Add(row.Id)) return;
        row.IsRegistering = true;
        try { await Guard(() => RetryCreatorCompanionCoreAsync(row)); }
        finally { registrationRetries.Remove(row.Id); row.IsRegistering = false; }
    }

    private async Task RetryCreatorCompanionCoreAsync(UiImportRow row)
    {
        bool Current() => ImportQueue.CurrentForProject(app.Imports.Snapshot(), row.Path)
            is { CreatorCompanionPending: true, Active: false } job && job.Id == row.Id;
        if (!Current()) { ApplicationNotice = UiText.Text("This import changed. Choose it again to continue."); return; }
        bool registered;
        try
        {
            registered = await app.Imports.RetryRegistrationAsync(row.Id, (job, token) => app.Unity.RegisterAsync(job.ProjectPath,
                new SynchronizationProgress<OperationEvent>(context, value => AppendActivity(value with
                { Phase = "VCC", Severity = value.Severity == EventSeverity.Error ? EventSeverity.Warning : value.Severity })), token), lifetime.Token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            app.Diagnostics.Write("Creator Companion", error.Message, error);
            registered = false;
        }
        if (registered) ShowSavedFeedback("Project added to Creator Companion");
        else if (!Current()) ApplicationNotice = UiText.Text("This import changed. Choose it again to continue.");
        else
        {
            ErrorSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
            ErrorNeedsDiagnostics = false;
            ErrorMessage = UiText.Text("Your project is ready. It was not added to Creator Companion. Retry here, or add it there as an existing project. You do not need to import it again.");
        }
    }
}
