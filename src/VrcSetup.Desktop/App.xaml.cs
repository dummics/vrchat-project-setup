using Microsoft.UI.Xaml;

namespace VrcSetup_Desktop;

public partial class App : Application
{
    private Window? window;
    public App() => InitializeComponent();
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var smokeIndex = Array.IndexOf(arguments, "--smoke-test");
        var diagnosis = Array.IndexOf(arguments, "--diagnose");
        if (diagnosis >= 0 && diagnosis + 1 < arguments.Length)
        {
            var output = arguments[diagnosis + 1];
            var app = new VrcSetup.Infrastructure.VrcSetupApplication(System.IO.Path.Combine(output, "home"));
            try
            {
                await app.Configuration.ImportAsync(new VrcSetup.Infrastructure.AppPaths().ConfigFile);
                var cached = System.IO.Path.Combine(new VrcSetup.Infrastructure.AppPaths().Cache, "known-packages.json");
                if (System.IO.File.Exists(cached)) { System.IO.Directory.CreateDirectory(app.Paths.Cache); System.IO.File.Copy(cached, System.IO.Path.Combine(app.Paths.Cache, "known-packages.json"), true); }
                var main = new MainWindow(app); window = main; main.Page.SuppressDialogs = true; main.AppWindow.Show(false);
                await main.Page.Initialized;
                await Task.Delay(700);
                await Verification.UiSmokeTest.Render(main.Page, System.IO.Path.Combine(output, "startup.png"));
                await VrcSetup.Infrastructure.AtomicFile.WriteJsonAsync(System.IO.Path.Combine(output, "startup.json"), new { Projects = main.Page.ViewModel.Projects.Count, main.Page.ViewModel.Status, Activity = main.Page.ViewModel.Activity.ToArray() });
                main.Close(); Exit();
            }
            catch (Exception error) { app.Diagnostics.Write("Startup", error.Message, error); Environment.ExitCode = 1; Exit(); }
            return;
        }
        if (smokeIndex >= 0 && smokeIndex + 1 < arguments.Length)
        {
            try
            {
                var artifacts = arguments[smokeIndex + 1];
                var application = await Verification.UiSmokeTest.PrepareAsync(artifacts);
                var main = new MainWindow(application); window = main;
                main.AppWindow.Show(false);
                await Verification.UiSmokeTest.RunAsync(main, artifacts);
                main.Close(); Exit();
            }
            catch (Exception error)
            {
                System.IO.Directory.CreateDirectory(arguments[smokeIndex + 1]);
                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(arguments[smokeIndex + 1], "failure.txt"), error.ToString());
                Environment.ExitCode = 1; Exit();
            }
            return;
        }
        window = new MainWindow(new VrcSetup.Infrastructure.VrcSetupApplication(diagnosticFlags: arguments)); window.Activate();
    }
}
