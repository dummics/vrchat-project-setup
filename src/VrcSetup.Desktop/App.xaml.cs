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
        window = new MainWindow(); window.Activate();
    }
}
