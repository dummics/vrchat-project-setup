using System.Diagnostics;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

internal static class MultiImportVerification
{
    public static async Task RunAsync(string editor, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a new evidence directory.");
        UnityProjectDriver.ValidateEditor(editor);
        Directory.CreateDirectory(evidence);
        var app = new VrcSetupApplication(Path.Combine(evidence, "home"), ["--no-vcc-registration"]);
        var active = 0; var peak = 0; var started = 0; var gate = new object();
        await using var queue = new ImportQueue(app, async (plan, _, token) =>
        {
            var count = Interlocked.Increment(ref active); Interlocked.Increment(ref started);
            lock (gate) peak = Math.Max(peak, count);
            try
            {
                await UnityImportVerification.RunAsync(editor, Path.Combine(evidence, plan.Projects[0].Name));
                return new OperationReport(plan.Id, DateTime.UtcNow,
                    plan.Projects.Select(x => new ProjectOutcome(x.Name, x.Path, OperationStatus.Completed, "Done")).ToArray(), []);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        var projects = Enumerable.Range(1, 3).Select(index =>
        {
            var name = "fixture" + index;
            var path = Path.Combine(evidence, name, "Project [PC] & città");
            return new ProjectPlan { Name = name, Path = path, ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [],
                Creation = new CreationRequest("", name, path, editor, "StandaloneWindows64", ["StandaloneWindows64"], [], EmptyProject: true) };
        }).ToArray();
        var watch = Stopwatch.StartNew();
        await queue.EnqueueAsync(new OperationPlan(Guid.NewGuid().ToString("N"), DateTime.UtcNow, projects), 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        while (queue.HasActiveJobs) await Task.Delay(500, timeout.Token);
        watch.Stop();
        var jobs = queue.Snapshot();
        if (started != 3 || peak is < 1 or > 2 || jobs.Length != 3 || jobs.Any(x => x.State != "Completed"))
            throw new IOException($"Automatic import queue did not complete safely: started={started}, peak={peak}, states={string.Join(',', jobs.Select(x => x.State))}.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "multi-import.json"), new { Passed = true, PeakConcurrent = peak, ElapsedSeconds = watch.Elapsed.TotalSeconds, Count = started });
        Console.WriteLine($"PASS: {started} isolated Unity imports completed; peak concurrent {peak}; {watch.Elapsed.TotalSeconds:F1}s.");
    }
}
