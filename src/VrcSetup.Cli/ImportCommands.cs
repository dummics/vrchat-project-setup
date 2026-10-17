using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Cli;

internal static class ImportCommands
{
    public static async Task<int> ExecuteAsync(VrcSetupApplication app, CommandLine command, AppConfiguration config, CancellationToken token)
    {
        var action = command.Required(1, "Import action").ToLowerInvariant();
        if (action == "list")
        {
            var history = await AtomicFile.ReadJsonAsync<ImportJob[]>(Path.Combine(app.Paths.Home, "imports.json"), token) ?? [];
            Console.WriteLine(JsonSerializer.Serialize(history, JsonDefaults.Options)); return 0;
        }
        if (action is not ("run" or "recover" or "apply-plan")) throw new ArgumentException("Use imports run, list, apply-plan or recover.");
        var parallelValue = command.Value("parallel") ?? "1";
        var parallel = parallelValue.Equals("auto", StringComparison.OrdinalIgnoreCase) ? 0 : int.TryParse(parallelValue, out var limit) && limit > 0 ? limit : throw new ArgumentException("--parallel must be auto or a positive number.");
        await using var queue = app.Imports;
        OperationPlan reviewed; CreationRequest? recoverySource = null;
        if (action == "apply-plan")
        {
            reviewed = await AtomicFile.ReadJsonAsync<OperationPlan>(command.Required(2, "Reviewed plan file"), token) ?? throw new IOException("Import plan is empty.");
            if (reviewed.Projects.Any(x => x.Creation is null)) throw new ArgumentException("Import apply-plan accepts new project creation plans. Use imports recover for interrupted jobs.");
        }
        else if (action == "recover")
        {
            await queue.InitializeAsync(token);
            var id = command.Required(2, "Import job ID");
            reviewed = await queue.RecoveryPlanAsync(id, config, token);
            recoverySource = queue.Snapshot().Single(x => x.Id == id).Source;
        }
        else
        {
            var files = command.Positionals.Skip(2).Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length is < 1 or > 50) throw new ArgumentException("Choose 1 to 50 UnityPackages.");
            var plans = new List<ProjectPlan>();
            foreach (var file in files)
            {
                var platform = command.Value("buildtarget") ?? ProjectRules.SuggestBuildTarget(file);
                var plan = await app.PlanCreationAsync(file, config, preset: command.Value("preset"), buildTarget: platform, publishingPlatforms: [platform], cancellationToken: token);
                plans.AddRange(plan.Projects);
            }
            if (plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count) throw new ArgumentException("Import destinations must be distinct. Use Create to choose individual names.");
            reviewed = new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
        }
        if (command.Value("plan") is { } planFile) await AtomicFile.WriteJsonAsync(planFile, reviewed, token);
        if (command.DryRun) { Console.WriteLine(JsonSerializer.Serialize(reviewed, JsonDefaults.Options)); return 0; }
        if (!command.Has("json")) Program.PrintPlan(reviewed, false);
        else foreach (var project in reviewed.Projects) Console.Error.WriteLine($"Import: {project.Name} → {project.Path}");
        if (!command.Has("yes") && !Program.Confirm("Start these imports?")) return 2;
        var last = new Dictionary<string, string>(); var outputGate = new object();
        queue.Changed += jobs =>
        {
            lock (outputGate)
                foreach (var job in jobs)
                {
                    var state = job.State + " · " + job.Step;
                    if (last.GetValueOrDefault(job.Id) == state) continue;
                    last[job.Id] = state; Console.Error.WriteLine($"[{job.Name}] {state} · {job.Progress}% · {job.Message}");
                }
        };
        await queue.InitializeAsync(token);
        var previous = queue.Snapshot().Select(x => x.Id).ToHashSet();
        try
        {
            await queue.EnqueueAsync(reviewed, parallel, recoverySource, token);
            while (queue.HasActiveJobs) await Task.Delay(250, token);
        }
        catch (OperationCanceledException) { queue.CancelAll(); throw; }
        var result = queue.Snapshot().Where(x => !previous.Contains(x.Id)).ToArray();
        if (command.Has("json")) Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Options));
        return result.Length > 0 && result.All(x => x.State == "Completed") ? 0 : 1;
    }
}
