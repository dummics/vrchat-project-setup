using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public static class ProjectCreationReceipt
{
    public static async Task<IDisposable> AcquireAsync(ProjectWorkspace workspace, CreationRequest request, CancellationToken token = default)
    {
        var applicationLock = await workspace.AcquireAsync(request.TargetPath, token);
        IDisposable? pathLock = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var identity = ProjectDirectoryIdentity.TryRead(request.TargetPath)
                ?? throw new IOException("The project location cannot be verified. Its files have been preserved.");
            pathLock = ProjectDirectoryIdentity.HoldPath(request.TargetPath, identity);
            if (workspace.IsBusy(request.TargetPath)) throw new IOException("Close Unity before continuing this project.");
            var metadataPath = Path.Combine(request.TargetPath, ".vrcsetup");
            var metadataIdentity = ProjectDirectoryIdentity.TryRead(metadataPath)
                ?? throw new IOException("The project creation record cannot be verified. Its files have been preserved.");
            var metadataLock = ProjectDirectoryIdentity.HoldPath(metadataPath, metadataIdentity);
            try
            {
                var file = Path.Combine(metadataPath, "state.json");
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The project creation record is linked. Its files have been preserved.");
                var state = await AtomicFile.ReadJsonAsync<SetupState>(file, token);
                if (state is null || !Matches(request, state, identity))
                    throw new IOException("The project creation record does not match. Its files have been preserved.");
                return new ReceiptLease(applicationLock, pathLock, metadataLock);
            }
            catch { metadataLock.Dispose(); throw; }
        }
        catch { pathLock?.Dispose(); applicationLock.Dispose(); throw; }
    }

    private sealed class ReceiptLease(IDisposable applicationLock, IDisposable pathLock, IDisposable metadataLock) : IDisposable
    {
        public void Dispose()
        {
            try { metadataLock.Dispose(); }
            finally { try { pathLock.Dispose(); } finally { applicationLock.Dispose(); } }
        }
    }

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
