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
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var arguments = e.Args;
        string? Option(string name) { var i = Array.IndexOf(arguments, name); return i >= 0 && i + 1 < arguments.Length ? arguments[i + 1] : null; }
        if (arguments.Contains("--silent"))
        {
            try
            {
                var target = Option("--root") ?? InstallationManager.DefaultRoot;
                var home = Option("--home");
                if (Option("--wait-for-pid") is { } processId)
                {
                    if (!int.TryParse(processId, out var pid) || pid <= 0 || pid == Environment.ProcessId) throw new IOException("Invalid application process.");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    try { using var previous = Process.GetProcessById(pid); await previous.WaitForExitAsync(timeout.Token); }
                    catch (ArgumentException) { /* Already closed. Never terminate a running app. */ }
                }
                if (arguments.Contains("--uninstall")) await new InstallationManager(new AppPaths(home)).UninstallAsync(target, arguments.Contains("--yes"));
                else if (arguments.Contains("--repair")) await new InstallationManager(new AppPaths(home)).RepairAsync(target);
                else await Install(target, home);
                if (Option("--result") is { } result) await AtomicFile.WriteJsonAsync(result, new { Success = true, Root = target });
                if (arguments.Contains("--launch") && !arguments.Contains("--uninstall"))
                {
                    var launch = new ProcessStartInfo(Path.Combine(target, "app", "desktop", "VrcSetup.Desktop.exe")) { UseShellExecute = false };
                    if (Environment.GetEnvironmentVariable("VRCSETUP_UPDATE_SMOKE_SPEC") is { Length: > 0 } spec)
                    { launch.ArgumentList.Add("--verify-update-smoke"); launch.ArgumentList.Add(spec); }
                    Process.Start(launch);
                }
                Shutdown(0);
            }
            catch (Exception error)
            {
                if (Option("--result") is { } result) await AtomicFile.WriteJsonAsync(result, new { Success = false, Error = error.ToString() });
                Shutdown(1);
            }
            return;
        }
        var window = new InstallerWindow(); MainWindow = window; window.Show();
    }
    internal static async Task<InstallationRecord> Install(string destination, string? home = null)
    {
        EnsureApplicationClosed(destination);
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
            var paths = AppPaths.ForInstallation(home, extraction);
            return await new InstallationManager(paths).InstallAsync(destination);
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
                            throw new IOException("Close VRChat Project Setup and its terminal commands before installing. Running imports are preserved.");
                    }
                    catch (InvalidOperationException) { /* Process exited during inspection. */ }
                }
    }
}

public sealed class InstallerWindow : Window
{
    private readonly TextBox target;
    private readonly TextBlock status;
    private readonly ProgressBar progress;
    private readonly Button install;
    private readonly Button launch;
    private string? installedRoot;
    public InstallerWindow()
    {
        Title = "Install VRChat Project Setup"; Width = 600; Height = 420; MinWidth = 520; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "VRChat Project Setup", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) });
        panel.Children.Add(new TextBlock { Text = "Install folder", Margin = new Thickness(0, 0, 0, 8) });
        target = new TextBox { Text = InstallationManager.DefaultRoot, Margin = new Thickness(0, 0, 0, 14) }; panel.Children.Add(target);
        panel.Children.Add(new TextBlock { Text = "GUI and command-line tool. Installed for your Windows user; Unity projects are preserved.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        progress = new ProgressBar { IsIndeterminate = false, Height = 5, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(progress);
        status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) }; panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        install = new Button { Content = "Install", Padding = new Thickness(20, 9, 20, 9), Margin = new Thickness(0, 0, 10, 0) };
        launch = new Button { Content = "Open VRCSETUP", Padding = new Thickness(20, 9, 20, 9), IsEnabled = false };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false; target.IsEnabled = false; progress.IsIndeterminate = true; status.Text = "Verifying and installing…";
            try { var record = await App.Install(ProjectRules.NormalizePath(target.Text)); installedRoot = record.Root; status.Text = "Installed. Find VRChat Project Setup in Windows Search."; launch.IsEnabled = true; }
            catch (Exception error) { status.Text = error.Message; install.IsEnabled = true; target.IsEnabled = true; }
            finally { progress.IsIndeterminate = false; }
        };
        launch.Click += (_, _) => { if (installedRoot is not null) Process.Start(new ProcessStartInfo(Path.Combine(installedRoot, "app", "desktop", "VrcSetup.Desktop.exe")) { UseShellExecute = true }); Close(); };
        actions.Children.Add(install); actions.Children.Add(launch); panel.Children.Add(actions); Content = panel;
        Closed += (_, _) => Application.Current.Shutdown();
    }
}
