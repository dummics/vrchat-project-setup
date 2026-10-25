using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class ToolRunner(DiagnosticLog? diagnostics = null, string? bundledPackageTool = null,
    Func<string, string[], CancellationToken, Task<ToolResult>>? commandOverride = null)
{
    public async Task<ToolResult> RunAsync(string executable, IEnumerable<string> arguments, string? workingDirectory = null,
        IProgress<OperationEvent>? progress = null, string phase = "Tool", CancellationToken cancellationToken = default, TimeSpan? timeout = null,
        string? logFile = null, string? standardInput = null)
    {
        if (commandOverride is not null) return await commandOverride(executable, arguments.ToArray(), cancellationToken);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.RedirectStandardInput = standardInput is not null;
        diagnostics?.Write(phase, "Starting " + Path.GetFileName(executable));
        using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new IOException($"Could not start {executable}."); }
        catch (System.ComponentModel.Win32Exception error) { throw new IOException($"Cannot start {Path.GetFileName(executable)}. Check its installation and runtime prerequisites.", error); }
        using var guard = new WindowsJob(process);
        if (standardInput is not null) { await process.StandardInput.WriteAsync(standardInput); process.StandardInput.Close(); }
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        var outputTask = Capture(process.StandardOutput, stdout);
        var errorTask = Capture(process.StandardError, stderr);
        var logPosition = 0L;
        var lastLogWrite = DateTime.MinValue;
        var startTime = DateTime.UtcNow;
        try
        {
            while (!process.HasExited)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (logFile is not null && File.Exists(logFile))
                {
                    try
                    {
                    var info = new FileInfo(logFile);
                    if (info.LastWriteTimeUtc != lastLogWrite || info.Length != logPosition)
                    {
                        lastLogWrite = info.LastWriteTimeUtc;
                        if (info.Length < logPosition) logPosition = 0;
                        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        stream.Seek(logPosition, SeekOrigin.Begin);
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var text = await reader.ReadToEndAsync(linked.Token);
                        logPosition = stream.Position;
                        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Emit(line.TrimEnd('\r'));
                    }
                    }
                    catch (IOException) { /* Log sharing is transient; keep the child running and retry. */ }
                }
                await Task.Delay(250, linked.Token);
            }
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(outputTask, errorTask);
            // Include the end of a Unity log written immediately before the process exited.
            if (logFile is not null && File.Exists(logFile))
            {
                for (var attempt = 0; ; attempt++)
                {
                try
                {
                using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < logPosition) logPosition = 0;
                stream.Seek(logPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync(linked.Token);
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Emit(line.TrimEnd('\r'));
                break;
                }
                catch (IOException) when (attempt < 15) { await Task.Delay(200, linked.Token); }
                }
            }
            diagnostics?.Write(phase, "Exit " + process.ExitCode + " after " + (DateTime.UtcNow - startTime).TotalSeconds.ToString("F1") + "s");
            return new(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(outputTask, errorTask); } catch (IOException) { }
            if (!cancellationToken.IsCancellationRequested) throw new TimeoutException($"{phase} exceeded its timeout after {DateTime.UtcNow - startTime:g}.");
            throw;
        }
        async Task Capture(StreamReader reader, StringBuilder target)
        {
            while (await reader.ReadLineAsync() is { } line) { target.AppendLine(line); Emit(line); }
        }
        void Emit(string line)
        {
            if (diagnostics?.TraceTools == true) diagnostics.Write(phase, line);
            var severity = Regex.IsMatch(line, @"(?i)\b(ERR|ERROR|FAILED|FAILURE)\b") ? EventSeverity.Error : Regex.IsMatch(line, @"(?i)\b(WRN|WARN|WARNING)\b") ? EventSeverity.Warning : EventSeverity.Info;
            progress?.Report(new(DateTime.UtcNow, phase, line, severity, workingDirectory));
        }
    }

    public Task<ToolResult> VpmAsync(IEnumerable<string> arguments, string? project = null, IProgress<OperationEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        var argv = arguments.ToArray();
        return Task.Run(() =>
        {
            var translated = bundledPackageTool is null ? null : PackageToolArguments(argv);
            var executable = translated is not null ? bundledPackageTool! : FindExecutable("vpm");
            if (executable is null && argv is ["add", "project", _])
                return new ToolResult(1, "", "Creator Companion registration is unavailable; open the project from VRC Project Setup.");
            if (executable is null) throw new IOException("The package tool is unavailable. Reinstall VRC Project Setup.");
            var command = translated ?? argv;
            using var mutex = new Mutex(false, OperatingSystem.IsWindows() ? @"Local\VrcSetup.Vpm.Commands" : "VrcSetup.Vpm.Commands");
            var acquired = false;
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(15);
                while (!acquired)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { acquired = mutex.WaitOne(100); } catch (AbandonedMutexException) { acquired = true; }
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for the shared VPM lock.");
                }
                ToolResult result = new(1, "", "");
                if (translated is not null && argv is ["remove", "package", var removing, "-p", _])
                {
                    // Ask the bundled resolver for its proposal and decline it.
                    // Never force a removal which VPM itself reports as conflicting.
                    var preview = RunAsync(executable, command.Where(x => x != "-y"), project,
                        cancellationToken: cancellationToken, standardInput: "n\n").GetAwaiter().GetResult();
                    if (Regex.IsMatch(preview.Output, @"(?i)conflicts? with"))
                        return new ToolResult(1, "Package removal blocked: " + removing, preview.Output);
                    if (!preview.Output.Contains("Do you want to apply", StringComparison.Ordinal)) return preview;
                }
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    result = RunAsync(executable, command, project, progress, "VPM", cancellationToken).GetAwaiter().GetResult();
                    if (result.ExitCode == 0 || !Regex.IsMatch(result.Output, @"(?i)(being used by another process|sharing violation|file.*lock|locked)") || attempt == 3) break;
                    Task.Delay(500 * (1 << attempt), cancellationToken).GetAwaiter().GetResult();
                }
                return result;
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }, cancellationToken);
    }

    public static string[]? PackageToolArguments(string[] args) => args switch
    {
        ["list", "repos"] => ["repo", "list"],
        ["add", "repo", var url] => ["repo", "add", url],
        ["remove", "package", var id, "-p", var path] => ["remove", id, "-p", path, "-y"],
        ["resolve", "project", var path] => ["resolve", "-p", path],
        ["downgrade", "package", var package, "-p", var path] when package.LastIndexOf('@') is var at && at > 0 =>
            ["downgrade", package[..at], package[(at + 1)..], "-p", path, "-y", "--prerelease"],
        ["add", "package", var package, "-p", var path] when package.LastIndexOf('@') is var at && at > 0 =>
            ["install", package[..at], package[(at + 1)..], "-p", path, "-y", "--prerelease"],
        ["add", "package", var package, "-p", var path] => ["install", package, "-p", path, "-y"],
        ["add", "project", _] => null,
        _ => throw new NotSupportedException("Unsupported package-tool command.")
    };

    public static string? FindExecutable(string name)
    {
        var file = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools")))
            if (!string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder.Trim('"'), file))) return Path.Combine(folder.Trim('"'), file);
        return null;
    }
}

internal sealed class WindowsJob : IDisposable
{
    private IntPtr handle;
    public WindowsJob(Process process)
    {
        if (!OperatingSystem.IsWindows() || process.HasExited) return;
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new IOException("Cannot create the child process guard.");
        var limits = new ExtendedLimits { BasicLimitInformation = new BasicLimits { LimitFlags = 0x2000 } };
        var size = Marshal.SizeOf<ExtendedLimits>(); var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, memory, false);
            if (!SetInformationJobObject(handle, 9, memory, (uint)size) || !AssignProcessToJobObject(handle, process.Handle))
            {
                if (!process.HasExited) { process.Kill(true); Dispose(); throw new IOException("Cannot attach the owned child process guard."); }
            }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
