using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageInspectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VrcSetup.Inspection.Tests", Guid.NewGuid().ToString("N"));
    private const string A = "11111111111111111111111111111111", B = "22222222222222222222222222222222", C = "33333333333333333333333333333333";
    private PackageInspectionService Service(IUnityPackageInspector? reader = null) => new(new AppPaths(Path.Combine(root, "home")), reader);
    private string Archive(params (string Path, string Text)[] members)
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, Guid.NewGuid() + ".unitypackage");
        using var file = File.Create(path); using var zip = new GZipStream(file, CompressionMode.Compress); using var writer = new TarWriter(zip);
        foreach (var member in members)
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(member.Text));
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, member.Path) { DataStream = data });
        }
        return path;
    }
    [Fact] public async Task StreamHandlesPathAfterAssetAndDoesNotExtract()
    {
        var path = Archive(($"{A}/asset", $"%YAML 1.1\nm_Script: {{fileID: 1, guid: {B}, type: 3}}"), ($"{A}/pathname", "../../escape.prefab\n"));
        var report = await new UnityPackageInspector().ReadAsync(path, new(), default);
        Assert.True(report.ArchiveComplete); Assert.Equal("../../escape.prefab", report.Assets.Single().Path);
        Assert.Equal(new AssetReference(B, "script"), report.Assets.Single().References.Single());
        Assert.False(File.Exists(Path.Combine(root, "escape.prefab")));
    }
    [Fact] public async Task InternalReferencesAreNotExternalDependencies()
    {
        var path = Archive(($"{A}/asset", $"%YAML\nm_Script: {{guid: {B}}}"), ($"{A}/pathname", "Assets/Main.prefab"), ($"{B}/pathname", "Assets/Included.cs"));
        var observation = await new UnityPackageInspector().ReadAsync(path, new(), default);
        var report = DependencyAdvisor.Match(observation, new("test", [new(B, "example.package", "1.0.0", "script", "test")]));
        Assert.Empty(report.Suggestions); Assert.Equal(0, report.UnresolvedReferences);
    }
    [Theory] [InlineData("Assets/Main.prefab", DependencyConfidence.Detected)] [InlineData("Assets/Examples/Main.prefab", DependencyConfidence.Possible)]
    public async Task OptionalExamplesDoNotBecomeCertainRequirements(string asset, DependencyConfidence confidence)
    {
        var path = Archive(($"{A}/asset", $"%YAML\nm_Script: {{guid: {B}}}"), ($"{A}/pathname", asset));
        var observation = await new UnityPackageInspector().ReadAsync(path, new(), default);
        Assert.Equal(confidence, DependencyAdvisor.Match(observation, new("test", [new(B, "example.package", "1.0.0", "script", "test")])).Suggestions.Single().Confidence);
    }
    [Fact] public async Task AmbiguousGuidDoesNotPickOneOwner()
    {
        var path = Archive(($"{A}/asset", $"%YAML\nm_Shader: {{guid: {B}}}"), ($"{A}/pathname", "Assets/Main.mat"));
        var observation = await new UnityPackageInspector().ReadAsync(path, new(), default);
        var report = DependencyAdvisor.Match(observation, new("test", [new(B, "first", "1.0.0", "shader", "test"), new(B, "second", "1.0.0", "shader", "test")]));
        Assert.Equal(2, report.Suggestions.Length); Assert.All(report.Suggestions, x => Assert.Equal(DependencyConfidence.Possible, x.Confidence));
    }
    [Fact] public async Task CacheRematchesNewSignaturesWithoutReadingTheArchive()
    {
        var file = Archive(($"{A}/asset", $"%YAML\nm_Script: {{guid: {B}}}"), ($"{A}/pathname", "Assets/Main.prefab"));
        var service = Service(); Assert.Empty((await service.InspectAsync(file)).Suggestions);
        await service.RememberSignaturesAsync([new(B, "example.package", "1.0.0", "script", "test")]);
        using var denied = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        var report = await service.InspectAsync(file);
        Assert.True(report.FromCache); Assert.Equal("example.package", report.Suggestions.Single().Id);
    }
    [Fact] public async Task ManifestRequiresTheCorrectAssetPathAndPreservesRanges()
    {
        var json = "{\"name\":\"example.embedded\",\"version\":\"1.2.3\",\"vpmDependencies\":{\"example.required\":\">=2.0.0\"}}";
        var file = Archive(($"{A}/asset", json), ($"{A}/pathname", "Packages/example.embedded/package.json"), ($"{C}/asset", json), ($"{C}/pathname", "Assets/random.json"));
        var report = await Service().InspectAsync(file);
        Assert.Single(report.Observation.Assets, x => x.Manifest is not null);
        Assert.Equal(DependencyConfidence.Included, report.Suggestions.Single(x => x.Id == "example.embedded").Confidence);
        var dependency = report.Suggestions.Single(x => x.Id == "example.required");
        Assert.Equal(">=2.0.0", dependency.VersionConstraint); Assert.Null(DependencyAdvisor.SuggestedVersion(dependency));
    }
    [Fact] public async Task EntryAndTextLimitsAreReportedAndDeepHasItsOwnCache()
    {
        var file = Archive(($"{A}/asset", "%YAML\n" + new string('a', 1000)), ($"{A}/pathname", "Assets/Main.prefab"));
        var limited = await Service().InspectAsync(file, new() { EntryTextBytes = 10 });
        Assert.True(limited.Observation.ArchiveComplete); Assert.False(limited.Observation.TextComplete);
        var deep = await Service().InspectAsync(file, InspectionOptions.Extended); Assert.False(deep.FromCache); Assert.True(deep.Observation.TextComplete);
        var stopped = await Service().InspectAsync(file, new() { MaxEntries = 0 }); Assert.False(stopped.Observation.ArchiveComplete);
    }
    [Fact] public async Task InvalidGzipProducesPartialReportAndCancellationStaysCancellation()
    {
        Directory.CreateDirectory(root); var file = Path.Combine(root, "invalid.unitypackage"); await File.WriteAllTextAsync(file, "invalid gzip");
        Assert.False((await Service().InspectAsync(file)).Observation.ArchiveComplete);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().InspectAsync(file, cancellationToken: cancelled.Token));
    }
    [Fact] public async Task EmbeddedScriptsTeachThePersonalIndex()
    {
        var file = Archive(($"{A}/asset", "{\"name\":\"example.embedded\",\"version\":\"1.0.0\"}"), ($"{A}/pathname", "Packages/example.embedded/package.json"), ($"{B}/pathname", "Packages/example.embedded/Test.cs"));
        var service = Service(); await service.InspectAsync(file);
        Assert.Contains((await service.LoadIndexAsync()).Signatures, x => x.Guid == B && x.PackageId == "example.embedded");
    }
    [Fact] public async Task ChangedFileInvalidatesTheObservationCache()
    {
        var file = Archive(($"{A}/pathname", "Assets/Main.prefab")); var service = Service(); await service.InspectAsync(file);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(2)); Assert.False((await service.InspectAsync(file)).FromCache);
    }
    [Fact] public async Task StructurallyCorruptedCacheIsIgnored()
    {
        var file = Archive(($"{A}/pathname", "Assets/Test.prefab")); var service = Service(); await service.InspectAsync(file);
        var cache = Directory.GetFiles(Path.Combine(root, "home", "cache", "inspection"), "archive-*.json").Single();
        await File.WriteAllTextAsync(cache, "{\"Schema\":1,\"ArchiveComplete\":true,\"Assets\":[null],\"Limitations\":[]}");
        var report = await service.InspectAsync(file); Assert.False(report.FromCache); Assert.True(report.Observation.ArchiveComplete);
    }
    [Fact] public void OversizedVersionNumbersCannotCrashSuggestionSelection()
    {
        Assert.Null(DependencyAdvisor.SuggestedVersion(new("test.package", DependencyConfidence.Declared, "999999999999999999999.0.0", [])));
    }
    [Fact] public void ExcessAvatarCandidatesAreConservativeAndMatchingIsCancellable()
    {
        var assets = Enumerable.Range(1, 9).Select(i => new InspectedAsset(i.ToString("x32"), $"Assets/Variant{i}.prefab", [new(B, "script")], [], false)).ToArray();
        var observation = new ArchiveObservation(1, 0, 0, true, true, [], assets, 0);
        var signatures = new SignatureSnapshot("test", [new(B, ProjectRules.AvatarSdk, "1.0.0", "script", "test")]);
        var report = DependencyAdvisor.Match(observation, signatures);
        Assert.StartsWith("Multiple avatar candidates", report.Scope); Assert.Equal(DependencyConfidence.Possible, report.Suggestions.Single().Confidence);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DependencyAdvisor.Match(observation, signatures, token: cancelled.Token));
    }
    [Fact] public async Task SceneRootsAndDifferentAvatarVariantsKeepCommonPackageRequirements()
    {
        var sdk = "44444444444444444444444444444444";
        var file = Archive(($"{A}/asset", $"%YAML\nm_Script: {{guid: {sdk}}}\nm_Script: {{guid: {C}}}"), ($"{A}/pathname", "Assets/Avatar.unity"),
            ($"{B}/asset", $"%YAML\nm_Script: {{guid: {sdk}}}\nm_Script: {{guid: {C}}}"), ($"{B}/pathname", "Assets/Variant.prefab"));
        var observation = await new UnityPackageInspector().ReadAsync(file, new(), default);
        var report = DependencyAdvisor.Match(observation, new("test", [new(sdk, ProjectRules.AvatarSdk, "1.0.0", "script", "test"), new(C, "example.shared", "1.0.0", "script", "test")]));
        Assert.Equal("Shared avatar references", report.Scope);
        Assert.Equal(DependencyConfidence.Detected, report.Suggestions.Single(x => x.Id == "example.shared").Confidence);
    }
    [Fact] public async Task ACancelledConsumerDoesNotCancelTheOtherConsumer()
    {
        var file = Archive(($"{A}/pathname", "Assets/Test.prefab"));
        var reader = new WaitingReader(); var service = Service(reader); using var cancel = new CancellationTokenSource();
        var first = service.InspectAsync(file, cancellationToken: cancel.Token); await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.InspectAsync(file);
        // Keep the shared operation open long enough to exercise a genuinely pending consumer.
        await Task.Delay(100); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        reader.Release.TrySetResult();
        Assert.True((await second).Observation.ArchiveComplete);
    }
    private sealed class WaitingReader : IUnityPackageInspector
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ArchiveObservation> ReadAsync(string path, InspectionOptions options, CancellationToken token)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(token);
            var info = new FileInfo(path); return new(1, info.Length, info.LastWriteTimeUtc.Ticks, true, true, [], [], 0);
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
