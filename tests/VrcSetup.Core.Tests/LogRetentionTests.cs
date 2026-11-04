using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class LogRetentionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-log-policy-" + Guid.NewGuid().ToString("N"));
    [Fact] public async Task ActiveOperationIsBoundedAndDoesNotDuplicateUnityOutput()
    {
        var paths = new AppPaths(root);
        using var log = new OperationLog(paths, "bounded");
        log.Report(new(DateTime.UtcNow, "Unity import", "Verbose Unity sentinel"));
        for (var i = 0; i < 400; i++) log.Report(new(DateTime.UtcNow, "VPM", new string('x', 8192)));
        log.Report(new(DateTime.UtcNow, "Packages", "Important final failure", EventSeverity.Error));
        Assert.True(new FileInfo(log.Path).Length <= LogRetention.DetailBytes);
        var content = await SharedLog.ReadAsync(log.Path); Assert.Contains("Important final failure", content); Assert.DoesNotContain("Verbose Unity sentinel", content);
    }
    [Fact] public void RetentionPreservesActiveFilesAndUnrelatedDataAndLeavesValidErrorRecords()
    {
        var paths = new AppPaths(root); Directory.CreateDirectory(paths.Logs);
        for (var i = 0; i < 18; i++) File.WriteAllText(Path.Combine(paths.Logs, "old-" + i + ".log"), new string('x', 2 * 1024 * 1024));
        var unrelated = Path.Combine(paths.Logs, "notes.txt"); File.WriteAllText(unrelated, "Owner data");
        var active = Path.Combine(paths.Logs, "active.log");
        using var stream = new FileStream(active, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        stream.Write(new byte[1024]);
        var diagnostics = new DiagnosticLog(paths);
        for (var i = 0; i < 40; i++) diagnostics.Write("Fixture", new string('m', 8000), new IOException(new string('e', 32000)));
        LogRetention.Prune(paths);
        Assert.Equal("Owner data", File.ReadAllText(unrelated)); Assert.Equal(1024, stream.Length);
        foreach (var line in File.ReadLines(diagnostics.FilePath)) { using var json = JsonDocument.Parse(line); Assert.Equal("Fixture", json.RootElement.GetProperty("Area").GetString()); }
        Assert.True(new DirectoryInfo(paths.Logs).GetFiles().Where(x => x.Extension != ".txt").Sum(x => x.Length) <= LogRetention.TotalBytes);
    }
    [Fact] public void ValidationMessageExplainsTheFieldAndPackageFailureNamesThePackage()
    {
        Assert.Equal("Enter a name for the project.", ProjectRules.NameError(""));
        Assert.Equal(UiText.Text("Enter a name for the project."), UserErrors.Message(new UserInputException("ProjectName", ProjectRules.NameError("")!)));
        var error = UserErrors.Message(new IOException("Package repair failed: nadena.dev.ndmf"), _ => "NDMF");
        Assert.Contains("NDMF", error); Assert.DoesNotContain("nadena.dev.ndmf", error);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
