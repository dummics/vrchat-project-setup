using Spectre.Console;
using Spectre.Console.Rendering;
using VrcSetup.Core;

namespace VrcSetup.Cli;

// Legacy presentation and navigation, backed exclusively by the shared application services.
public static class LegacyTerminal
{
    public static string[] HomeChoices => ["Import avatar or asset", "My projects", "Packages and creator presets", "Settings", "Activity and recovery", "Open Windows app", "Exit"];
    public static IRenderable Menu(string title, string[] items, int selected, int width = 90, int height = 30)
    {
        var content = new List<IRenderable> { new Rule("[#6BD5FF]" + Markup.Escape(title.Length == 0 ? "Choose" : title) + "[/]").LeftJustified().RuleStyle("#4C6A7A") };
        var pageSize = Math.Max(3, (height - 5) / 3); var start = selected / pageSize * pageSize;
        for (var index = start; index < Math.Min(items.Length, start + pageSize); index++)
        {
            var item = items[index]; var primary = item.StartsWith("Import") || item.StartsWith("Create") || item.StartsWith("Save") || item.StartsWith("Apply changes");
            var utility = item is "Back" or "Exit" || item.StartsWith("Delete") || item.StartsWith("Remove");
            var color = item.Contains("preset", StringComparison.OrdinalIgnoreCase) ? "#C4A5FF" : item == "Settings" ? "#F2A0A8" : primary ? "#67E8A5" : utility ? "#A8B6C1" : "#75D7F7";
            var label = (index == selected ? "> " : "  ") + Markup.Escape(primary ? item.ToUpperInvariant() : item);
            var row = new Markup($"[{(index == selected ? "bold black on " + color : color)}]{label}[/]");
            if (utility) content.Add(row);
            else content.Add(new Panel(row) { Border = primary ? BoxBorder.Double : BoxBorder.Rounded, BorderStyle = new Style(Hex(index == selected ? color : "#4C6A7A")), Padding = new Padding(1, 0), Width = Math.Max(20, Math.Min(primary ? 88 : 72, width - 4)) });
        }
        content.Add(new Markup("[grey]↑↓ Navigate · Enter Select · Esc Back[/]"));
        return new Rows(content);
    }
    public static string Choose(string title, string[] items, int initial = 0)
    {
        var index = Math.Clamp(initial, 0, items.Length - 1); var done = false; var cancelled = false;
        AnsiConsole.Live(Menu(title, items, index, AnsiConsole.Profile.Width, AnsiConsole.Profile.Height)).AutoClear(true).Start(live =>
        {
            while (!done)
            {
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow: index = Math.Max(0, index - 1); break;
                    case ConsoleKey.DownArrow: index = Math.Min(items.Length - 1, index + 1); break;
                    case ConsoleKey.Home: index = 0; break;
                    case ConsoleKey.End: index = items.Length - 1; break;
                    case ConsoleKey.Enter: done = true; break;
                    case ConsoleKey.Escape: cancelled = done = true; break;
                    default:
                        if (!char.IsControl(key.KeyChar)) for (var step = 1; step <= items.Length; step++) { var next = (index + step) % items.Length; if (items[next].StartsWith(key.KeyChar.ToString(), StringComparison.OrdinalIgnoreCase)) { index = next; break; } }
                        break;
                }
                live.UpdateTarget(Menu(title, items, index, AnsiConsole.Profile.Width, AnsiConsole.Profile.Height));
            }
        });
        if (cancelled) { if (items.Contains("Back")) return "Back"; if (items.Contains("Exit")) return "Exit"; throw new NavigationBackException(); }
        return items[index];
    }
    public static IRenderable Workspace(string title, PackageWorkspaceRow[] packages, int selected, bool pending, int height = 30)
    {
        var visible = Math.Max(3, height - 13); var start = selected < packages.Length ? selected / visible * visible : Math.Max(0, packages.Length - visible);
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Hex("#4C6A7A")).AddColumn("Package").AddColumn("Version").AddColumn("After saving");
        for (var i = start; i < Math.Min(packages.Length, start + visible); i++)
        {
            var item = packages[i]; var color = item.Action == "Will be removed" ? "#FB7185" : item.Required ? "#F6C451" : "#DDEAF2";
            var style = selected == i ? "bold black on #75D7F7" : color;
            table.AddRow($"[{style}]{(i == selected ? "> " : "  ")}{Markup.Escape(item.Name)}[/]", "[#75D7F7]" + Markup.Escape(item.SelectedVersion) + "[/]", Markup.Escape(item.Action));
        }
        var controls = new[] { pending ? "Save changes" : "Nothing to save", "Add packages", "Creator presets", "Copy project packages", "Options" };
        var footer = string.Join("  ", controls.Select((label, i) => $"[{(selected == packages.Length + i ? "bold black on #67E8A5" : "#A8B6C1")}] {Markup.Escape(label)} [/]"));
        return new Rows(new Rule("[#6BD5FF]" + Markup.Escape(title) + "[/]").LeftJustified(), table, new Markup(footer), new Markup("[grey]↑↓ Navigate · ←→ Version · V Choose · Space Remove · A Add · P Presets · S Save · F2 Options · Esc Back[/]"));
    }
    public static async Task<WorkspaceChoice> EditPackages(string title, PackageEditSession session, Func<string, Task<string[]>> versions)
    {
        var index = 0; WorkspaceChoice? result = null;
        PackageWorkspaceRow[] Rows() => session.Rows().Where(x => x.Included || x.Action == "Will be removed").ToArray();
        await AnsiConsole.Live(Workspace(title, Rows(), index, session.HasPendingChanges, AnsiConsole.Profile.Height)).AutoClear(true).StartAsync(async live =>
        {
            while (result is null)
            {
                var rows = Rows(); index = Math.Clamp(index, 0, rows.Length + 4); var key = Console.ReadKey(true); var id = index < rows.Length ? rows[index].Id : null;
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow: index = Math.Max(0, index - 1); break;
                    case ConsoleKey.DownArrow: index = Math.Min(rows.Length + 4, index + 1); break;
                    case ConsoleKey.Tab: index = index < rows.Length || index == rows.Length + 4 ? rows.Length : index + 1; break;
                    case ConsoleKey.Home: index = 0; break;
                    case ConsoleKey.End: index = rows.Length; break;
                    case ConsoleKey.Escape: result = new("Back"); break;
                    case ConsoleKey.F2: result = new("Options"); break;
                    case ConsoleKey.Enter: result = index < rows.Length ? new("Change package version", id) : new(new[] { "Apply changes", "Add packages", "Apply creator preset", "Copy package selection", "Options" }[index - rows.Length]); break;
                    case ConsoleKey.V: if (id is not null) result = new("Change package version", id); break;
                    case ConsoleKey.Spacebar: if (id is not null && !rows[index].Required) session.Toggle(id); break;
                    case ConsoleKey.A: result = new("Add packages"); break;
                    case ConsoleKey.P: result = new("Apply creator preset"); break;
                    case ConsoleKey.F: result = new("Favorites"); break;
                    case ConsoleKey.I: result = new("Additional imports"); break;
                    case ConsoleKey.S: if (session.HasPendingChanges) result = new("Apply changes"); break;
                    case ConsoleKey.LeftArrow: case ConsoleKey.RightArrow:
                        if (id is null) break;
                        var available = new[] { "latest" }.Concat(SemanticVersion.Sort(await versions(id), rows[index].SelectedVersion.Contains('-'))).Distinct().ToArray();
                        var current = Array.IndexOf(available, rows[index].SelectedVersion); if (current < 0) current = 0;
                        session.SetVersion(id, available[Math.Clamp(current + (key.Key == ConsoleKey.LeftArrow ? 1 : -1), 0, available.Length - 1)]); break;
                }
                live.UpdateTarget(Workspace(title, Rows(), index, session.HasPendingChanges, AnsiConsole.Profile.Height));
            }
        });
        return result!;
    }
    private static Color Hex(string value) => new(Convert.ToByte(value.Substring(1, 2), 16), Convert.ToByte(value.Substring(3, 2), 16), Convert.ToByte(value.Substring(5, 2), 16));
    public static LibraryChoice Projects(ProjectSnapshot[] projects)
    {
        var filter = ""; var index = 0; var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase); LibraryChoice? result = null;
        ProjectSnapshot[] Filtered() => projects.Where(x => (x.Name + " " + x.Kind + " " + x.UnityVersion).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        IRenderable Render()
        {
            var rows = Filtered(); index = Math.Clamp(index, 0, Math.Max(0, rows.Length - 1)); var visible = Math.Max(3, AnsiConsole.Profile.Height - 9); var start = index / visible * visible;
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Hex("#4C6A7A")).AddColumn("Project").AddColumn("Type").AddColumn("Unity").AddColumn("Packages");
            for (var i = start; i < Math.Min(rows.Length, start + visible); i++)
            {
                var style = i == index ? "bold black on #75D7F7" : "#DDEAF2"; var row = rows[i];
                table.AddRow($"[{style}]{Markup.Escape((selected.Contains(row.Path) ? "[x] " : "[ ] ") + row.Name)}[/]", Markup.Escape(row.Kind), Markup.Escape(row.UnityVersion), row.PackageCount.ToString());
            }
            return new Rows(new Rule("[#6BD5FF]My projects[/]").LeftJustified(), new Markup("[grey]Search: " + Markup.Escape(filter.Length > 0 ? filter : "type to filter") + "[/]"), table, new Markup("[grey]↑↓ Navigate · Space Select · Enter Open · F2 Options · Esc Back[/]"));
        }
        AnsiConsole.Live(Render()).AutoClear(true).Start(live =>
        {
            while (result is null)
            {
                var rows = Filtered(); var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow: index = Math.Max(0, index - 1); break;
                    case ConsoleKey.DownArrow: index = Math.Min(Math.Max(0, rows.Length - 1), index + 1); break;
                    case ConsoleKey.PageUp: index = Math.Max(0, index - 10); break;
                    case ConsoleKey.PageDown: index = Math.Min(Math.Max(0, rows.Length - 1), index + 10); break;
                    case ConsoleKey.Home: index = 0; break;
                    case ConsoleKey.End: index = Math.Max(0, rows.Length - 1); break;
                    case ConsoleKey.Backspace: if (filter.Length > 0) filter = filter[..^1]; index = 0; break;
                    case ConsoleKey.Spacebar: if (rows.Length > 0 && !selected.Remove(rows[index].Path)) selected.Add(rows[index].Path); break;
                    case ConsoleKey.Enter: if (selected.Count > 0) result = new("Open", projects.Where(x => selected.Contains(x.Path)).ToArray()); else if (rows.Length > 0) result = new("Open", [rows[index]]); break;
                    case ConsoleKey.Escape: result = new("Back", []); break;
                    case ConsoleKey.F2: result = new("Options", []); break;
                    default: if (!char.IsControl(key.KeyChar)) { filter += key.KeyChar; index = 0; } break;
                }
                live.UpdateTarget(Render());
            }
        });
        return result!;
    }
}
public sealed record WorkspaceChoice(string Action, string? PackageId = null);
public sealed record LibraryChoice(string Action, ProjectSnapshot[] Projects);
public sealed class NavigationBackException : Exception;
