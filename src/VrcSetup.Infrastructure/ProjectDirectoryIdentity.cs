using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VrcSetup.Infrastructure;

// A missing identity never grants permission to remove or replace a directory.
public sealed record ProjectDirectoryIdentity(ulong Volume, ulong FileIdLow, ulong FileIdHigh)
{
    public static ProjectDirectoryIdentity? TryRead(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // Open the entry itself, including reparse points, rather than following its target.
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetAttributes(handle, 9, out var attributes, 8)
            || (attributes.Attributes & (uint)FileAttributes.Directory) == 0
            || (attributes.Attributes & (uint)FileAttributes.ReparsePoint) != 0
            || !GetIdentity(handle, 18, out var identity, 24)
            || (identity.Low == 0 && identity.High == 0)) return null;
        return new(identity.Volume, identity.Low, identity.High);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeInfo { public uint Attributes; public uint ReparseTag; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IdentityInfo { public ulong Volume; public ulong Low; public ulong High; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAttributes(SafeFileHandle handle, int infoClass, out AttributeInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIdentity(SafeFileHandle handle, int infoClass, out IdentityInfo info, uint size);
}
