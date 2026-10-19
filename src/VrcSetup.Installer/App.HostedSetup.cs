using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Installer;

public partial class App
{
    // The small native setup owns all visible UI. This engine never creates a window.
    private async Task RunHostedAsync(string[] args, string pipeName)
    {
        string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(pipeName, "^vrcsetup-setup-[a-f0-9]{32}$")) { Shutdown(1); return; }
        var root = Path.GetFullPath(Option("--root") ?? InstallationManager.DefaultRoot);
        var home = Option("--home"); var automatic = args.Contains("--silent");
        var tracePath = Path.Combine(AppPaths.ForInstallation(home, root).Home, "updates", "components", "last-engine.log");
        Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
        using var trace = new StreamWriter(tracePath, false, Encoding.UTF8) { AutoFlush = true };
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var gate = new object(); StreamWriter? writer = null;
        void Send(SetupMessage message) { lock (gate) { trace.WriteLine(DateTime.UtcNow.ToString("O") + " " + JsonSerializer.Serialize(message)); writer?.WriteLine(JsonSerializer.Serialize(message)); } }
        try
        {
            await pipe.ConnectAsync(30000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            using var output = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true }; writer = output;
            async Task<SetupMessage> Receive() => JsonSerializer.Deserialize<SetupMessage>(await reader.ReadLineAsync() ?? throw new IOException("Setup window disconnected.")) ?? throw new IOException("Invalid setup response.");
            if (!automatic)
            {
                var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(root, ".vrcsetup-install.json"));
                var change = InstallationVersions.Compare(record?.Version, ApplicationUpdates.CurrentVersion);
                if (change is InstallationChange.Reinstall or InstallationChange.Downgrade)
                {
                    Send(new("confirm", change == InstallationChange.Downgrade ? "downgrade" : "reinstall", Text: record!.Version));
                    if ((await Receive()) is not { Kind: "confirm", Accepted: true }) { Send(new("cancelled")); Shutdown(0); return; }
                }
            }
            Send(new("progress", "waiting"));
            Environment.CurrentDirectory = Path.GetTempPath();
            if (Option("--wait-for-pid") is { } id)
            {
                if (!int.TryParse(id, out var pid) || pid <= 0 || pid == Environment.ProcessId) throw new IOException("Invalid application process.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                try { using var previous = Process.GetProcessById(pid); await previous.WaitForExitAsync(timeout.Token); }
                catch (ArgumentException) { }
            }
            var detail = new CallbackProgress<string>(text => { lock (gate) trace.WriteLine(DateTime.UtcNow.ToString("O") + " " + text); });
            var measured = new CallbackProgress<SetupProgress>(p => Send(new("progress", p.Phase, p.Completed, p.Total)));
            if (args.Contains("--uninstall")) await new InstallationManager(new AppPaths(home)).UninstallAsync(root, args.Contains("--yes"));
            else if (args.Contains("--repair")) await new InstallationManager(new AppPaths(home)).RepairAsync(root);
            else await Install(root, home, detail, createStartMenu: automatic, measured: measured);
            if (!automatic && !args.Contains("--uninstall"))
            {
                Send(new("complete"));
                var choice = await Receive();
                if (choice.Kind != "finish") throw new IOException("Invalid completion response.");
                Send(new("progress", "finish"));
                await new InstallationManager(AppPaths.ForInstallation(home, root)).ConfigureShortcutsAsync(root, choice.StartMenu, choice.Desktop);
                if (choice.Launch) { Send(new("progress", "launch")); LaunchInstalled(root, home, false); }
            }
            else if (args.Contains("--launch") && !args.Contains("--uninstall"))
            { Send(new("progress", "launch")); await Task.Run(() => LaunchInstalled(root, home, true)); }
            if (Option("--result") is { } result) await AtomicFile.WriteJsonAsync(result, new { Success = true, Root = root, ExternalUi = true, EngineWindowCreated = false });
            Send(new("finished")); Shutdown(0);
        }
        catch (Exception error)
        {
            lock (gate) trace.WriteLine(error.ToString());
            if (Option("--result") is { } result) await AtomicFile.WriteJsonAsync(result, new { Success = false, Error = error.ToString(), ExternalUi = true, EngineWindowCreated = false });
            var reopened = false;
            if (automatic && args.Contains("--launch") && File.Exists(Path.Combine(root, ".vrcsetup-install.json")))
                try { LaunchInstalled(root, home, false); reopened = true; } catch (Exception launchError) { lock (gate) trace.WriteLine(launchError.ToString()); }
            try { Send(new("failed", Text: reopened ? "reopened" : null)); } catch (Exception sendError) when (sendError is IOException or ObjectDisposedException) { }
            Shutdown(1);
        }
    }
    private sealed class CallbackProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
