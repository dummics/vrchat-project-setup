using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VrcSetup.Infrastructure;

// A missing identity never grants permission to remove or replace a directory.
public sealed record ProjectDirectoryIdentity(ulong Volume, ulong FileIdLow, ulong FileIdHigh)
{
    // Only empty directories; ownership/receipt authorization belongs to the caller.
    public static void RemoveEmptyDirectory(string path, ProjectDirectoryIdentity expected)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var full = Path.GetFullPath(path);
        var parent = Directory.GetParent(full)?.FullName ?? throw new IOException("Cannot remove a volume root.");
        var parentIdentity = TryRead(parent) ?? throw new IOException("The parent location cannot be verified.");
        using var ancestors = HoldPath(parent, parentIdentity);
        using var handle = CreateFileW(full, 0x00010000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (Read(handle) != expected) throw new IOException("The project location changed. Its files have been preserved.");
        var disposition = new DispositionInfo { Delete = 1 };
        if (!SetDisposition(handle, 4, ref disposition, 1))
            throw new IOException("The directory could not be removed. It may still contain files.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        handle.Dispose();
        try { _ = File.GetAttributes(full); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        throw new IOException("Directory removal has not been confirmed. The cancellation remains pending.");
    }

    public static ProjectDirectoryIdentity? TryRead(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // Open the entry itself, including reparse points, rather than following its target.
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        return Read(handle);
    }

    // Pin every path component against rename/deletion, not just the final entry.
    // This is not an exclusive writer lock and does not protect descendants.
    public static IDisposable HoldPath(string path, ProjectDirectoryIdentity expected)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Directory ownership requires Windows file identity.");
        var handles = new List<SafeFileHandle>();
        try
        {
            var components = new Stack<string>();
            for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
                components.Push(directory.FullName);
            ProjectDirectoryIdentity? actual = null;
            foreach (var component in components)
            {
                var handle = CreateFileW(component, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                handles.Add(handle);
                actual = Read(handle);
                if (actual is null) throw new IOException("The project path cannot be protected safely. Its files have been preserved.");
            }
            if (actual != expected) throw new IOException("The project location changed. Its files have been preserved.");
            return new PathLease(handles);
        }
        catch { foreach (var handle in handles) handle.Dispose(); throw; }
    }

    private static ProjectDirectoryIdentity? Read(SafeFileHandle handle)
    {
        if (handle.IsInvalid || !GetAttributes(handle, 9, out var attributes, 8)
            || (attributes.Attributes & (uint)FileAttributes.Directory) == 0
            || (attributes.Attributes & (uint)FileAttributes.ReparsePoint) != 0
            || !GetIdentity(handle, 18, out var identity, 24)
            || (identity.Low == 0 && identity.High == 0)) return null;
        return new(identity.Volume, identity.Low, identity.High);
    }

    private sealed class PathLease(List<SafeFileHandle> handles) : IDisposable
    {
        public void Dispose() { for (var index = handles.Count - 1; index >= 0; index--) handles[index].Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeInfo { public uint Attributes; public uint ReparseTag; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IdentityInfo { public ulong Volume; public ulong Low; public ulong High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DispositionInfo { public byte Delete; }
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDisposition(SafeFileHandle handle, int infoClass, ref DispositionInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAttributes(SafeFileHandle handle, int infoClass, out AttributeInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIdentity(SafeFileHandle handle, int infoClass, out IdentityInfo info, uint size);
}
