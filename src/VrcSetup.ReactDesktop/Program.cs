using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startup = System.Diagnostics.Stopwatch.StartNew();
        var smoke = args is ["--smoke", var evidence] ? Path.GetFullPath(evidence) : null;
        var home = smoke ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VrcSetup-React-Candidate");
        Directory.CreateDirectory(home);
        using var lease = new FileStream(Path.Combine(home, "candidate.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var app = new Application();
        var engine = new VrcSetupApplication(home, ["--no-vcc-registration", "--no-catalog-refresh"]);
        var web = new WebView2();
        var window = new Window
        {
            Title = "VRC Project Setup • React candidate",
            Width = smoke is null ? 1160 : 720, Height = smoke is null ? 800 : 620,
            MinWidth = 520, MinHeight = 420, Content = web
        };
        if (smoke is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -12000;
            window.Top = -12000;
            window.ShowInTaskbar = false;
        }

        using var bridge = new ImportBridge(window, engine);
        var closing = false;
        var draining = false;
        window.Closing += async (_, args) =>
        {
            if (closing) return;
            args.Cancel = true;
            if (draining) return;
            if (engine.Imports.HasActiveJobs || bridge.IsBusy)
            {
                MessageBox.Show(window, "Attendi che le importazioni finiscano.", window.Title);
                return;
            }
            draining = true;
            await Task.Yield(); // Let WPF finish the cancelled close before issuing another.
            await engine.Imports.DisposeAsync();
            closing = true;
            window.Close();
        };
        window.Closed += (_, _) => web.Dispose();
        window.Loaded += async (_, _) =>
        {
            try
            {
                if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "web")))
                    throw new IOException("Gli asset React della build non sono disponibili.");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(home, "webview"));
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.SetVirtualHostNameToFolderMapping("app.vrcsetup.test", Path.Combine(AppContext.BaseDirectory, "web"), CoreWebView2HostResourceAccessKind.Deny);
                core.NavigationStarting += (_, e) => e.Cancel = !ImportBridge.IsPage(e.Uri);
                core.FrameNavigationStarting += (_, e) => e.Cancel = true;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.DownloadStarting += (_, e) => e.Cancel = true;
                await engine.Imports.InitializeAsync();
                if (smoke is not null) await bridge.SeedSmokeAsync(smoke);
                bridge.Attach(core);
                core.Navigate(ImportBridge.Page);
                if (smoke is not null)
                {
                    for (var i = 0; i < 100; ++i)
                    {
                        await Task.Delay(100);
                        if (await core.ExecuteScriptAsync("document.querySelector('[data-ready=true]') !== null && document.querySelectorAll('.tabs button').length === 6 && document.querySelector('.platforms [aria-pressed=true]').textContent === 'Quest'") != "true") continue;
                        var readyMs = startup.ElapsedMilliseconds;
                        await VerifyDraftActionsAsync(core);
                        using var screenshot = File.Create(Path.Combine(smoke, "import-react-720.png"));
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                        await File.WriteAllTextAsync(Path.Combine(smoke, "result.json"), JsonSerializer.Serialize(new
                        {
                            passed = true, readyMs, runtime = environment.BrowserVersionString, drafts = 6, target = "Android",
                            checks = new[] { "native handshake", "six analyzed archives", "compact Quest filename",
                                "independent tab state", "package panel and exclusion" }
                        }));
                        window.Close();
                        return;
                    }
                    throw new IOException("React did not complete the handshake.");
                }
            }
            catch (Exception error)
            {
                if (smoke is not null) File.WriteAllText(Path.Combine(smoke, "failure.txt"), error.ToString());
                else MessageBox.Show(window, error.Message, window.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.ExitCode = 1;
                window.Close();
            }
        };
        app.Run(window);
    }

    private static async Task VerifyDraftActionsAsync(CoreWebView2 core)
    {
        async Task Require(string condition)
        {
            for (var attempt = 0; attempt < 30; ++attempt)
            {
                if (await core.ExecuteScriptAsync(condition) == "true") return;
                await Task.Delay(50);
            }
            throw new IOException($"React smoke assertion failed: {condition}");
        }

        await core.ExecuteScriptAsync("document.querySelector('.platforms button').click()");
        await Require("document.querySelector('.platforms [aria-pressed=true]').textContent === 'PC'");
        await core.ExecuteScriptAsync("document.querySelectorAll('.tabs button')[1].click()");
        await Require("document.querySelector('.platforms [aria-pressed=true]').textContent === 'Quest'");
        await core.ExecuteScriptAsync("document.querySelectorAll('.tabs button')[0].click()");
        await Require("document.querySelector('.platforms [aria-pressed=true]').textContent === 'PC'");
        await core.ExecuteScriptAsync("document.querySelector('.platforms button:nth-child(2)').click(); document.querySelectorAll('.row')[1].click()");
        await Require("document.querySelector('h1').textContent === 'Pacchetti'");
        await core.ExecuteScriptAsync("document.querySelector('.editor button').click()");
        await Require("Array.from(document.querySelectorAll('.editor button')).some(x => x.textContent === 'Includi')");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.editor button')).find(x => x.textContent === 'Includi').click()");
        await Require("Array.from(document.querySelectorAll('.editor button')).every(x => x.textContent === 'Escludi')");
        await core.ExecuteScriptAsync("document.querySelector('header button').click()");
        await Require("document.querySelector('h1').textContent === 'Importa'");
        await Task.Delay(250);
    }
}
