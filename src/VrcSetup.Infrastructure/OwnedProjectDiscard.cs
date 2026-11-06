using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Only for explicitly confirmed removal of the entire newly created project.
// Existing-project imports require their own file/backup rollback implementation.
public sealed class OwnedProjectDiscard(AppPaths paths, ProjectWorkspace workspace)
{
    public async Task RemoveAsync(ImportJob job, string projectsRoot, CancellationToken token = default)
    {
        if (!job.DiscardRequested || job.Active || job.Source.CreationId is null)
            throw new IOException("This project cannot be removed automatically. Its files have been preserved.");
        var target = ProjectRules.NormalizePath(job.ProjectPath);
        var root = ProjectRules.NormalizePath(projectsRoot);
        ProjectRules.EnsureContained(root, target);
        if (target.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot remove the projects folder.");
        using var applicationLock = await workspace.AcquireAsync(target, token);
        var transaction = await ImportDiscardTransaction.ReadAsync(paths, job, token);
        if (transaction?.Phase == "Removed")
        {
            ConfirmAbsent(target);
            return;
        }
        var identity = ProjectDirectoryIdentity.TryRead(target)
            ?? throw new IOException("The project location cannot be verified. Cancellation remains pending.");
        using (ProjectDirectoryIdentity.HoldPath(target, identity))
        {
            if (workspace.IsBusy(target)) throw new IOException("Close Unity before removing this project.");
            if (transaction is null)
            {
                var metadata = Path.Combine(target, ".vrcsetup");
                var metadataIdentity = ProjectDirectoryIdentity.TryRead(metadata)
                    ?? throw new IOException("The project creation record cannot be verified.");
                using var metadataLock = ProjectDirectoryIdentity.HoldPath(metadata, metadataIdentity);
                var stateFile = Path.Combine(metadata, "state.json");
                if ((File.GetAttributes(stateFile) & FileAttributes.ReparsePoint) != 0) throw new IOException("The project creation record is linked.");
                var state = await AtomicFile.ReadJsonAsync<SetupState>(stateFile, token)
                    ?? throw new IOException("The project creation record is missing.");
                transaction = await ImportDiscardTransaction.PrepareAsync(paths, job, state, identity, token);
            }
            if (transaction.DirectoryIdentity != identity) throw new IOException("The project location changed. Its files have been preserved.");
            CheckTree(target, token);
            RemoveContents(target, token);
        }
        // Reopens with DELETE and compares identity, so replacing the target in
        // the handoff cannot cause removal of an unrelated directory.
        ProjectDirectoryIdentity.RemoveEmptyDirectory(target, identity);
        ConfirmAbsent(target);
        await AtomicFile.WriteJsonAsync(ImportDiscardTransaction.FilePath(paths, job.Id), transaction with { Phase = "Removed" }, token);
    }

    private static void CheckTree(string directory, CancellationToken token, int depth = 0)
    {
        if (depth > 128) throw new IOException("The project folder structure is too deep to remove safely.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The project contains linked files or folders. Its files have been preserved.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var identity = ProjectDirectoryIdentity.TryRead(entry) ?? throw new IOException("A project folder changed.");
                using (ProjectDirectoryIdentity.HoldPath(entry, identity)) CheckTree(entry, token, depth + 1);
            }
        }
    }

    private static void RemoveContents(string directory, CancellationToken token, int depth = 0)
    {
        if (depth > 128) throw new IOException("The project folder structure changed. Cancellation remains pending.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            var identity = ProjectDirectoryIdentity.TryRead(entry);
            if (identity is not null)
            {
                using (ProjectDirectoryIdentity.HoldPath(entry, identity)) RemoveContents(entry, token, depth + 1);
                ProjectDirectoryIdentity.RemoveEmptyDirectory(entry, identity);
            }
            else ProjectDirectoryIdentity.RemoveFile(entry, ProjectDirectoryIdentity.TryReadFile(entry)
                ?? throw new IOException("A project entry changed. Cancellation remains pending."));
        }
    }

    private static void ConfirmAbsent(string target)
    {
        try { _ = File.GetAttributes(target); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        throw new IOException("Project removal has not been confirmed. Cancellation remains pending.");
    }
}
