using System.Diagnostics;
using System.Text;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ToolRunnerCancellationTests
{
    [Fact] public async Task CancellationStopsReadingAPipeInheritedByAChildAfterTheParentExits()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var parentId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Encode("[Console]::Out.WriteLine('child:' + $PID); Start-Sleep -Seconds 30");
        var script = $$"""
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static class InheritedPipeChild {
                [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
                struct Startup {
                    public int cb; public string reserved, desktop, title;
                    public int x,y,width,height,charsX,charsY,fill,flags;
                    public short show, reservedBytes; public IntPtr reservedPointer,input,output,error;
                }
                [StructLayout(LayoutKind.Sequential)]
                struct Child { public IntPtr process,thread; public int pid,tid; }
                [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int kind);
                [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);
                [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
                static extern bool CreateProcessW(string app, System.Text.StringBuilder command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out Child child);
                [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
                public static void Start(string executable, string arguments) {
                    var s = new Startup(); s.cb=Marshal.SizeOf(s); s.flags=0x100;
                    s.output=GetStdHandle(-11); s.error=GetStdHandle(-12); s.input=GetStdHandle(-10);
                    if (!SetHandleInformation(s.output,1,1) || !SetHandleInformation(s.error,1,1)) throw new System.ComponentModel.Win32Exception();
                    Child c;
                    if (!CreateProcessW(executable,new System.Text.StringBuilder("\""+executable+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,true,0x08000000,IntPtr.Zero,null,ref s,out c)) throw new System.ComponentModel.Win32Exception();
                    CloseHandle(c.thread); CloseHandle(c.process);
                }
            }
            '@
            [Console]::Out.WriteLine('parent:' + $PID)
            [InheritedPipeChild]::Start((Get-Process -Id $PID).Path, '-NoProfile -NonInteractive -EncodedCommand {{child}}')
            """;
        using var cancellation = new CancellationTokenSource();
        Process? descendant = null;
        var running = new ToolRunner().RunAsync(executable,
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(script)],
            progress: new InlineProgress(value =>
            {
                if (value.Message.StartsWith("parent:") && int.TryParse(value.Message[7..], out var parent)) parentId.TrySetResult(parent);
                if (value.Message.StartsWith("child:") && int.TryParse(value.Message[6..], out var spawned)) childId.TrySetResult(spawned);
            }), cancellationToken: cancellation.Token, timeout: TimeSpan.FromMinutes(1));
        try
        {
            var parent = await parentId.Task.WaitAsync(TimeSpan.FromSeconds(15));
            descendant = Process.GetProcessById(await childId.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            try
            {
                using var process = Process.GetProcessById(parent);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException) { /* The parent already exited before its handle was acquired. */ }
            // The child keeps the inherited output pipe open after the root exits.
            Assert.False(running.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            cancellation.Cancel();
            // Only this fixture's captured child handle is eligible for cleanup.
            if (descendant is not null)
            {
                using (descendant)
                    try { if (!descendant.HasExited) descendant.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
            }
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    private sealed class InlineProgress(Action<OperationEvent> report) : IProgress<OperationEvent>
    {
        public void Report(OperationEvent value) => report(value);
    }
}
