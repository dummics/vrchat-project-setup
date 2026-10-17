using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

// Recovery imports never replace existing assets, including assets moved by GUID.
public static class MissingAssetArchive
{
    public static Task<bool> CreateAsync(string source, string project, string output, CancellationToken token) => Task.Run(() =>
    {
        var identity = ProjectRules.FileIdentity(source);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assets = Path.Combine(project, "Assets");
        if (Directory.Exists(assets))
            foreach (var meta in Directory.EnumerateFiles(assets, "*.meta", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                token.ThrowIfCancellationRequested();
                // A surviving meta without its asset must not hide the missing asset.
                if (!File.Exists(meta[..^5]) && !Directory.Exists(meta[..^5])) continue;
                var guid = File.ReadLines(meta).Take(12).Select(x => Regex.Match(x, @"^guid:\s*([a-fA-F0-9]{32})\s*$")).FirstOrDefault(x => x.Success);
                if (guid is not null) existing.Add(guid.Groups[1].Value);
            }
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var input = new GZipStream(File.OpenRead(source), CompressionMode.Decompress))
        using (var reader = new TarReader(input))
        {
            TarEntry? entry; var count = 0;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                token.ThrowIfCancellationRequested();
                if (++count > 1_000_000) throw new IOException("Package contains too many entries to recover safely.");
                var parts = entry.Name.TrimStart('.','/').Split('/');
                if (parts.Length != 2 || parts[1] != "pathname" || !Regex.IsMatch(parts[0], "^[a-fA-F0-9]{32}$")) continue;
                if (entry.Length > 8192 || entry.DataStream is null) throw new IOException("Invalid package pathname.");
                using var text = new StreamReader(entry.DataStream, Encoding.UTF8, leaveOpen: true);
                var relative = text.ReadToEnd().Trim('\0', '\r', '\n').Replace('\\', '/');
                if (!relative.Equals("Assets", StringComparison.OrdinalIgnoreCase) && !relative.Equals("Packages", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) throw new IOException("Package recovery requires an Assets or Packages path.");
                var target = ProjectRules.NormalizePath(relative, project); ProjectRules.EnsureContained(project, target);
                for (var parent = Directory.GetParent(target); parent is not null && !parent.FullName.Equals(ProjectRules.NormalizePath(project), StringComparison.OrdinalIgnoreCase); parent = parent.Parent)
                    if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Package recovery cannot follow linked project folders.");
                if (File.Exists(target) && !existing.Contains(parts[0])) throw new IOException("An existing asset has a different or missing GUID. It was preserved: " + relative);
                if (File.Exists(target) || Directory.Exists(target) || existing.Contains(parts[0])) continue;
                if (File.Exists(target + ".meta") && !File.ReadLines(target + ".meta").Take(12).Any(x => x.Trim().Equals("guid: " + parts[0], StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("A missing asset has a conflicting meta file: " + relative);
                selected.Add(parts[0]);
            }
        }
        if (selected.Count == 0)
        { if (identity != ProjectRules.FileIdentity(source)) throw new IOException("Source package changed during recovery analysis."); return false; }
        var staging = output + ".tar";
        try
        {
            // TarWriter needs a seekable destination for streamed entry data.
            using (var archive = File.Create(staging))
            using (var writer = new TarWriter(archive, TarEntryFormat.Pax))
            using (var sourceArchive = new GZipStream(File.OpenRead(source), CompressionMode.Decompress))
            using (var sourceReader = new TarReader(sourceArchive))
            {
                TarEntry? item;
                while ((item = sourceReader.GetNextEntry()) is not null)
                {
                    token.ThrowIfCancellationRequested();
                    var name = item.Name.TrimStart('.', '/'); var parts = name.Split('/');
                    if (parts.Length != 2 || parts[1].Length == 0 || !selected.Contains(parts[0])) continue;
                    if (item.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new IOException("Unsupported package entry in recovery archive.");
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = item.DataStream });
                }
            }
            if (identity != ProjectRules.FileIdentity(source)) throw new IOException("Source package changed during recovery analysis.");
            using var destination = new GZipStream(File.Create(output), CompressionLevel.Fastest);
            using var stage = File.OpenRead(staging);
            var buffer = new byte[81920]; int read;
            while ((read = stage.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); destination.Write(buffer, 0, read); }
            return true;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }, token);
}
