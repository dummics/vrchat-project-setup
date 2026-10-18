using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace VrcSetup.Bootstrapper;

internal static class Program
{
    private static string? Option(string[] args, string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    internal static string Text(string english, string italian) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? italian : english;
    [STAThread]
    private static int Main(string[] args)
    {
        var diagnostic = Environment.GetEnvironmentVariable("VRCSETUP_UPDATE_SMOKE_SPEC") is { Length: > 0 } || args.Contains("--prefetch");
        try
        {
            using var window = new DownloadWindow(!diagnostic);
            var work = Task.Run(() => RunAsync(args, window));
            _ = work.ContinueWith(_ => window.Finish(), TaskScheduler.Default);
            window.Pump();
            return work.GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            // Startup/configuration errors must also produce a receipt rather
            // than leaving old updaters waiting forever for an outcome.
            var details = Path.Combine(Path.GetTempPath(), "VrcSetup-bootstrap-error-" + Guid.NewGuid().ToString("N") + ".log");
            File.WriteAllText(details, error.ToString());
            if (Option(args, "--result") is { } result)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result))!);
                File.WriteAllText(result, "{\"Success\":false,\"Error\":" + JsonSerializer.Serialize(error.ToString(), DeliveryJson.Default.String) + "}");
            }
            if (!diagnostic) DownloadWindow.ShowError(Text("Setup could not start. Details: ", "Impossibile avviare l’installazione. Dettagli: ") + details);
            return 1;
        }
    }
    private static async Task<int> RunAsync(string[] args, DownloadWindow window)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.GetFullPath(Option(args, "--root") ?? Path.Combine(local, "Programs", "VrcSetup"));
        var home = Option(args, "--home");
        if (home is null && File.Exists(Path.Combine(root, ".vrcsetup-install.json")))
        {
            using var record = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".vrcsetup-install.json")));
            if (record.RootElement.TryGetProperty("Home", out var saved) && saved.ValueKind == JsonValueKind.String) home = saved.GetString();
        }
        home = Path.GetFullPath(home ?? Environment.GetEnvironmentVariable("VRCSETUP_HOME") ?? Path.Combine(local, "VrcSetup"));
        var cache = Path.Combine(home, "updates", "components");
        var staging = Path.Combine(Path.GetTempPath(), "VrcSetup-components-" + Guid.NewGuid().ToString("N"));
        var handedOff = false;
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(window.Cancellation);
        if (args.Contains("--prefetch") && Option(args, "--parent-pid") is { } parentId && int.TryParse(parentId, out var parentProcessId))
            _ = CancelWhenParentExitsAsync(parentProcessId, preparation);
        try
        {
            using var manifestStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("components.json") ?? throw new IOException("Installer manifest missing.");
            var manifest = ComponentDelivery.Read(manifestStream);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var result = await new ComponentDelivery(client).PrepareAsync(manifest, staging, cache, root,
                new InlineProgress<(int Percent, string Step)>(p => window.Report(p.Percent, p.Step == "Verifying files"
                    ? Text("Verifying files", "Verifica file") : Text("Downloading components…", "Download dei componenti…"))), preparation.Token);
            using (var payload = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(staging, "payload", ".vrcsetup-payload.json"))))
                if (payload.RootElement.GetProperty("Version").GetString() != manifest.Version) throw new IOException("Component and payload versions differ.");
            Directory.CreateDirectory(cache);
            await File.WriteAllTextAsync(Path.Combine(cache, "last-delivery.json"), JsonSerializer.Serialize(result, DeliveryJson.Default.DeliveryResult));
            preparation.Token.ThrowIfCancellationRequested();
            // Background preparation never starts the installer or changes the app being used.
            if (args.Contains("--prefetch"))
            {
                if (Option(args, "--result") is { } receipt)
                { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(receipt))!); await File.WriteAllTextAsync(receipt, "{\"Success\":true}"); }
                return 0;
            }
            window.Report(100, Text("Starting setup…", "Avvio installazione…"));
            var engine = new ProcessStartInfo(Path.Combine(staging, "engine", "VrcSetupInstaller.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            foreach (var arg in args) engine.ArgumentList.Add(arg);
            engine.ArgumentList.Add("--source-payload"); engine.ArgumentList.Add(Path.Combine(staging, "payload"));
            engine.Environment["VRCSETUP_HOME"] = home;
            using var process = Process.Start(engine) ?? throw new IOException("Could not start setup.");
            handedOff = true; window.Hide();
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception error)
        {
            // Until handoff, the previous installation has never been touched.
            var resultFile = Option(args, "--result");
            if (resultFile is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultFile))!);
                await File.WriteAllTextAsync(resultFile, "{\"Success\":false,\"Error\":" + JsonSerializer.Serialize(error.ToString(), DeliveryJson.Default.String) + "}");
            }
            Directory.CreateDirectory(cache);
            await File.WriteAllTextAsync(Path.Combine(cache, "last-error.log"), error.ToString());
            if (!handedOff && args.Contains("--launch") && File.Exists(Path.Combine(root, ".vrcsetup-install.json")))
            {
                try
                {
                    if (Option(args, "--wait-for-pid") is { } id && int.TryParse(id, out var pid))
                        try { using var previous = Process.GetProcessById(pid); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); await previous.WaitForExitAsync(timeout.Token); } catch (ArgumentException) { }
                    var launch = new ProcessStartInfo(Path.Combine(root, "app", "desktop", "VrcSetup.Desktop.exe")) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() };
                    launch.Environment["VRCSETUP_HOME"] = home; Process.Start(launch);
                }
                catch (Exception recovery) { await File.AppendAllTextAsync(Path.Combine(cache, "last-error.log"), "\nRecovery: " + recovery); }
            }
            if (!args.Contains("--prefetch") && (error is not OperationCanceledException || !window.Cancellation.IsCancellationRequested)) window.Failure(Text("Download failed. Your installation is preserved. Details: ", "Download non riuscito. L’installazione precedente è conservata. Dettagli: ") + Path.Combine(cache, "last-error.log"));
            return 1;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private static async Task CancelWhenParentExitsAsync(int id, CancellationTokenSource preparation)
    {
        try { using var parent = Process.GetProcessById(id); await parent.WaitForExitAsync(); preparation.Cancel(); }
        catch (ArgumentException) { preparation.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

