using System.Diagnostics;
using Spectre.Console;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Cli;

public static class TerminalClient
{
    public static async Task<int> RunAsync(VrcSetupApplication app, CancellationToken cancellationToken)
    {
        var config = await app.Configuration.LoadAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.Clear(); AnsiConsole.Write(new Rule("[bold cyan]VRChat Project Setup[/]").LeftJustified());
            var choice = Select("", ["My projects", "Create project", "Packages and creator presets", "Settings", "Activity and recovery", "Open Windows app", "Exit"]);
            try
            {
                switch (choice)
                {
                    case "My projects": await Projects(app, config, cancellationToken); break;
                    case "Create project": await Create(app, config, cancellationToken); break;
                    case "Packages and creator presets": await Presets(app, config, cancellationToken); break;
                    case "Settings": await Settings(app, config, cancellationToken); break;
                    case "Activity and recovery": await Recovery(app, config, cancellationToken); break;
                    case "Open Windows app": await Program.ExecuteAsync(app, new(["gui"]), cancellationToken); break;
                    default: return 0;
                }
                config = await app.Configuration.LoadAsync(cancellationToken);
            }
            catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or System.Text.Json.JsonException)
            { Notice(error.Message, true); }
        }
        return 2;
    }

    private static async Task Projects(VrcSetupApplication app, AppConfiguration config, CancellationToken token)
    {
        var root = config.UnityProjectsRoot;
        if (string.IsNullOrWhiteSpace(root)) root = Ask("Projects folder");
        var snapshots = await app.Projects.ScanAsync(root, config.ProjectLibrarySort, cancellationToken: token);
        while (true)
        {
            var action = Select("Projects", ["Choose projects", "Open folder directly", "Filter", "Refresh", "Change sort order", "Back"]);
            if (action == "Back") return;
            if (action == "Change sort order") { config.ProjectLibrarySort = Select("Order", ["recent", "name"]); await app.Configuration.SaveAsync(config, token); }
            else if (action == "Refresh") snapshots = await app.Projects.ScanAsync(root, config.ProjectLibrarySort, true, cancellationToken: token);
            else if (action == "Open folder directly") await Edit(app, config, [await app.Projects.ReadAsync(ProjectRules.NormalizePath(Ask("Unity project folder")), token)], token);
            else
            {
                var query = action == "Filter" ? Ask("Project name, type or Unity version", "") : "";
                var filtered = snapshots.Where(x => (x.Name + " " + x.Kind + " " + x.UnityVersion).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (filtered.Length == 0) { Notice("No projects match."); continue; }
                var selected = AnsiConsole.Prompt(new MultiSelectionPrompt<ProjectSnapshot>().Title("[cyan]Choose projects[/]").NotRequired().PageSize(15)
                    .UseConverter(x => Escape($"{x.Name}  | {x.Kind}  | {x.UnityVersion}  | {x.PackageCount} packages"))
                    .InstructionsText("[grey]Space: select · Enter: edit selected · select none to return[/]").AddChoices(filtered));
                if (selected.Count > 0) await Edit(app, config, selected.ToArray(), token);
            }
        }
    }

    private static async Task Edit(VrcSetupApplication app, AppConfiguration config, ProjectSnapshot[] snapshots, CancellationToken token)
    {
        var session = new PackageEditSession(snapshots, config, await app.Catalog.KnownAsync(config, snapshots.Select(x => (IReadOnlyDictionary<string, string>)x.Packages), token));
        while (true)
        {
            AnsiConsole.Clear(); AnsiConsole.Write(new Rule(Escape(snapshots.Length == 1 ? snapshots[0].Name : $"{snapshots.Length} projects")));
            var table = new Table().AddColumn("Package").AddColumn("Installed").AddColumn("Choice").AddColumn("After saving");
            foreach (var row in session.Rows().Where(x => x.Included || x.Action == "Will be removed")) table.AddRow(Escape(row.Name), Escape(row.InstalledVersion), Escape(row.SelectedVersion), Escape(row.Action));
            AnsiConsole.Write(table);
            var action = Select("", ["Change package version", "Add packages", "Remove package", "Apply creator preset", "Copy package selection", "Favorites", "Choose projects for a package", "Additional imports", "Save as preset", "Undo pending changes", "Apply changes", "Back"]);
            if (action == "Back")
            {
                if (!session.HasPendingChanges || AnsiConsole.Confirm("Discard pending changes?", false)) return;
                continue;
            }
            try
            {
                switch (action)
                {
                    case "Change package version":
                    {
                        var id = ChooseRow(session.Rows().Where(x => x.Included).ToArray());
                        if (id is not null) session.SetVersion(id, await Version(app, id, token)); break;
                    }
                    case "Add packages":
                    {
                        var matches = await app.Catalog.SearchAsync(Ask("Search packages"), token);
                        if (matches.Length == 0) { Notice("No packages found."); break; }
                        var selected = AnsiConsole.Prompt(new MultiSelectionPrompt<PackageDefinition>().NotRequired().Title("Add packages").PageSize(15).UseConverter(x => Escape($"{x.DisplayName} ({x.Id})")).AddChoices(matches));
                        foreach (var item in selected) session.SetVersion(item.Id, await Version(app, item.Id, token)); break;
                    }
                    case "Remove package": { var id = ChooseRow(session.Rows().Where(x => x.Included && !x.Required).ToArray()); if (id is not null) session.Toggle(id); break; }
                    case "Apply creator preset":
                    {
                        var name = Select("Preset", config.PackagePresets.Keys.Prepend("My default packages").Append("Back")); if (name == "Back") break;
                        var mode = Select("Apply mode", ["Keep custom packages", "Match exactly", "Back"]); if (mode == "Back") break;
                        session.ApplyPreset(name, name == "My default packages" ? config.VpmPackages : config.PackagePresets[name], mode == "Match exactly"); break;
                    }
                    case "Copy package selection":
                    {
                        var source = await app.Projects.ReadAsync(ProjectRules.NormalizePath(Ask("Reference project folder")), token);
                        var policy = source.Packages;
                        var profile = await AtomicFile.ReadJsonAsync<System.Text.Json.Nodes.JsonObject>(Path.Combine(source.Path, ".vrcsetup", "package-profile.json"), token);
                        if (profile?["Policy"] is { } saved && Select("Source", ["Installed packages", "Saved policy"]) == "Saved policy") policy = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(saved.ToJsonString(), JsonDefaults.Options)!;
                        session.ApplyPreset("Reference: " + source.Name, policy, Select("Apply mode", ["Keep custom packages", "Match exactly"]) == "Match exactly"); break;
                    }
                    case "Favorites":
                        foreach (var id in AnsiConsole.Prompt(new MultiSelectionPrompt<string>().Title("Favorites").NotRequired().AddChoices(config.FavoritePackages))) session.SetVersion(id, "latest"); break;
                    case "Choose projects for a package":
                    {
                        var id = ChooseRow(session.Rows()); if (id is null) break;
                        var targets = AnsiConsole.Prompt(new MultiSelectionPrompt<ProjectSnapshot>().Title("Projects for " + Escape(id)).NotRequired().UseConverter(x => Escape(x.Name)).AddChoices(snapshots));
                        session.SetTargets(id, targets.Select(x => x.Path)); break;
                    }
                    case "Additional imports": session.ExtraImports = app.ExtraImports(config); Notice($"Queued {session.ExtraImports.Length} extra imports."); break;
                    case "Save as preset":
                    {
                        var name = Ask("Preset name"); var baseSet = snapshots[0].Packages;
                        var set = ProjectRules.Merge(baseSet, session.Selected, session.Exact); foreach (var id in session.Removed) set.Remove(id);
                        config.PackagePresets[name] = set; await app.Configuration.SaveAsync(config, token); break;
                    }
                    case "Undo pending changes": session.Reset(); break;
                    case "Apply changes":
                    {
                        var plan = await app.Planner.PlanBulkAsync(snapshots.Select(x => x.Path), session.Selected, config, session.Exact, session.Removed, session.Targets, session.Preset, session.ExtraImports, token);
                        if (await ReviewAndRun(app, plan, token)) return; break;
                    }
                }
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { Notice(error.Message, true); }
        }
    }

    private static async Task Create(VrcSetupApplication app, AppConfiguration config, CancellationToken token)
    {
        var package = ProjectRules.NormalizePath(Ask("UnityPackage file (paste or drag and drop)"));
        var name = Ask("Project name", ProjectRules.SuggestName(package, config));
        var preset = Select("Packages", config.PackagePresets.Keys.Prepend("My default packages"));
        var target = Select("Unity opens on", ProjectRules.BuildTargets, ProjectRules.SuggestBuildTarget(package));
        var platforms = AnsiConsole.Prompt(new MultiSelectionPrompt<string>().Title("Build and publish platforms").AddChoices(ProjectRules.BuildTargets).Select(target));
        var plan = await app.PlanCreationAsync(package, config, name, preset == "My default packages" ? null : preset, buildTarget: target, publishingPlatforms: platforms.ToArray(), cancellationToken: token);
        await ReviewAndRun(app, plan, token);
    }
    private static async Task<bool> ReviewAndRun(VrcSetupApplication app, OperationPlan plan, CancellationToken token)
    {
        Program.PrintPlan(plan, false);
        if (!AnsiConsole.Confirm("Apply these changes?", false)) return false;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var lines = new Queue<string>();
        var progress = new InlineProgress<OperationEvent>(value =>
        {
            var color = value.Severity == EventSeverity.Error ? "red" : value.Severity == EventSeverity.Warning ? "yellow" : "grey";
            AnsiConsole.MarkupLine($"[{color}]{Escape("[" + value.Phase + "] " + value.Message)}[/]");
            lines.Enqueue(value.Message); if (lines.Count > 100) lines.Dequeue();
        });
        AnsiConsole.MarkupLine("[grey]Ctrl+C cancels the operation and preserves recovery information.[/]");
        var report = await app.ApplyAsync(plan, progress, cancel.Token);
        var table = new Table().AddColumn("Project").AddColumn("Status").AddColumn("Result");
        foreach (var result in report.Results) table.AddRow(Escape(result.Project), Escape(result.Status.ToString()), Escape(result.Message));
        AnsiConsole.Write(table); Notice("Report: " + report.ReportFile);
        return report.Success;
    }

    private static async Task Presets(VrcSetupApplication app, AppConfiguration config, CancellationToken token)
    {
        while (true)
        {
            var choice = Select("Packages and creator presets", ["My default packages", "Create preset", "Edit preset", "Remove preset", "Favorite packages", "Saved package registry", "Refresh registry", "Creation recipes", "Back"]);
            if (choice == "Back") return;
            if (choice == "Saved package registry") { foreach (var item in await app.Catalog.KnownAsync(config, cancellationToken: token)) AnsiConsole.MarkupLine(Escape($"{item.DisplayName} | {item.Id} | {item.SelectedVersion}")); Notice("Saved package registry."); continue; }
            if (choice == "Refresh registry") { await app.Catalog.RefreshAsync(await app.Catalog.KnownAsync(config, cancellationToken: token), new InlineProgress<OperationEvent>(x => Console.WriteLine(x.Message)), token); continue; }
            if (choice == "Creation recipes")
            {
                var action = Select("Recipes", ["Create recipe", "Remove recipe", "Back"]); if (action == "Back") continue;
                if (action == "Create recipe")
                {
                    var name = Ask("Recipe name"); var preset = Select("Preset", config.PackagePresets.Keys.Prepend("My default packages"));
                    config.CreationRecipes[name] = new() { Preset = preset == "My default packages" ? "" : preset, Packages = new(preset == "My default packages" ? config.VpmPackages : config.PackagePresets[preset]) };
                }
                else if (config.CreationRecipes.Count > 0) config.CreationRecipes.Remove(Select("Recipe", config.CreationRecipes.Keys));
            }
            else if (choice == "Favorite packages")
            {
                var action = Select("Favorites", ["Add favorite", "Remove favorite", "Back"]);
                if (action == "Back") continue;
                if (action == "Add favorite") { var id = Ask("Package ID"); ProjectRules.ValidatePackageId(id); if (!config.FavoritePackages.Contains(id)) config.FavoritePackages.Add(id); }
                else if (config.FavoritePackages.Count > 0) config.FavoritePackages.Remove(Select("Favorite", config.FavoritePackages));
            }
            else
            {
                var name = choice == "My default packages" ? "My default packages" : choice == "Create preset" ? Ask("Preset name") : config.PackagePresets.Count > 0 ? Select("Preset", config.PackagePresets.Keys) : "";
                if (name.Length == 0) { Notice("No presets saved."); continue; }
                if (choice == "Remove preset") { if (AnsiConsole.Confirm("Remove this preset?", false)) config.PackagePresets.Remove(name); }
                else
                {
                    var working = new Dictionary<string, string>(name == "My default packages" ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(name) ?? config.VpmPackages);
                    while (true)
                    {
                        var action = Select("Edit " + Escape(name), ["Add/change package", "Remove package", "Save", "Discard"]);
                        if (action == "Discard") break;
                        if (action == "Save") { if (name == "My default packages") config.VpmPackages = working; else config.PackagePresets[name] = working; break; }
                        if (action == "Add/change package") { var id = Ask("Package ID"); working[id] = await Version(app, id, token); }
                        else if (working.Count > 0)
                        { var id = Select("Package", working.Keys); if (ProjectRules.Foundation(working, config).Contains(id)) Notice("Required package stays included."); else working.Remove(id); }
                    }
                }
            }
            await app.Configuration.SaveAsync(config, token);
        }
    }

    private static async Task Settings(VrcSetupApplication app, AppConfiguration config, CancellationToken token)
    {
        while (true)
        {
            var action = Select("Settings", ["Unity Editor", "Projects folder", "Extra UnityPackages folder", "Naming rules", "Reset configuration", "Back"]);
            if (action == "Back") return;
            if (action == "Unity Editor") { var editor = ProjectRules.NormalizePath(Ask("Unity.exe path", config.UnityEditorPath)); UnityProjectDriver.ValidateEditor(editor); config.UnityEditorPath = editor; }
            if (action == "Projects folder") config.UnityProjectsRoot = ProjectRules.NormalizePath(Ask("Projects folder", config.UnityProjectsRoot));
            if (action == "Extra UnityPackages folder") config.UnityPackagesFolder = Ask("Extra imports folder (blank disables)", config.UnityPackagesFolder ?? "") is { Length: > 0 } extras ? ProjectRules.NormalizePath(extras) : null;
            if (action == "Naming rules")
            {
                config.Naming.DefaultPrefix = Ask("Default prefix", config.Naming.DefaultPrefix); config.Naming.DefaultSuffix = Ask("Default suffix", config.Naming.DefaultSuffix);
                var rule = Ask("Add regex cleanup rule (blank skips)", ""); if (rule.Length > 0) { _ = new System.Text.RegularExpressions.Regex(rule); config.Naming.RegexRemovePatterns.Add(rule); }
                config.Naming.RememberUnityPackageNames = AnsiConsole.Confirm("Remember UnityPackage names?", config.Naming.RememberUnityPackageNames);
            }
            if (action == "Reset configuration") { await Program.ExecuteAsync(app, new(["config", "reset"]), token); return; }
            await app.Configuration.SaveAsync(config, token);
        }
    }
    private static async Task Recovery(VrcSetupApplication app, AppConfiguration config, CancellationToken token)
    {
        var action = Select("Activity and recovery", ["Open logs folder", "List incomplete projects", "Restore backup", "Back"]);
        if (action == "Back") return;
        if (action == "Open logs folder") { Directory.CreateDirectory(app.Paths.Logs); Process.Start(new ProcessStartInfo(app.Paths.Logs) { UseShellExecute = true }); return; }
        if (action == "Restore backup") { await Program.ExecuteAsync(app, new(["recovery", "restore", Ask("Project folder"), Ask("backup.json file")]), token); return; }
        var projects = (await app.Projects.ScanAsync(config.UnityProjectsRoot, cancellationToken: token)).Where(x => x.Incomplete).ToArray();
        if (projects.Length == 0) { Notice("No incomplete projects."); return; }
        var project = AnsiConsole.Prompt(new SelectionPrompt<ProjectSnapshot>().Title("Incomplete projects").UseConverter(x => Escape(x.Name)).AddChoices(projects));
        var next = Select("Recovery", ["Inspect packages / retry", "Delete incomplete project", "Back"]);
        if (next == "Inspect packages / retry") await Edit(app, config, [project], token);
        if (next == "Delete incomplete project") await Program.ExecuteAsync(app, new(["recovery", "cleanup", project.Path]), token);
    }
    private static string? ChooseRow(PackageWorkspaceRow[] rows) => rows.Length == 0 ? null : AnsiConsole.Prompt(new SelectionPrompt<PackageWorkspaceRow>().Title("Package").PageSize(15).UseConverter(x => Escape($"{x.Name} | {x.SelectedVersion}")).AddChoices(rows)).Id;
    private static async Task<string> Version(VrcSetupApplication app, string id, CancellationToken token)
    {
        ProjectRules.ValidatePackageId(id);
        var versions = await app.Catalog.GetVersionsAsync(id, token);
        var filter = Ask("Version prefix (blank shows all)", "");
        var prerelease = AnsiConsole.Confirm("Include prerelease versions?", false);
        var choice = Select("Version", new[] { "latest", "Enter exact version" }.Concat(SemanticVersion.Sort(versions, prerelease).Where(x => x.StartsWith(filter, StringComparison.OrdinalIgnoreCase))));
        return choice == "Enter exact version" ? Ask("Exact version") : choice;
    }
    private static string Select(string title, IEnumerable<string> choices, string? initial = null)
    {
        var items = choices.ToArray(); if (items.Length == 0) throw new InvalidOperationException("No choices available.");
        if (initial is not null) items = items.OrderBy(x => x == initial ? 0 : 1).ToArray();
        return AnsiConsole.Prompt(new SelectionPrompt<string>().Title(title).PageSize(15).UseConverter(Escape).AddChoices(items));
    }
    private static string Ask(string prompt, string? initial = null) => AnsiConsole.Prompt(new TextPrompt<string>(Escape(prompt)).AllowEmpty().DefaultValue(initial ?? ""));
    private static string Escape(string value) => Markup.Escape(value);
    private static void Notice(string message, bool error = false) { AnsiConsole.MarkupLine($"[{(error ? "red" : "grey")}]{Escape(message)}[/]"); AnsiConsole.Prompt(new TextPrompt<string>("Press Enter to continue").AllowEmpty()); }
}
