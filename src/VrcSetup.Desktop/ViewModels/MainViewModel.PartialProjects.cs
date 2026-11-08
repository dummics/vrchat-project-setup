using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    internal static ProjectSnapshot[] IncludePendingProjectFolders(ProjectSnapshot[] scanned, ImportJob[] journal, string root)
    {
        var projects = scanned.ToList();
        var known = scanned.Select(x => ProjectRules.NormalizePath(x.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in journal.GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase))
        {
            var job = ImportQueue.CurrentForProject(group, group.Key)!;
            if (known.Contains(group.Key) || job.Source.CreationId is null || job.DiscardCompleted
                || !(job.Active || job.Recoverable || job.DiscardRequested) || !Directory.Exists(group.Key)) continue;
            var relative = Path.GetRelativePath(ProjectRules.NormalizePath(root), group.Key);
            if (relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            try { if ((File.GetAttributes(group.Key) & FileAttributes.ReparsePoint) != 0) continue; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            projects.Add(new ProjectSnapshot { Name = job.Name, Path = group.Key, UnityVersion = "—", Status = "Setup unfinished",
                Incomplete = true, InitializationPending = true, IncompleteStep = "create", CreatedUtc = job.Plan.CreatedUtc });
            known.Add(group.Key);
        }
        return projects.ToArray();
    }
}
