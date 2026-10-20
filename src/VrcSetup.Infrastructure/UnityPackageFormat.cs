using System.Formats.Tar;
using System.IO.Compression;

namespace VrcSetup.Infrastructure;

public static class UnityPackageFormat
{
    // Cheap container preflight before starting Unity or resolving dependencies.
    // Asset inspection and Unity still validate the remaining archive contents.
    public static async Task ValidateAsync(string path, CancellationToken token = default)
    {
        if (!path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new IOException("Choose a .unitypackage file, not a folder or another file type.");
        try
        {
            await using var file = File.OpenRead(path);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            if (await tar.GetNextEntryAsync(copyData: false, cancellationToken: token) is null)
                throw new InvalidDataException("Empty archive");
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or FormatException)
        { throw new IOException("This file is not a valid UnityPackage. Choose the original .unitypackage file.", error); }
    }
}
