using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Evidence for reconstructing an import when its queue journal is unavailable.
// A caller must still review and enqueue a fresh plan; this never mutates a project.
public static class OrphanImportRecovery
{
    public static async Task<CreationRequest?> ReadAsync(ProjectWorkspace workspace, string project, CancellationToken token = default)
    {
        project = ProjectRules.NormalizePath(project);
        var file = Path.Combine(project, ".vrcsetup", "state.json");
        var initial = await AtomicFile.ReadJsonAsync<SetupState>(file, token);
        if (initial is not { Completed: false, CreationRequest: { RecoveryPolicy: not null } source }) return null;
        if (!ProjectRules.NormalizePath(source.TargetPath).Equals(project, StringComparison.OrdinalIgnoreCase) ||
            (!source.EmptyProject && source.SourceIdentity is null)) return null;
        using var receipt = await ProjectCreationReceipt.AcquireAsync(workspace, source, token);
        var current = await AtomicFile.ReadJsonAsync<SetupState>(file, token);
        if (current is not { Completed: false, CreationRequest: { } verified } || !source.MatchesInputs(verified) ||
            current.UnityPackagePath != verified.UnityPackagePath) return null;
        return verified;
    }
}
