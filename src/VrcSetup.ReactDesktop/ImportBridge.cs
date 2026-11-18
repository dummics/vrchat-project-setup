using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal sealed class ImportBridge(Window window, VrcSetupApplication engine, bool isolated, DesktopUpdates updates) : IDisposable
{
    internal const string Page = "https://app.vrcsetup.test/index.html";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Path, PackageIdentity? Identity)> sources = [];
    private readonly HashSet<string> chosenProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource lifetime = new();
    private CoreWebView2? web;
    private bool handlingRequest;
    private OperationPlan? reviewed;
    private string? continuationId;
    private OperationPlan? packageReview;
    private OperationPlan? newProjectReview;
    private AppConfiguration? preferencesBaseline;
    private SourceDraft[] initial = [];
    private readonly string candidateProjects = Path.Combine(engine.Paths.Home, "candidate-projects");
    internal bool IsBusy => handlingRequest;
    internal bool Closing { get; set; }

    internal static bool IsPage(string? uri) => string.Equals(uri, Page, StringComparison.Ordinal);

    internal void Attach(CoreWebView2 core)
    {
        web = core;
        core.WebMessageReceived += Receive;
    }

    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsPage(args.Source) || web is null) return;
        string? id = null;
        var ownsRequest = false;
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            id = root.GetProperty("id").GetString();
            if (handlingRequest || Closing) throw new InvalidOperationException(UiText.Text("Wait for the current operation to finish."));
            handlingRequest = ownsRequest = true;
            var result = await Dispatch(root.GetProperty("method").GetString(), root.GetProperty("data"), args);
            Send(new { id, result });
        }
        catch (Exception error) when (!lifetime.IsCancellationRequested)
        {
            engine.Diagnostics.Write("React desktop", error.Message, error);
            Send(new { id, error = UserErrors.Message(error) });
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        finally { if (ownsRequest) handlingRequest = false; }
    }

    private async Task<object> Dispatch(string? method, JsonElement data, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var token = lifetime.Token;
        switch (method)
        {
            case "state":
                return await State(token);
            case "pick":
                var picker = new OpenFileDialog { Filter = "UnityPackage|*.unitypackage", Multiselect = true };
                return picker.ShowDialog(window) == true ? await Stage(picker.FileNames, token) : [];
            case "drop":
                return await Stage(args.AdditionalObjects.OfType<CoreWebView2File>().Select(x => x.Path), token);
            case "workspace":
                var config = await engine.Configuration.LoadAsync(token);
                config.UnityEditorPath = (await Task.Run(UnityEditorDiscovery.FindEditors, token))
                    .FirstOrDefault(x => x.Version == UnityEditorDiscovery.PinnedVersion).Path ?? "";
                if (config.UnityEditorPath.Length == 0) throw new IOException(UiText.Text("Unity 2022.3.22f1 was not found."));
                if (isolated)
                {
                    Directory.CreateDirectory(candidateProjects);
                    config.UnityProjectsRoot = candidateProjects;
                }
                else if (!Directory.Exists(config.UnityProjectsRoot))
                {
                    var projectRootPicker = new OpenFolderDialog { Title = UiText.Text("Choose the Unity projects folder") };
                    if (projectRootPicker.ShowDialog(window) != true) return await State(token);
                    config.UnityProjectsRoot = ProjectRules.NormalizePath(projectRootPicker.FolderName);
                }
                await engine.Configuration.SaveAsync(config, token);
                return await State(token);
            case "remove":
                sources.Remove(data.GetGuid());
                reviewed = null;
                return true;
            case "review":
                reviewed = null;
                continuationId = null;
                var settings = await engine.Configuration.LoadAsync(token);
                _ = RequireWorkspace(settings);
                var plans = new List<ProjectPlan>();
                foreach (var choice in data.Deserialize<DraftChoice[]>(Json) ?? [])
                {
                    if (!ProjectRules.BuildTargets.Contains(choice.Target)) throw new ArgumentException(UiText.Text("Choose a supported platform."));
                    var source = sources[choice.Id];
                    var plan = await engine.PlanCreationAsync(source.Path, settings, name: choice.Name,
                        preset: choice.Preset, packages: choice.Packages, buildTarget: choice.Target, cancellationToken: token);
                    if (source.Identity is not null && plan.Projects.Any(x => (x.Creation ?? x.RecoverySource)?.SourceIdentity != source.Identity))
                        throw new IOException(UiText.Text("The file changed. Remove it and add it again to check its packages."));
                    plans.AddRange(plan.Projects);
                }
                if (plans.Count == 0 || plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
                    throw new ArgumentException(UiText.Text("Choose a different destination for each project."));
                reviewed = new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
                return SummarizeImportReview(reviewed, continuation: false);
            case "reviewContinuation":
                reviewed = null;
                continuationId = null;
                var job = CurrentJob(data.GetString());
                if (!job.Recoverable || job.HasCompilationBlock)
                    throw new IOException(UiText.Text("This import cannot be resumed."));
                _ = await ProjectPath(job.ProjectPath, token);
                string? replacement = null;
                if (!job.Source.EmptyProject)
                {
                    var archive = job.Source.UnityPackagePath;
                    var changed = File.Exists(archive) && job.Source.SourceIdentity is { } identity &&
                        await PackageSourceIdentity.ReadAsync(archive, token) != identity;
                    if (!File.Exists(archive) || changed)
                    {
                        var sourcePicker = new OpenFileDialog { Title = UiText.Text("Choose the original UnityPackage file"), Filter = "UnityPackage|*.unitypackage" };
                        if (sourcePicker.ShowDialog(window) != true) return false;
                        replacement = sourcePicker.FileName;
                    }
                    if (job.Source.SourceIdentity is null)
                    {
                        if (MessageBox.Show(window, UiText.Text("This import was created before file verification. Continue with the selected file?"),
                                UiText.Text("Confirm UnityPackage"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                            return false;
                        replacement ??= archive;
                    }
                }
                reviewed = await engine.Imports.RecoveryPlanAsync(job.Id, await engine.Configuration.LoadAsync(token), token, replacement);
                continuationId = job.Id;
                return SummarizeImportReview(reviewed, continuation: true);
            case "start":
                if (reviewed is not { } accepted || data.GetString() != accepted.Id) throw new InvalidOperationException(UiText.Text("Review the import first."));
                var expectedId = continuationId;
                await engine.Imports.EnqueueAsync(accepted, parallelism: 0, token: token, expectedImportId: expectedId);
                reviewed = null;
                continuationId = null;
                if (expectedId is null) { sources.Clear(); initial = []; }
                return await State(token);
            case "pause":
                await engine.Imports.PauseAsync(CurrentJob(data.GetString()).Id, token);
                return await State(token);
            case "dismiss":
                await engine.Imports.DismissAsync(CurrentJob(data.GetString()).Id, token);
                return await State(token);
            case "discard":
                await Discard(CurrentJob(data.GetString()), token);
                return await State(token);
            case "clearFinished":
                await engine.Imports.ClearFinishedAsync(token);
                return await State(token);
            case "retrySave":
                if (!await engine.Imports.RetryHistorySaveAsync(CurrentJob(data.GetString()).Id, token))
                    throw new IOException(UiText.Text("Could not save the import status. Check disk space and retry."));
                return await State(token);
            case "retryRegistration":
                if (isolated) throw new IOException(UiText.Text("Project registration requires an installed or portable profile."));
                var registration = CurrentJob(data.GetString());
                _ = await ProjectPath(registration.ProjectPath, token);
                if (!await engine.Imports.RetryRegistrationAsync(registration.Id,
                        (job, registerToken) => engine.Unity.RegisterAsync(job.ProjectPath, null, registerToken), token))
                    throw new IOException(UiText.Text("Could not add the project to Creator Companion. You can add it there as an existing project."));
                return await State(token);
            case "openImportProject":
                var import = CurrentJob(data.GetString());
                if (import.Active || import.DiscardRequested)
                    throw new IOException(UiText.Text("Wait for the import to stop."));
                await OpenProject(import.ProjectPath, token);
                return true;
            case "projects":
                return await Projects(token);
            case "pickExistingProject":
                var configuredRoot = ProjectRoot(await engine.Configuration.LoadAsync(token));
                var folder = new OpenFolderDialog { Title = UiText.Text("Choose a Unity project") };
                if (Directory.Exists(configuredRoot)) folder.InitialDirectory = configuredRoot;
                if (folder.ShowDialog(window) != true) return false;
                var pickedPath = ProjectRules.NormalizePath(folder.FolderName);
                if (!isolated)
                {
                    var parent = Path.GetDirectoryName(pickedPath) ?? throw new IOException(UiText.Text("Choose a project folder."));
                    ProjectRules.EnsureContained(parent, pickedPath);
                    await engine.Projects.ReadAsync(pickedPath, token);
                    chosenProjects.Add(pickedPath);
                }
                packageReview = null;
                return await ReadProjectPackages(pickedPath, token);
            case "reviewNewProject":
                newProjectReview = null;
                var creation = data.Deserialize<NewProjectChoice>(Json) ?? throw new ArgumentException(UiText.Text("Choose a name and platform."));
                var name = creation.Name.Trim();
                ProjectRules.ValidateName(name);
                var newSettings = await engine.Configuration.LoadAsync(token);
                var root = RequireWorkspace(newSettings);
                var destination = Path.Combine(root, name);
                ProjectRules.EnsureContained(root, destination);
                EnsureProjectIdle(destination);
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException(UiText.Text("That name is already in use. Choose another name or open the project from the list."));
                newProjectReview = await engine.PlanEmptyProjectAsync(name, newSettings,
                    creation.Preset, creation.Target, token);
                return new { newProjectReview.Id, destination };
            case "createProject":
                if (newProjectReview is not { } emptyProject || data.GetString() != emptyProject.Id)
                    throw new IOException(UiText.Text("Review the new project first."));
                var emptyPath = await ProjectPath(emptyProject.Projects.Single().Path, token);
                EnsureProjectIdle(emptyPath);
                if (File.Exists(emptyPath) || Directory.Exists(emptyPath))
                    throw new IOException(UiText.Text("That name is already in use. Choose another name or open the project from the list."));
                await engine.Imports.EnqueueAsync(emptyProject, parallelism: 0, token: token);
                newProjectReview = null;
                return await State(token);
            case "presets":
                return await ReadPresets(token);
            case "editPreset":
                return await EditPreset(data.Deserialize<PresetEdit>(Json) ?? throw new ArgumentException(UiText.Text("Choose a preset.")), token);
            case "importPreset":
                return await ImportPreset(token);
            case "exportPreset":
                var export = data.Deserialize<PortablePreset>(Json) ?? throw new ArgumentException(UiText.Text("Choose a preset."));
                export.Validate();
                var fileName = string.Concat(export.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var exportDialog = new SaveFileDialog { FileName = fileName, Filter = "Preset|*.json", DefaultExt = ".json" };
                if (exportDialog.ShowDialog(window) != true) return false;
                await AtomicFile.WriteJsonAsync(exportDialog.FileName, export, token);
                return true;
            case "importPreferences":
                preferencesBaseline = await engine.Configuration.LoadAsync(token);
                return new
                {
                    preferencesBaseline.MaxParallelImports, preferencesBaseline.AutoHideCompletedImports,
                    preferencesBaseline.CheckUpdatesOnStartup, preferencesBaseline.AutomaticUpdates,
                    preferencesBaseline.IncludeBetaUpdates, preferencesBaseline.IncludeAlphaUpdates,
                    appearance = Appearance(preferencesBaseline), palettes = AppearancePalette.Presets,
                    preferenceLanguage = preferencesBaseline.Language,
                    CanEditPaths = !isolated, preferencesBaseline.UnityEditorPath, preferencesBaseline.UnityProjectsRoot,
                    preferencesBaseline.UnityPackagesFolder
                };
            case "pickSettingsPath":
                if (isolated) return false;
                switch (data.GetString())
                {
                    case "editor":
                        var editorPicker = new OpenFileDialog { Title = UiText.Text("Choose Unity.exe"), Filter = "Unity Editor|Unity.exe" };
                        return editorPicker.ShowDialog(window) == true ? ProjectRules.NormalizePath(editorPicker.FileName) : false;
                    case "projects":
                    case "extras":
                        var directoryPicker = new OpenFolderDialog { Title = UiText.Text("Choose a folder") };
                        return directoryPicker.ShowDialog(window) == true ? ProjectRules.NormalizePath(directoryPicker.FolderName) : false;
                    default:
                        throw new ArgumentException(UiText.Text("Unknown path option."));
                }
            case "saveImportPreferences":
                var baseline = preferencesBaseline ?? throw new IOException(UiText.Text("Reopen Settings before saving."));
                var preferences = data.Deserialize<ImportPreferences>(Json) ?? throw new ArgumentException(UiText.Text("Invalid settings."));
                if (preferences.MaxParallelImports is < 0 or > 8) throw new ArgumentException(UiText.Text("Choose a limit between 1 and 8, or select Automatic."));
                var updated = baseline.Copy();
                updated.MaxParallelImports = preferences.MaxParallelImports;
                updated.AutoHideCompletedImports = preferences.AutoHideCompletedImports;
                updated.CheckUpdatesOnStartup = preferences.CheckUpdatesOnStartup;
                updated.AutomaticUpdates = preferences.AutomaticUpdates;
                updated.IncludeBetaUpdates = preferences.IncludeBetaUpdates;
                updated.IncludeAlphaUpdates = preferences.IncludeAlphaUpdates;
                if (preferences.PreferenceLanguage is { } language)
                {
                    if (language is not ("system" or "it" or "en")) throw new ArgumentException(UiText.Text("Choose an available language."));
                    updated.Language = language;
                }
                if (preferences.Appearance is { } appearance && appearance != Appearance(baseline))
                {
                    if (appearance.Theme is not ("Dark" or "Light" or "System") ||
                        appearance.ColorPalette != "Custom" && !AppearancePalette.Presets.ContainsKey(appearance.ColorPalette))
                        throw new ArgumentException(UiText.Text("Choose an available theme and color palette."));
                    updated.Theme = appearance.Theme;
                    updated.ColorPalette = appearance.ColorPalette;
                    updated.CanvasColor = ColorSetting(appearance.CanvasColor);
                    updated.SurfaceColor = ColorSetting(appearance.SurfaceColor);
                    updated.AccentColor = ColorSetting(appearance.AccentColor);
                    updated.StrokeColor = ColorSetting(appearance.StrokeColor);
                }
                if (!isolated)
                {
                    if (preferences.UnityEditorPath != baseline.UnityEditorPath)
                    {
                        updated.UnityEditorPath = NormalizeOptionalPath(preferences.UnityEditorPath);
                        if (updated.UnityEditorPath.Length != 0) UnityProjectDriver.ValidateEditor(updated.UnityEditorPath);
                    }
                    if (preferences.UnityProjectsRoot != baseline.UnityProjectsRoot)
                        updated.UnityProjectsRoot = ValidateOptionalFolder(preferences.UnityProjectsRoot);
                    if (preferences.UnityPackagesFolder != baseline.UnityPackagesFolder)
                        updated.UnityPackagesFolder = ValidateOptionalFolder(preferences.UnityPackagesFolder);
                }
                preferencesBaseline = await engine.Configuration.MergeAsync(baseline, updated, token);
                UiText.Language = preferencesBaseline.Language;
                ConfigureQueue(preferencesBaseline);
                return await State(token);
            case "updateStatus":
                return await updates.StatusAsync(token);
            case "openSupport":
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ApplicationUpdates.BugReportUrl) { UseShellExecute = true })) { }
                return true;
            case "checkUpdate":
                return await updates.CheckAsync(manual: true, token);
            case "prepareUpdate":
                return await updates.PrepareAsync(data.GetString(), token);
            case "applyUpdate":
                if (!await updates.StartReadyAsync(manual: true, token))
                    throw new IOException(UiText.Text("Wait for imports to finish or prepare the update again."));
                _ = window.Dispatcher.BeginInvoke(new Action(window.Close));
                return true;
            case "projectPackages":
                packageReview = null;
                return await ReadProjectPackages(data.GetString(), token);
            case "searchProjectPackages":
                var query = data.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(query)) return Array.Empty<object>();
                return (await engine.Catalog.SearchAsync(query, token)).Take(100)
                    .Select(x => new { x.Id, Name = x.DisplayName }).ToArray();
            case "projectPackageVersions":
                var versions = await engine.Catalog.GetVersionsForPreviewAsync(data.GetString() ?? "", token);
                return new { versions, latest = SemanticVersion.Sort(versions, false).FirstOrDefault() };
            case "reviewProjectPackages":
                packageReview = null;
                var edit = data.Deserialize<ProjectPackageEdit>(Json) ?? throw new ArgumentException(UiText.Text("Choose the packages."));
                var editPath = await ProjectPath(edit.Path, token);
                EnsureProjectIdle(editPath);
                var snapshot = await engine.Projects.ReadAsync(editPath, token);
                if (snapshot.Incomplete) throw new IOException(UiText.Text("Finish the import before changing its packages."));
                if (edit.Fingerprint != snapshot.Fingerprint) throw new IOException(UiText.Text("The project changed. Reopen Packages to refresh the list."));
                var configuration = await engine.Configuration.LoadAsync(token);
                var proposed = await engine.Planner.PlanBulkAsync([editPath], edit.Selected, configuration,
                    remove: edit.Removed, cancellationToken: token);
                if (proposed.Projects.Single().ExpectedFingerprint != edit.Fingerprint)
                    throw new IOException(UiText.Text("The project changed. Reopen Packages to refresh the list."));
                packageReview = proposed;
                return SummarizePackageReview(proposed);
            case "reviewPackageRestore":
                packageReview = null;
                var restorePath = await ProjectPath(data.GetString(), token);
                EnsureProjectIdle(restorePath);
                if (engine.Projects.IsBusy(restorePath)) throw new IOException(UiText.Text("Close Unity before restoring packages."));
                packageReview = await engine.Planner.PlanPackageReinstallAsync(restorePath, token);
                return SummarizePackageReview(packageReview);
            case "applyProjectPackages":
                var approval = data.Deserialize<PackageApproval>(Json);
                if (packageReview is not { } acceptedPackages || approval?.Id != acceptedPackages.Id)
                    throw new IOException(UiText.Text("Review the package changes first."));
                if (PackageDowngrades.RequiresConfirmation(acceptedPackages) && !approval.AcceptRisk)
                    throw new IOException(UiText.Text("Confirm installing older versions."));
                packageReview = null;
                var reviewedProject = acceptedPackages.Projects.Single();
                var reviewedPath = await ProjectPath(reviewedProject.Path, token);
                EnsureProjectIdle(reviewedPath);
                if (reviewedProject.ReinstallLockedPackages && engine.Projects.IsBusy(reviewedPath))
                    throw new IOException(UiText.Text("Close Unity before restoring packages."));
                if ((await engine.Projects.ReadAsync(reviewedPath, token)).Incomplete)
                    throw new IOException(UiText.Text("Finish the import before changing its packages."));
                var outcome = await Task.Run(() => engine.ApplyAsync(acceptedPackages, cancellationToken: token), token);
                var result = outcome.Results.FirstOrDefault();
                if (!outcome.Success) throw new IOException(result?.Message ?? UiText.Text("Could not change the packages. Open the log in Activity."));
                return new { warning = result?.Status == OperationStatus.CompletedWithWarnings ? UserErrors.Message(new IOException(result.Message)) : null };
            case "activity":
                return await Activity(token);
            case "activityDetails":
                var activity = data.Deserialize<ActivitySelection>(Json) ?? throw new IOException(UiText.Text("Choose an activity."));
                return await GetActivityDetails(activity, token);
            case "readOperationLog":
                var report = (await new ActivityHistory(engine.Paths).ReadAsync(token)).FirstOrDefault(x => x.Id == data.GetString());
                var log = report?.Results.Select(x => x.LogFile).FirstOrDefault(File.Exists) ?? report?.ReportFile;
                if (log is null || !File.Exists(log)) throw new IOException(UiText.Text("The log is no longer available."));
                ProjectRules.EnsureContained(engine.Paths.Logs, log);
                if (Path.GetExtension(log).ToLowerInvariant() is not (".log" or ".txt" or ".json" or ".jsonl"))
                    throw new IOException(UiText.Text("Unrecognized log file format."));
                if (File.GetAttributes(log).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException(UiText.Text("The log file is unavailable."));
                return await SharedLog.ReadTailAsync(log, token);
            case "exportDiagnostics":
                var save = new SaveFileDialog { FileName = SupportLogs.SuggestedName, Filter = "ZIP|*.zip", DefaultExt = ".zip" };
                if (save.ShowDialog(window) != true) return false;
                await SupportLogs.ExportAsync(engine.Paths, save.FileName, token);
                return true;
            case "openProject":
                var selected = (await Projects(token)).SingleOrDefault(x => x.Path.Equals(data.GetString(), StringComparison.OrdinalIgnoreCase));
                if (selected is null) throw new IOException(UiText.Text("The project is no longer available. Refresh the list."));
                await OpenProject(selected.Path, token);
                return true;
            case "log":
                return (await engine.Imports.ReadLogTailAsync(data.GetString() ?? "", token: token)).Text;
            default:
                throw new ArgumentException(UiText.Text("Unknown operation."));
        }
    }

    private async Task<object> ReadPresets(CancellationToken token, string? selected = null)
    {
        var config = await engine.Configuration.LoadAsync(token);
        var catalog = await engine.Catalog.KnownAsync(config, cancellationToken: token);
        return new
        {
            config.Revision, selected, defaults = config.VpmPackages, presets = config.PackagePresets,
            favorites = config.FavoritePackages, required = ProjectRules.Foundation(config.VpmPackages, config),
            catalog = catalog.Select(x => new { x.Id, Name = x.DisplayName, Latest = SemanticVersion.Sort(x.Versions, false).FirstOrDefault() })
        };
    }

    private async Task<object> EditPreset(PresetEdit edit, CancellationToken token)
    {
        var config = await engine.Configuration.LoadAsync(token);
        if (edit.Revision != config.Revision) throw new IOException(UiText.Text("The presets changed. Reopen the screen before saving."));
        string? selected = edit.Name;
        switch (edit.Action)
        {
            case "save":
                var preset = new PortablePreset(edit.Name ?? "Predefinito", edit.Packages ?? []);
                preset.Validate();
                if (edit.Name is null) config.VpmPackages = preset.Packages;
                else if (config.PackagePresets.ContainsKey(edit.Name)) config.PackagePresets[edit.Name] = preset.Packages;
                else throw new IOException(UiText.Text("The preset is no longer available."));
                break;
            case "create":
            case "rename":
                var name = edit.NewName?.Trim() ?? "";
                var nameCheck = new PortablePreset(name, new() { [ProjectRules.BaseSdk] = "latest" });
                nameCheck.Validate();
                if (nameCheck.AvailableName(config.PackagePresets.Keys) != name)
                    throw new IOException(UiText.Text("Choose another preset name."));
                var source = edit.Name is null ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(edit.Name)
                    ?? throw new IOException(UiText.Text("The preset is no longer available."));
                config.PackagePresets[name] = new(source, StringComparer.OrdinalIgnoreCase);
                if (edit.Action == "rename")
                {
                    if (edit.Name is null) throw new IOException(UiText.Text("The default preset cannot be renamed."));
                    config.PackagePresets.Remove(edit.Name);
                    foreach (var (recipeName, recipe) in config.CreationRecipes.ToArray())
                        if (recipe.Preset.Equals(edit.Name, StringComparison.OrdinalIgnoreCase))
                            config.CreationRecipes[recipeName] = recipe with { Preset = name };
                }
                selected = name;
                break;
            case "delete":
                if (edit.Name is null || !config.PackagePresets.Remove(edit.Name))
                    throw new IOException(UiText.Text("Choose a saved preset to delete."));
                selected = null;
                break;
            case "favorite":
                var id = edit.Package ?? "";
                ProjectRules.ValidatePackageId(id);
                if (config.FavoritePackages.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0)
                    config.FavoritePackages.Add(id);
                break;
            default:
                throw new ArgumentException(UiText.Text("Unknown preset change."));
        }
        await engine.Configuration.SaveAsync(config, token);
        return await ReadPresets(token, selected);
    }

    private async Task<object> ImportPreset(CancellationToken token)
    {
        var picker = new OpenFileDialog { Filter = "Preset|*.json" };
        if (picker.ShowDialog(window) != true) return false;
        if (new FileInfo(picker.FileName).Length > PortablePreset.MaxBytes) throw new InvalidDataException(UiText.Text("The preset file is too large."));
        var preset = PortablePreset.Read(await File.ReadAllTextAsync(picker.FileName, token));
        var config = await engine.Configuration.LoadAsync(token);
        var name = preset.AvailableName(config.PackagePresets.Keys);
        if (MessageBox.Show(window, string.Format(UiText.Text("Import “{0}” with {1} packages?"), name, preset.Packages.Count), UiText.Text("Import preset"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
        config.PackagePresets[name] = preset.Packages;
        await engine.Configuration.SaveAsync(config, token);
        return await ReadPresets(token, name);
    }

    private static object SummarizeImportReview(OperationPlan plan, bool continuation) => new
    {
        plan.Id, continuation,
        projects = plan.Projects.Select(project => new
        {
            project.Name, project.Path, Target = (project.Creation ?? project.RecoverySource)?.BuildTarget,
            packages = project.Changes.Where(x => x.Kind != ChangeKind.Remove)
                .Select(x => new { x.Id, Name = PackageCatalog.FriendlyName(x.Id), Version = x.ResolvedVersion }),
            removals = project.Changes.Where(x => x.Kind == ChangeKind.Remove)
                .Select(x => PackageCatalog.FriendlyName(x.Id))
        })
    };

    private static object SummarizePackageReview(OperationPlan plan)
    {
        var project = plan.Projects.Single();
        return new
        {
            plan.Id, Restore = project.ReinstallLockedPackages,
            RiskyDowngrade = PackageDowngrades.RequiresConfirmation(plan),
            packages = project.Changes
                .Where(x => project.ReinstallLockedPackages || x.Kind != ChangeKind.Keep)
                .Select(x => new { x.Id, Name = PackageCatalog.FriendlyName(x.Id), Before = x.InstalledVersion,
                    Version = x.ResolvedVersion, Kind = x.Kind.ToString() })
        };
    }

    private string ProjectRoot(AppConfiguration config) => isolated ? candidateProjects : config.UnityProjectsRoot;

    private static string NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (!Path.IsPathFullyQualified(path.Trim())) throw new ArgumentException(UiText.Text("Enter a full path."));
        return ProjectRules.NormalizePath(path);
    }

    private static string ValidateOptionalFolder(string? path)
    {
        var folder = NormalizeOptionalPath(path);
        if (folder.Length != 0 && !Directory.Exists(folder)) throw new IOException(UiText.Text("The selected folder is unavailable."));
        return folder;
    }

    private string RequireWorkspace(AppConfiguration config)
    {
        var root = ProjectRoot(config);
        if (!Directory.Exists(root) || isolated && !string.Equals(config.UnityProjectsRoot, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException(UiText.Text("Set up the projects folder before continuing."));
        return ProjectRules.NormalizePath(root);
    }

    private async Task<string> ProjectPath(string? path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(UiText.Text("Select a project."));
        path = ProjectRules.NormalizePath(path);
        var root = !isolated && chosenProjects.Contains(path)
            ? Path.GetDirectoryName(path) ?? throw new IOException(UiText.Text("Choose a project folder."))
            : ProjectRoot(await engine.Configuration.LoadAsync(token));
        if (!Directory.Exists(root)) throw new IOException(UiText.Text("The projects folder is unavailable."));
        ProjectRules.EnsureContained(root, path);
        return path;
    }

    private void EnsureProjectIdle(string path)
    {
        if (engine.Imports.Snapshot().Any(x => (x.Active || x.DiscardRequested) &&
            x.ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            throw new IOException(UiText.Text("Wait for the import to finish."));
    }

    private async Task<object> ReadProjectPackages(string? path, CancellationToken token)
    {
        var projectPath = await ProjectPath(path, token);
        EnsureProjectIdle(projectPath);
        var project = await engine.Projects.ReadAsync(projectPath, token);
        var config = await engine.Configuration.LoadAsync(token);
        var required = ProjectRules.Foundation(project.Packages, config).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new
        {
            project.Path, project.Name, project.Fingerprint, CanEdit = !project.Incomplete,
            CanRestore = !project.Incomplete && PackagePresence.Selection(project.LockedPackages).Count > 0,
            packages = PackagePresence.Selection(project.LockedPackages).Keys
                .Union(PackagePresence.Selection(project.Packages).Keys, StringComparer.OrdinalIgnoreCase)
                .Union(PackagePresence.Selection(project.InstalledVersions).Keys, StringComparer.OrdinalIgnoreCase)
                .Select(id => new
                {
                    id, Name = project.PackageDisplayNames.GetValueOrDefault(id) ?? PackageCatalog.FriendlyName(id),
                    Installed = project.InstalledVersions.GetValueOrDefault(id),
                    Locked = project.LockedPackages.GetValueOrDefault(id),
                    Required = required.Contains(id)
                }).OrderBy(x => x.Name).ToArray()
        };
    }

    private async Task Discard(ImportJob job, CancellationToken token)
    {
        if (!CanDiscard(job)) throw new IOException(UiText.Text("This import cannot be cancelled."));
        var root = ProjectRoot(await engine.Configuration.LoadAsync(token));
        if (!Directory.Exists(root)) throw new IOException(UiText.Text("The projects folder is unavailable."));
        ProjectRules.EnsureContained(root, job.ProjectPath);
        var noFolder = IsAbsent(job.ProjectPath);
        if (!noFolder && job.Source.CreationId is null)
            throw new IOException(UiText.Text("The project folder cannot be removed automatically."));
        var message = string.Format(UiText.Text(noFolder
            ? "Cancel the import of “{0}”?"
            : "Cancel the import of “{0}” and delete the folder created for it?"), job.Name);
        if (MessageBox.Show(window, message, UiText.Text("Cancel import"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        using var stopWait = CancellationTokenSource.CreateLinkedTokenSource(token);
        stopWait.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await engine.Imports.RequestDiscardAsync(job.Id, stopWait.Token);
            await engine.Imports.WaitForStoppedAsync(job.Id, stopWait.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException(UiText.Text("Stopping is taking longer than expected. Refresh the list and retry."));
        }
        var cleanup = new OwnedProjectDiscard(engine.Paths, engine.Projects);
        await engine.Imports.FinishDiscardAsync(job.Id,
            (current, cancel) => noFolder ? ConfirmAbsent(current.ProjectPath, cancel)
                : Task.Run(() => cleanup.RemoveAsync(current, root, cancel), cancel), token, noProjectFolder: noFolder);
    }

    private static bool CanDiscard(ImportJob job) => !job.HistorySaveFailed && !job.DiscardCompleted
        && (job.Source.CreationId is not null || IsAbsent(job.ProjectPath))
        && (job.Active || job.Recoverable || job.DiscardRequested);

    private static bool IsAbsent(string path)
    {
        if (Path.GetDirectoryName(path) is not { } parent || !Directory.Exists(parent)) return false;
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return Directory.Exists(parent); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static Task ConfirmAbsent(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAbsent(path)) throw new IOException(UiText.Text("Check the project folder before finishing cancellation."));
        return Task.CompletedTask;
    }

    private async Task<ActivityRow[]> Activity(CancellationToken token)
    {
        var jobs = engine.Imports.Snapshot();
        var jobPlans = jobs.Select(x => x.Plan.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reports = await new ActivityHistory(engine.Paths).ReadAsync(token);
        return jobs.Select(x => new ActivityRow(x.Id, x.Name, x.FinishedAt ?? x.StartedAt ?? x.Plan.CreatedUtc,
                x.Source.EmptyProject ? "project" : "import", JobStatus(x)))
            .Concat(reports.Where(x => !jobPlans.Contains(x.Id)).Select(x => new ActivityRow(
                x.Id, string.Join(", ", x.Results.Select(result => result.Project).Distinct()),
                x.CompletedUtc, "operation", x.Success ? x.Results.Any(y => y.Status == OperationStatus.CompletedWithWarnings)
                    ? "warning" : "completed" : x.Cancelled ? "cancelled" : "failed")))
            .OrderByDescending(x => x.Date).ToArray();
    }

    private async Task<object> GetActivityDetails(ActivitySelection selected, CancellationToken token)
    {
        if (selected.Kind is "import" or "project")
        {
            var job = engine.Imports.Snapshot().FirstOrDefault(x => x.Id == selected.Id)
                ?? throw new IOException(UiText.Text("The import is no longer available."));
            var project = job.Plan.Projects.Single();
            return new[] { new ActivityProject(job.Name, job.ProjectPath, JobStatus(job), ActivityPackages(project)) };
        }
        if (selected.Kind != "operation") throw new ArgumentException(UiText.Text("Unknown activity."));
        var report = (await new ActivityHistory(engine.Paths).ReadAsync(token)).FirstOrDefault(x => x.Id == selected.Id)
            ?? throw new IOException(UiText.Text("The report is no longer available."));
        var details = report.Results.Select(result => new ActivityProject(result.Project, result.Path,
            result.Status == OperationStatus.CompletedWithWarnings ? "warning" : result.Status.ToString().ToLowerInvariant(),
            ActivityPackages(report.Plan?.Projects.FirstOrDefault(x => x.Path.Equals(result.Path, StringComparison.OrdinalIgnoreCase)))));
        return details.Concat(report.NotProcessed.Select(path => new ActivityProject(Path.GetFileName(path), path, "skipped", []))).ToArray();
    }

    private static ActivityPackage[] ActivityPackages(ProjectPlan? project) =>
        project?.Changes.Select(item => new ActivityPackage(item.Id, PackageCatalog.FriendlyName(item.Id),
            item.ResolvedVersion ?? item.InstalledVersion, item.Kind.ToString().ToLowerInvariant())).ToArray() ?? [];

    private static string JobStatus(ImportJob job)
    {
        if (job.DiscardCompleted) return "cancelled";
        if (job.UnityCompilationNeedsAttention || job.PackageContentsNeedRestore || job.CreatorCompanionPending || job.CreatorCompanionUnknown)
            return "warning";
        return job.State.ToLowerInvariant();
    }

    private async Task<CandidateProject[]> Projects(CancellationToken token)
    {
        var root = ProjectRoot(await engine.Configuration.LoadAsync(token));
        if (!Directory.Exists(root)) return [];
        var projects = await engine.Projects.ScanAsync(root, cancellationToken: token);
        var jobs = engine.Imports.Snapshot();
        return await Task.Run(() => projects.Select(project =>
        {
            var job = ImportQueue.CurrentForProject(jobs, project.Path);
            return new CandidateProject(project.Name, project.Path, project.UnityVersion, project.LastUpdatedUtc,
                project.CreatedUtc, project.Incomplete, engine.Projects.IsBusy(project.Path), job?.Active == true,
                job is { Recoverable: true, RecoveryNoticeDismissed: false } ? job.Id : null);
        }).ToArray(), token);
    }

    private async Task OpenProject(string path, CancellationToken token)
    {
        path = await ProjectPath(path, token);
        if (!File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt")))
            throw new IOException(UiText.Text("The project is not ready to open in Unity yet."));
        EnsureProjectIdle(path);
        if (engine.Projects.IsBusy(path)) throw new IOException(UiText.Text("The project is already open in Unity."));
        var editor = (await engine.Configuration.LoadAsync(token)).UnityEditorPath;
        UnityProjectDriver.ValidateEditor(editor);
        var start = new System.Diagnostics.ProcessStartInfo(editor) { UseShellExecute = false };
        start.ArgumentList.Add("-projectPath");
        start.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(start);
    }

    internal void ConfigureQueue(AppConfiguration configuration)
        => engine.Imports.ConfigureAutomaticLimit(configuration.MaxParallelImports == 0 ? 8 : Math.Clamp(configuration.MaxParallelImports, 1, 8));

    private object[] VisibleJobs(bool hideCompleted)
    {
        var jobs = engine.Imports.Snapshot();
        return jobs.GroupBy(x => ProjectRules.NormalizePath(x.ProjectPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => ImportQueue.CurrentForProject(group, group.Key)!)
            .Where(x => !x.HiddenFromImports &&
                !(hideCompleted && x.CanHide && x.State is ("Completed" or "Recovered") &&
                    !x.CreatorCompanionPending && !x.CreatorCompanionUnknown && !x.UnityCompilationNeedsAttention && !x.PackageContentsNeedRestore))
            .OrderByDescending(x => x.Plan.CreatedUtc)
            .Select(x => (object)new
            {
                x.Id, x.Name, x.ProjectPath, x.State, x.Progress, x.Active, x.Recoverable, x.HasCompilationBlock,
                x.UnityCompilationNeedsAttention, x.PackageContentsNeedRestore, x.HistorySaveFailed,
                x.CreatorCompanionPending, x.CreatorCompanionUnknown,
                CanRetryRegistration = !isolated && x.CreatorCompanionPending && !x.Active && !x.HistorySaveFailed,
                PackageResolutionFailed = x.State == "Failed" && x.Message.StartsWith("VPM resolve failed.", StringComparison.Ordinal),
                x.RecoveryNoticeDismissed, x.DiscardCompleted, x.DiscardRequested,
                CanDiscard = CanDiscard(x),
                DiscardRemovesFolder = x.Source.CreationId is not null && Directory.Exists(x.ProjectPath),
                CanDismiss = x.CanDismiss && !x.RecoveryNoticeDismissed
                    && (!x.Recoverable || Directory.Exists(x.ProjectPath))
            }).ToArray();
    }

    private ImportJob CurrentJob(string? id)
    {
        var snapshot = engine.Imports.Snapshot();
        var job = snapshot.FirstOrDefault(x => x.Id == id);
        if (job is null || ImportQueue.CurrentForProject(snapshot, job.ProjectPath)?.Id != job.Id)
            throw new IOException(UiText.Text("The import changed. Refresh the list and select it again."));
        return job;
    }

    private async Task<object> State(CancellationToken token)
    {
        var settings = await engine.Configuration.LoadAsync(token);
        return new
        {
            root = Directory.Exists(settings.UnityProjectsRoot) && (!isolated ||
                string.Equals(settings.UnityProjectsRoot, candidateProjects, StringComparison.OrdinalIgnoreCase)) ? settings.UnityProjectsRoot : "",
            packages = settings.VpmPackages,
            presets = settings.PackagePresets,
            appearance = Appearance(settings),
            language = UiText.Resolve(settings.Language),
            version = ApplicationUpdates.CurrentVersion,
            initial,
            waitingForMemory = engine.Imports.EffectiveParallelism == 0,
            jobs = VisibleJobs(settings.AutoHideCompletedImports)
        };
    }

    internal async Task<SourceDraft[]> Stage(IEnumerable<string> paths, CancellationToken token)
    {
        var settings = await engine.Configuration.LoadAsync(token);
        var added = new List<(SourceDraft Draft, string Path, PackageIdentity? Identity)>();
        foreach (var file in paths.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)
                || sources.Values.Any(x => x.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;
            var report = await engine.Inspection.InspectForImportAsync(file, cancellationToken: token);
            var suggestions = report.Suggestions
                .Where(x => x.Confidence is DependencyConfidence.Declared or DependencyConfidence.Detected)
                .Select(x => new PackageChoice(x.Id, PackageCatalog.FriendlyName(x.Id), DependencyAdvisor.SuggestedVersion(x)))
                .Where(x => x.Version is not null).ToArray();
            var id = Guid.NewGuid();
            var assetPaths = report.Observation.ArchiveComplete ? report.Observation.Assets.Select(x => x.Path) : null;
            added.Add((new(id, Path.GetFileName(file), ProjectRules.SuggestName(file, settings),
                ProjectRules.SuggestBuildTarget(file, assetPaths), suggestions), file, report.SourceIdentity));
        }
        foreach (var item in added) sources.Add(item.Draft.Id, (item.Path, item.Identity));
        reviewed = null;
        return added.Select(item => item.Draft).ToArray();
    }

    internal async Task SeedSmokeAsync(string folder)
    {
        var settings = await engine.Configuration.LoadAsync(lifetime.Token);
        settings.Language = "it";
        await engine.Configuration.SaveAsync(settings, lifetime.Token);
        var inputs = new List<string>();
        for (var i = 1; i <= 6; ++i)
        {
            var input = Path.Combine(folder, i == 2 ? "avatarv2.unitypackage" : $"avatarquestv{i}.unitypackage");
            using (var gzip = new System.IO.Compression.GZipStream(File.Create(input), System.IO.Compression.CompressionMode.Compress))
            using (var tar = new System.Formats.Tar.TarWriter(gzip))
            {
                var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/pathname")
                {
                    DataStream = new MemoryStream(i == 2 ? "Assets/Quest/Avatar.prefab"u8.ToArray() : "Assets/Avatar.prefab"u8.ToArray())
                };
                tar.WriteEntry(entry);
            }
            inputs.Add(input);
        }
        initial = await Stage(inputs, lifetime.Token);
        var project = Path.Combine(candidateProjects, "Avatar di prova");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"),
            """
            {
                "dependencies": {
                    "example.fixture": {"version":"1.0.0"},
                    "com.vrchat.base": {"version":"3.10.6"},
                    "com.vrchat.core.vpm-resolver": {"version":"0.1.29"}
                },
                "locked": {"example.fixture":{"version":"1.0.0"}}
            }
            """);
        foreach (var (id, version) in new[] { (ProjectRules.BaseSdk, "3.10.6"), (ProjectRules.Resolver, "0.1.29") })
        {
            var packagePath = Path.Combine(project, "Packages", id);
            Directory.CreateDirectory(packagePath);
            await File.WriteAllTextAsync(Path.Combine(packagePath, "package.json"),
                JsonSerializer.Serialize(new { name = id, version }, Json));
        }
        var source = new CreationRequest(inputs[0], "Avatar di prova", project, "", "Android", ["Android"], []);
        var plan = new OperationPlan("smoke-compilation", DateTime.UtcNow,
            [new() { Path = project, Name = source.Name, DesiredPolicy = new Dictionary<string, string>(), Changes = [], ExpectedFingerprint = "" }]);
        var log = Path.Combine(engine.Paths.Logs, "smoke-import.log");
        Directory.CreateDirectory(engine.Paths.Logs);
        await File.WriteAllTextAsync(log, "Script compilation failed in test fixture.");
        var job = new ImportJob("smoke-compilation", plan, source, "Failed", "Validate", 85, "", log)
        { UnityCompilationBlockedImport = true };
        var completed = source with { TargetPath = Path.Combine(candidateProjects, "Import completato"), Name = "Import completato" };
        var completedPlan = new OperationPlan("smoke-completed", DateTime.UtcNow, [plan.Projects[0] with { Path = completed.TargetPath, Name = completed.Name }]);
        var completedJob = new ImportJob("smoke-completed", completedPlan, completed, "Completed", "Finish", 100, "", log);
        await AtomicFile.WriteJsonAsync(Path.Combine(engine.Paths.Home, "imports.json"), new[] { job, completedJob }, lifetime.Token);
        var operationLog = Path.Combine(engine.Paths.Logs, "smoke-vpm.log");
        await File.WriteAllTextAsync(operationLog, "Fixture package operation completed.");
        var packagePlan = new OperationPlan("smoke-packages", DateTime.UtcNow,
            [plan.Projects[0] with { Name = "VPM di prova", Changes = [new("example.fixture", "1.0.0", "2.0.0", "2.0.0", ChangeKind.Update)] }]);
        await AtomicFile.WriteJsonAsync(Path.Combine(engine.Paths.Logs, "operation-smoke-packages.json"),
            new OperationReport(packagePlan.Id, DateTime.UtcNow,
                [new("VPM di prova", project, OperationStatus.Completed, "Fixture packages updated.", operationLog)], [])
            { Plan = packagePlan }, lifetime.Token);
    }

    internal async Task SeedUnityWorkflowAsync(string folder, string editor)
    {
        UnityProjectDriver.ValidateEditor(editor);
        Directory.CreateDirectory(candidateProjects);
        var configuration = await engine.Configuration.LoadAsync(lifetime.Token);
        configuration.UnityEditorPath = editor;
        configuration.Language = "it";
        configuration.UnityProjectsRoot = candidateProjects;
        configuration.UnityPackagesFolder = null;
        configuration.VpmPackages = new(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectRules.BaseSdk] = "3.10.6",
            [ProjectRules.AvatarSdk] = "3.10.6",
            [ProjectRules.Resolver] = "0.1.29"
        };
        await engine.Configuration.SaveAsync(configuration, lifetime.Token);
        var archive = ReactImportVerification.MakeArchive(folder);
        initial = await Stage([archive], lifetime.Token);
    }

    private void Send(object response)
    {
        if (!lifetime.IsCancellationRequested && web is { } core && IsPage(core.Source))
            core.PostWebMessageAsJson(JsonSerializer.Serialize(response, Json));
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    internal sealed record SourceDraft(Guid Id, string File, string Name, string Target, PackageChoice[] Suggestions);
    internal sealed record PackageChoice(string Id, string Name, string? Version);
    private sealed record DraftChoice(Guid Id, string Name, string Target, string? Preset, Dictionary<string, string> Packages);
    private sealed record CandidateProject(string Name, string Path, string UnityVersion, DateTime LastUpdatedUtc,
        DateTime CreatedUtc, bool Incomplete, bool Open, bool Importing, string? RecoveryId);
    private sealed record ActivityRow(string Id, string Name, DateTime Date, string Kind, string Status);
    private sealed record ActivitySelection(string Id, string Kind);
    private sealed record ActivityProject(string Name, string Path, string Status, ActivityPackage[] Packages);
    private sealed record ActivityPackage(string Id, string Name, string? Version, string Change);
    private static AppearancePreferences Appearance(AppConfiguration config) =>
        new(config.Theme, config.ColorPalette, config.CanvasColor, config.SurfaceColor, config.AccentColor, config.StrokeColor);


    private static string ColorSetting(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))
            throw new ArgumentException(UiText.Text("Enter a valid hexadecimal color."));
        return value.ToUpperInvariant();
    }

    private sealed record AppearancePreferences(string Theme, string ColorPalette, string CanvasColor,
        string SurfaceColor, string AccentColor, string StrokeColor);
    private sealed record ImportPreferences(int MaxParallelImports, bool AutoHideCompletedImports,
        bool CheckUpdatesOnStartup, bool AutomaticUpdates, bool IncludeBetaUpdates, bool IncludeAlphaUpdates,
        string UnityEditorPath, string UnityProjectsRoot, string? UnityPackagesFolder, AppearancePreferences? Appearance,
        string? PreferenceLanguage);
    private sealed record ProjectPackageEdit(string Path, string Fingerprint, Dictionary<string, string> Selected, string[] Removed);
    private sealed record PackageApproval(string Id, bool AcceptRisk);
    private sealed record PresetEdit(string Revision, string Action, string? Name,
        string? NewName, Dictionary<string, string>? Packages, string? Package);
    private sealed record NewProjectChoice(string Name, string Target, string? Preset);
}
