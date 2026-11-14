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
            var editorWasOpen = false;
            var warnings = new List<string>();
            IDisposable? projectLock = null;
            IAsyncDisposable? editorSession = null;
            IAsyncDisposable? sourceLease = null;
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
                if (project.ReinstallLockedPackages)
                {
                    if (project.Creation is not null || project.RecoverySource is not null || project.Initialization is not null ||
                        project.ExtraImports.Length != 0 || project.LegacyAssetPaths.Length != 0 || project.Changes.Any(x => x.Kind != ChangeKind.Keep))
                        throw new ArgumentException("Package restore must be reviewed separately.");
                    var before = await workspace.ReadAsync(project.Path, cancellationToken);
                    if (before.Fingerprint != project.ExpectedFingerprint) throw new ProjectSkippedException("Project changed after review; skipped. Review again.");
                    var locked = PackagePresence.Selection(before.LockedPackages);
                    if (before.Incomplete || locked.Count == 0 || locked.Count != project.Changes.Length ||
                        project.Changes.Any(x => locked.GetValueOrDefault(x.Id) != x.ResolvedVersion))
                        throw new InvalidOperationException("Restore is available for projects with registered VPM packages and no unfinished import.");
                    editorWasOpen = workspace.IsBusy(project.Path);
                    editorSession = await driver.BeginPackageChangesAsync(project, progress, cancellationToken);
                    phase = "Backup";
                    backup = await workspace.BackupAsync(project, cancellationToken);
                    phase = "Restore packages";
                    Emit(phase, "Restoring VPM packages");
                    mutated = true;
                    var result = await driver.ReinstallPackagesAsync(project.Path, project.Changes.Select(x => x.Id).ToArray(), progress, cancellationToken);
                    if (ProjectRules.ToolFailed(result)) throw new IOException("Could not restore VPM packages. " + result.Output);
                    var after = await workspace.ReadAsync(project.Path, cancellationToken);
                    if (!SameVersions(before.Packages, after.Packages) || !SameVersions(locked, PackagePresence.Selection(after.LockedPackages)) ||
                        locked.Any(x => after.InstalledVersions.GetValueOrDefault(x.Key) != x.Value))
                        throw new IOException("Package restore changed the selected versions or left files missing.");
                    if (editorSession is not null) { await editorSession.DisposeAsync(); editorSession = null; }
                    await workspace.ConfirmBackupAsync(backup);
                    results.Add(new(project.Name, project.Path, OperationStatus.Completed, "VPM packages restored.", BackupPath: backup.Directory));
                    Emit("Completed", "VPM packages restored.");
                    continue;
                }
                sourceLease = await driver.HoldImportSourceAsync(project, cancellationToken);
                // VPM edits are consumed by the running editor. Only operations
                // launching another Unity instance need exclusive editor access.
                if (project.RequiresUnityBatch && workspace.IsBusy(project.Path))
                    throw new ProjectsInUseException([project.Name]);
                if (project.Creation is null)
                {
                    if (project.Initialization is not null)
                    {
                        if (!project.Initialization.Source.MatchesInputs(project.RecoverySource))
                            throw new ArgumentException("Partial initialization has no matching recovery source.");
                        phase = "Creating";
                        await driver.InitializePartialAsync(project, progress, cancellationToken);
                        mutated = true;
                        await workspace.SetStepAsync(project.Path, "created", true, cancellationToken);
                    }
                    var fresh = await workspace.ReadAsync(project.Path, cancellationToken);
                    if (project.Initialization is null && fresh.Fingerprint != project.ExpectedFingerprint) throw new ProjectSkippedException("Project changed after review; skipped. Review again.");
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
                editorWasOpen = workspace.IsBusy(project.Path);
                editorSession = await driver.BeginPackageChangesAsync(project, progress, cancellationToken);
                // New imports are temporary creations. Their owned recovery resumes
                // in place; only transactions on pre-existing projects need rollback.
                if (project.Creation is null && (project.RecoverySource is null || project.RecoverySource.CreationId is null))
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
                var resolve = await driver.ApplyPackagePlanAsync(project, progress, cancellationToken);
                if (ProjectRules.ToolFailed(resolve)) throw new IOException("VPM resolve failed. " + resolve.Output);
                }
                await VerifyPackages(project, progress, cancellationToken, warnings);
                await workspace.SetStepAsync(project.Path, "vpm", true, cancellationToken);
                if (editorSession is not null) { await editorSession.DisposeAsync(); editorSession = null; }

                var compilationNeedsAttention = false;
                var packageContentsNeedRestore = false;
                async Task CompleteUnityStep(Func<Task> action)
                {
                    try { await action(); }
                    catch (UnityCompilationWarningException warning) { compilationNeedsAttention = true; warnings.Add(warning.Message); }
                }
                if (project.RequiresUnityBatch && (project.Creation is not null || project.ExtraImports.Length > 0))
                {
                    var contentsBefore = await driver.CapturePackageContentsAsync(project.Path, cancellationToken);
                    phase = "Import";
                    await CompleteUnityStep(() => driver.ImportAsync(project, progress, cancellationToken));
                    await workspace.SetStepAsync(project.Path, "importMain", true, cancellationToken);
                    await workspace.SetStepAsync(project.Path, "importExtras", true, cancellationToken);
                    phase = "Post-import validation";
                    var repaired = await VerifyPackages(project, progress, cancellationToken, warnings);
                    if (repaired) await CompleteUnityStep(() => driver.SettleAsync(project, progress, cancellationToken));
                    var contentsAfter = await driver.CapturePackageContentsAsync(project.Path, cancellationToken);
                    var changedPackages = contentsBefore.Where(x => contentsAfter.GetValueOrDefault(x.Key) != x.Value).Select(x => x.Key).ToArray();
                    if (changedPackages.Length > 0)
                    {
                        packageContentsNeedRestore = true;
                        warnings.Add("The avatar was imported, but some package files need restoring. Open the project's packages and choose Restore packages.");
                        Emit("Verify", "VPM package contents changed during import: " + string.Join(", ", changedPackages), EventSeverity.Warning);
                    }
                }
                else
                {
                    if (project.RecoverySource is not null && project.RequiresUnityBatch)
                    {
                        phase = "Post-import validation";
                        await CompleteUnityStep(() => driver.SettleAsync(project, progress, cancellationToken));
                    }
                    await workspace.SetStepAsync(project.Path, "importMain", true, cancellationToken);
                    await workspace.SetStepAsync(project.Path, "importExtras", true, cancellationToken);
                }
                await workspace.SetStepAsync(project.Path, "finalize", true, cancellationToken);
                await workspace.SaveProfileAsync(project, cancellationToken);
                await workspace.CompleteAsync(project.Path, cancellationToken);
                if (backup is not null) await workspace.ConfirmBackupAsync(backup);
                if (project.Changes.Any(x => x.Id == "com.poiyomi.pro" && x.Kind is ChangeKind.Add or ChangeKind.Update))
                    Emit("Packages", "Poiyomi Pro installer is ready. Open the project in Unity and complete Poiyomi's Patreon sign-in if requested.");
                var registrationPending = false;
                if (project.Creation is not null || project.RecoverySource is not null)
                {
                    var registered = false;
                    // Creation has committed. Optional discovery in VCC must never roll it back
                    // or present tool stderr as a failed package installation.
                    var registrationProgress = progress is null ? null : new RegistrationProgress(progress);
                    try { registered = await driver.RegisterAsync(project.Path, registrationProgress, cancellationToken); }
                    catch (Exception error)
                    {
                        progress?.Report(new(DateTime.UtcNow, "VCC", "Creator Companion registration did not complete.", EventSeverity.Warning, project.Path, error.Message));
                    }
                    registrationPending = !registered;
                    if (!registered) warnings.Add("Project ready; Creator Companion registration failed. Add the existing project in VCC.");
                }
                var status = warnings.Count > 0 ? OperationStatus.CompletedWithWarnings : OperationStatus.Completed;
                results.Add(new(project.Name, project.Path, status, string.Join(" ", warnings), BackupPath: backup?.Directory) { CreatorCompanionPending = registrationPending, UnityCompilationNeedsAttention = compilationNeedsAttention, PackageContentsNeedRestore = packageContentsNeedRestore });
                Emit("Completed", warnings.Count > 0 ? warnings[0] : "Project saved.", warnings.Count > 0 ? EventSeverity.Warning : EventSeverity.Info);
            }
            catch (ProjectSkippedException error)
            {
                results.Add(new(project.Name, project.Path, OperationStatus.Skipped, error.Message));
                Emit(phase, error.Message, EventSeverity.Warning);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var recovered = !mutated || backup is null || await TryRestore(project, backup, editorWasOpen);
                results.Add(new(project.Name, project.Path, OperationStatus.Cancelled,
                    "Operation interrupted. " + (recovered ? "Project and recovery information preserved." : "Automatic restore incomplete; use recovery."), BackupPath: backup?.Directory));
                Emit("Cancelled", "Operation interrupted; remaining projects were not processed.", EventSeverity.Warning);
                break;
            }
            catch (Exception error)
            {
                var recovered = !mutated || backup is null || await TryRestore(project, backup, editorWasOpen);
                var message = error.Message + (recovered ? "" : " Automatic restore incomplete; use recovery.");
                results.Add(new(project.Name, project.Path, OperationStatus.Failed, message, BackupPath: backup?.Directory) { UnityCompilationBlockedImport = error is UnityCompilationBlockedException });
                Emit(phase, message, EventSeverity.Error);
            }
            finally
            {
                try { if (editorSession is not null) await editorSession.DisposeAsync(); }
                finally { try { if (sourceLease is not null) await sourceLease.DisposeAsync(); } finally { projectLock?.Dispose(); } }
            }
            void Emit(string eventPhase, string message, EventSeverity severity = EventSeverity.Info) => progress?.Report(new(DateTime.UtcNow, eventPhase, message, severity, project.Path));
        }
        return new(plan.Id, DateTime.UtcNow, results.ToArray(), plan.Projects.Select(x => x.Path).Except(results.Select(x => x.Path), StringComparer.OrdinalIgnoreCase).ToArray());

        async Task<bool> TryRestore(ProjectPlan project, BackupRecord record, bool editorWasOpen)
        {
            // A created project's asset import cannot be undone by manifest restore.
            if (project.Creation is not null) return true;
            // Never replace Assets/ProjectSettings while the editor may have
            // unsaved work. Retain the recovery copy and allow a package retry.
            if (editorWasOpen || workspace.IsBusy(project.Path)) return false;
            try
            {
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                return await workspace.RestoreAsync(project.Path, record, progress, recoveryTimeout.Token);
            }
            catch (Exception error) { progress?.Report(new(DateTime.UtcNow, "Recovery", error.Message, EventSeverity.Error, project.Path)); return false; }
        }
    }

    private static bool SameVersions(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        before.Count == after.Count && before.All(x => after.TryGetValue(x.Key, out var version) && version == x.Value);

    private async Task<bool> VerifyPackages(ProjectPlan project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken, List<string>? warnings = null)
    {
        var fresh = await workspace.ReadAsync(project.Path, cancellationToken);
        var mismatched = project.Changes.Where(x => x.Kind != ChangeKind.Remove &&
            (!fresh.InstalledVersions.TryGetValue(x.Id, out var physical) ||
             fresh.LockedPackages.TryGetValue(x.Id, out var locked) && physical != locked)).ToArray();
        if (mismatched.Length > 0)
        {
            // An install of an already locked version can be a no-op (including
            // exit 1). Ask VPM to restore its locked files before trying adds.
            var resolve = await driver.ResolveAsync(project.Path, progress, cancellationToken);
            if (ProjectRules.ToolFailed(resolve)) throw new IOException("Package repair resolve failed. " + resolve.Output);
            fresh = await workspace.ReadAsync(project.Path, cancellationToken);
            var remaining = mismatched.Where(x => !PackagePresence.Matches(x, fresh)).ToArray();
            foreach (var missing in remaining)
            {
                progress?.Report(new(DateTime.UtcNow, "Repair", $"Restoring package {missing.Id}@{missing.ResolvedVersion}", EventSeverity.Warning, project.Path));
                var result = await driver.ChangePackageAsync(project.Path, missing with { Kind = ChangeKind.Update }, progress, cancellationToken);
                if (ProjectRules.ToolFailed(result, missing.Id))
                {
                    progress?.Report(new(DateTime.UtcNow, "Repair", $"Package tool exited with code {result.ExitCode} while restoring {missing.Id}. {result.Output.Trim()}", EventSeverity.Error, project.Path));
                    throw new IOException($"Package repair failed: {missing.Id}");
                }
            }
            if (remaining.Length > 0)
            {
                resolve = await driver.ResolveAsync(project.Path, progress, cancellationToken);
                if (ProjectRules.ToolFailed(resolve)) throw new IOException("Package repair resolve failed. " + resolve.Output);
                fresh = await workspace.ReadAsync(project.Path, cancellationToken);
            }
        }
        // Dependencies may have moved a version explicitly selected by the user.
        // Reapply those choices through VPM after its general resolve, once only.
        // Resolving again here would undo the requested downgrade.
        var restoredPins = false;
        foreach (var pin in project.Changes.Where(x => x.Kind != ChangeKind.Remove && x.RequestedVersion != "latest"))
        {
            if (PackagePresence.Matches(pin, fresh)) continue;
            progress?.Report(new(DateTime.UtcNow, "Packages", $"Applying selected version: {pin.Id}@{pin.ResolvedVersion}", EventSeverity.Info, project.Path));
            var result = await driver.ChangePackageAsync(project.Path, pin with { Kind = ChangeKind.Update }, progress, cancellationToken);
            if (ProjectRules.ToolFailed(result, pin.Id)) throw new IOException($"Package operation failed: {pin.Id}. {result.Output}");
            restoredPins = true;
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
            {
                if (change.RequestedVersion != "latest" && fresh.InstalledVersions.TryGetValue(change.Id, out var installed) && installed != change.ResolvedVersion)
                    throw new IOException($"Package version conflict: {change.Id}@{change.ResolvedVersion}; resolved {installed}.");
                throw new IOException($"Package metadata validation failed: {change.Id}@{change.ResolvedVersion}");
            }
            else if (change.RequestedVersion == "latest" && fresh.InstalledVersions.GetValueOrDefault(change.Id) is { } actual && actual != change.ResolvedVersion)
                progress?.Report(new(DateTime.UtcNow, "Verify", $"VPM selected {change.Id}@{actual} for this project's dependencies.", EventSeverity.Info, project.Path));
        }
        return mismatched.Length > 0 || restoredPins;
    }

    private sealed class RegistrationProgress(IProgress<OperationEvent> target) : IProgress<OperationEvent>
    {
        public void Report(OperationEvent value) => target.Report(value with
        {
            Phase = "VCC",
            Severity = value.Severity == EventSeverity.Error ? EventSeverity.Warning : value.Severity
        });
    }
    private sealed class ProjectSkippedException(string message) : Exception(message);
}
