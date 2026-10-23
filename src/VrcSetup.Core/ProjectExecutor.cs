namespace VrcSetup.Core;

public sealed class ProjectExecutor(IProjectWorkspace workspace, IProjectDriver driver)
{
    public async Task<OperationReport> ApplyAsync(OperationPlan plan, IProgress<OperationEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        if (plan.Projects.Length == 0) throw new ArgumentException("The operation plan is empty.");
        if (plan.Projects.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Projects.Length)
            throw new ArgumentException("A project can only appear once in an operation plan.");
        var results = new List<ProjectOutcome>();
        foreach (var project in plan.Projects)
        {
            if (cancellationToken.IsCancellationRequested) break;
            BackupRecord? backup = null;
            var phase = "Preflight";
            var mutated = false;
            var warnings = new List<string>();
            IDisposable? projectLock = null;
            Emit(phase, $"Project: {project.Name}");
            try
            {
                projectLock = await workspace.AcquireAsync(project.Path, cancellationToken);
                if (project.Changes.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != project.Changes.Length ||
                    project.DesiredPolicy.Keys.Except(project.Changes.Where(x => x.Kind != ChangeKind.Remove).Select(x => x.Id), StringComparer.OrdinalIgnoreCase).Any())
                    throw new ArgumentException("The reviewed plan must contain exactly one change entry for every desired package.");
                foreach (var change in project.Changes)
                {
                    ProjectRules.ValidatePackageId(change.Id);
                    if (change.Kind != ChangeKind.Remove && (string.IsNullOrWhiteSpace(change.ResolvedVersion) || !project.DesiredPolicy.ContainsKey(change.Id)))
                        throw new ArgumentException("The reviewed package plan is inconsistent: " + change.Id);
                    if (change.Kind == ChangeKind.Remove && project.DesiredPolicy.ContainsKey(change.Id))
                        throw new ArgumentException("Removed package remains in the reviewed policy: " + change.Id);
                }
                foreach (var import in project.ImportFingerprints)
                    if (ProjectRules.FileIdentity(import.Key) != import.Value) throw new ProjectSkippedException("UnityPackage changed after review. Review again before importing.");
                if (project.RequiresUnityBatch && workspace.IsBusy(project.Path)) throw new ProjectSkippedException("Close Unity before importing additional files. No project was changed.");
                if (project.Creation is null)
                {
                    var fresh = await workspace.ReadAsync(project.Path, cancellationToken);
                    if (fresh.Fingerprint != project.ExpectedFingerprint) throw new ProjectSkippedException("Project changed after review; skipped. Review again.");
                    var required = ProjectRules.Foundation(fresh.Packages, new()).Concat(project.RequiredPackages);
                    foreach (var id in required)
                        if (!project.DesiredPolicy.ContainsKey(id) || project.Changes.Any(x => x.Id == id && x.Kind == ChangeKind.Remove))
                            throw new InvalidOperationException("Cannot remove required package: " + id);
                }
                else
                {
                    if (!project.Path.Equals(project.Creation.TargetPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Creation target does not match the reviewed project.");
                    foreach (var id in ProjectRules.Foundation(project.DesiredPolicy, new()).Concat(project.RequiredPackages))
                        if (!project.DesiredPolicy.ContainsKey(id)) throw new InvalidOperationException("Required package missing from creation plan: " + id);
                    if (Directory.Exists(project.Path) || File.Exists(project.Path)) throw new ProjectSkippedException("Destination appeared after review. Choose another name.");
                    phase = "Creating";
                    await driver.CreateAsync(project.Creation, progress, cancellationToken);
                    mutated = true;
                    await workspace.SetStepAsync(project.Path, "created", true, cancellationToken);
                }
                if (!project.HasChanges)
                {
                    await workspace.SaveProfileAsync(project, cancellationToken);
                    results.Add(new(project.Name, project.Path, OperationStatus.Unchanged, "Already up to date."));
                    continue;
                }
                if (project.RecoverySource is null)
                {
                    phase = "Backup";
                    backup = await workspace.BackupAsync(project, cancellationToken);
                    Emit(phase, $"Backup: {backup.Directory}");
                }
                mutated = true;
                if (!(project.RecoverySource is not null && project.PreparationComplete))
                {
                await workspace.SetStepAsync(project.Path, "vpm", false, cancellationToken);
                phase = "Prepare";
                mutated = true;
                await driver.PrepareAsync(project, progress, cancellationToken);
                await workspace.SetStepAsync(project.Path, "nunit", true, cancellationToken);
                phase = "Packages";
                foreach (var change in project.Changes.Where(x => x.Kind != ChangeKind.Keep).OrderBy(x => x.Kind == ChangeKind.Remove ? 0 : 1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (project.RequiresUnityBatch && workspace.IsBusy(project.Path)) throw new IOException("Unity opened the project before the file import. Close Unity and try again.");
                    progress?.Report(new OperationEvent(DateTime.UtcNow, phase, $"{change.Kind}: {change.Id}{(change.ResolvedVersion is null ? "" : " @ " + change.ResolvedVersion)}", Project: project.Path) { Package = change });
                    var result = await driver.ChangePackageAsync(project.Path, change, progress, cancellationToken);
                    if (ProjectRules.ToolFailed(result, change.Kind == ChangeKind.Remove ? null : change.Id)) throw new IOException($"Package operation failed: {change.Id}. {result.Output}");
                }
                var resolve = await driver.ResolveAsync(project.Path, progress, cancellationToken);
                if (ProjectRules.ToolFailed(resolve)) throw new IOException("VPM resolve failed. " + resolve.Output);
                }
                await VerifyPackages(project, progress, cancellationToken, warnings);
                await workspace.SetStepAsync(project.Path, "vpm", true, cancellationToken);

                if (project.RequiresUnityBatch && (project.Creation is not null || project.ExtraImports.Length > 0))
                {
                    phase = "Import";
                    await driver.ImportAsync(project, progress, cancellationToken);
                    await workspace.SetStepAsync(project.Path, "importMain", true, cancellationToken);
                    await workspace.SetStepAsync(project.Path, "importExtras", true, cancellationToken);
                    phase = "Post-import validation";
                    var repaired = await VerifyPackages(project, progress, cancellationToken, warnings);
                    if (repaired) await driver.SettleAsync(project, progress, cancellationToken);
                }
                else
                {
                    if (project.RecoverySource is not null && project.RequiresUnityBatch)
                    {
                        phase = "Post-import validation";
                        await driver.SettleAsync(project, progress, cancellationToken);
                    }
                    await workspace.SetStepAsync(project.Path, "importMain", true, cancellationToken);
                    await workspace.SetStepAsync(project.Path, "importExtras", true, cancellationToken);
                }
                await workspace.SetStepAsync(project.Path, "finalize", true, cancellationToken);
                await workspace.SaveProfileAsync(project, cancellationToken);
                await workspace.CompleteAsync(project.Path, cancellationToken);
                if (project.Creation is not null && !await driver.RegisterAsync(project.Path, progress, cancellationToken))
                    warnings.Add("Project ready; Creator Companion registration failed. Add the existing project in VCC.");
                var status = warnings.Count > 0 ? OperationStatus.CompletedWithWarnings : OperationStatus.Completed;
                results.Add(new(project.Name, project.Path, status, string.Join(" ", warnings), BackupPath: backup?.Directory));
                Emit("Completed", warnings.Count > 0 ? warnings[0] : "Project saved.", warnings.Count > 0 ? EventSeverity.Warning : EventSeverity.Info);
            }
            catch (ProjectSkippedException error)
            {
                results.Add(new(project.Name, project.Path, OperationStatus.Skipped, error.Message));
                Emit(phase, error.Message, EventSeverity.Warning);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var recovered = !mutated || backup is null || await TryRestore(project, backup);
                results.Add(new(project.Name, project.Path, OperationStatus.Cancelled,
                    "Operation interrupted. " + (recovered ? "Project and recovery information preserved." : "Automatic restore incomplete; use recovery."), BackupPath: backup?.Directory));
                Emit("Cancelled", "Operation interrupted; remaining projects were not processed.", EventSeverity.Warning);
                break;
            }
            catch (Exception error)
            {
                var recovered = !mutated || backup is null || await TryRestore(project, backup);
                var message = error.Message + (recovered ? "" : " Automatic restore incomplete; use recovery.");
                results.Add(new(project.Name, project.Path, OperationStatus.Failed, message, BackupPath: backup?.Directory));
                Emit(phase, message, EventSeverity.Error);
            }
            finally { projectLock?.Dispose(); }
            void Emit(string eventPhase, string message, EventSeverity severity = EventSeverity.Info) => progress?.Report(new(DateTime.UtcNow, eventPhase, message, severity, project.Path));
        }
        return new(plan.Id, DateTime.UtcNow, results.ToArray(), plan.Projects.Select(x => x.Path).Except(results.Select(x => x.Path), StringComparer.OrdinalIgnoreCase).ToArray());

        async Task<bool> TryRestore(ProjectPlan project, BackupRecord record)
        {
            // A created project's asset import cannot be undone by manifest restore.
            if (project.Creation is not null) return true;
            try
            {
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                return await workspace.RestoreAsync(project.Path, record, progress, recoveryTimeout.Token);
            }
            catch (Exception error) { progress?.Report(new(DateTime.UtcNow, "Recovery", error.Message, EventSeverity.Error, project.Path)); return false; }
        }
    }

    private async Task<bool> VerifyPackages(ProjectPlan project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken, List<string>? warnings = null)
    {
        var fresh = await workspace.ReadAsync(project.Path, cancellationToken);
        var mismatched = project.Changes.Where(x => x.Kind != ChangeKind.Remove &&
            (!fresh.InstalledVersions.TryGetValue(x.Id, out var physical) ||
             fresh.LockedPackages.TryGetValue(x.Id, out var locked) && physical != locked)).ToArray();
        if (mismatched.Length > 0)
        {
            foreach (var missing in mismatched)
            {
                progress?.Report(new(DateTime.UtcNow, "Repair", $"Restoring package {missing.Id}@{missing.ResolvedVersion}", EventSeverity.Warning, project.Path));
                var result = await driver.ChangePackageAsync(project.Path, missing with { Kind = ChangeKind.Update }, progress, cancellationToken);
                if (ProjectRules.ToolFailed(result, missing.Id)) throw new IOException($"Package repair failed: {missing.Id}");
            }
            var resolve = await driver.ResolveAsync(project.Path, progress, cancellationToken);
            if (ProjectRules.ToolFailed(resolve)) throw new IOException("Package repair resolve failed.");
            fresh = await workspace.ReadAsync(project.Path, cancellationToken);
        }
        foreach (var change in project.Changes)
        {
            if (change.Kind == ChangeKind.Remove)
            {
                if (fresh.Packages.ContainsKey(change.Id)) throw new IOException($"Removed package remains in the manifest: {change.Id}");
                if (fresh.LockedPackages.ContainsKey(change.Id) && fresh.InstalledVersions.ContainsKey(change.Id))
                {
                    var warning = $"Package is still required by another package: {change.Id}";
                    if (warnings is not null && !warnings.Contains(warning)) warnings.Add(warning);
                    progress?.Report(new(DateTime.UtcNow, "Verify", warning, EventSeverity.Warning, project.Path));
                }
            }
            else if (!PackagePresence.Matches(change, fresh) ||
                     (!fresh.Packages.ContainsKey(change.Id) && !fresh.LockedPackages.ContainsKey(change.Id)))
                throw new IOException($"Package metadata validation failed: {change.Id}@{change.ResolvedVersion}");
        }
        return mismatched.Length > 0;
    }

    private sealed class ProjectSkippedException(string message) : Exception(message);
}
