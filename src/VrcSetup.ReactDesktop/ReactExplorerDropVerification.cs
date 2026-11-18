using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactExplorerDropVerification
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RootWindow = 2;

    internal static async Task RunAsync(CoreWebView2 core, Window window, string folder, CancellationToken token)
    {
        // Only invoked by the hosted fixture path after Program verifies the runner
        // and the isolated install and profile. The runner has its own desktop.
        var source = Directory.CreateDirectory(Path.Combine(folder, "explorer-source")).FullName;
        const string name = "VrcSetupExplorerQuestV2";
        var archive = Path.Combine(source, name + ".unitypackage");
        File.Move(ReactImportVerification.MakeArchive(source), archive);
        var originalHash = AtomicFile.Hash(archive);
        var app = new WindowInteropHelper(window).Handle;
        if (GetSystemMetrics(0) < 1000 || GetSystemMetrics(1) < 600)
            throw new IOException("Explorer drop needs at least a 1000 × 600 hosted desktop.");

        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { UseShellExecute = true, Arguments = "/n,\"" + source + "\"" };
        using (Process.Start(start)) { }
        var explorer = await Task.Run(() => FindItemAsync(name, token), token);
        try
        {
            const uint showWindow = 0x0040;
            if (!SetWindowPos(explorer.Window, 0, 0, 0, 450, 550, showWindow) ||
                !SetWindowPos(app, 0, 460, 0, 530, 550, showWindow))
                throw new IOException("Could not arrange the two hosted fixture windows.");
            await Task.Delay(400, token);
            var item = await Task.Run(() => FindItemAsync(name, token), token);
            var destination = window.PointToScreen(new Point(55, 165));
            var target = new CursorPoint((int)destination.X, (int)destination.Y);
            if (item.Window != explorer.Window || GetAncestor(WindowFromPoint(item.Center), RootWindow) != explorer.Window ||
                GetAncestor(WindowFromPoint(target), RootWindow) != app)
                throw new IOException("The archive and target are not visible in the expected hosted fixture windows.");

            Move(item.Center);
            SendButton(LeftDown);
            try
            {
                await Task.Delay(180, token);
                for (var step = 1; step <= 8; step++)
                {
                    Move(new(item.Center.X + (target.X - item.Center.X) * step / 8,
                        item.Center.Y + (target.Y - item.Center.Y) * step / 8));
                    await Task.Delay(70, token);
                }
                await Program.RequireAsync(core, "document.querySelector('main.dragging') !== null");
            }
            finally { SendButton(LeftUp); }

            await Program.RequireAsync(core, """
                Array.from(document.querySelectorAll('.tabs button')).some(tab => tab.textContent === 'VrcSetupExplorerQuestV2.unitypackage')
                && document.querySelector('.platforms [aria-pressed=true]')?.textContent === 'Quest'
                && document.querySelector('main.dragging') === null
                """, attempts: 400);
            if (AtomicFile.Hash(archive) != originalHash)
                throw new IOException("The Explorer drag changed the source archive.");
        }
        finally
        {
            PostMessage(explorer.Window, 0x0010, 0, 0);
            SetWindowPos(app, 0, -12000, -12000, 720, 620, 0);
        }
    }

    private static async Task<(nint Window, CursorPoint Center)> FindItemAsync(string name, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var fileItem = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.NameProperty, name + ".unitypackage")));
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            token.ThrowIfCancellationRequested();
            (nint Window, CursorPoint Center) found = default;
            EnumWindows((handle, _) =>
            {
                var kind = new StringBuilder(64);
                GetClassName(handle, kind, kind.Capacity);
                if (kind.ToString() != "CabinetWClass") return true;
                try
                {
                    var file = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants, fileItem);
                    if (file is null || file.Current.IsOffscreen) return true;
                    var box = file.Current.BoundingRectangle;
                    found = (handle, new((int)(box.Left + Math.Min(40, box.Width / 2)), (int)(box.Top + box.Height / 2)));
                    return false;
                }
                catch (ElementNotAvailableException) { return true; }
            }, 0);
            if (found.Window != 0) return found;
            await Task.Delay(200, token);
        }
        throw new IOException("Explorer did not show the archive in the hosted desktop session.");
    }

    private static void Move(CursorPoint point)
    {
        if (!SetCursorPos(point.X, point.Y)) throw new IOException("The hosted runner has no active cursor.");
    }

    private static void SendButton(uint flags)
    {
        var input = new Input { Mouse = new MouseInput { Flags = flags } };
        if (SendInput(1, ref input, Marshal.SizeOf<Input>()) != 1)
            throw new IOException("The hosted desktop did not accept the drag input.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CursorPoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nint ExtraInfo; }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int length);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, ref Input input, int size);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nint parameter, nint data);
}
