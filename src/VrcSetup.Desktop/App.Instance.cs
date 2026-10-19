using System.Diagnostics;
using System.Runtime.InteropServices;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop;

public partial class App
{
    private DesktopInstanceLease? desktopLease;
    private bool AcquireDesktop(VrcSetupApplication application)
    {
        desktopLease = DesktopInstanceLease.TryAcquire(application.Paths.Home, out var owner);
        if (desktopLease is not null) return true;
        try
        {
            using var process = Process.GetProcessById(owner);
            var handle = process.MainWindowHandle;
            if (handle != 0) { ShowWindow(handle, 9); SetForegroundWindow(handle); }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return false;
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
