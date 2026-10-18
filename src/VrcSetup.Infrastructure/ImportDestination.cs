using System.Text.Json;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public enum ImportDestinationKind { New, Busy, Completed, Recoverable, Partial, Unmanaged, DifferentSource, InvalidState }
public sealed record ImportDestination(ImportDestinationKind Kind, string Message, bool CanImportMissing = false);

public sealed class ImportDestinationInspector(ProjectWorkspace workspace)
{
    public async Task<ImportDestination> InspectAsync(string path, string source, CancellationToken token = default)
    {
        path = ProjectRules.NormalizePath(path); source = ProjectRules.NormalizePath(source);
        if (File.Exists(path)) return new(ImportDestinationKind.Unmanaged, "The destination is a file. Choose another project name.");
        if (!Directory.Exists(path)) return new(ImportDestinationKind.New, "New project");
        if (workspace.IsBusy(path)) return new(ImportDestinationKind.Busy, "This project is currently open or importing in Unity. Wait for it to finish.");
        IDisposable projectLock;
        try { projectLock = await workspace.AcquireAsync(path, token); }
        catch (IOException) { return new(ImportDestinationKind.Busy, "Another operation is preparing this project. Wait for it to finish."); }
        using (projectLock)
        {
            SetupState? state;
            try { state = await AtomicFile.ReadJsonAsync<SetupState>(Path.Combine(path, ".vrcsetup", "state.json"), token); }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            { return new(ImportDestinationKind.InvalidState, "The project state could not be read. Its files have been preserved; inspect the project before continuing."); }
            if (state is null) return new(ImportDestinationKind.Unmanaged, "This folder has no VRCSETUP import state. Open it from Projects or choose another name.");
            if (state.Steps is null || state.SchemaVersion is < 1 or > 2) return new(ImportDestinationKind.InvalidState, "The project import state is invalid or newer than this app. Its files have been preserved.");
            if (state.Completed)
            {
                var sameSource = false;
                try { sameSource = !string.IsNullOrWhiteSpace(state.UnityPackagePath) && ProjectRules.NormalizePath(state.UnityPackagePath).Equals(source, StringComparison.OrdinalIgnoreCase); }
                catch (ArgumentException) { }
                return new(ImportDestinationKind.Completed, "This project was already completed. Existing assets stay unchanged; the same source can add missing assets only.", sameSource);
            }
            if (string.IsNullOrWhiteSpace(state.UnityPackagePath)) return new(ImportDestinationKind.InvalidState, "The unfinished project has no recorded source package. Its files have been preserved.");
            string recorded;
            try { recorded = ProjectRules.NormalizePath(state.UnityPackagePath); }
            catch (ArgumentException) { return new(ImportDestinationKind.InvalidState, "The recorded source path is invalid. Its files have been preserved."); }
            if (!recorded.Equals(source, StringComparison.OrdinalIgnoreCase)) return new(ImportDestinationKind.DifferentSource, "This unfinished project belongs to another source package. Select that file or choose a new project name.");
            if (!Directory.Exists(Path.Combine(path, "Assets")) || !File.Exists(Path.Combine(path, "Packages", "manifest.json")))
                return new(ImportDestinationKind.Partial, "Project creation stopped before Unity initialized it. The partial folder is preserved; review it in Activity before restarting.");
            return new(ImportDestinationKind.Recoverable, "Complete the existing project by adding missing assets from this package. Existing files stay unchanged.");
        }
    }
}
