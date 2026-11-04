using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using VrcSetup.Core;

namespace VrcSetup.Bootstrapper;

// One native window owns initial choices, all measured phases and completion.
// The installation engine runs headless; its runtime is downloaded as needed.
internal sealed partial class DownloadWindow : IDisposable
{
    private readonly WndProc procedure;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentQueue<Action> actions = new();
    private readonly nint window, heading, label, counter, bar, target, targetLabel, primary, cancel, start, desktop, launch, details;
    private readonly nint background = CreateSolidBrush(0x2A2725);
    private readonly nint icon, smallIcon, font, titleFont, phaseFont;
    private readonly bool visible;
    private readonly double scale;
    private TaskCompletionSource<string>? targetChoice;
    private TaskCompletionSource<SetupMessage>? finishChoice;
    private TaskCompletionSource? failureClosed;
    private volatile SetupProgress current = new("prepare", 0, 0);
    private string? logFile;
    private int state = 1, progressPending;
    private bool canCancel = true;
    public CancellationToken Cancellation => cancellation.Token;
    private int S(double value) => (int)Math.Round(value * scale);
    private static string T(string en, string it) => Program.Text(en, it);
    public DownloadWindow(bool visible, bool updating = false)
    {
        this.visible = visible; procedure = Handle;
        _ = SetProcessDPIAware(); scale = Math.Max(1, GetDpiForSystem() / 96d);
        _ = ExtractIconExW(Environment.ProcessPath!, 0, out icon, out smallIcon, 1);
        var controls = new CommonControls { Size = 8, Classes = 0x20 }; InitCommonControlsEx(ref controls);
        var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Instance = GetModuleHandleW(null), Name = "VrcSetupDownload", Background = background, Cursor = LoadCursorW(0, 32512), Icon = icon, SmallIcon = smallIcon };
        if (RegisterClassExW(ref cls) == 0) throw new System.ComponentModel.Win32Exception();
        window = CreateWindowExW(0, cls.Name, "VRC Project Setup", 0x00CA0000,
            (GetSystemMetrics(0) - S(560)) / 2, (GetSystemMetrics(1) - S(260)) / 2, S(560), S(260), 0, 0, cls.Instance, 0);
        font = CreateFontW(-S(16), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        titleFont = CreateFontW(-S(22), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        phaseFont = CreateFontW(-S(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        nint Control(string type, string text, uint style, int x, int y, int width, int height, int id = 0)
        {
            var control = CreateWindowExW(0, type, text, 0x50000000 | style, S(x), S(y), S(width), S(height), window, id, cls.Instance, 0);
            if (control == 0) throw new System.ComponentModel.Win32Exception();
            SendMessageW(control, 0x30, font, 1);
            if (type == "BUTTON" || type == "EDIT") SetWindowTheme(control, "DarkMode_Explorer", "");
            return control;
        }
        heading = Control("STATIC", updating ? T("Updating", "Aggiornamento") : T("Installation", "Installazione"), 0, 24, 18, 504, 30);
        SendMessageW(heading, 0x30, titleFont, 1);
        label = Control("STATIC", T("Preparing…", "Preparazione…"), 0, 24, 60, 504, 22);
        bar = Control("msctls_progress32", "", 8, 24, 88, 504, 24);
        counter = Control("STATIC", "", 2, 24, 120, 504, 22);
        SendMessageW(label, 0x30, phaseFont, 1); SendMessageW(counter, 0x30, phaseFont, 1);
        targetLabel = Control("STATIC", T("Install folder", "Cartella di installazione"), 0, 24, 58, 504, 22);
        target = Control("EDIT", "", 0x00800080, 24, 84, 504, 30);
        primary = Control("BUTTON", T("Install", "Installa"), 0x10001, 408, 166, 120, 34, 110);
        cancel = Control("BUTTON", T("Cancel", "Annulla"), 0x10000, 280, 166, 120, 34, 111);
        start = Control("BUTTON", T("Add to Start", "Aggiungi a Start"), 0x10003, 24, 56, 504, 28, 114);
        desktop = Control("BUTTON", T("Create desktop shortcut", "Crea collegamento sul desktop"), 0x10003, 24, 86, 504, 28, 115);
        launch = Control("BUTTON", T("Open VRC Project Setup", "Apri VRC Project Setup"), 0x10003, 24, 116, 504, 28, 116);
        details = Control("BUTTON", T("Open log", "Apri log"), 0x10000, 24, 166, 120, 34, 113);
        foreach (var item in new[] { target, targetLabel, primary, start, desktop, launch, details }) ShowWindow(item, 0);
        var dark = 1; _ = DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
        SetWindowTheme(bar, "", ""); SendMessageW(bar, 0x409, 0, 0xBBCD50); SendMessageW(bar, 0x2001, 0, 0x45413E);
        SendMessageW(bar, 0x40A, 1, 0);
        if (visible) ShowWindow(window, 5);
    }
    private void Post(Action action) { actions.Enqueue(action); PostMessageW(window, 0x8004, 0, 0); }
    public Task<string> ChooseTargetAsync(string root)
    {
        if (!visible) return Task.FromResult(root);
        targetChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => {
            state = 0; SetWindowTextW(target, root);
            foreach (var item in new[] { label, bar, counter }) ShowWindow(item, 0);
            foreach (var item in new[] { target, targetLabel, primary }) ShowWindow(item, 5);
        });
        return targetChoice.Task;
    }
    public void Installing() => Post(() => { canCancel = false; ShowWindow(cancel, 0); });
    public void Report(SetupProgress value)
    {
        current = value;
        if (Interlocked.Exchange(ref progressPending, 1) == 0) PostMessageW(window, 0x8001, 0, 0);
    }
    public Task<bool> ConfirmVersionAsync(bool downgrade, string? version)
    {
        if (!visible) return Task.FromResult(false);
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => result.TrySetResult(MessageBoxW(window,
            downgrade ? T("A newer version is installed (" + version + "). Install this older version?", "È già installata una versione più recente (" + version + "). Installare questa versione precedente?")
                : T("This version is already installed. Install it again?", "Questa versione è già installata. Installarla di nuovo?"),
            "VRC Project Setup", 0x134) == 6));
        return result.Task;
    }
    public Task<SetupMessage> CompleteAsync()
    {
        if (!visible) return Task.FromResult(new SetupMessage("finish", Launch: false));
        finishChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => {
            state = 2;
            SetWindowTextW(heading, T("Installation complete", "Installazione completata"));
            foreach (var item in new[] { label, bar, counter, cancel }) ShowWindow(item, 0);
            foreach (var item in new[] { start, desktop, launch, primary }) ShowWindow(item, 5);
            SendMessageW(start, 0xF1, 1, 0); SendMessageW(launch, 0xF1, 1, 0);
            SetWindowTextW(primary, T("Finish", "Fine")); EnableWindow(primary, true);
        });
        return finishChoice.Task;
    }
    public Task FailureAsync(string log, bool reopened)
    {
        if (!visible) return Task.CompletedTask;
        failureClosed = new(TaskCreationOptions.RunContinuationsAsynchronously); logFile = log;
        Post(() => {
            state = 3;
            SetWindowTextW(heading, T("Installation could not finish", "Installazione non completata"));
            SetWindowTextW(label, reopened ? T("The previous app was reopened.", "L'app precedente è stata riaperta.") : T("Your files have been preserved. See the log for details.", "I tuoi file sono stati conservati. Puoi consultare il log."));
            foreach (var item in new[] { bar, counter, target, targetLabel, cancel, start, desktop, launch }) ShowWindow(item, 0);
            foreach (var item in new[] { label, details, primary }) ShowWindow(item, 5);
            SetWindowTextW(primary, T("Close", "Chiudi")); EnableWindow(primary, true);
        });
        return failureClosed.Task;
    }
    public void Finish() => PostMessageW(window, 0x8003, 0, 0);
    public static void ShowError(string message) => MessageBoxW(0, message, "VRC Project Setup", 0x10);
    private void CloseOrCancel()
    {
        if (state == 3) { failureClosed?.TrySetResult(); return; }
        if (state == 2) { Complete(false); return; }
        if (!canCancel) return;
        cancellation.Cancel(); targetChoice?.TrySetCanceled(cancellation.Token);
    }
    private void Complete(bool allowLaunch)
    {
        state = 1;
        EnableWindow(primary, false);
        foreach (var item in new[] { start, desktop, launch, primary }) ShowWindow(item, 0);
        foreach (var item in new[] { label, bar, counter }) ShowWindow(item, 5);
        SetWindowTextW(heading, T("Finishing", "Completamento"));
        finishChoice?.TrySetResult(new("finish", StartMenu: SendMessageW(start, 0xF0, 0, 0) == 1,
            Desktop: SendMessageW(desktop, 0xF0, 0, 0) == 1, Launch: allowLaunch && SendMessageW(launch, 0xF0, 0, 0) == 1));
    }
    private nint Handle(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case 0x10: CloseOrCancel(); return 0;
            case 0x2: PostQuitMessage(0); return 0;
            case 0x111:
                switch ((int)(wParam & 0xFFFF))
                {
                    case 1: case 110:
                        if (state == 0)
                        {
                            var buffer = new StringBuilder(32768); GetWindowTextW(target, buffer, buffer.Capacity);
                            state = 1; targetChoice?.TrySetResult(buffer.ToString());
                            foreach (var item in new[] { target, targetLabel, primary }) ShowWindow(item, 0);
                            foreach (var item in new[] { label, bar, counter }) ShowWindow(item, 5);
                        }
                        else if (state == 2) Complete(true);
                        else if (state == 3) failureClosed?.TrySetResult();
                        break;
                    case 2: case 111: CloseOrCancel(); break;
                    case 113: if (logFile is not null) ShellExecuteW(hwnd, "open", logFile, null, null, 1); break;
                }
                return 0;
            case 0x138: SetTextColor((nint)wParam, 0xF5F5F5); SetBkMode((nint)wParam, 1); return background;
            case 0x8001:
                Interlocked.Exchange(ref progressPending, 0);
                var p = current;
                SetWindowTextW(label, p.Phase switch {
                    "download" => T("Downloading…", "Download…"), "prepare-files" => T("Preparing files…", "Preparazione dei file…"),
                    "install" => T("Installing…", "Installazione…"), "finish" => T("Finishing…", "Completamento…"),
                    "launch" => T("Opening the app…", "Apertura dell'app…"), "waiting" => T("Waiting for the app to close…", "Attesa della chiusura dell'app…"),
                    _ => T("Preparing…", "Preparazione…") });
                var barStyle = GetWindowLongW(bar, -16);
                SetWindowLongW(bar, -16, p.Total > 0 ? barStyle & ~8 : barStyle | 8);
                SendMessageW(bar, 0x40A, p.Total > 0 ? 0 : 1, 0);
                if (p.Total > 0) SendMessageW(bar, 0x402, p.Percent, 0);
                SetWindowTextW(counter, p.Total <= 0 ? "" : p.Phase == "download" ? $"{p.Completed / 1048576d:0.0} / {p.Total / 1048576d:0.0} MB · {p.Percent}%" : p.Percent + "%");
                return 0;
            case 0x8003: DestroyWindow(hwnd); return 0;
            case 0x8004: while (actions.TryDequeue(out var action)) action(); return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
    public void Pump() { while (GetMessageW(out var message, 0, 0, 0) > 0) { if (IsDialogMessageW(window, ref message)) continue; TranslateMessage(ref message); DispatchMessageW(ref message); } }
    public void Dispose() { cancellation.Dispose(); DeleteObject(background); DeleteObject(font); DeleteObject(titleFont); DeleteObject(phaseFont); if (icon != 0) DestroyIcon(icon); if (smallIcon != 0) DestroyIcon(smallIcon); GC.KeepAlive(procedure); }
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
    [DllImport("user32")] private static extern uint GetDpiForSystem();
    [DllImport("user32")] private static extern bool EnableWindow(nint hwnd, bool enabled);
    [DllImport("user32")] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32")] private static extern int SetWindowLongW(nint hwnd, int index, int value);
    [DllImport("user32")] private static extern bool IsDialogMessageW(nint hwnd, ref Message message);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int capacity);
    [DllImport("shell32", CharSet = CharSet.Unicode)] private static extern nint ShellExecuteW(nint hwnd, string verb, string file, string? parameters, string? directory, int show);
    [DllImport("gdi32", CharSet = CharSet.Unicode)] private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    [DllImport("gdi32")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
    [DllImport("uxtheme", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint hwnd, string app, string id);
}
