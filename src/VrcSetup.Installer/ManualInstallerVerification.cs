using System.IO;
using System.Runtime.InteropServices;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Installer;

// Exercises the real manual window without touching the owner's installation or shell folders.
internal static class ManualInstallerVerification
{
    internal static async Task RunAsync(string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        var root = Path.Combine(evidence, "installation"); var home = Path.Combine(evidence, "home");
        var menu = Path.Combine(evidence, "start"); var desktop = Path.Combine(evidence, "desktop");
        if (!File.Exists(Path.Combine(evidence, ".manual-installer-fixture")) ||
            Environment.GetEnvironmentVariable("VRCSETUP_SKIP_PATH_UPDATE") != "1" ||
            Environment.GetEnvironmentVariable("VRCSETUP_START_MENU_ROOT") != menu ||
            Environment.GetEnvironmentVariable("VRCSETUP_DESKTOP_ROOT") != desktop || Directory.Exists(root))
            throw new IOException("Manual installer verification requires a fresh, isolated fixture and shell overrides.");
        Directory.CreateDirectory(home); await File.WriteAllTextAsync(Path.Combine(home, "owner-sentinel.txt"), "preserve");
        var checks = new List<string>();
        void Require(bool condition, string label) { if (!condition) throw new IOException(label); checks.Add(label); }
        var window = new InstallerWindow(root, home);
        window.RenderForVerification(Path.Combine(evidence, "manual-start.png"));
        Require(await window.InstallAsync(), "Manual installation succeeds");
        Require(window.Complete && !Directory.Exists(menu), "Completion waits for Finish before Start shortcuts");
        window.RenderForVerification(Path.Combine(evidence, "manual-complete.png"));
        window.DesktopShortcut = true; window.LaunchApplication = false;
        Require(await window.FinishAsync(close: false), "Finish applies the selected shortcuts");
        var expected = Path.Combine(root, "app", "desktop", "VrcSetup.Desktop.exe");
        var menuFolder = Path.Combine(menu, "VRChat Project Setup");
        Require(File.Exists(Path.Combine(menuFolder, "VRC Project Setup.lnk")), "Start contains the app under its product name");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            foreach (var folder in new[] { menuFolder, desktop })
            {
                dynamic link = shell.CreateShortcut(Path.Combine(folder, "VRC Project Setup.lnk"));
                try { Require(string.Equals((string)link.TargetPath, expected, StringComparison.OrdinalIgnoreCase), "Shortcut targets the installed app: " + Path.GetFileName(folder)); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        var same = new InstallerWindow(root, home); var asked = false;
        Require(!await same.InstallAsync(change => { asked = change == InstallationChange.Reinstall; return false; }) && asked, "Same-version reinstall can be declined");
        var marker = Path.Combine(root, ".vrcsetup-install.json"); var original = await File.ReadAllTextAsync(marker);
        try
        {
            var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(marker) ?? throw new IOException("Missing installation marker");
            await AtomicFile.WriteJsonAsync(marker, record with { Version = "99.0.0" });
            var older = new InstallerWindow(root, home); asked = false;
            Require(!await older.InstallAsync(change => { asked = change == InstallationChange.Downgrade; return false; }) && asked, "Older installer requires explicit downgrade confirmation");
        }
        finally { await File.WriteAllTextAsync(marker, original); }
        await new InstallationManager(new AppPaths(home)).UninstallAsync(root, true);
        Require(!File.Exists(Path.Combine(menuFolder, "VRC Project Setup.lnk")) && !File.Exists(Path.Combine(desktop, "VRC Project Setup.lnk")), "Uninstall removes fixture-owned shortcuts");
        Require(await File.ReadAllTextAsync(Path.Combine(home, "owner-sentinel.txt")) == "preserve", "Installation and uninstall preserve user data");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "manual-installer.json"), new { Passed = true, Checks = checks });
    }
}
