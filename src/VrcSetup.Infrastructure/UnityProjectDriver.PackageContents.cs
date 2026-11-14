using System.Security.Cryptography;
using System.Text;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class UnityProjectDriver
{
    private static readonly HashSet<string> PackageSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".asmdef", ".asmref", ".dll", ".shader", ".compute", ".cginc", ".hlsl", ".glslinc" };

    public async Task<IReadOnlyDictionary<string, string>> CapturePackageContentsAsync(string project, CancellationToken token)
    {
        var snapshot = await workspace.ReadAsync(project, token);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in PackagePresence.Selection(snapshot.LockedPackages).Keys)
        {
            ProjectRules.ValidatePackageId(id);
            var root = Path.Combine(project, "Packages", id);
            ProjectRules.EnsureContained(project, root);
            if (!Directory.Exists(root)) continue;
            var files = new List<string>();
            var pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.TryDequeue(out var folder))
            {
                token.ThrowIfCancellationRequested();
                ProjectRules.EnsureContained(project, folder);
                foreach (var child in Directory.EnumerateDirectories(folder)) pending.Enqueue(child);
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var source = file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? file[..^5] : file;
                    if (PackageSourceExtensions.Contains(Path.GetExtension(source))) files.Add(file);
                }
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                EditorPackageLease.EnsureSafePath(project, file);
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file) + "\0"));
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                hash.AppendData(await SHA256.HashDataAsync(stream, token));
            }
            result[id] = Convert.ToHexString(hash.GetHashAndReset());
        }
        return result;
    }
}
