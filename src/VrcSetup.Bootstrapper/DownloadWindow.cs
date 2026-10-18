using System.Runtime.InteropServices;

namespace VrcSetup.Bootstrapper;

// A native progress surface keeps the bootstrapper independent of .NET/WPF
// runtimes. All network, hashing and archive work runs outside its message pump.
internal sealed class DownloadWindow : IDisposable
{
    private readonly WndProc procedure;
    private readonly CancellationTokenSource cancellation = new();
    private readonly nint window, label, bar;
    private volatile string step = Program.Text("Preparing setup…", "Preparazione installazione…");
    private volatile int percent;
    private readonly bool visible;
    private readonly nint background = CreateSolidBrush(0x2D2A28);
    private readonly nint icon, smallIcon;
    public CancellationToken Cancellation => cancellation.Token;
    public DownloadWindow(bool visible)
    {
        this.visible = visible; procedure = Handle;
        _ = ExtractIconExW(Environment.ProcessPath!, 0, out icon, out smallIcon, 1);
        var controls = new CommonControls { Size = 8, Classes = 0x20 }; InitCommonControlsEx(ref controls);
        var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Instance = GetModuleHandleW(null), Name = "VrcSetupDownload", Background = background, Cursor = LoadCursorW(0, 32512), Icon = icon, SmallIcon = smallIcon };
        if (RegisterClassExW(ref cls) == 0) throw new System.ComponentModel.Win32Exception();
        _ = SetProcessDPIAware();
        window = CreateWindowExW(0, cls.Name, "VRC Project Setup", 0x00CA0000, (GetSystemMetrics(0) - 460) / 2, (GetSystemMetrics(1) - 150) / 2, 460, 150, 0, 0, cls.Instance, 0);
        label = CreateWindowExW(0, "STATIC", step, 0x50000000, 22, 22, 408, 24, window, 0, cls.Instance, 0);
        bar = CreateWindowExW(0, "msctls_progress32", "", 0x50000000, 22, 62, 408, 16, window, 0, cls.Instance, 0);
        if (window == 0 || label == 0 || bar == 0) throw new System.ComponentModel.Win32Exception();
        var dark = 1; _ = DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
        SetWindowTheme(bar, "", ""); SendMessageW(bar, 0x409, 0, 0xDCE6A0); SendMessageW(bar, 0x2001, 0, 0x45413E);
        SendMessageW(label, 0x0030, GetStockObject(17), 1);
        if (visible) ShowWindow(window, 5);
    }
    public void Report(int value, string message) { percent = value; step = message; PostMessageW(window, 0x8001, 0, 0); }
    public void Hide() => PostMessageW(window, 0x8002, 0, 0);
    public void Finish() => PostMessageW(window, 0x8003, 0, 0);
    public static void ShowError(string message) => MessageBoxW(0, message, "VRC Project Setup", 0x10);
    public void Failure(string message) { if (visible) ShowError(message); }
    private nint Handle(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case 0x0010: cancellation.Cancel(); return 0;
            case 0x0002: PostQuitMessage(0); return 0;
            case 0x0138: SetTextColor((nint)wParam, 0xF5F5F5); SetBkMode((nint)wParam, 1); return background;
            case 0x8001: SetWindowTextW(label, step); SendMessageW(bar, 0x402, (nint)percent, 0); return 0;
            case 0x8002: ShowWindow(hwnd, 0); return 0;
            case 0x8003: DestroyWindow(hwnd); return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
    public void Pump() { while (GetMessageW(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessageW(ref message); } }
    public void Dispose() { cancellation.Dispose(); DeleteObject(background); if (icon != 0) DestroyIcon(icon); if (smallIcon != 0) DestroyIcon(smallIcon); GC.KeepAlive(procedure); }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu, Name; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct CommonControls { public uint Size, Classes; }
    [DllImport("comctl32")] private static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WindowClass cls);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string? cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32")] private static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32")] private static extern int GetMessageW(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32")] private static extern bool PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32")] private static extern nint SendMessageW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(nint hwnd, string text);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
    [DllImport("user32")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32")] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32")] private static extern void PostQuitMessage(int code);
    [DllImport("user32")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32")] private static extern nint LoadCursorW(nint instance, int id);
    [DllImport("user32")] private static extern bool SetProcessDPIAware();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    [DllImport("gdi32")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
    [DllImport("uxtheme", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint hwnd, string app, string id);
}
