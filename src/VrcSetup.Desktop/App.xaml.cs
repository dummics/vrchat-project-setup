using Microsoft.UI.Xaml;

namespace VrcSetup_Desktop;

public partial class App : Application
{
    private Window? window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, error) =>
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "--smoke-test");
            if (index >= 0 && index + 1 < args.Length)
                try { System.IO.Directory.CreateDirectory(args[index + 1]); System.IO.File.WriteAllText(System.IO.Path.Combine(args[index + 1], "unhandled.txt"), error.Exception.ToString()); } catch (System.IO.IOException) { }
        };
    }
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
                if (arguments.Contains("--inspect-pages"))
                {
                    var vm = main.Page.ViewModel;
                    if (vm.Projects.Count > 0) await vm.OpenProjectPackagesCommand.ExecuteAsync(vm.Projects[0]);
                    foreach (var screen in new[] { "Workspace", "Create", "Catalog" })
                    {
                        if (screen == "Catalog") vm.OpenProjectCatalogCommand.Execute(null); else vm.CurrentPage = screen;
                        await Task.Delay(250); main.Page.UpdateLayout();
                        await Verification.UiSmokeTest.Render(main.Page, System.IO.Path.Combine(output, screen.ToLowerInvariant() + ".png"));
                    }
                    vm.CurrentPage = "Create"; await Task.Delay(200); main.Page.UpdateLayout();
                    var form = (FrameworkElement)main.Page.FindName("CreateForm"); var origin = form.TransformToVisual(main.Page).TransformPoint(new(0, 0));
                    var host = (FrameworkElement)main.Page.FindName("PageContent"); var view = (FrameworkElement)main.Page.FindName("CreateView"); var nav = (Microsoft.UI.Xaml.Controls.NavigationView)main.Page.FindName("Navigation");
                    await VrcSetup.Infrastructure.AtomicFile.WriteJsonAsync(System.IO.Path.Combine(output, "layout.json"), new { ClientWidth = main.Page.ActualWidth, FormLeft = origin.X, FormWidth = form.ActualWidth, HostWidth = host.ActualWidth, HostMax = host.MaxWidth, ViewWidth = view.ActualWidth, NavWidth = nav.ActualWidth, nav.IsPaneOpen, Mode = nav.DisplayMode.ToString(), Fits = origin.X >= 0 && origin.X + form.ActualWidth <= main.Page.ActualWidth + 1 });
                }
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
