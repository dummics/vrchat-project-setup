using System.Runtime.InteropServices;
using VrcSetup.Core;

namespace VrcSetup.Bootstrapper;

internal sealed partial class DownloadWindow
{
    internal Task CapturePreviewAsync(string page, string path)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                SetWindowLongPtrW(window, -20, 0x80); // Tool window: no taskbar entry, no activation.
                SetWindowPos(window, 0, -10000, -10000, 0, 0, 0x15);
                ShowWindow(window, 4);
                foreach (var item in new[] { target, targetLabel, primary, cancel, start, desktop, launch, details }) ShowWindow(item, 0);
                foreach (var item in new[] { label, bar, counter }) ShowWindow(item, 5);
                if (page == "start")
                {
                    foreach (var item in new[] { label, bar, counter }) ShowWindow(item, 0);
                    foreach (var item in new[] { target, targetLabel, primary, cancel }) ShowWindow(item, 5);
                    SetWindowTextW(target, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup"));
                }
                else if (page == "complete")
                {
                    SetWindowTextW(heading, T("Installation complete", "Installazione completata"));
                    foreach (var item in new[] { label, bar, counter }) ShowWindow(item, 0);
                    foreach (var item in new[] { start, desktop, launch, primary }) ShowWindow(item, 5);
                    SendMessageW(start, 0xF1, 1, 0); SendMessageW(launch, 0xF1, 1, 0); SetWindowTextW(primary, T("Finish", "Fine"));
                }
                else
                {
                    current = page == "download" ? new SetupProgress("download", 34 * 1048576, 100 * 1048576) : new("install", 42, 100);
                    Handle(window, 0x8001, 0, 0);
                }
                SaveBitmap(path); completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }
    private void SaveBitmap(string path)
    {
        RedrawWindow(window, 0, 0, 0x185);
        GetWindowRect(window, out var rect); var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var dc = GetWindowDC(window); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, width, height); var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(window, memory, 0)) throw new IOException("Native setup capture failed.");
            SelectObject(memory, previous);
            var data = new byte[checked(width * height * 4)];
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32, ImageSize = (uint)data.Length };
            if (GetDIBits(memory, bitmap, 0, (uint)height, data, ref info, 0) == 0) throw new IOException("Native setup pixels unavailable.");
            using var output = new BinaryWriter(File.Create(path));
            output.Write((ushort)0x4D42); output.Write(54 + data.Length); output.Write(0); output.Write(54);
            output.Write(40); output.Write(width); output.Write(-height); output.Write((ushort)1); output.Write((ushort)32);
            output.Write(0); output.Write(data.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(data);
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(window, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Colors, Important; }
    [DllImport("user32")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32")] private static extern nint GetWindowDC(nint window);
    [DllImport("user32")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern bool RedrawWindow(nint window, nint update, nint region, uint flags);
    [DllImport("gdi32")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] data, ref BitmapInfo info, uint usage);
}
