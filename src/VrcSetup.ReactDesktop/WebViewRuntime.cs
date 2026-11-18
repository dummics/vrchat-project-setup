using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using VrcSetup.Core;

namespace VrcSetup.ReactDesktop;

internal static class WebViewRuntime
{
    internal static string? FindFixed()
    {
        var directory = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "webview2-runtime"));
        if (!directory.Exists) return null;
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            !File.Exists(Path.Combine(directory.FullName, "msedgewebview2.exe")))
            throw new IOException(UiText.Text("The bundled WebView2 runtime is incomplete. Extract the entire archive again."));

        // AppContainer processes require runtime read/execute access on Windows 10.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            GrantWindows10Access(directory);
        return directory.FullName;
    }

    private static void GrantWindows10Access(DirectoryInfo directory)
    {
        const FileSystemRights rights = FileSystemRights.ReadAndExecute;
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = directory.GetAccessControl(AccessControlSections.Access);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>().ToArray();
        var changed = false;
        foreach (var sid in new[] { "S-1-15-2-1", "S-1-15-2-2" })
        {
            var identity = new SecurityIdentifier(sid);
            if (rules.Any(rule => identity.Equals(rule.IdentityReference) && rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & rights) == rights && (rule.InheritanceFlags & inherit) == inherit
                && rule.PropagationFlags == PropagationFlags.None)) continue;
            security.AddAccessRule(new FileSystemAccessRule(identity, rights, inherit, PropagationFlags.None, AccessControlType.Allow));
            changed = true;
        }
        if (changed) directory.SetAccessControl(security);
    }
}
