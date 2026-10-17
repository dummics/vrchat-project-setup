using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ImportRecoveryTests
{
    [Fact] public async Task DestinationClassificationUsesSourceAndCompletionMarkers()
    {
        var root = Path.Combine(Path.GetTempPath(), "VrcSetup.Recovery.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets")); Directory.CreateDirectory(Path.Combine(root, "Packages"));
        File.WriteAllText(Path.Combine(root, "Packages", "manifest.json"), "{}");
        try
        {
            var source = Path.Combine(root, "source.unitypackage");
            var inspect = new ImportDestinationInspector(new ProjectWorkspace(new AppPaths(Path.Combine(root, "home"))));
            Assert.Equal(ImportDestinationKind.Unmanaged, (await inspect.InspectAsync(root, source)).Kind);
            var stateFile = Path.Combine(root, ".vrcsetup", "state.json");
            var state = new SetupState { UnityPackagePath = source };
            await AtomicFile.WriteJsonAsync(stateFile, state);
            Assert.Equal(ImportDestinationKind.Recoverable, (await inspect.InspectAsync(root, source)).Kind);
            state.Completed = true; await AtomicFile.WriteJsonAsync(stateFile, state);
            var completed = await inspect.InspectAsync(root, source);
            Assert.Equal(ImportDestinationKind.Completed, completed.Kind); Assert.True(completed.CanImportMissing);
            Assert.False((await inspect.InspectAsync(root, Path.Combine(root, "other.unitypackage"))).CanImportMissing);
            File.WriteAllText(stateFile, "broken JSON");
            Assert.Equal(ImportDestinationKind.InvalidState, (await inspect.InspectAsync(root, source)).Kind);
            Assert.True(File.Exists(Path.Combine(root, "Packages", "manifest.json")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task ReadingUnityLogPermitsAnExistingWriter()
    {
        var file = Path.GetTempFileName();
        try
        {
            using var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            writer.Write(Encoding.UTF8.GetBytes("Unity finished")); writer.Flush();
            Assert.Equal("Unity finished", await SharedLog.ReadAsync(file));
        }
        finally { File.Delete(file); }
    }
    [Fact] public async Task MissingArchivePreservesEditedAssetsAndMovedGuids()
    {
        var root = Path.Combine(Path.GetTempPath(), "VrcSetup.Recovery.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        try
        {
            var source = Path.Combine(root, "original.unitypackage"); var output = Path.Combine(root, "missing.unitypackage");
            var edited = new string('a', 32); var moved = new string('b', 32); var added = new string('c', 32);
            File.WriteAllText(Path.Combine(root, "Assets", "edited.txt"), "personal edit");
            File.WriteAllText(Path.Combine(root, "Assets", "edited.txt.meta"), "guid: " + edited);
            File.WriteAllText(Path.Combine(root, "Assets", "moved.txt"), "moved asset");
            File.WriteAllText(Path.Combine(root, "Assets", "moved.txt.meta"), "guid: " + moved);
            using (var gzip = new GZipStream(File.Create(source), CompressionLevel.Fastest))
            using (var writer = new TarWriter(gzip))
                foreach (var (guid, name) in new[] { (edited, "edited.txt"), (moved, "original-name.txt"), (added, "new.txt") })
                    foreach (var (part, data) in new[] { ("pathname", "Assets/" + name), ("asset", "package content"), ("asset.meta", "guid: " + guid) })
                    {
                        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(data));
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, guid + "/" + part) { DataStream = bytes });
                    }
            Assert.True(await MissingAssetArchive.CreateAsync(source, root, output, default));
            using var result = new TarReader(new GZipStream(File.OpenRead(output), CompressionMode.Decompress));
            var names = new List<string>(); TarEntry? entry;
            while ((entry = result.GetNextEntry()) is not null) names.Add(entry.Name);
            Assert.Equal(3, names.Count); Assert.All(names, x => Assert.StartsWith(added + "/", x));
            Assert.Equal("personal edit", File.ReadAllText(Path.Combine(root, "Assets", "edited.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
