using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class UnityProjectDriver
{
    // Called only inside the executor's project lock, after explicit plan review.
    public async Task InitializePartialAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken token)
    {
        var initialization = plan.Initialization ?? throw new IOException("Project initialization was not reviewed.");
        var source = initialization.Source;
        if (plan.Creation is not null || !source.MatchesInputs(plan.RecoverySource)
            || !ProjectRules.NormalizePath(plan.Path).Equals(ProjectRules.NormalizePath(source.TargetPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The reviewed initialization does not match the project.");
        var identity = ProjectDirectoryIdentity.TryRead(plan.Path)
            ?? throw new IOException("The project location cannot be verified.");
        if (identity.Key != initialization.ExpectedDirectoryIdentity) throw new IOException("The project location changed after review.");
        using var location = ProjectDirectoryIdentity.HoldPath(plan.Path, identity);
        if (workspace.IsBusy(plan.Path)) throw new IOException("Close Unity before continuing project initialization.");
        var metadata = Path.Combine(plan.Path, ".vrcsetup");
        using var metadataLock = ProjectDirectoryIdentity.HoldPath(metadata, ProjectDirectoryIdentity.TryRead(metadata)
            ?? throw new IOException("The project creation record cannot be verified."));
        var stateFile = Path.Combine(metadata, "state.json");
        if ((File.GetAttributes(stateFile) & FileAttributes.ReparsePoint) != 0) throw new IOException("The project creation record is linked.");
        var state = await AtomicFile.ReadJsonAsync<SetupState>(stateFile, token);
        if (state is null || !ProjectCreationReceipt.Matches(source, state, identity)) throw new IOException("The project creation record does not match.");
        if (ProjectWorkspace.Fingerprint(plan.Path) != plan.ExpectedFingerprint) throw new IOException("The project changed after review.");
        string[] scaffold = ["Packages/manifest.json", "ProjectSettings/ProjectVersion.txt"];
        foreach (var relative in scaffold)
        {
            if (!initialization.ExistingFileFingerprints.TryGetValue(relative, out var expected))
                throw new IOException("The initialization plan is missing a file check.");
            var file = Path.Combine(plan.Path, relative);
            var parent = Path.GetDirectoryName(file)!;
            if (Directory.Exists(parent))
            {
                using var parentLock = ProjectDirectoryIdentity.HoldPath(parent, ProjectDirectoryIdentity.TryRead(parent)
                    ?? throw new IOException("The project folder is linked or changed."));
                if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("The project file is linked.");
                if ((File.Exists(file) ? AtomicFile.Hash(file) : "") != expected) throw new IOException("The project files changed after review.");
            }
            else if (expected.Length > 0) throw new IOException("The project files changed after review.");
        }
        ValidateEditor(source.UnityEditorPath);
        foreach (var buildTarget in source.PublishingPlatforms.Append(source.BuildTarget).Distinct())
            if (!HasBuildSupport(source.UnityEditorPath, buildTarget)) throw new IOException("Unity build support is missing for " + buildTarget);
        var version = Regex.Match(FileVersionInfo.GetVersionInfo(source.UnityEditorPath).ProductVersion ?? "", @"\d+\.\d+\.\d+[abfp]\d+").Value;
        if (version.Length == 0) throw new IOException("The selected Unity version cannot be verified.");
        var manifestFile = Path.Combine(plan.Path, "Packages", "manifest.json");
        if (File.Exists(manifestFile))
        {
            try
            {
                using var existing = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile, token));
                if (!existing.RootElement.TryGetProperty("dependencies", out var existingDependencies) || existingDependencies.ValueKind != JsonValueKind.Object)
                    throw new IOException("The existing Unity manifest is invalid. It has been preserved.");
            }
            catch (JsonException error) { throw new IOException("The existing Unity manifest is invalid. It has been preserved.", error); }
        }
        var versionFile = Path.Combine(plan.Path, "ProjectSettings", "ProjectVersion.txt");
        if (File.Exists(versionFile) && Regex.Match(await File.ReadAllTextAsync(versionFile, token), @"m_EditorVersion:\s*(\S+)").Groups[1].Value != version)
            throw new IOException("Choose the Unity version already recorded in this project. Its files have been preserved.");
        await AddMissingFileAsync("Packages/manifest.json", "{\"dependencies\":{}}");
        await AddMissingFileAsync("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: " + version + "\n");
        var assets = Path.Combine(plan.Path, "Assets"); Directory.CreateDirectory(assets);
        using var assetLock = ProjectDirectoryIdentity.HoldPath(assets, ProjectDirectoryIdentity.TryRead(assets)
            ?? throw new IOException("The Assets folder cannot be verified."));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plan.Path, "Packages", "manifest.json"), token));
        if (!manifest.RootElement.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
            throw new IOException("The existing Unity manifest is invalid. It has been preserved.");

        async Task AddMissingFileAsync(string relative, string contents)
        {
            token.ThrowIfCancellationRequested();
            var file = Path.Combine(plan.Path, relative); var parent = Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(parent);
            using var parentLock = ProjectDirectoryIdentity.HoldPath(parent, ProjectDirectoryIdentity.TryRead(parent)
                ?? throw new IOException("The project folder cannot be verified."));
            if (File.Exists(file))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                    || AtomicFile.Hash(file) != initialization.ExistingFileFingerprints[relative])
                    throw new IOException("The project files changed after review.");
                return;
            }
            if (initialization.ExistingFileFingerprints[relative].Length > 0) throw new IOException("The project files changed after review.");
            var temporary = file + ".initialize-" + Guid.NewGuid().ToString("N");
            try { await File.WriteAllTextAsync(temporary, contents, token); File.Move(temporary, file, overwrite: false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
