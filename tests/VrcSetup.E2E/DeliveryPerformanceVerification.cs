using System.Diagnostics;
using VrcSetup.Bootstrapper;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

internal static class DeliveryPerformanceVerification
{
    // Reads an existing install; writes only to a fresh, explicitly supplied evidence folder.
    public static async Task RunAsync(string manifestFile, string installed, string cache, string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose fresh performance evidence.");
        Directory.CreateDirectory(evidence);
        using var input = File.OpenRead(manifestFile);
        var manifest = ComponentDelivery.Read(input);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var clock = Stopwatch.StartNew();
        var phases = new Dictionary<string, (double First, double Last, long Events)>();
        var progress = new Measurements(p =>
        {
            var now = clock.Elapsed.TotalSeconds;
            var old = phases.GetValueOrDefault(p.Phase, (now, now, 0));
            phases[p.Phase] = (old.First, now, old.Events + 1);
        });
        var result = await new ComponentDelivery(client).PrepareAsync(manifest, Path.Combine(evidence, "stage"), cache, installed, measured: progress);
        var preparationSeconds = clock.Elapsed.TotalSeconds;
        var previousPathSetting = Environment.GetEnvironmentVariable("VRCSETUP_SKIP_PATH_UPDATE");
        try
        {
            Environment.SetEnvironmentVariable("VRCSETUP_SKIP_PATH_UPDATE", "1");
            await new InstallationManager(AppPaths.ForInstallation(Path.Combine(evidence, "home"), Path.Combine(evidence, "stage", "payload")))
                .InstallAsync(Path.Combine(evidence, "installed"), createStartMenu: false, measured: progress);
        }
        finally { Environment.SetEnvironmentVariable("VRCSETUP_SKIP_PATH_UPDATE", previousPathSetting); }
        clock.Stop();
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "timings.json"), new
        {
            TotalSeconds = clock.Elapsed.TotalSeconds, PreparationSeconds = preparationSeconds,
            InstallationSeconds = clock.Elapsed.TotalSeconds - preparationSeconds, Delivery = result,
            Phases = phases.Select(p => new { Phase = p.Key, First = p.Value.First, Last = p.Value.Last, Seconds = p.Value.Last - p.Value.First, p.Value.Events })
        });
        Console.WriteLine($"Preparation {preparationSeconds:F3}s, install {clock.Elapsed.TotalSeconds - preparationSeconds:F3}s, total {clock.Elapsed.TotalSeconds:F3}s; downloaded {result.DownloadedBytes} bytes; reused {result.ReusedComponents} components.");
    }
    private sealed class Measurements(Action<SetupProgress> report) : IProgress<SetupProgress>
    { public void Report(SetupProgress value) => report(value); }
}
