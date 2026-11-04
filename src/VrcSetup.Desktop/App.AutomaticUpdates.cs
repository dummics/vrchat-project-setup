using System.Diagnostics;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop;

public partial class App
{
    private static async Task<bool> StartPreparedUpdateAsync(VrcSetupApplication application)
    {
        if (!application.AllowBackgroundUpdateChecks) return false;
        try
        {
            var automatic = new AutomaticUpdates(application.Paths);
            if (!automatic.Installed) return false;
            var journal = Path.Combine(application.Paths.Home, "imports.json");
            if (File.Exists(journal))
            {
                if (new FileInfo(journal).Length > 32 * 1024 * 1024) return false;
                var imports = await AtomicFile.ReadJsonAsync<ImportJob[]>(journal);
                if (imports?.Any(x => x is null || x.Active) == true) return false;
            }
            // Another window may have unsaved work. Leave the pending update for a later launch.
            var executable = Path.GetFullPath(Environment.ProcessPath!);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try { if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return false; }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
                }
            }
            var config = await application.Configuration.LoadAsync();
            var ready = await automatic.TakeReadyAsync(ApplicationUpdates.CurrentVersion, config.CheckUpdatesOnStartup && config.AutomaticUpdates, config.IncludeBetaUpdates);
            if (ready is null) return false;
            var start = new ProcessStartInfo(ready.Installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            foreach (var value in new[] { "--silent", "--wait-for-pid", Environment.ProcessId.ToString(), "--root", application.Paths.PayloadRoot, "--home", application.Paths.Home, "--result", Path.Combine(application.Paths.Home, "updates", "last-result.json"), "--launch" }) start.ArgumentList.Add(value);
            if (application.UpdateSmokeSpec is { } specPath)
            {
                var spec = await Verification.UpdateSmokeTest.ReadSpecAsync(specPath);
                start.Environment["VRCSETUP_UPDATE_SMOKE_SPEC"] = specPath;
                start.Environment["VRCSETUP_HOME"] = application.Paths.Home;
                start.Environment["VRCSETUP_SKIP_PATH_UPDATE"] = "1";
                start.Environment["VRCSETUP_START_MENU_ROOT"] = Path.Combine(spec.Root, "start-menu");
            }
            using var updater = Process.Start(start) ?? throw new IOException("Could not start the prepared update.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or ArgumentException)
        { application.Diagnostics.Write("Prepared update", error.Message, error); return false; }
    }
}
