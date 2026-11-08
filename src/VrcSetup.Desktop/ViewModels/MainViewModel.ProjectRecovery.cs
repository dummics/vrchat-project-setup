using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ImportJob? ActiveImportForProject(ProjectSnapshot project) =>
        ImportQueue.CurrentForProject(app.Imports.Snapshot(), project.Path) is { Active: true } job ? job : null;
    public ImportJob? PendingRegistrationForProject(ProjectSnapshot project) =>
        ImportQueue.CurrentForProject(app.Imports.Snapshot(), project.Path) is { CreatorCompanionPending: true, Active: false } job ? job : null;
    public ImportJob? RecoverableImportForProject(ProjectSnapshot project) =>
        ImportQueue.CurrentForProject(app.Imports.Snapshot(), project.Path) is { Recoverable: true, Active: false } job ? job : null;

    public async Task<CreationRequest?> OrphanRecoveryForProjectAsync(ProjectSnapshot project)
    {
        if (IsClosingApp || ImportQueue.CurrentForProject(app.Imports.Snapshot(), project.Path) is not null) return null;
        try { return await OrphanImportRecovery.ReadAsync(app.Projects, project.Path, lifetime.Token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { app.Diagnostics.Write("Import recovery", error.Message, error); return null; }
    }

    public Task ResumeOrphanImportAsync(ProjectSnapshot project, CreationRequest recorded) => Guard(async () =>
    {
        var current = await OrphanRecoveryForProjectAsync(project);
        if (current is null || !recorded.MatchesInputs(current)) throw new IOException("This import changed. Choose it again to continue.");
        var source = current;
        if (!source.EmptyProject && (!File.Exists(source.UnityPackagePath) ||
            await PackageSourceIdentity.ReadAsync(source.UnityPackagePath, lifetime.Token) != source.SourceIdentity))
        {
            if (!await ui.ConfirmAsync("Continue import", "The original package is missing or changed. Select the original UnityPackage to continue.", "Choose file")) return;
            var selected = await ui.PickFileAsync(".unitypackage"); if (selected is null) return;
            if (await PackageSourceIdentity.ReadAsync(selected, lifetime.Token) != source.SourceIdentity)
                throw new IOException("This package differs from the original. Choose the original UnityPackage to continue.");
            source = source with { UnityPackagePath = selected };
        }
        if (!await FindEditorAsync(true)) return;
        source = source with { UnityEditorPath = config.UnityEditorPath };
        var planned = await app.PlanResumeAsync(source, source.RecoveryPolicy!, config.Copy(), null, source.ExtraImports, lifetime.Token, current.UnityPackagePath);
        var plan = new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, [planned]);
        if (!await ui.ReviewAsync(plan)) return;
        await app.Imports.EnqueueAsync(plan, RequestedParallelism, source, lifetime.Token, requireNoProjectHistory: true);
        await NavigateAsync(source.EmptyProject ? "Projects" : "Create");
    });

    public Task RecoverProjectImportAsync(ProjectSnapshot project, string expectedImportId)
    {
        var job = RecoverableImportForProject(project);
        if (job is not null && job.Id == expectedImportId) return RepairHistoryImportAsync(new(job));
        ApplicationNotice = UiText.Text("This import changed. Choose it again to continue.");
        return Task.CompletedTask;
    }

    public Task StartNewImportForUnrecoverableProjectAsync(ProjectSnapshot project) => Guard(async () =>
    {
        if (!project.InitializationPending || IsClosingApp || !Directory.Exists(project.Path)) return;
        if (HasPendingProjectWork(project) || await OrphanRecoveryForProjectAsync(project) is not null)
        { ApplicationNotice = UiText.Text("This import changed. Choose it again to continue."); return; }
        var selected = await ui.PickFileAsync(".unitypackage");
        if (selected is null) return;
        selected = ProjectRules.NormalizePath(selected);
        if (!selected.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) || !File.Exists(selected))
            throw new IOException("Choose a .unitypackage file, not a folder or another file type.");
        if (IsClosingApp || !Directory.Exists(project.Path) || HasPendingProjectWork(project) ||
            await OrphanRecoveryForProjectAsync(project) is not null)
        { ApplicationNotice = UiText.Text("This import changed. Choose it again to continue."); return; }
        if (!await NavigateAsync("Create")) return;
        if (IsClosingApp || !Directory.Exists(project.Path) || HasPendingProjectWork(project) ||
            await OrphanRecoveryForProjectAsync(project) is not null)
        { ApplicationNotice = UiText.Text("This import changed. Choose it again to continue."); return; }
        var name = project.Name + " (new)"; var index = 2;
        while (Directory.Exists(Path.Combine(ProjectsRoot, name)) || File.Exists(Path.Combine(ProjectsRoot, name)))
            name = project.Name + " (new " + index++ + ")";
        var draft = new UiImportDraft(selected, name, ProjectRules.SuggestBuildTarget(selected));
        ImportDrafts.Add(draft); SelectedImportDraft = draft; NotifyDrafts();
    });

    private bool HasPendingProjectWork(ProjectSnapshot project) =>
        ImportQueue.CurrentForProject(app.Imports.Snapshot(), project.Path) is { } current &&
        (current.Active || current.Recoverable || current.DiscardRequested || current.CreatorCompanionPending);

    private bool CanRecoverCurrentImport(ImportJob job)
    {
        var current = ImportQueue.CurrentForProject(app.Imports.Snapshot(), job.ProjectPath);
        if (!IsClosingApp && current is { Active: false, DiscardRequested: false, DiscardCompleted: false } && current.Id == job.Id) return true;
        ApplicationNotice = UiText.Text("This import changed. Choose it again to continue.");
        return false;
    }
}
