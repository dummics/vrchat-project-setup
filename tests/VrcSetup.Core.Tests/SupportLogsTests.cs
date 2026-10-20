using System.IO.Compression;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class SupportLogsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-support-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task BusyUnityLogsCannotDisplaceErrorsFromOtherSections()
    {
        var paths = new AppPaths(root); Directory.CreateDirectory(paths.Logs);
        var diagnostics = new DiagnosticLog(paths);
        diagnostics.Write("Project creation", "Creation failed", new IOException("Creation fixture"));
        diagnostics.Write("Packages", "Apply failed", new IOException("Apply fixture"));
        for (var i = 0; i < 20; i++)
            await File.WriteAllTextAsync(Path.Combine(paths.Logs, "unity-" + i + ".log"), new string('x', 1024 * 1024 * 3));
        var updates = Path.Combine(paths.Home, "updates", "components"); Directory.CreateDirectory(updates);
        await File.WriteAllTextAsync(Path.Combine(updates, "last-error.log"), "Update fixture");
        using var activeLog = new FileStream(diagnostics.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var destination = Path.Combine(root, "diagnostics.zip");
        await SupportLogs.ExportAsync(paths, destination);
        using var zip = ZipFile.OpenRead(destination);
        using var reader = new StreamReader(zip.GetEntry("VRC-Project-Setup.txt")!.Open());
        var summary = await reader.ReadToEndAsync();
        Assert.Contains("Project creation", summary); Assert.Contains("Packages", summary);
        Assert.NotNull(zip.GetEntry("Update/last-error.log"));
        Assert.True(zip.Entries.Sum(x => x.Length) < 17L * 1024 * 1024);
        using var manifest = new StreamReader(zip.GetEntry("Archive-contents.txt")!.Open());
        Assert.Contains("older content omitted", await manifest.ReadToEndAsync());
    }
    [Fact]
    public async Task CancellationPreservesExistingExportAndLeavesNoPartialArchive()
    {
        var paths = new AppPaths(root); Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "diagnostics.zip"); await File.WriteAllTextAsync(destination, "Previous export");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SupportLogs.ExportAsync(paths, destination, cancel.Token));
        Assert.Equal("Previous export", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(root, "*.partial"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
