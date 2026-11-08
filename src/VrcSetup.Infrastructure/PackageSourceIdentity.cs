using System.Security.Cryptography;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Content identity survives a move or rename. Open with read sharing only so a
// writer cannot replace or modify the file while the digest is being computed.
public static class PackageSourceIdentity
{
    public static async Task<PackageIdentity> ReadAsync(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        var hash = await SHA256.HashDataAsync(stream, token);
        if (stream.Length != length) throw new IOException("The package changed while it was being checked.");
        return new(length, Convert.ToHexString(hash));
    }
    public static async Task<FileStream> OpenVerifiedAsync(string path, PackageIdentity expected, CancellationToken token = default)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var length = stream.Length;
            var hash = await SHA256.HashDataAsync(stream, token);
            if (stream.Length != length || new PackageIdentity(length, Convert.ToHexString(hash)) != expected)
                throw new IOException("This package differs from the original. Choose the original UnityPackage to continue.");
            return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
}
