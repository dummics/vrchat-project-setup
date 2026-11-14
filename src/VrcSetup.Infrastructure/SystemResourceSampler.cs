using System.Runtime.InteropServices;

namespace VrcSetup.Infrastructure;

public sealed record SystemResourceUsage(double TotalGiB, double AvailableGiB, double AvailableCommitGiB, double? CpuPercent, int LogicalProcessors)
{
    // Admission estimate, not a promise about a particular avatar's peak usage.
    // Unity can occupy every core during a healthy import: CPU is telemetry,
    // not a reason to serialize independent projects. Preserve RAM/commit space
    // for the OS and allow roughly 4 GiB of headroom per simultaneous import.
    public int SuggestedParallelism => !double.IsFinite(TotalGiB) || TotalGiB <= 0 || !double.IsFinite(AvailableGiB) || AvailableGiB < 0 || AvailableGiB > TotalGiB || !double.IsFinite(AvailableCommitGiB) || AvailableCommitGiB < 0
        ? 1 : (int)Math.Clamp((Math.Min(AvailableGiB, AvailableCommitGiB) - Math.Max(2, TotalGiB * 0.1)) / 4, 0, 8);
    public bool MemoryHigh => AvailableGiB < 1 || AvailableCommitGiB < 1 || TotalGiB > 0 && AvailableGiB / TotalGiB < 0.1;
}

public sealed class SystemResourceSampler
{
    private long previousIdle, previousTotal;
    public SystemResourceUsage? Sample()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) return null;
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user; var elapsed = total - previousTotal;
            if (previousTotal > 0 && elapsed > 0) cpu = Math.Clamp(100d * (1d - (double)(idle - previousIdle) / elapsed), 0, 100);
            previousTotal = total; previousIdle = idle;
        }
        const double gib = 1024d * 1024 * 1024;
        return new(memory.TotalPhysical / gib, memory.AvailablePhysical / gib, memory.AvailablePageFile / gib, cpu, Environment.ProcessorCount);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
