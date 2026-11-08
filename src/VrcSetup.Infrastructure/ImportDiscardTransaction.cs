using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Stored outside the project so recovery does not depend on a directory being removed.
public sealed record ImportDiscardTransaction(int Schema, string ImportId, string CreationId,
    string TargetPath, ProjectDirectoryIdentity DirectoryIdentity, string Phase)
{
    public ProjectDirectoryIdentity? ParentIdentity { get; init; }
    public static async Task<ImportDiscardTransaction?> ReadAsync(AppPaths paths, ImportJob job, CancellationToken token = default)
    {
        var file = FilePath(paths, job.Id);
        if (File.Exists(file) && new FileInfo(file).Length > 16384)
            throw new IOException("The saved cancellation is invalid. Its files have been preserved.");
        var saved = await AtomicFile.ReadJsonAsync<ImportDiscardTransaction>(file, token);
        if (saved is not null && (saved.Schema != 1 || saved.ImportId != job.Id || saved.CreationId != job.Source.CreationId
            || !Guid.TryParseExact(saved.CreationId, "N", out _) || saved.DirectoryIdentity is null
            || ((saved.Phase is "RootRemovalRequested" or "Removed") && saved.ParentIdentity is null)
            || string.IsNullOrWhiteSpace(saved.TargetPath)
            || !saved.TargetPath.Equals(ProjectRules.NormalizePath(job.ProjectPath), StringComparison.OrdinalIgnoreCase)
            || saved.Phase is not ("Prepared" or "RootRemovalRequested" or "Removed")))
            throw new IOException("The saved cancellation does not match this project. Its files have been preserved.");
        return saved;
    }

    // Caller must await the actual worker and hold the application/path receipt lease.
    public static async Task<ImportDiscardTransaction> PrepareAsync(AppPaths paths, ImportJob job, SetupState state,
        ProjectDirectoryIdentity actual, CancellationToken token = default)
    {
        if (!job.DiscardRequested || job.Active || !Guid.TryParseExact(job.Id, "N", out _)
            || !ProjectCreationReceipt.Matches(job.Source, state, actual))
            throw new IOException("The project cancellation cannot be verified. Its files have been preserved.");
        var transaction = new ImportDiscardTransaction(1, job.Id, job.Source.CreationId!,
            ProjectRules.NormalizePath(job.ProjectPath), actual, "Prepared");
        var file = FilePath(paths, job.Id);
        var previous = await ReadAsync(paths, job, token);
        if (previous is not null)
        {
            if (previous.Schema != 1 || previous.ImportId != transaction.ImportId || previous.CreationId != transaction.CreationId
                || previous.DirectoryIdentity != actual || !previous.TargetPath.Equals(transaction.TargetPath, StringComparison.OrdinalIgnoreCase)
                || previous.Phase is not ("Prepared" or "RootRemovalRequested" or "Removed"))
                throw new IOException("The saved cancellation does not match this project. Its files have been preserved.");
            return previous;
        }
        await AtomicFile.WriteJsonAsync(file, transaction, token);
        return transaction;
    }

    public static string FilePath(AppPaths paths, string importId)
    {
        if (!Guid.TryParseExact(importId, "N", out _)) throw new ArgumentException("Invalid import identity.", nameof(importId));
        return Path.Combine(paths.Home, "import-cleanup", importId + ".json");
    }
}
