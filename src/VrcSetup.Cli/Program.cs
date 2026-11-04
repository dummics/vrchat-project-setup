using System.Diagnostics;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        try
        {
            var command = new CommandLine(args);
            if (command.Has("help") || command.Positionals.FirstOrDefault() is "help" or "/?") { PrintHelp(); return 0; }
            var app = new VrcSetupApplication(command.Value("home"), new[] { "--developer", "--verbose", "--trace-tools", "--no-catalog-refresh", "--no-vcc-registration" }.Where(command.Has).ToArray());
            if (command.Positionals.Count == 0 && !command.Has("projectpath") || command.Has("wizard"))
            {
                if (Console.IsInputRedirected || Console.IsOutputRedirected) { PrintHelp(); return 0; }
                return await TerminalClient.RunAsync(app, cancellation.Token);
            }
            return await ExecuteAsync(app, command, cancellation.Token);
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 2; }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or JsonException or UnauthorizedAccessException or TimeoutException)
        { Console.Error.WriteLine("Error: " + error.Message); return 1; }
    }

    public static async Task<int> ExecuteAsync(VrcSetupApplication app, CommandLine command, CancellationToken cancellationToken)
    {
        var verb = command.Positionals.FirstOrDefault()?.ToLowerInvariant() ?? "setup";
        if (verb == "config" && command.Positionals.ElementAtOrDefault(1) == "reset")
        {
            if (!command.Has("yes") && !Confirm("Reset your VRCSETUP settings?")) return 2;
            await app.Configuration.ResetAsync(cancellationToken); Console.WriteLine("Settings reset; previous file backed up."); return 0;
        }
        if (verb == "config" && command.Positionals.ElementAtOrDefault(1) == "import")
        { await app.Configuration.ImportAsync(command.Required(2, "Legacy config path"), cancellationToken); Console.WriteLine("Configuration imported; original preserved."); return 0; }
        var config = await app.Configuration.LoadAsync(cancellationToken);
        app.ConfigureDiagnostics(config);
        var progress = new InlineProgress<OperationEvent>(value => Console.Error.WriteLine($"[{value.Phase}] {value.Message}"));
        switch (verb)
        {
            case "imports":
                return await ImportCommands.ExecuteAsync(app, command, config, cancellationToken);
            case "inspect":
            {
                if (command.Value("index-project") is { } project) await app.Inspection.IndexProjectAsync(project, cancellationToken);
                var report = await app.Inspection.InspectAsync(command.Required(1, "UnityPackage path"), command.Has("deep") ? InspectionOptions.Extended : null, cancellationToken);
                if (command.Has("json")) Console.WriteLine(JsonSerializer.Serialize(report, JsonDefaults.Options));
                else
                {
                    Console.WriteLine($"{report.Scope} · {(report.FromCache ? "cached" : "scanned")} · {(report.Observation.ArchiveComplete ? "archive read" : "partial archive")}");
                    PrintData(report.Suggestions.Select(x => new { Package = x.Id, x.Confidence, Requirement = x.VersionConstraint ?? "" }).ToArray(), false);
                    foreach (var limitation in report.Observation.Limitations) Console.Error.WriteLine(limitation);
                }
                return report.Observation.ArchiveComplete ? 0 : 2;
            }
            case "projects":
            {
                var root = command.Value("root") ?? config.UnityProjectsRoot;
                if (string.IsNullOrWhiteSpace(root)) throw new IOException("Projects folder is not configured. Use vrcsetup config set --root <folder>.");
                var projects = await app.Projects.ScanAsync(root, command.Value("sort") ?? config.ProjectLibrarySort, command.Has("refresh"), cancellationToken: cancellationToken);
                PrintData(projects.Select(x => new { x.Name, x.Kind, x.UnityVersion, x.PackageCount, LastUpdatedUtc = x.LastUpdatedUtc, x.Status, x.Path }).ToArray(), command.Has("json"));
                return 0;
            }
            case "packages":
            {
                var action = command.Required(1, "Package action").ToLowerInvariant();
                if (action == "search")
                {
                    var query = string.Join(' ', command.Positionals.Skip(2));
                    if (query.Length == 0) throw new ArgumentException("Package search needs one or more words.");
                    var matches = await app.Catalog.SearchAsync(query, cancellationToken);
                    PrintData(matches.Select(x => new { x.Id, x.DisplayName, x.LatestVersion, x.Description }).ToArray(), command.Has("json"));
                    return matches.Length == 0 ? 2 : 0;
                }
                if (action == "versions")
                {
                    var versions = await app.Catalog.GetVersionsAsync(command.Required(2, "Package ID"), cancellationToken);
                    PrintData(versions, command.Has("json")); return versions.Length == 0 ? 2 : 0;
                }
                var project = ProjectRules.NormalizePath(command.Required(2, "Project path"));
                if (action == "list")
                {
                    var snapshot = await app.Projects.ReadAsync(project, cancellationToken);
                    if (snapshot.Status == "Invalid VPM manifest") throw new IOException("Project VPM manifest is invalid.");
                    PrintData(snapshot.Packages.Select(x => new { Package = x.Key, Version = x.Value }).ToArray(), command.Has("json")); return 0;
                }
                if (action is not ("add" or "remove")) throw new ArgumentException("Use packages list, search, versions, add or remove.");
                var specs = command.Positionals.Skip(3).Concat(command.All("package")).ToArray();
                if (specs.Length == 0) throw new ArgumentException("At least one package is required.");
                var selected = action == "add" ? ParsePackages(specs) : new Dictionary<string, string>();
                var plan = await app.Planner.PlanBulkAsync([project], selected, config, remove: action == "remove" ? specs : [], cancellationToken: cancellationToken);
                return await ApplyOrPreview(app, plan, command, progress, cancellationToken);
            }
            case "presets":
            {
                var action = command.Required(1, "Preset action").ToLowerInvariant();
                if (action == "list") { PrintData(config.PackagePresets.Select(x => new { Name = x.Key, Packages = x.Value }).ToArray(), command.Has("json")); return 0; }
                var name = command.Required(2, "Preset name");
                if (action == "remove")
                {
                    if (!config.PackagePresets.Remove(name)) throw new ArgumentException("Preset not found.");
                    if (!command.DryRun) await app.Configuration.SaveAsync(config, cancellationToken);
                    Console.WriteLine($"Preset removed{(command.DryRun ? " [DRY RUN]" : "")}: {name}"); return 0;
                }
                var project = command.Required(3, "Project path");
                if (action == "save")
                {
                    var snapshot = await app.Projects.ReadAsync(project, cancellationToken);
                    var packages = snapshot.Packages.Keys.Concat(ProjectRules.Foundation(snapshot.Packages, config)).Distinct().Where(x => snapshot.Packages.ContainsKey(x) || snapshot.LockedPackages.ContainsKey(x)).ToDictionary(x => x, x => snapshot.InstalledVersions.GetValueOrDefault(x) ?? snapshot.LockedPackages.GetValueOrDefault(x) ?? snapshot.Packages[x]);
                    config.PackagePresets[name] = packages;
                    if (!command.DryRun) await app.Configuration.SaveAsync(config, cancellationToken);
                    Console.WriteLine($"Preset saved{(command.DryRun ? " [DRY RUN]" : "")}: {name}"); return 0;
                }
                if (action != "apply") throw new ArgumentException("Use presets list, save, remove or apply.");
                var desired = config.PackagePresets.GetValueOrDefault(name) ?? throw new ArgumentException("Preset not found.");
                var plan = await app.Planner.PlanBulkAsync([project], desired, config, command.Has("exact"), preset: name, cancellationToken: cancellationToken);
                return await ApplyOrPreview(app, plan, command, progress, cancellationToken);
            }
            case "create":
            {
                var package = command.Required(1, "UnityPackage path");
                Dictionary<string, string>? choices = null;
                if (command.All("package").Length > 0)
                {
                    var baseSet = command.Value("preset") is { } preset ? config.PackagePresets.GetValueOrDefault(preset) ?? throw new ArgumentException("Preset not found.") : config.VpmPackages;
                    choices = ProjectRules.Merge(baseSet, ParsePackages(command.All("package")));
                }
                var platforms = command.All("publishingplatforms");
                var plan = await app.PlanCreationAsync(package, config, command.Value("name"), command.Value("preset"), choices, command.Value("buildtarget"), platforms.Length > 0 ? platforms : null, command.Value("recipe"), cancellationToken);
                return await ApplyOrPreview(app, plan, command, progress, cancellationToken);
            }
            case "setup":
            {
                var path = ProjectRules.NormalizePath(command.Value("projectpath") ?? command.Required(1, "Project or UnityPackage path"));
                if (path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                    return await ApplyOrPreview(app, await app.PlanCreationAsync(path, config, command.Value("name"), command.Value("preset"), cancellationToken: cancellationToken), command, progress, cancellationToken);
                var preset = command.Value("preset");
                var selected = preset is null ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(preset) ?? throw new ArgumentException("Preset not found.");
                var plan = await app.Planner.PlanBulkAsync([path], selected, config, command.Has("exact"), preset: preset, extraImports: command.Has("extras") ? app.ExtraImports(config) : command.All("extra"), cancellationToken: cancellationToken);
                return await ApplyOrPreview(app, plan, command, progress, cancellationToken);
            }
            case "bulk":
            {
                var action = command.Required(1, "Bulk action");
                if (action != "apply") throw new ArgumentException("Use bulk apply <projects...> -Package <specs...> [-Preset name] [-Exact].");
                var projects = command.Positionals.Skip(2).ToArray();
                var preset = command.Value("preset");
                var selected = preset is null ? ParsePackages(command.All("package")) : config.PackagePresets.GetValueOrDefault(preset) ?? throw new ArgumentException("Preset not found.");
                Dictionary<string, string[]>? targets = null;
                if (command.Value("targetmap") is { } targetFile) targets = await AtomicFile.ReadJsonAsync<Dictionary<string, string[]>>(targetFile, cancellationToken);
                var plan = await app.Planner.PlanBulkAsync(projects, selected, config, command.Has("exact"), packageTargets: targets, preset: preset, extraImports: command.Has("extras") ? app.ExtraImports(config) : command.All("extra"), cancellationToken: cancellationToken);
                return await ApplyOrPreview(app, plan, command, progress, cancellationToken);
            }
            case "config":
            {
                var action = command.Positionals.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "show";
                if (action == "show") { Console.WriteLine(JsonSerializer.Serialize(config, JsonDefaults.Options)); return 0; }
                if (action == "import") { await app.Configuration.ImportAsync(command.Required(2, "Legacy config path"), cancellationToken); Console.WriteLine("Configuration imported; original preserved."); return 0; }
                if (action == "reset")
                {
                    if (!command.Has("yes") && !Confirm("Reset your VRCSETUP settings?")) return 2;
                    await app.Configuration.ResetAsync(cancellationToken); return 0;
                }
                if (action != "set") throw new ArgumentException("Use config show, set, import or reset.");
                if (command.Value("root") is { } root) config.UnityProjectsRoot = ProjectRules.NormalizePath(root);
                if (command.Value("unity") is { } editor) { editor = ProjectRules.NormalizePath(editor); UnityProjectDriver.ValidateEditor(editor); config.UnityEditorPath = editor; }
                if (command.Value("theme") is { } theme) { if (theme is not ("System" or "Light" or "Dark")) throw new ArgumentException("Theme must be System, Light or Dark."); config.Theme = theme; }
                await app.Configuration.SaveAsync(config, cancellationToken); Console.WriteLine("Settings saved."); return 0;
            }
            case "recipes":
            {
                var action = command.Required(1, "Recipe action");
                if (action == "list") { PrintData(config.CreationRecipes.Select(x => new { Name = x.Key, Recipe = x.Value }).ToArray(), command.Has("json")); return 0; }
                var name = command.Required(2, "Recipe name");
                if (action == "remove") { if (!config.CreationRecipes.Remove(name)) throw new ArgumentException("Recipe not found."); }
                else if (action == "save")
                {
                    var preset = command.Value("preset") ?? "";
                    var packages = preset.Length > 0 ? config.PackagePresets.GetValueOrDefault(preset) ?? throw new ArgumentException("Preset not found.") : config.VpmPackages;
                    var buildTarget = command.Value("buildtarget") ?? "StandaloneWindows64";
                    var platforms = command.All("publishingplatforms");
                    if (!ProjectRules.BuildTargets.Contains(buildTarget) || platforms.Any(x => !ProjectRules.BuildTargets.Contains(x))) throw new ArgumentException("Unsupported platform.");
                    config.CreationRecipes[name] = new() { Preset = preset, Packages = new(packages), BuildTarget = buildTarget, PublishingPlatforms = platforms.Length > 0 ? platforms : [buildTarget], ExtraImports = command.All("extra") };
                }
                else throw new ArgumentException("Use recipes list, save or remove.");
                if (!command.DryRun) await app.Configuration.SaveAsync(config, cancellationToken); return 0;
            }
            case "catalog":
                if (command.Positionals.ElementAtOrDefault(1) == "refresh") await app.Catalog.RefreshAsync(await app.Catalog.KnownAsync(config, cancellationToken: cancellationToken), progress, cancellationToken);
                PrintData(await app.Catalog.KnownAsync(config, cancellationToken: cancellationToken), command.Has("json")); return 0;
            case "recovery":
            {
                var action = command.Positionals.ElementAtOrDefault(1) ?? "list";
                if (action == "list")
                {
                    var projects = await app.Projects.ScanAsync(config.UnityProjectsRoot, cancellationToken: cancellationToken);
                    PrintData(projects.Where(x => x.Incomplete).ToArray(), command.Has("json")); return 0;
                }
                var project = ProjectRules.NormalizePath(command.Required(2, "Project path"));
                if (action == "cleanup")
                {
                    var confirmed = command.Has("yes") || Confirm("Delete this incomplete project and all of its contents? " + project);
                    if (!confirmed) return 2;
                    await app.Projects.CleanupAsync(project, config.UnityProjectsRoot, confirmed, cancellationToken); return 0;
                }
                if (action == "close-unity")
                {
                    if (!int.TryParse(command.Value("pid"), out var pid)) throw new ArgumentException("--pid must identify the Unity editor to close.");
                    var confirmed = command.Has("yes") || Confirm("Force close Unity PID " + pid + " for " + project + "? Unsaved work may be lost.");
                    if (!confirmed) return 2;
                    await app.Projects.CloseUnityAsync(project, pid, confirmed, cancellationToken); return 0;
                }
                if (action == "restore")
                {
                    var record = await AtomicFile.ReadJsonAsync<BackupRecord>(command.Value("backup") ?? command.Required(3, "backup.json path"), cancellationToken) ?? throw new IOException("Backup record not found.");
                    if (!command.Has("yes") && !Confirm("Restore this backup over the current project state? " + project)) return 2;
                    using var projectLock = await app.Projects.AcquireAsync(project, cancellationToken);
                    return await app.Projects.RestoreAsync(project, record, progress, cancellationToken) ? 0 : 1;
                }
                throw new ArgumentException("Use recovery list, cleanup or restore.");
            }
            case "apply-plan":
            {
                var plan = await AtomicFile.ReadJsonAsync<OperationPlan>(command.Required(1, "Plan JSON path"), cancellationToken) ?? throw new IOException("Plan file not found.");
                return await ApplyOrPreview(app, plan, command, progress, cancellationToken);
            }
            case "gui":
            {
                var executable = new[] { Path.Combine(app.Paths.PayloadRoot, "app", "desktop", "VrcSetup.Desktop.exe"), Path.Combine(AppContext.BaseDirectory, "VrcSetup.Desktop.exe") }.FirstOrDefault(File.Exists) ?? throw new IOException("Desktop payload not found. Run the Desktop project or a published release.");
                  var start = new ProcessStartInfo(executable) { UseShellExecute = true };
                  foreach (var flag in new[] { "developer", "verbose", "trace-tools", "no-catalog-refresh" }) if (command.Has(flag)) start.ArgumentList.Add("--" + flag);
                  Process.Start(start); return 0;
            }
            case "repair":
            case "install":
            case "uninstall":
                return await LifecycleCommand.RunAsync(app, verb, command, cancellationToken);
            default: throw new ArgumentException($"Unknown command: {verb}. Run vrcsetup help.");
        }
    }
    public static Dictionary<string, string> ParsePackages(IEnumerable<string> specs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs) { var entry = ProjectRules.ParseSpec(spec); result[entry.Id] = entry.Version; } return result;
    }
    public static async Task<int> ApplyOrPreview(VrcSetupApplication app, OperationPlan plan, CommandLine command, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        if (command.Value("plan") is { } file) await AtomicFile.WriteJsonAsync(ProjectRules.NormalizePath(file), plan, cancellationToken);
        if (command.Has("json")) Console.WriteLine(JsonSerializer.Serialize(plan, JsonDefaults.Options));
        else PrintPlan(plan, command.DryRun);
        if (command.DryRun) return 0;
        var report = await app.ApplyAsync(plan, progress, cancellationToken);
        if (command.Has("json")) Console.Error.WriteLine(JsonSerializer.Serialize(report, JsonDefaults.Options));
        else
        {
            foreach (var result in report.Results) Console.WriteLine($"{result.Project}: {result.Status} {result.Message}");
            Console.WriteLine("Report: " + report.ReportFile);
        }
        if (report.Cancelled) return 2;
        return report.Success ? 0 : 1;
    }
    public static void PrintPlan(OperationPlan plan, bool dryRun)
    {
        Console.WriteLine($"Package plan [{(dryRun ? "DRY RUN" : "APPLY")}] {plan.Id}");
        foreach (var project in plan.Projects)
        {
            Console.WriteLine($"Project: {project.Name} | {project.Path}");
            foreach (var kind in new[] { ChangeKind.Add, ChangeKind.Update, ChangeKind.Remove })
                Console.WriteLine($"  {kind}: {string.Join(", ", project.Changes.Where(x => x.Kind == kind).Select(x => x.Id + (x.ResolvedVersion is null ? "" : "@" + x.ResolvedVersion)))}");
            if (project.Creation is { } create) Console.WriteLine($"  Create from: {create.UnityPackagePath} | {create.BuildTarget} | Publish: {string.Join(", ", create.PublishingPlatforms)}");
            foreach (var import in project.ExtraImports.Concat(project.Creation?.ExtraImports ?? [])) Console.WriteLine("  Import: " + import);
            if (project.NeedsEasyLoginRepository) Console.WriteLine("  Ensure Easy Login repository; back up and migrate recognized legacy assets if present.");
        }
    }
    public static void PrintData<T>(T[] data, bool json)
    {
        if (json) Console.WriteLine(JsonSerializer.Serialize(data, JsonDefaults.Options));
        else foreach (var value in data) Console.WriteLine(value is string text ? text : JsonSerializer.Serialize(value, JsonDefaults.Options));
    }
    public static bool Confirm(string message)
    {
        if (Console.IsInputRedirected) throw new IOException("This action requires confirmation. Use --yes explicitly for unattended scripts.");
        Console.Write(message + " [y/N] "); return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes";
    }
    public static void PrintHelp() => Console.WriteLine("""
        VRChat Project Setup — GUI, terminal UI and shared .NET engine

        vrcsetup                         Interactive terminal UI
        vrcsetup gui                     Open the Windows app
        vrcsetup projects [-Refresh] [-Sort recent|name] [-Json]
        vrcsetup packages list <project> [-Json]
        vrcsetup packages search <words> [-Json]
        vrcsetup packages versions <package> [-Json]
        vrcsetup packages add <project> <package[@version]> [...] [-DryRun]
        vrcsetup packages remove <project> <package> [...] [-DryRun]
        vrcsetup presets list [-Json]
        vrcsetup presets save <name> <project> [-DryRun]
        vrcsetup presets remove <name> [-DryRun]
        vrcsetup presets apply <name> <project> [-Exact] [-DryRun]
        vrcsetup create <file.unitypackage> [-Name name] [-Preset name] [-Package specs...]
                         [-BuildTarget target] [-PublishingPlatforms targets...] [-DryRun]
        vrcsetup setup <project-or-package> [-Preset name] [-Exact] [-DryRun]
        vrcsetup inspect <file.unitypackage> [--json] [--deep] [--index-project <project>]
        vrcsetup imports run <files...> [--parallel auto|number] [-Preset name] [-DryRun] [--yes] [-Json]
        vrcsetup imports list|recover [job-id] [--yes] [-Json]
        vrcsetup imports apply-plan <reviewed-plan.json> [--parallel auto|number] [--yes] [-Json]
        vrcsetup bulk apply <projects...> [-Preset name] [-Package specs...] [-Exact]
                         [--target-map file.json] [-DryRun]
        vrcsetup recipes list|save|remove [name] [-Preset name] [-DryRun]
        vrcsetup catalog [refresh] [-Json]
        vrcsetup config show|set|import|reset [--root folder] [--unity Unity.exe]
        vrcsetup recovery list|cleanup|restore [project] [backup.json] [--yes]
        vrcsetup apply-plan <plan.json> [-DryRun]
        vrcsetup install|repair|uninstall [--yes]

        Options accept old PowerShell spelling and --kebab-case aliases.
        --home isolates configuration/cache/logs. --plan exports the reviewed plan.
        --developer --verbose --trace-tools enable local development diagnostics.
        --no-catalog-refresh disables automatic desktop package version lookups.
        Platforms: StandaloneWindows64, Android, iOS.
        """);
}

public sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
