using System.IO.Compression;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class SupportLogsTests : IDisposable
{
    [Fact] public async Task SuccessfulUpdateDoesNotExportItsSupersededErrorAndLargeReportRemainsJson()
    {
        var paths = new AppPaths(root); Directory.CreateDirectory(paths.Logs);
        var component = Path.Combine(root, "updates", "components"); Directory.CreateDirectory(component);
        var error = Path.Combine(component, "last-error.log"); await File.WriteAllTextAsync(error, "Old cancelled update"); File.SetLastWriteTimeUtc(error, DateTime.UtcNow.AddDays(-1));
        await File.WriteAllTextAsync(Path.Combine(root, "updates", "last-result.json"), "{\"Success\":true}");
        var report = new VrcSetup.Core.OperationReport("failed", DateTime.UtcNow,
            [new("Fixture project", root, VrcSetup.Core.OperationStatus.Failed, new string('e', 600000))], []);
        await AtomicFile.WriteJsonAsync(Path.Combine(paths.Logs, "operation-failed.json"), report);
        var destination = Path.Combine(root, "export.zip"); await SupportLogs.ExportAsync(paths, destination);
        using var zip = ZipFile.OpenRead(destination);
        Assert.Null(zip.GetEntry("Update/last-error.log"));
        using var reader = new StreamReader(zip.GetEntry("App/operation-failed.json")!.Open());
        using var parsed = System.Text.Json.JsonDocument.Parse(await reader.ReadToEndAsync());
        Assert.Equal("failed", parsed.RootElement.GetProperty("Id").GetString());
        using var summary = new StreamReader(zip.GetEntry("VRC-Project-Setup.txt")!.Open());
        Assert.Contains("Fixture project", await summary.ReadToEndAsync());
    }
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
