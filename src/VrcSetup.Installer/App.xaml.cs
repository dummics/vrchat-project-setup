using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Installer;

public partial class App : Application
{
    private static string? sourcePayload;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var arguments = e.Args;
        string? Option(string name) { var i = Array.IndexOf(arguments, name); return i >= 0 && i + 1 < arguments.Length ? arguments[i + 1] : null; }
        sourcePayload = Option("--source-payload");
        UiText.LoadPreference(AppPaths.ForInstallation(Option("--home"), Option("--root") ?? InstallationManager.DefaultRoot).ConfigFile);
        if (Option("--verify-manual-installer") is { } manualEvidence)
        {
            try { await ManualInstallerVerification.RunAsync(manualEvidence); }
            catch (Exception error) { Directory.CreateDirectory(manualEvidence); await File.WriteAllTextAsync(Path.Combine(manualEvidence, "failure.txt"), error.ToString()); Environment.ExitCode = 1; }
            Shutdown(); return;
        }
        if (Option("--render-update-progress") is { } render)
        {
            Directory.CreateDirectory(render);
            var preview = new UpdateProgressWindow(); preview.RenderForVerification(Path.Combine(render, "updater-waiting.png"));
            preview.SetStep("Installing update"); preview.RenderForVerification(Path.Combine(render, "updater-installing.png"));
            preview.SetStep("Updated. Reopening the app…", true); preview.RenderForVerification(Path.Combine(render, "updater-complete.png"));
            Shutdown(); return;
        }
        if (arguments.Contains("--silent"))
        {
            var target = Option("--root") ?? InstallationManager.DefaultRoot;
            var home = Option("--home");
            var diagnostic = Environment.GetEnvironmentVariable("VRCSETUP_UPDATE_SMOKE_SPEC") is { Length: > 0 };
            var progressWindow = !diagnostic && (arguments.Contains("--wait-for-pid") || arguments.Contains("--show-progress")) ? new UpdateProgressWindow() : null;
            progressWindow?.Show();
            var progress = new Progress<string>(message => progressWindow?.SetStep(message));
            var dispatcherClock = Stopwatch.StartNew();
            var lastTick = TimeSpan.Zero; var maximumGap = TimeSpan.Zero; var ticks = 0;
            var heartbeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            void SampleDispatcher()
            {
                var now = dispatcherClock.Elapsed; var gap = now - lastTick; lastTick = now;
                if (gap > maximumGap) maximumGap = gap;
            }
            heartbeat.Tick += (_, _) => { SampleDispatcher(); ticks++; };
            if (diagnostic) { heartbeat.Start(); await Task.Delay(100); }
            try
            {
                // An updater launched by the installed shortcut inherits the install
                // directory. Windows will not rename its current directory, even
                // after the old GUI exits. This also protects older GUI updaters.
                Environment.CurrentDirectory = Path.GetTempPath();
                if (Option("--wait-for-pid") is { } processId)
                {
                    if (!int.TryParse(processId, out var pid) || pid <= 0 || pid == Environment.ProcessId) throw new IOException("Invalid application process.");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    try { using var previous = Process.GetProcessById(pid); await previous.WaitForExitAsync(timeout.Token); }
                    catch (ArgumentException) { /* Already closed. Never terminate a running app. */ }
                }
                if (arguments.Contains("--uninstall")) await new InstallationManager(new AppPaths(home)).UninstallAsync(target, arguments.Contains("--yes"));
                else if (arguments.Contains("--repair")) await new InstallationManager(new AppPaths(home)).RepairAsync(target);
                else await Install(target, home, progress);
                if (diagnostic) { SampleDispatcher(); heartbeat.Stop(); }
                if (Option("--result") is { } result) await AtomicFile.WriteJsonAsync(result, new { Success = true, Root = target, DispatcherTicks = ticks, DispatcherMaximumGapMs = maximumGap.TotalMilliseconds });
                if (arguments.Contains("--launch") && !arguments.Contains("--uninstall"))
                {
                    progressWindow?.SetStep("Updated. Reopening the app…", true);
                    if (progressWindow is not null) await Task.Delay(900);
                    await Task.Run(() => LaunchInstalled(target, home, true));
                }
                Shutdown(0);
            }
            catch (Exception error)
            {
                heartbeat.Stop();
                if (Option("--result") is { } result) await AtomicFile.WriteJsonAsync(result, new { Success = false, Error = error.ToString() });
                var reopened = false;
                if (!diagnostic && arguments.Contains("--launch") && File.Exists(Path.Combine(target, ".vrcsetup-install.json")))
                    try { await Task.Run(() => LaunchInstalled(target, home, false)); reopened = true; } catch (Exception launchError) when (launchError is IOException or System.ComponentModel.Win32Exception) { }
                if (progressWindow is not null) progressWindow.ShowFailure(reopened, Option("--result"));
                else Shutdown(1);
            }
            return;
        }
        var window = new InstallerWindow(Option("--root"), Option("--home")); MainWindow = window; window.Show();
    }
    internal static void LaunchInstalled(string target, string? home, bool updated)
    {
        var launch = new ProcessStartInfo(Path.Combine(target, "app", "desktop", "VrcSetup.Desktop.exe")) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() };
        launch.Environment["VRCSETUP_HOME"] = AppPaths.ForInstallation(home, target).Home;
        if (updated && Environment.GetEnvironmentVariable("VRCSETUP_UPDATE_SMOKE_SPEC") is { Length: > 0 } spec)
        { launch.ArgumentList.Add("--verify-update-smoke"); launch.ArgumentList.Add(spec); }
        Process.Start(launch);
    }
    // ZIP extraction, hashing and filesystem moves include synchronous work.
    // Keep the entire operation off WPF's dispatcher, including before the first await.
    internal static Task<InstallationRecord> Install(string destination, string? home = null, IProgress<string>? progress = null, bool createStartMenu = true)
        => Task.Run(() => InstallCore(destination, home, progress, createStartMenu));
    private static async Task<InstallationRecord> InstallCore(string destination, string? home, IProgress<string>? progress, bool createStartMenu)
    {
        Environment.CurrentDirectory = Path.GetTempPath();
        EnsureApplicationClosed(destination);
        if (sourcePayload is not null)
            return await new InstallationManager(AppPaths.ForInstallation(home, Path.GetFullPath(sourcePayload))).InstallAsync(destination, progress: progress, createStartMenu: createStartMenu);
        var extraction = Path.Combine(Path.GetTempPath(), "VrcSetup-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraction);
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VrcSetup.Payload.zip") ?? throw new IOException("Installer payload missing. Build this executable with scripts/Publish-Windows.ps1.");
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var target = ProjectRules.NormalizePath(entry.FullName, extraction); ProjectRules.EnsureContained(extraction, target);
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Linked payload files are not supported.");
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target);
                }
            }
            if (File.Exists(Path.Combine(extraction, DeltaUpdates.ManifestName)))
                await DeltaUpdates.ReconstructAsync(extraction, destination);
            var paths = AppPaths.ForInstallation(home, extraction);
            return await new InstallationManager(paths).InstallAsync(destination, progress: progress, createStartMenu: createStartMenu);
        }
        finally { if (Directory.Exists(extraction)) Directory.Delete(extraction, true); }
    }
    private static void EnsureApplicationClosed(string destination)
    {
        var root = ProjectRules.NormalizePath(destination) + Path.DirectorySeparatorChar;
        foreach (var name in new[] { "VrcSetup.Desktop", "vrcsetup" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    try
                    {
                        if (!process.HasExited && process.MainModule?.FileName is { } executable && executable.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Close VRC Project Setup and its terminal commands before installing. Running imports are preserved.");
                    }
                    catch (InvalidOperationException) { /* Process exited during inspection. */ }
                }
    }
}
