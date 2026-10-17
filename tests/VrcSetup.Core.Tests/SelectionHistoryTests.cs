using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class SelectionHistoryTests
{
    [Fact] public void MergedPreviewPreservesPinsAndDoesNotPromoteWeakOrBundledEvidence()
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["com.vrcfury.vrcfury"] = "1.1279.0" };
        var hints = new[] {
            new DependencySuggestion("COM.VRCFURY.VRCFURY", DependencyConfidence.Detected, null, []),
            new DependencySuggestion("exact", DependencyConfidence.Declared, "2.1.0", []),
            new DependencySuggestion("range", DependencyConfidence.Declared, ">=2.0.0", []),
            new DependencySuggestion("bundled", DependencyConfidence.Included, "1.0.0", []),
            new DependencySuggestion("maybe", DependencyConfidence.Possible, null, []),
            new DependencySuggestion(ProjectRules.WorldSdk, DependencyConfidence.Declared, "3.0.0", []) };
        var report = new PackageInspectionReport(new(1, 0, 0, true, true, [], [], 0), "test", "test", 0, hints, false);
        var preview = DependencyAdvisor.CombineSelection(selected, report);
        Assert.Equal(3, preview.Length); Assert.Equal("1.1279.0", preview.Single(x => x.Selected).Version);
        Assert.Equal("2.1.0", preview.Single(x => x.Id == "exact").Version);
        Assert.Null(DependencyAdvisor.SuggestedVersion(preview.Single(x => x.Id == "range").Evidence!));
        Assert.Single(selected); Assert.Equal("1.1279.0", selected.Values.Single());
    }
    [Fact] public async Task DurableHistoryReadsOldAndStructuredReportsAndSkipsMalformedEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "VrcSetup.History.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root); Directory.CreateDirectory(paths.Logs);
            var project = new ProjectPlan { Name = "Avatar", Path = Path.Combine(root, "Avatar"), ExpectedFingerprint = "fixture", DesiredPolicy = new Dictionary<string, string> { ["tool"] = "1.0.0" }, Changes = [] };
            var plan = new OperationPlan("structured", DateTime.UtcNow, [project]);
            var report = new OperationReport(plan.Id, DateTime.UtcNow, [new("Avatar", project.Path, OperationStatus.Completed, "Done")], []) { Plan = plan };
            await AtomicFile.WriteJsonAsync(Path.Combine(paths.Logs, "operation-structured.json"), report);
            await AtomicFile.WriteJsonAsync(Path.Combine(paths.Logs, "operation-old.json"), report with { Id = "old", Plan = null });
            await File.WriteAllTextAsync(Path.Combine(paths.Logs, "operation-broken.json"), "{");
            await File.WriteAllTextAsync(Path.Combine(paths.Logs, "operation-invalid.json"), "{\"id\":\"invalid\",\"results\":[null],\"notProcessed\":[]}");
            var history = await new ActivityHistory(paths).ReadAsync();
            Assert.Equal(2, history.Length);
            Assert.Equal("1.0.0", history.Single(x => x.Id == "structured").Plan!.Projects[0].DesiredPolicy["tool"]);
            Assert.Null(history.Single(x => x.Id == "old").Plan);
            Assert.All(history, x => Assert.True(File.Exists(x.ReportFile)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
