using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class UnityProjectTemplateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-template-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task TemplateCopiesOnlySourceFilesAndUsesTheSelectedEditorVersion()
    {
        var archive = Create(("Packages/manifest.json", "{\"dependencies\":{}}"), ("ProjectSettings/TagManager.asset", "fixture"), ("Assets/Scene.unity", "fixture"), ("Library/stale", "cache"));
        var destination = Path.Combine(root, "project");
        await UnityProjectTemplate.ExtractAsync(archive, destination, "2022.3.22f1", default);
        Assert.True(File.Exists(Path.Combine(destination, "Assets", "Scene.unity")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Library")));
        Assert.Contains("2022.3.22f1", await File.ReadAllTextAsync(Path.Combine(destination, "ProjectSettings", "ProjectVersion.txt")));
    }
    [Fact] public async Task TemplateRejectsTraversalBeforeWritingOutsideTheDestination()
    {
        var archive = Create(("Assets/../../outside.txt", "unexpected"));
        await Assert.ThrowsAsync<IOException>(() => UnityProjectTemplate.ExtractAsync(archive, Path.Combine(root, "project"), "2022.3.22f1", default));
        Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
    }
    [Fact] public async Task IncompleteTemplateIsNotAcceptedAsAReadyProject()
    {
        var archive = Create(("Assets/Marker.txt", "fixture"));
        await Assert.ThrowsAsync<IOException>(() => UnityProjectTemplate.ExtractAsync(archive, Path.Combine(root, "project"), "2022.3.22f1", default));
    }
    private string Create(params (string Name, string Text)[] entries)
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "template.tgz");
        using var file = File.Create(path); using var gzip = new GZipStream(file, CompressionLevel.Fastest); using var writer = new TarWriter(gzip);
        foreach (var entry in entries)
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(entry.Text));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "package/ProjectData~/" + entry.Name) { DataStream = data });
        }
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
