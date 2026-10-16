namespace VrcSetup.Cli;

public sealed class CommandLine
{
    public List<string> Positionals { get; } = [];
    private readonly Dictionary<string, List<string>> options = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Flags = new(["json", "dryrun", "test", "refresh", "exact", "yes", "wizard", "help", "extras", "replace", "prerelease", "developer", "verbose", "tracetools", "nocatalogrefresh"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Values = new(["home", "name", "sort", "preset", "recipe", "package", "buildtarget", "publishingplatforms", "projectpath", "root", "unity", "theme", "targetmap", "plan", "backup", "pid", "extra"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MultiValues = new(["package", "publishingplatforms", "extra"], StringComparer.OrdinalIgnoreCase);
    public CommandLine(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        var positionalOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument == "--") { positionalOnly = true; continue; }
            if (positionalOnly || !argument.StartsWith('-')) { Positionals.Add(argument); continue; }
            var equal = argument.IndexOf('=');
            var key = Normalize(equal >= 0 ? argument[..equal] : argument);
            if (key == "h") key = "help";
            if (!Flags.Contains(key) && !Values.Contains(key)) throw new ArgumentException($"Unknown option: {argument}");
            if (!options.TryGetValue(key, out var values)) options[key] = values = [];
            if (Flags.Contains(key)) { values.Add("true"); continue; }
            if (equal >= 0) { values.Add(argument[(equal + 1)..]); continue; }
            if (i + 1 >= args.Length || args[i + 1].StartsWith('-')) throw new ArgumentException($"Option {argument} requires a value.");
            values.Add(args[++i]);
            while (MultiValues.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith('-')) values.Add(args[++i]);
        }
    }
    private static string Normalize(string key) => key.TrimStart('-').Replace("-", "").ToLowerInvariant();
    public bool Has(string key) => options.ContainsKey(Normalize(key));
    public string? Value(string key) => options.GetValueOrDefault(Normalize(key))?.LastOrDefault();
    public string[] All(string key) => options.GetValueOrDefault(Normalize(key))?.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray() ?? [];
    public bool DryRun => Has("dryrun") || Has("test");
    public string Required(int index, string label) => Positionals.Count > index ? Positionals[index] : throw new ArgumentException($"{label} is required.");
}
