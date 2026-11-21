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
        var benchmark = args is ["--verify-projects-startup", var metricFolder] ? Path.GetFullPath(metricFolder) : null;
        var unityEditor = args is ["--verify-unity-workflow" or "--verify-pause-import" or "--verify-new-project", _, var editor] ? Path.GetFullPath(editor) : null;
        var verifyNewProject = args is ["--verify-new-project", _, _];
        var launchFixture = args is ["--verify-open-project", var openProjectFixture] ? Path.GetFullPath(openProjectFixture) : null;
        var pauseImport = args is ["--verify-pause-import", _, _];
        var discardImport = args is ["--verify-discard-import", _];
        var relocatedSource = args is ["--verify-relocated-import", _, var relocated] ? Path.GetFullPath(relocated) : null;
        var recoverWithoutJournal = args is ["--verify-resume-from-receipt", _];
        var recoveryFixture = args is ["--verify-resume-import" or "--verify-discard-import" or "--verify-resume-from-receipt", _] || relocatedSource is not null
            ? Path.GetFullPath(args[1]) : null;
        var restoreFixture = args is ["--verify-restore-workflow" or "--verify-import-missing-assets", var restore] ? Path.GetFullPath(restore) : null;
        var verifyMissingAssets = args is ["--verify-import-missing-assets", _];
        var verifyCancelledPackageRecovery = args is ["--verify-cancelled-package-recovery", _];
        var verifyBackup = verifyCancelledPackageRecovery || args is ["--verify-project-backup", _];
        var verifyOpenPackageCancellation = args is ["--verify-open-package-cancellation", _, _];
        var packageFixture = args is ["--verify-vpm-edits" or "--verify-project-backup" or "--verify-cancelled-package-recovery", _] or ["--verify-vpm-edits" or "--verify-open-package-cancellation", _, _]
            ? Path.GetFullPath(args[1]) : null;
        var smoke = args switch
        {
            ["--smoke" or "--verify-batch-packages" or "--verify-batch-package-conflict" or "--verify-package-cancellation", var evidence] => Path.GetFullPath(evidence),
            ["--verify-unity-workflow" or "--verify-pause-import" or "--verify-new-project", var evidence, _] => Path.GetFullPath(evidence),
            _ => null
        };
        var verifyBatchConflict = args is ["--verify-batch-package-conflict", _];
        var verifyPackageCancellation = args is ["--verify-package-cancellation", _];
        var verifyBatchPackages = verifyBatchConflict || verifyPackageCancellation || args is ["--verify-batch-packages", _];
        var verifyPrepared = args is ["--verify-prepared-update", _];
        var verifyFeed = args is ["--verify-feed-update", _];
        var verifyRollback = args is ["--verify-stable-rollback", _];
        var verifyRestarted = args is ["--verify-update-smoke", _];
        var portableFixture = args is ["--verify-portable-startup", var archiveRoot] ? Path.GetFullPath(archiveRoot) : null;
        var verifyMaintenance = args is ["--verify-installation-maintenance", _];
        var verifyVcc = args is ["--verify-hosted-vcc", _];
        var verifyNativePicker = args is ["--verify-hosted-native-picker", _];
        var profileFixture = args is ["--verify-installed-profile" or "--verify-prepared-update" or "--verify-update-smoke" or "--verify-feed-update" or "--verify-stable-rollback" or "--verify-installation-maintenance" or "--verify-hosted-vcc" or "--verify-hosted-native-picker", var fixture]
            ? Path.GetFullPath(fixture) : null;
        if (verifyRestarted && profileFixture is not null && File.Exists(profileFixture))
        {
            using var specification = JsonDocument.Parse(File.ReadAllText(profileFixture));
            var root = ProjectRules.NormalizePath(specification.RootElement.GetProperty("Root").GetString()
                ?? throw new IOException("Update fixture root missing."));
            ProjectRules.EnsureContained(root, profileFixture);
            if (!File.Exists(Path.Combine(root, ".vrcsetup-update-smoke")))
                throw new IOException("Update fixture marker missing.");
            profileFixture = root;
        }
        var offscreen = smoke is not null || profileFixture is not null || restoreFixture is not null || portableFixture is not null ||
            recoveryFixture is not null || packageFixture is not null || launchFixture is not null || benchmark is not null;
        if (benchmark is not null && (!File.Exists(Path.Combine(benchmark, ".startup-fixture")) ||
            !File.Exists(Path.Combine(benchmark, "home", "config.json"))))
        {
            Environment.ExitCode = 2;
            return;
        }
        if (smoke is not null && Directory.Exists(smoke))
        {
            Environment.ExitCode = 2;
            return;
        }
        if (restoreFixture is not null && !File.Exists(Path.Combine(restoreFixture, "react-unity-import.json")) &&
            (!verifyMissingAssets || !File.Exists(Path.Combine(restoreFixture, "resumed-import.json"))))
        {
            Environment.ExitCode = 2;
            return;
        }
        if (recoveryFixture is not null && !File.Exists(Path.Combine(recoveryFixture, "paused-import.json")))
        {
            Environment.ExitCode = 2;
            return;
        }
        if (packageFixture is not null && !File.Exists(Path.Combine(packageFixture, "resumed-import.json")))
        {
            Environment.ExitCode = 2;
            return;
        }
        if (launchFixture is not null && !File.Exists(Path.Combine(launchFixture, "react-new-project.json")))
        {
            Environment.ExitCode = 2;
            return;
        }
        var paths = new AppPaths();
        var distributed = IsDistribution(paths);
        var portable = distributed && File.Exists(Path.Combine(paths.PayloadRoot, ".vrcsetup-portable"));
        if (portableFixture is not null && (!portable ||
            !paths.PayloadRoot.Equals(portableFixture, StringComparison.OrdinalIgnoreCase) ||
            !paths.Home.Equals(Path.Combine(portableFixture, "data"), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(portableFixture, ".react-profile-fixture")) || File.Exists(paths.ConfigFile)))
        {
            Environment.ExitCode = 2;
            return;
        }
        if (profileFixture is not null)
        {
            if ((verifyVcc || verifyNativePicker) && !IsHostedFixture(profileFixture))
            {
                Environment.ExitCode = 2;
                return;
            }
            if (!distributed || !File.Exists(Path.Combine(profileFixture, ".react-profile-fixture")) ||
                !File.Exists(Path.Combine(paths.PayloadRoot, ".vrcsetup-install.json")) || !File.Exists(paths.ConfigFile))
            {
                Environment.ExitCode = 2;
                return;
            }
            ProjectRules.EnsureContained(profileFixture, paths.PayloadRoot);
            ProjectRules.EnsureContained(profileFixture, paths.Home);
        }
        var isolated = smoke is not null || restoreFixture is not null || recoveryFixture is not null || packageFixture is not null ||
            launchFixture is not null || benchmark is not null || !distributed;
        var home = smoke ?? restoreFixture ?? recoveryFixture ?? packageFixture ?? launchFixture
            ?? (benchmark is null ? null : Path.Combine(benchmark, "home")) ?? (isolated
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrcSetup-React-Candidate")
            : paths.Home);
        Directory.CreateDirectory(home);
        using var lease = new FileStream(Path.Combine(home, "react-desktop.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var app = new Application();
        var feedFixture = (verifyFeed || verifyRollback) && profileFixture is not null ? new ReactUpdateVerification(profileFixture, verifyRollback) : null;
        var engine = new VrcSetupApplication(isolated || portable ? home : null, verifyVcc
            ? ["--no-catalog-refresh"] : isolated || offscreen
            ? ["--no-vcc-registration", "--no-catalog-refresh"] : args)
        { UpdateHttpClientFactory = feedFixture is null ? null : feedFixture.CreateClient };
        UiText.LoadPreference(engine.Paths.ConfigFile);
        using var shutdown = new CancellationTokenSource();
        var updates = new DesktopUpdates(engine, !isolated && (!offscreen || verifyPrepared || verifyFeed || verifyRollback));
        var web = new WebView2();
        var window = new Window
        {
            Title = isolated ? "VRC Project Setup • React candidate" : "VRC Project Setup",
            Width = offscreen && benchmark is null ? 720 : 1160, Height = offscreen && benchmark is null ? 620 : 800,
            MinWidth = 520, MinHeight = 420, Content = web
        };
        if (offscreen)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -12000;
            window.Top = -12000;
            window.ShowInTaskbar = false;
        }

        using var bridge = new ImportBridge(window, engine, isolated, updates) { EditorLaunchFixture = launchFixture };
        var closing = false;
        var draining = false;
        window.Closing += async (_, args) =>
        {
            if (closing) return;
            args.Cancel = true;
            if (draining) return;
            if (!offscreen && bridge.IsBusy)
            {
                MessageBox.Show(window, UiText.Text("Wait for the current operation before closing the app."), window.Title);
                return;
            }
            draining = true;
            if (!offscreen && engine.Imports.HasActiveJobs &&
                MessageBox.Show(window, UiText.Text("Pause imports and close the app?"), window.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                draining = false;
                return;
            }
            bridge.Closing = true;
            try
            {
                await Task.Yield(); // Let WPF finish the cancelled close before issuing another.
                while (offscreen && bridge.IsBusy) await Task.Delay(50);
                await engine.Imports.DisposeAsync();
            }
            catch (Exception error) when (DesktopUpdates.IsRecoverable(error))
            {
                engine.Diagnostics.Write("React shutdown", error.Message, error);
                bridge.Closing = false;
                draining = false;
                MessageBox.Show(window, UiText.Text("Could not save the paused imports. Retry before closing."), window.Title);
                return;
            }
            closing = true;
            window.Close();
        };
        window.Closed += (_, _) => { shutdown.Cancel(); web.Dispose(); };
        window.Loaded += async (_, _) =>
        {
            try
            {
                try
                {
                    if (!verifyFeed && !verifyRollback && await updates.StartReadyAsync(manual: false, shutdown.Token))
                    {
                        window.Close();
                        return;
                    }
                }
                catch (Exception error) when (!verifyPrepared && DesktopUpdates.IsRecoverable(error))
                { engine.Diagnostics.Write("React prepared update", error.Message, error); }
                if (verifyPrepared) throw new IOException("The prepared fixture update did not start.");
                if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "web")))
                    throw new IOException(UiText.Text("The web assets for this build are unavailable."));
                var fixedRuntime = WebViewRuntime.FindFixed();
                CoreWebView2Environment environment;
                try
                {
                    environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: fixedRuntime,
                        userDataFolder: Path.Combine(home, "webview"));
                }
                catch (WebView2RuntimeNotFoundException error) when (fixedRuntime is null)
                {
                    throw new IOException(UiText.Text("Install Microsoft Edge WebView2 Runtime or use the offline portable archive."), error);
                }
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
                if (smoke is not null && unityEditor is not null)
                    await bridge.SeedUnityWorkflowAsync(smoke, unityEditor, stageArchive: !verifyNewProject);
                else if (smoke is not null)
                    await bridge.SeedSmokeAsync(smoke);
                var configuration = await engine.Configuration.LoadAsync();
                engine.ConfigureDiagnostics(configuration);
                UiText.Language = configuration.Language;
                if (benchmark is not null)
                    ProjectRules.EnsureContained(benchmark, configuration.UnityProjectsRoot);
                if (profileFixture is not null)
                    ProjectRules.EnsureContained(profileFixture, configuration.UnityProjectsRoot);
                if (verifyVcc && profileFixture is not null)
                    await ReactVccVerification.SeedAsync(engine, profileFixture, configuration, shutdown.Token);
                if (verifyNativePicker && profileFixture is not null)
                    await ReactNativePickerVerification.SeedRecoveryAsync(engine, profileFixture, configuration, shutdown.Token);
                bridge.ConfigureQueue(configuration);
                await engine.Imports.InitializeAsync();
                bridge.Attach(core);
                core.Navigate(ImportBridge.Page);
                if (!offscreen && distributed && Path.GetDirectoryName(home) is { } entrypointFixture &&
                    IsHostedFixture(entrypointFixture) &&
                    paths.PayloadRoot.Equals(Path.Combine(entrypointFixture, "desktop-installation"), StringComparison.OrdinalIgnoreCase) &&
                    home.Equals(Path.Combine(entrypointFixture, "desktop-home"), StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(entrypointFixture, ".react-entrypoint-fixture")))
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true] .drop')?.disabled === false");
                    await AtomicFile.WriteJsonAsync(Path.Combine(entrypointFixture, "entrypoint-ready.json"),
                        new { Process = Environment.ProcessId, Browser = core.BrowserProcessId, Version = ApplicationUpdates.CurrentVersion }, shutdown.Token);
                }
                if (benchmark is not null)
                {
                    await MeasureProjectsStartupAsync(core, environment, benchmark, shutdown.Token);
                    window.Close();
                    return;
                }
                if (profileFixture is null && engine.AllowBackgroundUpdateChecks) _ = updates.RunInBackgroundAsync(shutdown.Token);
                if (portableFixture is not null)
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true] .drop') !== null");
                    var browserExecutable = VerifyFixedBrowser(core, fixedRuntime);
                    await AtomicFile.WriteJsonAsync(Path.Combine(portableFixture, "portable-startup.json"),
                        new
                        {
                            home, browserExecutable, runtime = environment.BrowserVersionString,
                            windowsVersion = Environment.OSVersion.Version.ToString(),
                            freshConfiguration = File.Exists(paths.ConfigFile)
                        });
                    window.Close();
                    return;
                }
                if (restoreFixture is not null)
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true]') !== null");
                    if (verifyMissingAssets)
                        await ReactImportVerification.VerifyMissingAssetsAsync(core, engine, restoreFixture, shutdown.Token);
                    else
                        await ReactImportVerification.VerifyRestoreAsync(core, engine, restoreFixture, shutdown.Token);
                    window.Close();
                    return;
                }
                if (recoveryFixture is not null)
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true]') !== null");
                    if (recoverWithoutJournal)
                        await ReactImportVerification.VerifyResumeFromReceiptAsync(core, engine, recoveryFixture, shutdown.Token);
                    else
                        await ReactImportVerification.VerifyRecoveryAsync(core, engine, recoveryFixture, discardImport, shutdown.Token, relocatedSource);
                    window.Close();
                    return;
                }
                if (packageFixture is not null)
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true]') !== null");
                    if (verifyBackup) await ReactPackageVerification.VerifyBackupAsync(core, engine, packageFixture, shutdown.Token, verifyCancelledPackageRecovery);
                    else await ReactPackageVerification.RunAsync(core, engine, packageFixture,
                            args.Length == 3 ? Path.GetFullPath(args[2]) : null, shutdown.Token, verifyOpenPackageCancellation);
                    window.Close();
                    return;
                }
                if (launchFixture is not null)
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true]') !== null");
                    await ReactNewProjectVerification.VerifyOpenAsync(core, engine, launchFixture, shutdown.Token);
                    window.Close();
                    return;
                }
                if (profileFixture is not null)
                {
                    await RequireAsync(core, "document.querySelector('[data-ready=true]') !== null");
                    if (feedFixture is not null)
                    {
                        if (verifyRollback) await feedFixture.VerifyRollbackAsync(core, engine, shutdown.Token);
                        else await feedFixture.VerifyAsync(core, engine, shutdown.Token);
                        return;
                    }
                    if (verifyMaintenance)
                    {
                        await VerifyInstallationMaintenanceAsync(core, engine, profileFixture);
                        return;
                    }
                    if (verifyVcc)
                    {
                        await ReactVccVerification.RunAsync(core, engine, profileFixture, shutdown.Token);
                        window.Close();
                        return;
                    }
                    if (verifyNativePicker)
                    {
                        await ReactNativePickerVerification.RunAsync(core, window, engine, profileFixture, shutdown.Token);
                        window.Close();
                        return;
                    }
                    await VerifyInstalledProfileAsync(core, profileFixture, home, configuration,
                        verifyRestarted ? "restarted-profile.json" : "installed-profile.json");
                    window.Close();
                    return;
                }
                if (smoke is not null)
                {
                    if (unityEditor is not null)
                    {
                        await RequireAsync(core, "document.querySelector('[data-ready=true]') !== null");
                        if (verifyNewProject) await ReactNewProjectVerification.RunAsync(core, engine, smoke, unityEditor, shutdown.Token);
                        else
                        {
                            await RequireAsync(core, "document.querySelector('.tabs button') !== null");
                            if (pauseImport) await ReactImportVerification.VerifyPauseAsync(core, engine, smoke, shutdown.Token);
                            else await ReactImportVerification.RunAsync(core, engine, smoke, shutdown.Token);
                        }
                        window.Close();
                        return;
                    }
                    for (var i = 0; i < 100; ++i)
                    {
                        await Task.Delay(100);
                        if (await core.ExecuteScriptAsync("document.querySelector('[data-ready=true]') !== null && document.querySelectorAll('.tabs button').length === 6 && document.querySelector('.platforms [aria-pressed=true]').textContent === 'Quest'") != "true") continue;
                        var readyMs = startup.ElapsedMilliseconds;
                        await VerifyDraftActionsAsync(core);
                        using var screenshot = File.Create(Path.Combine(smoke, "import-react-720.png"));
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                        await VerifyProjectListAsync(core, verifyBatchPackages ? smoke : null, verifyBatchConflict,
                            verifyPackageCancellation ? engine : null);
                        if (verifyBatchPackages)
                        {
                            window.Close();
                            return;
                        }
                        await VerifyRecoveryAndActivityAsync(core);
                        var diagnostics = Path.Combine(smoke, "diagnostics.zip");
                        await SupportLogs.ExportAsync(engine.Paths, diagnostics);
                        using var diagnosticZip = System.IO.Compression.ZipFile.OpenRead(diagnostics);
                        if (diagnosticZip.GetEntry("App/operation-smoke-packages.json") is null ||
                            diagnosticZip.GetEntry("App/smoke-vpm.log") is null)
                            throw new IOException("The diagnostic ZIP is missing the package report or its log.");
                        await VerifySettingsAsync(core, smoke);
                        await VerifyPresetsAsync(core);
                        await VerifyLanguageAsync(core);
                        await VerifyMaintenanceAsync(core, engine);
                        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
                        await RequireAsync(core, "document.querySelector('.tabs button') !== null");
                        await ReactImportVerification.VerifyFileDropAsync(core, smoke);
                        await ReactImportVerification.VerifyOrphanReviewAsync(core, engine, smoke, shutdown.Token);
                        string? browserExecutable = null;
                        if (fixedRuntime is not null)
                            browserExecutable = VerifyFixedBrowser(core, fixedRuntime);
                        await File.WriteAllTextAsync(Path.Combine(smoke, "result.json"), JsonSerializer.Serialize(new
                        {
                            passed = true, readyMs, runtime = environment.BrowserVersionString, browserExecutable, drafts = 6, droppedDrafts = 2, target = "Android",
                            checks = new[] { "native handshake", "six analyzed archives", "Quest filename and prefab path detection",
                                "independent tab state", "package panel and exclusion", "project discovery, search and name validation",
                                "compilation recovery action and dismissal", "activity detail, operation log and diagnostics archive",
                                "VPM restore and removal review", "VPM batch review distinguishes changed and unchanged projects",
                                "import preferences persist and hide completed imports", "independent alpha opt-in persists",
                                "color picker opens and closes; theme and custom accent persist", "help and plain version label", "manual file path and project naming settings",
                                "preset create and rename persist", "preset favorites, pinning, discard, save and delete",
                                "live English labels and language persistence", "diagnostics settings and maintenance confirmations",
                                "WebView2 file drop stages two independent archives", "Projects rebuilds an import review from the project receipt" }
                        }));
                        window.Close();
                        return;
                    }
                    throw new IOException("React did not complete the handshake.");
                }
            }
            catch (Exception error)
            {
                if ((smoke ?? profileFixture ?? restoreFixture ?? portableFixture ?? recoveryFixture ?? packageFixture ?? launchFixture ?? benchmark) is { } evidenceFolder)
                    File.WriteAllText(Path.Combine(evidenceFolder, "failure.txt"), error.ToString());
                else MessageBox.Show(window, error.Message, window.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.ExitCode = 1;
                window.Close();
            }
        };
        app.Run(window);
    }

    private static async Task MeasureProjectsStartupAsync(CoreWebView2 core, CoreWebView2Environment environment, string folder, CancellationToken token)
    {
        const string projects = "Array.from(document.querySelectorAll('.nav-item')).find(button => button.getAttribute('aria-label') === 'Progetti')";
        await RequireAsync(core, projects + "?.disabled === false");
        await core.ExecuteScriptAsync(projects + ".click()");
        await RequireAsync(core, "document.querySelectorAll('.project-table tbody tr').length === 1");
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var readyMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        await Task.Delay(500, token);
        process.Refresh();
        var privateBytes = process.PrivateMemorySize64;
        var browserProcesses = environment.GetProcessInfos().Select(info => info.ProcessId).Distinct().ToArray();
        foreach (var id in browserProcesses)
        {
            using var browser = System.Diagnostics.Process.GetProcessById(id);
            privateBytes += browser.PrivateMemorySize64;
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "startup.json"),
            new { readyMs, privateBytes, processes = 1 + browserProcesses.Length }, token);
    }

    private static bool IsDistribution(AppPaths paths)
    {
        var desktop = Path.Combine(paths.PayloadRoot, "app", "desktop");
        return string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), desktop, StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(Path.Combine(paths.PayloadRoot, ".vrcsetup-install.json")) ||
             File.Exists(Path.Combine(paths.PayloadRoot, ".vrcsetup-portable")));
    }

    private static bool IsHostedFixture(string folder)
    {
        var temp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        return Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted" &&
            !string.IsNullOrWhiteSpace(temp) &&
            string.Equals(Path.GetDirectoryName(folder), Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase);
    }

    private static string VerifyFixedBrowser(CoreWebView2 core, string? runtime)
    {
        if (runtime is null) throw new IOException("The offline archive has no bundled runtime.");
        using var process = System.Diagnostics.Process.GetProcessById((int)core.BrowserProcessId);
        var executable = process.MainModule?.FileName;
        if (!string.Equals(executable, Path.Combine(runtime, "msedgewebview2.exe"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The offline preview did not start the bundled WebView2 executable.");
        return executable!;
    }

    private static async Task VerifyInstalledProfileAsync(CoreWebView2 core, string folder, string home,
        AppConfiguration configuration, string reportName)
    {
        async Task<string[]> Read(string page, string selector)
        {
            var label = JsonSerializer.Serialize(page);
            await core.ExecuteScriptAsync($"Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === {label}).click()");
            await RequireAsync(core, $"document.querySelector('h1').textContent === {label}");
            var result = await core.ExecuteScriptAsync($"Array.from(document.querySelectorAll({JsonSerializer.Serialize(selector)}), x => x.textContent.trim())");
            await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
            await RequireAsync(core, "document.querySelector('main[data-screen=import]') !== null");
            return JsonSerializer.Deserialize<string[]>(result) ?? [];
        }

        var projects = await Read(UiText.Text("Projects"), ".project-table tbody th");
        var presets = await Read(UiText.Text("Packages"), ".view select option");
        var activity = await Read(UiText.Text("Activity"), ".activity article strong");
        await core.ExecuteScriptAsync($"Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === {JsonSerializer.Serialize(UiText.Text("Settings"))}).click()");
        await RequireAsync(core, $"document.querySelector('#unityProjectsRoot')?.value === {JsonSerializer.Serialize(configuration.UnityProjectsRoot)}");
        await RequireAsync(core, $"document.querySelector('#appearance-theme')?.value === {JsonSerializer.Serialize(configuration.Theme)}");
        await RequireAsync(core, $"document.querySelector('#appearance-palette')?.value === {JsonSerializer.Serialize(configuration.ColorPalette)}");
        await RequireAsync(core, $"document.documentElement.lang === {JsonSerializer.Serialize(UiText.CurrentLanguage)}");
        var rendered = await core.ExecuteScriptAsync("""
            ({
              theme: document.documentElement.dataset.theme,
              accent: document.documentElement.style.getPropertyValue('--accent'),
              boundary: document.documentElement.style.getPropertyValue('--line')
            })
            """);
        await File.WriteAllTextAsync(Path.Combine(folder, reportName), JsonSerializer.Serialize(new
        {
            home, version = ApplicationUpdates.CurrentVersion, process = Environment.ProcessId,
            configuration.UnityProjectsRoot, configuration.IncludeAlphaUpdates, projects, presets, activity,
            workspaceSettingsVisible = true, language = UiText.CurrentLanguage, appearance = JsonSerializer.Deserialize<JsonElement>(rendered)
        }));
    }

    private static async Task VerifyProjectListAsync(CoreWebView2 core, string? batchEvidence, bool simulateConflict, VrcSetupApplication? cancelEngine)
    {
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Progetti').click()");
        await RequireAsync(core, "document.querySelector('.project-table tbody')?.textContent.includes('Avatar di prova')");
        await core.ExecuteScriptAsync("""
            const search = document.querySelector('.project-tools input');
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(search, 'no-match');
            search.dispatchEvent(new Event('input', { bubbles: true }));
            """);
        await RequireAsync(core, "document.querySelector('.view').textContent.includes('Nessun progetto corrispondente')");
        await core.ExecuteScriptAsync("""
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(search, '');
            search.dispatchEvent(new Event('input', { bubbles: true }));
            """);
        await RequireAsync(core, "document.querySelectorAll('.project-select input').length === 2");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.project-select input')).forEach(x => x.click())");
        await RequireAsync(core, "document.querySelector('.batch-actions button')?.textContent.includes('2 selezionati')");
        await core.ExecuteScriptAsync("document.querySelector('.batch-actions button').click()");
        await RequireAsync(core, "document.querySelectorAll('.batch-targets li').length === 2");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.project-table button')).find(x => x.textContent === 'Rimuovi').click()");
        await RequireAsync(core, "document.querySelector('.view .extras')?.textContent.includes('example.fixture · Da rimuovere')");
        if (!simulateConflict)
        {
            await core.ExecuteScriptAsync("""
            document.querySelector('.batch-package-targets summary').click();
            Array.from(document.querySelectorAll('.batch-package-targets label')).find(x => x.textContent.includes('Secondo avatar'))
              .querySelector('input').click();
            """);
            await RequireAsync(core, "document.querySelector('.batch-package-targets summary')?.textContent.includes('1 / 2')");
        }
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.view button')).find(x => x.textContent === 'Rivedi modifiche').click()");
        var expectedSecondReview = JsonSerializer.Serialize(simulateConflict ? "example.fixture · Da rimuovere" : "Nessuna modifica.");
        await RequireAsync(core, $$"""
            (() => {
              const rows = Array.from(document.querySelectorAll('[aria-label="Riepilogo pacchetti"] h3'));
              return rows.some(x => x.textContent === 'Avatar di prova' && x.parentElement.textContent.includes('example.fixture · Da rimuovere'))
                && rows.some(x => x.textContent === 'Secondo avatar' && x.parentElement.textContent.includes({{expectedSecondReview}}));
            })()
            """);
        if (batchEvidence is not null)
        {
            var workspace = Path.Combine(batchEvidence, "candidate-projects");
            var changed = Path.Combine(workspace, "Avatar di prova", "Packages", "vpm-manifest.json");
            var unchanged = Path.Combine(workspace, "Secondo avatar", "Packages", "vpm-manifest.json");
            var unchangedBefore = await File.ReadAllTextAsync(unchanged);
            var changedBefore = await File.ReadAllTextAsync(changed);
            if (simulateConflict)
            {
                unchangedBefore += Environment.NewLine;
                await File.WriteAllTextAsync(unchanged, unchangedBefore);
            }
            using (var preview = File.Create(Path.Combine(batchEvidence, "batch-review-720.png")))
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
            if (cancelEngine is not null)
            {
                using var lease = await cancelEngine.Projects.AcquireAsync(Path.Combine(workspace, "Avatar di prova"), CancellationToken.None);
                await core.ExecuteScriptAsync("document.querySelector('.view .extras button.primary').click()");
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!Directory.EnumerateFiles(cancelEngine.Paths.Logs, "vrcsetup-*.log").Any())
                    await Task.Delay(25, wait.Token);
                await core.ExecuteScriptAsync("""document.querySelector('[data-package-action="cancel"]').click()""");
                await RequireAsync(core, """
                    document.querySelector('[data-nav=import]')?.disabled === false &&
                    [...document.querySelectorAll('[aria-live="polite"] .package')].some(row =>
                      row.textContent.includes('Avatar di prova') && row.textContent.includes('Annullato')
                      && row.textContent.includes('Il progetto è conservato.'))
                    """);
                if (await File.ReadAllTextAsync(changed) != changedBefore || await File.ReadAllTextAsync(unchanged) != unchangedBefore)
                    throw new IOException("Cancelling package changes modified a project manifest.");
                await AtomicFile.WriteJsonAsync(Path.Combine(batchEvidence, "package-cancellation.json"),
                    new { Passed = true, BothManifestsPreserved = true, CancelledThroughReact = true });
                return;
            }
            await core.ExecuteScriptAsync("document.querySelector('.view .extras button.primary').click()");
            var expectedSecondResult = JsonSerializer.Serialize(simulateConflict ? "Il progetto è cambiato" : "Nessuna modifica.");
            await RequireAsync(core, $$"""
                (() => {
                  const rows = Array.from(document.querySelectorAll('[aria-live="polite"] .package'));
                  return !document.querySelector('[data-nav=import]').disabled
                    && rows.some(x => x.textContent.includes('Avatar di prova') && x.textContent.includes('Completato'))
                    && rows.some(x => x.textContent.includes('Secondo avatar') && x.textContent.includes({{expectedSecondResult}}));
                })()
                """, attempts: 360);
            if ((await File.ReadAllTextAsync(changed)).Contains("example.fixture", StringComparison.Ordinal) ||
                unchangedBefore != await File.ReadAllTextAsync(unchanged))
                throw new IOException("Batch apply did not update only the intended manifest.");
            if (simulateConflict)
            {
                await core.ExecuteScriptAsync("document.querySelector('[data-nav=projects]').click()");
                await RequireAsync(core, "document.querySelector('h1').textContent === 'Progetti'");
                await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
                await RequireAsync(core, "document.querySelector('main[data-screen=import]') !== null");
                await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Attività').click()");
                await RequireAsync(core, "document.querySelector('.activity article')?.textContent.includes('Completata in parte')");
                await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.activity article button')).find(x => x.textContent === 'Dettagli').click()");
                await RequireAsync(core, """
                    (() => {
                      const rows = Array.from(document.querySelectorAll('.activity-detail'));
                      return rows.some(x => x.textContent.includes('Avatar di prova') && x.textContent.includes('Completata'))
                        && rows.some(x => x.textContent.includes('Secondo avatar') && x.textContent.includes('Saltato'));
                    })()
                    """);
            }
            await AtomicFile.WriteJsonAsync(Path.Combine(batchEvidence, "batch-packages.json"),
                new { Passed = true, Changed = "Avatar di prova", Unchanged = "Secondo avatar", PerPackageSelection = !simulateConflict,
                    StaleProjectSkipped = simulateConflict, PartialActivityVerified = simulateConflict,
                    ReviewedAndAppliedThroughReact = true });
            return;
        }
        await core.ExecuteScriptAsync("document.querySelector('.page-back').click()");
        await RequireAsync(core, "document.querySelector('.batch-actions') !== null");
        await core.ExecuteScriptAsync("""
            Array.from(document.querySelectorAll('.batch-actions button')).find(x => x.textContent === 'Deseleziona').click();
            Array.from(document.querySelectorAll('.project-table tbody tr')).find(x => x.textContent.includes('Avatar di prova'))
              .querySelector('.project-actions .icon-button').click();
            """);
        await RequireAsync(core, "document.querySelector('.view').textContent.includes('Mancante')");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.view button')).find(x => x.textContent === 'Ripristina pacchetti').click()");
        await RequireAsync(core, "document.querySelector('.view .extras')?.textContent.includes('1.0.0')");
        await core.ExecuteScriptAsync("document.querySelector('.view .extras button').click()");
        await RequireAsync(core, "document.querySelectorAll('.project-table tbody tr').length === 3");
        await RequireAsync(core, "Array.from(document.querySelectorAll('.project-table tbody tr')).filter(x => x.textContent.includes('Richiesto')).length === 2");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.project-table button')).find(x => x.textContent === 'Rimuovi').click()");
        await RequireAsync(core, "document.querySelector('.view .extras')?.textContent.includes('example.fixture · Da rimuovere')");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.view button')).find(x => x.textContent === 'Rivedi modifiche').click()");
        await RequireAsync(core, "document.querySelector('[aria-label=\"Riepilogo pacchetti\"]')?.textContent.includes('example.fixture · Da rimuovere')");
        await core.ExecuteScriptAsync("document.querySelector('.page-back').click()");
        await RequireAsync(core, "document.querySelector('h1').textContent === 'Progetti'");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.project-header-actions button')).find(x => x.textContent === 'Nuovo progetto').click()");
        await RequireAsync(core, """document.querySelector('input[aria-label="Nome nuovo progetto"]') !== null""");
        await core.ExecuteScriptAsync("""
            (() => {
              const input = document.querySelector('input[aria-label="Nome nuovo progetto"]');
              Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '..');
              input.dispatchEvent(new Event('input', { bubbles: true }));
            })()
            """);
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.extras button')).find(x => x.textContent === 'Rivedi progetto').click()");
        await RequireAsync(core, "document.querySelector('footer [role=status]').textContent.length > 0 && !document.querySelector('.extras button.primary').disabled");
        await RequireAsync(core, "document.querySelector('.extras button.primary').textContent === 'Rivedi progetto'");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.extras button')).find(x => x.textContent === 'Annulla').click()");
    }

    private static async Task VerifyRecoveryAndActivityAsync(CoreWebView2 core)
    {
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await RequireAsync(core, """
            (() => {
              const card = document.querySelector('#job-smoke-compilation');
              return card?.textContent.includes('Apri in Unity') && !card.textContent.includes('Riprova');
            })()
            """);
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('#job-smoke-compilation button')).find(x => x.textContent === 'Nascondi avviso').click()");
        await RequireAsync(core, "!document.querySelector('#job-smoke-compilation').textContent.includes('Nascondi avviso')");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Attività').click()");
        await RequireAsync(core, "document.querySelector('.activity')?.textContent.includes('Avatar di prova')");
        await core.ExecuteScriptAsync("""
            const importRow = Array.from(document.querySelectorAll('.activity article')).find(x => x.textContent.includes('Avatar di prova'));
            Array.from(importRow.querySelectorAll('button')).find(x => x.textContent === 'Apri log').click();
            """);
        await RequireAsync(core, "document.querySelector('.activity pre')?.textContent.includes('Script compilation failed in test fixture')");
        await core.ExecuteScriptAsync("""
            const operationRow = Array.from(document.querySelectorAll('.activity article')).find(x => x.textContent.includes('VPM di prova'));
            Array.from(operationRow.querySelectorAll('button')).find(x => x.textContent === 'Dettagli').click();
            """);
        await RequireAsync(core, "document.querySelector('.activity-detail li')?.textContent.includes('2.0.0')");
        await core.ExecuteScriptAsync("Array.from(operationRow.querySelectorAll('button')).find(x => x.textContent === 'Apri log').click()");
        await RequireAsync(core, "document.querySelector('.activity pre')?.textContent.includes('Fixture package operation completed')");
    }

    private static async Task VerifySettingsAsync(CoreWebView2 core, string folder)
    {
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await RequireAsync(core, "document.getElementById('job-smoke-completed') !== null");
        const string settings = "Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Impostazioni').click()";
        await core.ExecuteScriptAsync(settings);
        await RequireAsync(core, "document.querySelector('#hide-completed')?.checked === false");
        await core.ExecuteScriptAsync("""
            document.querySelector('#hide-completed').click();
            document.querySelector('#manual-import-path').click();
            Array.from(document.querySelectorAll('.settings label')).find(x => x.textContent.includes('Diagnostica dettagliata')).querySelector('input').click();
            Array.from(document.querySelectorAll('.settings label')).find(x => x.textContent.includes('alpha sperimentali')).querySelector('input').click();
            const limit = document.querySelector('#import-limit');
            limit.value = '6';
            limit.dispatchEvent(new Event('change', { bubbles: true }));
            """);
        await core.ExecuteScriptAsync("""
            const theme = document.querySelector('#appearance-theme');
            theme.value = 'Light';
            theme.dispatchEvent(new Event('change', { bubbles: true }));
            """);
        await RequireAsync(core, "document.documentElement.dataset.theme === 'light'");
        await core.ExecuteScriptAsync("""
            const palette = document.querySelector('#appearance-palette');
            palette.value = 'Ocean';
            palette.dispatchEvent(new Event('change', { bubbles: true }));
            """);
        await RequireAsync(core, "document.querySelector('#accentColor').value === '#70b9f2'");
        await VerifyColorPickerAsync(core);
        await core.ExecuteScriptAsync("""
            const accent = document.querySelector('#accentColor');
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(accent, '#68cab6');
            accent.dispatchEvent(new Event('input', { bubbles: true }));
            accent.dispatchEvent(new Event('change', { bubbles: true }));
            """);
        await RequireAsync(core, "document.querySelector('#appearance-palette').value === 'Custom'");
        await core.ExecuteScriptAsync("""
            document.querySelector('.settings .naming summary').click();
            const prefix = document.querySelector('.naming-fields input');
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(prefix, 'Importato ');
            prefix.dispatchEvent(new Event('input', { bubbles: true }));
            const patterns = document.querySelector('.settings .naming textarea');
            Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(patterns, '-[0-9]+$');
            patterns.dispatchEvent(new Event('input', { bubbles: true }));
            """);
        await core.ExecuteScriptAsync("document.querySelector('.settings button[type=submit]').click()");
        await RequireAsync(core, "document.querySelector('footer [role=status]').textContent === 'Impostazioni salvate.'");
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await RequireAsync(core, "document.getElementById('job-smoke-completed') === null");
        await RequireAsync(core, "document.documentElement.style.getPropertyValue('--accent') === 'rgb(104, 202, 182)'");
        var manualArchive = Path.Combine(folder, "Manuale-123.unitypackage");
        File.Copy(Path.Combine(folder, "avatarv2.unitypackage"), manualArchive);
        await core.ExecuteScriptAsync($$"""
            const manualPath = document.querySelector('.manual-path input');
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(manualPath, {{JsonSerializer.Serialize(manualArchive)}});
            manualPath.dispatchEvent(new Event('input', { bubbles: true }));
            """);
        await RequireAsync(core, "document.querySelector('.manual-path button').disabled === false");
        await core.ExecuteScriptAsync("document.querySelector('.manual-path button').click()");
        await RequireAsync(core, """document.querySelector('input[aria-label="Nome progetto"]')?.value === 'Importato Manuale'""");
        await core.ExecuteScriptAsync(settings);
        await RequireAsync(core, "document.querySelector('#hide-completed')?.checked && document.querySelector('#import-limit').value === '6'");
        await RequireAsync(core, "document.querySelector('#appearance-theme').value === 'Light' && document.querySelector('#accentColor').value === '#68cab6'");
        await RequireAsync(core, "document.querySelector('#manual-import-path').checked && document.querySelector('.naming-fields input').value === 'Importato '");
        await RequireAsync(core, "Array.from(document.querySelectorAll('.settings label')).find(x => x.textContent.includes('Diagnostica dettagliata')).querySelector('input').checked");
        await RequireAsync(core, "document.querySelector('.app-version')?.textContent.trim().length > 0");
        await core.ExecuteScriptAsync("document.querySelector('.help summary').click()");
        await RequireAsync(core, "document.querySelector('.help details[open] p')?.textContent.includes('UnityPackage')");
        await RequireAsync(core, """
            Array.from(document.querySelectorAll('.settings label')).find(x => x.textContent.includes('alpha sperimentali')).querySelector('input').checked &&
              !Array.from(document.querySelectorAll('.settings label')).find(x => x.textContent.includes('versioni beta')).querySelector('input').checked
            """);
    }

    private static async Task VerifyColorPickerAsync(CoreWebView2 core)
    {
        var position = JsonSerializer.Deserialize<double[]>(await core.ExecuteScriptAsync("""
            (() => {
              const input = document.querySelector('#accentColor');
              input.scrollIntoView({ block: 'center' });
              const box = input.getBoundingClientRect();
              return [box.x + box.width / 2, box.y + box.height / 2];
            })()
            """)) ?? throw new IOException("The accent color control is not visible.");
        await ClickAsync(position[0], position[1]);
        await RequireAsync(core, "document.querySelector('#accentColor:open') !== null");
        await ClickAsync(2, 2);
        await RequireAsync(core, "document.querySelector('#accentColor:open') === null && document.querySelector('#accentColor').value === '#70b9f2'");

        async Task ClickAsync(double x, double y)
        {
            foreach (var type in new[] { "mousePressed", "mouseReleased" })
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
        }
    }

    private static async Task VerifyPresetsAsync(CoreWebView2 core)
    {
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        const string open = "document.querySelector('[data-nav=presets]').click()";
        await core.ExecuteScriptAsync(open);
        await RequireAsync(core, "document.querySelector('h1').textContent === 'Pacchetti'");
        await Click("Nuovo preset");
        await EnterName("Preset fixture");
        await RequireAsync(core, "document.querySelector('.view select').value === 'Preset fixture'");
        await Click("Rinomina");
        await EnterName("Preset rinominato");
        await RequireAsync(core, "document.querySelector('.view select').value === 'Preset rinominato'");
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await core.ExecuteScriptAsync(open);
        await RequireAsync(core, "Array.from(document.querySelectorAll('.view select option')).some(x => x.value === 'Preset rinominato')");
        await core.ExecuteScriptAsync("""
            const preset = document.querySelector('.view select');
            preset.value = 'Preset rinominato';
            preset.dispatchEvent(new Event('change', { bubbles: true }));
            """);
        const string item = """document.querySelector('[data-package-id="example.preset-tool"]')""";
        await RequireAsync(core, item + " !== null");
        await core.ExecuteScriptAsync(item + ".querySelector('[aria-pressed=false]').click()");
        await RequireAsync(core, item + "?.querySelector('[aria-pressed=true]') != null");

        await ChooseFixtureVersion();
        await RequireAsync(core, "document.querySelector('[data-nav=import]').disabled");
        await Click("Scarta modifiche");
        await RequireAsync(core, "document.querySelector('[data-nav=import]').disabled === false && " +
            item + "?.querySelector('[aria-pressed=true]') != null");
        await ChooseFixtureVersion();
        await Click("Salva preset");
        await RequireAsync(core, "document.querySelector('[data-nav=import]').disabled === false");
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await core.ExecuteScriptAsync(open);
        await RequireAsync(core, "Array.from(document.querySelectorAll('.view select option')).some(x => x.value === 'Preset rinominato')");
        await core.ExecuteScriptAsync("""
            document.querySelector('.view select').value = 'Preset rinominato';
            document.querySelector('.view select').dispatchEvent(new Event('change', { bubbles: true }));
            """);
        await RequireAsync(core, item + "?.textContent.includes('1.0.0') === true");
        await Click("Elimina");
        await Click("Elimina preset");
        await RequireAsync(core, "Array.from(document.querySelectorAll('.view select option')).every(x => x.value !== 'Preset rinominato')");

        async Task Click(string label)
        {
            var button = $"Array.from(document.querySelectorAll('.view button')).find(x => x.textContent === {JsonSerializer.Serialize(label)})";
            await RequireAsync(core, button + "?.disabled === false", attempts: 100);
            await core.ExecuteScriptAsync(button + ".click()");
        }
        async Task ChooseFixtureVersion()
        {
            await core.ExecuteScriptAsync(item + ".querySelector('button:last-child').click()");
            const string picker = """document.querySelector('select[aria-label="Versione preset"]')""";
            await RequireAsync(core, $"Array.from({picker}?.options ?? []).some(x => x.value === '1.0.0')", attempts: 200);
            await core.ExecuteScriptAsync(picker + ".value = '1.0.0'; " + picker + ".dispatchEvent(new Event('change', { bubbles: true }))");
            await Click("Usa versione");
            await RequireAsync(core, item + "?.textContent.includes('1.0.0') === true");
        }
        async Task EnterName(string name)
        {
            await RequireAsync(core, """document.querySelector('input[aria-label="Nome preset"]') !== null""");
            await core.ExecuteScriptAsync($$"""
                (() => {
                  const input = document.querySelector('input[aria-label="Nome preset"]');
                  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, {{JsonSerializer.Serialize(name)}});
                  input.dispatchEvent(new Event('input', { bubbles: true }));
                })()
                """);
            await Click("Conferma nome");
        }
    }

    private static async Task VerifyLanguageAsync(CoreWebView2 core)
    {
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Impostazioni').click()");
        await RequireAsync(core, "document.querySelector('#language')?.value === 'it'");
        await core.ExecuteScriptAsync("""
            const language = document.querySelector('#language');
            language.value = 'en';
            language.dispatchEvent(new Event('change', { bubbles: true }));
            """);
        await core.ExecuteScriptAsync("document.querySelector('.settings button[type=submit]').click()");
        await RequireAsync(core, "document.documentElement.lang === 'en' && document.querySelector('h1').textContent === 'Settings'");
        await RequireAsync(core, "document.querySelector('.help summary').textContent === 'How do I import an avatar?'");
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await RequireAsync(core, "document.querySelector('h1').textContent === 'Import'");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Activity').click()");
        await RequireAsync(core, "document.querySelector('.activity article button').textContent === 'Details'");
        await core.ExecuteScriptAsync("document.querySelector('[data-nav=import]').click()");
        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Settings').click()");
        await RequireAsync(core, "document.querySelector('#language')?.value === 'en'");
    }

    private static async Task VerifyInstallationMaintenanceAsync(CoreWebView2 core, VrcSetupApplication engine, string folder)
    {
        var menu = Path.Combine(folder, "start-menu");
        var desktop = Path.Combine(folder, "desktop");
        if (Environment.GetEnvironmentVariable("VRCSETUP_SKIP_PATH_UPDATE") != "1" ||
            Environment.GetEnvironmentVariable("VRCSETUP_START_MENU_ROOT") != menu ||
            Environment.GetEnvironmentVariable("VRCSETUP_DESKTOP_ROOT") != desktop ||
            !string.Equals(Path.GetDirectoryName(engine.Paths.PayloadRoot), folder, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Installation maintenance requires an isolated install and shortcut roots.");
        var shortcut = Path.Combine(menu, "VRChat Project Setup", "VRC Project Setup.lnk");
        if (File.Exists(shortcut)) throw new IOException("Remove the isolated shortcut before verifying repair.");
        var label = JsonSerializer.Serialize(UiText.Text("Settings"));
        await core.ExecuteScriptAsync($"Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === {label}).click()");
        const string menuSelector = "document.querySelector('.settings-maintenance summary')";
        const string repair = """document.querySelector('[data-maintenance-action="repairInstallation"]')""";
        const string uninstall = """document.querySelector('[data-maintenance-action="uninstallInstallation"]')""";
        await RequireAsync(core, repair + "?.disabled === false");
        await core.ExecuteScriptAsync(menuSelector + ".click()");
        await core.ExecuteScriptAsync(repair + ".click()");
        await RequireAsync(core, repair + "?.disabled === false && document.querySelector('footer [role=status]').textContent.length > 0");
        if (!File.Exists(shortcut)) throw new IOException("Repair did not recreate the shortcut.");
        await core.ExecuteScriptAsync(uninstall + ".click()");
        await RequireAsync(core, "document.querySelector('dialog[open]') !== null");
        await core.ExecuteScriptAsync("document.querySelector('dialog button').click()");
        if (!File.Exists(shortcut)) throw new IOException("Cancelling uninstall modified the shortcuts.");
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "maintenance-handoff.json"),
            new { ShortcutRepaired = true, CancelledUninstallPreservedShortcut = true, ConfigHash = AtomicFile.Hash(engine.Paths.ConfigFile) });
        await core.ExecuteScriptAsync(uninstall + ".click()");
        await core.ExecuteScriptAsync("document.querySelector('dialog .primary').click()");
    }

    private static async Task VerifyMaintenanceAsync(CoreWebView2 core, VrcSetupApplication engine)
    {
        var cached = Directory.GetFiles(engine.Paths.Cache, "archive-*.json", SearchOption.AllDirectories);
        if (cached.Length == 0) throw new IOException("Missing archive cache for maintenance verification.");
        var configHash = AtomicFile.Hash(engine.Paths.ConfigFile);
        var project = Path.Combine(engine.Paths.Home, "candidate-projects", "Avatar di prova", "Packages", "vpm-manifest.json");
        var projectHash = AtomicFile.Hash(project);
        var archive = Path.Combine(engine.Paths.Home, "Manuale-123.unitypackage");
        const string clear = "Array.from(document.querySelectorAll('.settings-maintenance button')).find(x => x.textContent === 'Clear analysis cache').click()";
        await core.ExecuteScriptAsync("document.querySelector('.settings-maintenance summary').click()");
        await core.ExecuteScriptAsync(clear);
        await RequireAsync(core, "document.querySelector('dialog[open]') !== null");
        await core.ExecuteScriptAsync("document.querySelector('dialog button').click()");
        if (cached.Any(file => !File.Exists(file))) throw new IOException("Cancelling maintenance removed a cache entry.");
        await core.ExecuteScriptAsync(clear);
        await core.ExecuteScriptAsync("document.querySelector('dialog .primary').click()");
        await RequireAsync(core, "document.querySelector('footer [role=status]').textContent === 'Analysis cache cleared.'");
        if (cached.Any(File.Exists) || AtomicFile.Hash(engine.Paths.ConfigFile) != configHash)
            throw new IOException("Cache maintenance did not preserve the configuration or clear the cached observations.");

        await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.settings-maintenance button')).find(x => x.textContent === 'Reset settings').click()");
        await RequireAsync(core, "document.querySelector('dialog[open]') !== null");
        await core.ExecuteScriptAsync("document.querySelector('dialog .primary').click()");
        await RequireAsync(core, "document.querySelector('#manual-import-path')?.checked === false");
        if (AtomicFile.Hash(project) != projectHash || !File.Exists(archive) ||
            !Directory.GetFiles(Path.Combine(engine.Paths.Home, "migration"), "before-reset-*.json").Any(file => AtomicFile.Hash(file) == configHash))
            throw new IOException("Reset did not preserve the Unity fixture, source and previous configuration.");
    }

    internal static async Task RequireAsync(CoreWebView2 core, string condition, int attempts = 30)
    {
        for (var attempt = 0; attempt < attempts; ++attempt)
        {
            if (await core.ExecuteScriptAsync(condition) == "true") return;
            await Task.Delay(50);
        }
        var status = await core.ExecuteScriptAsync("document.querySelector('footer [role=status]')?.textContent");
        throw new IOException($"React smoke assertion failed: {condition}. Status: {status}");
    }

    private static async Task VerifyDraftActionsAsync(CoreWebView2 core)
    {
        Task Require(string condition) => RequireAsync(core, condition);

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
        await core.ExecuteScriptAsync("document.querySelector('.page-back').click()");
        await Require("document.querySelector('h1').textContent === 'Importa'");
        await Task.Delay(250);
    }
}
