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
            $start = New-Object System.Diagnostics.ProcessStartInfo
            $start.FileName = (Get-Process -Id $PID).Path
            $start.Arguments = '-NoProfile -NonInteractive -EncodedCommand {{child}}'
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            [Console]::Out.WriteLine('parent:' + $PID)
            [void][System.Diagnostics.Process]::Start($start)
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
