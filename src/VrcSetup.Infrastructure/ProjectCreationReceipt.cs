using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public static class ProjectCreationReceipt
{
    // A match is necessary, not sufficient, for mutation: callers must still hold
    // project ownership, check editor activity and prevent path replacement races.
    public static bool Matches(CreationRequest request, SetupState state, ProjectDirectoryIdentity? actual)
    {
        if (!Guid.TryParseExact(request.CreationId, "N", out _) || state.CreationId != request.CreationId
            || actual is null || state.CreatedDirectoryIdentity != actual || state.CreatedTargetPath is null) return false;
        try
        {
            return ProjectRules.NormalizePath(state.CreatedTargetPath).Equals(ProjectRules.NormalizePath(request.TargetPath), StringComparison.OrdinalIgnoreCase)
                && state.ProjectName == request.Name;
        }
        catch (ArgumentException) { return false; }
    }
}
