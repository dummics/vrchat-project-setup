using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class StorageContractTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-net-tests-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(root, "home");
    private string Project => Path.Combine(root, "Project [avatar] & café");
    private VrcSetupApplication App => new(Home);
    public StorageContractTests()
    {
        Directory.CreateDirectory(Path.Combine(Project, "Assets")); Directory.CreateDirectory(Path.Combine(Project, "Packages", ProjectRules.BaseSdk)); Directory.CreateDirectory(Path.Combine(Project, "ProjectSettings"));
        File.WriteAllText(Path.Combine(Project, "Packages", "vpm-manifest.json"), "{\"dependencies\":{\"com.vrchat.base\":{\"version\":\"3.10.5\"},\"tool\":\"1.0.0\"},\"locked\":{\"com.vrchat.base\":{\"version\":\"3.10.5\"}}}");
        File.WriteAllText(Path.Combine(Project, "Packages", "manifest.json"), "{\"dependencies\":{}}");
        File.WriteAllText(Path.Combine(Project, "Packages", ProjectRules.BaseSdk, "package.json"), "{\"name\":\"com.vrchat.base\",\"version\":\"3.10.5\"}");
        File.WriteAllText(Path.Combine(Project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
    }
    [Fact] public async Task ObjectAndStringManifestVersionsRemainReadable()
    {
        var snapshot = await App.Projects.ReadAsync(Project);
        Assert.Equal("3.10.5", snapshot.Packages[ProjectRules.BaseSdk]); Assert.Equal("1.0.0", snapshot.Packages["tool"]);
        Assert.Equal("2022.3.22f1", snapshot.UnityVersion); Assert.Equal("3.10.5", snapshot.InstalledVersions[ProjectRules.BaseSdk]);
    }
    [Fact] public void InstallerDataHomeDoesNotFollowThePortableExtractionMarker()
    {
        var payload = Path.Combine(root, "installer-extraction"); Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, ".vrcsetup-portable"), "portable");
        var paths = AppPaths.ForInstallation(null, payload);
        Assert.NotEqual(Path.Combine(payload, "data"), paths.Home);
        Assert.Equal(ProjectRules.NormalizePath(Environment.GetEnvironmentVariable("VRCSETUP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VrcSetup")), paths.Home);
        Assert.Equal(ProjectRules.NormalizePath(Path.Combine(root, "isolated-data")), AppPaths.ForInstallation(Path.Combine(root, "isolated-data"), payload).Home);
    }
    [Fact] public async Task MissingCanonicalMetadataIsMarkedRecovered()
    {
        File.Delete(Path.Combine(Project, "Packages", "vpm-manifest.json")); File.Delete(Path.Combine(Project, "ProjectSettings", "ProjectVersion.txt"));
        File.WriteAllText(Path.Combine(Project, "Assembly-CSharp.csproj"), "<Project><PropertyGroup><UnityVersion>2022.3.22f1</UnityVersion></PropertyGroup></Project>");
        var snapshot = await App.Projects.ReadAsync(Project);
        Assert.Equal("EmbeddedPackages", snapshot.PackageMetadataSource); Assert.Equal("GeneratedProject", snapshot.UnityVersionSource); Assert.Equal("Recovered", snapshot.Status);
    }
    [Fact] public async Task InvalidManifestHasAVisibleState()
    {
        File.WriteAllText(Path.Combine(Project, "Packages", "vpm-manifest.json"), "broken");
        Assert.Equal("Recovered (invalid manifest)", (await App.Projects.ReadAsync(Project)).Status);
    }
    [Fact] public async Task FingerprintDetectsChangesEvenWithSameSizeAndTimestamp()
    {
        var app = App; var before = await app.Projects.ReadAsync(Project);
        var file = Path.Combine(Project, "Packages", "vpm-manifest.json"); var time = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, File.ReadAllText(file).Replace("1.0.0", "1.0.1")); File.SetLastWriteTimeUtc(file, time);
        Assert.NotEqual(before.Fingerprint, (await app.Projects.ReadAsync(Project)).Fingerprint);
    }
    [Fact] public async Task ScannerExcludesLibraryAndFindsNestedProjects()
    {
        Directory.CreateDirectory(Path.Combine(root, "Library", "Hidden", "Assets")); Directory.CreateDirectory(Path.Combine(root, "Library", "Hidden", "Packages"));
        var snapshots = await App.Projects.ScanAsync(root);
        Assert.Single(snapshots); Assert.Equal(Project, snapshots[0].Path);
    }
    [Fact] public async Task CacheHandlesStateAndProfileChanges()
    {
        var app = App; await app.Configuration.LoadAsync();
        Assert.False((await app.Projects.ScanAsync(root))[0].Incomplete);
        await app.Projects.SetStepAsync(Project, "vpm", false, default);
        Assert.True((await app.Projects.ScanAsync(root))[0].Incomplete);
    }
    [Fact] public async Task ConfigurationRevisionPreventsLostUpdates()
    {
        var store = App.Configuration; var first = await store.LoadAsync(); var second = await store.LoadAsync();
        first.Theme = "Dark"; await store.SaveAsync(first); second.Theme = "Light";
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(second));
        Assert.Equal("Dark", (await store.LoadAsync()).Theme);
    }
    [Fact] public async Task AtomicConfigurationRoundTripPreservesPinsAndUnknownSettings()
    {
        var store = App.Configuration; var config = await store.LoadAsync();
        config.PackagePresets["Creator"] = new() { ["tool"] = "1.0.0", ["other"] = "latest" };
        config.AdditionalSettings = new() { ["futureOption"] = JsonSerializer.SerializeToElement("keep") };
        await store.SaveAsync(config); var saved = await store.LoadAsync();
        Assert.Equal("1.0.0", saved.PackagePresets["Creator"]["tool"]); Assert.Equal("latest", saved.PackagePresets["Creator"]["other"]); Assert.Equal("keep", saved.AdditionalSettings!["futureOption"].GetString());
    }
    [Fact] public async Task HeldUnityLockfileMarksProjectBusy()
    {
        Directory.CreateDirectory(Path.Combine(Project, "Temp")); using var held = new FileStream(Path.Combine(Project, "Temp", "UnityLockfile"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Assert.True(App.Projects.IsBusy(Project)); await Task.CompletedTask;
    }
    [Fact] public async Task ManifestBackupRecordsAbsentFilesAndRestoreUsesResolver()
    {
        var app = App; var plan = new ProjectPlan { Path = Project, Name = "test", ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [] };
        var record = await app.Projects.BackupAsync(plan, default);
        Assert.Null(record.ManifestFiles["Packages/packages-lock.json"]);
        File.WriteAllText(Path.Combine(Project, "Packages", "packages-lock.json"), "{\"dependencies\":{}}");
        File.WriteAllText(Path.Combine(Project, "Packages", "vpm-manifest.json"), "changed");
        app.Projects.Resolve = (_, _, _) => Task.FromResult(new ToolResult(0, "", ""));
        Assert.True(await app.Projects.RestoreAsync(Project, record, null, default));
        Assert.False(File.Exists(Path.Combine(Project, "Packages", "packages-lock.json"))); Assert.Contains("3.10.5", File.ReadAllText(Path.Combine(Project, "Packages", "vpm-manifest.json")));
    }
    [Fact] public async Task CleanupNeedsConfirmationAndIncompleteMarker()
    {
        var app = App; await app.Projects.SetStepAsync(Project, "vpm", false, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Projects.CleanupAsync(Project, root, false));
        Assert.True(Directory.Exists(Project));
        await app.Projects.CleanupAsync(Project, root, true); Assert.False(Directory.Exists(Project));
    }
    [Fact] public async Task MaliciousRestoreDestinationIsRejected()
    {
        var app = App; var backup = Path.Combine(Project, ".vrcsetup", "backups", "test"); Directory.CreateDirectory(backup);
        var record = new BackupRecord(backup, new Dictionary<string, string?> { ["../outside.json"] = null });
        await Assert.ThrowsAsync<IOException>(() => app.Projects.RestoreAsync(Project, record, null, default));
    }
    [Fact] public async Task RegistryMetadataRefreshPreservesChosenVersion()
    {
        var app = App; await app.Catalog.RememberAsync([new("tool", "Tool", "1.0.0") { SelectedVersion = "1.0.0", Versions = ["1.0.0"] }]);
        await app.Catalog.RememberAsync([new("tool", "Tool", "2.0.0") { SelectedVersion = "latest", Versions = ["1.0.0", "2.0.0"] }], preserveSelections: true);
        var definition = (await app.Catalog.KnownAsync()).Single(x => x.Id == "tool");
        Assert.Equal("1.0.0", definition.SelectedVersion); Assert.Contains("2.0.0", definition.Versions);
    }
    [Fact] public void PathNormalizationKeepsLiteralSpecialCharacters() => Assert.Equal(Project, ProjectRules.NormalizePath("\"" + Project + "\""));
    [Fact] public void DriveRootKeepsItsDirectorySeparator()
    { var drive = Path.GetPathRoot(root)!; Assert.Equal(drive, ProjectRules.NormalizePath(drive)); }
    [Fact] public async Task LatestPolicySurvivesProfileRoundTrip()
    {
        var app = App;
        var plan = new ProjectPlan { Path = Project, Name = "test", ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string> { [ProjectRules.BaseSdk] = "latest" }, Changes = [], RequiredPackages = [ProjectRules.BaseSdk] };
        await app.Projects.SaveProfileAsync(plan, default);
        Assert.Equal("latest", (await app.Projects.ReadAsync(Project)).Policy[ProjectRules.BaseSdk]);
    }
    [Fact] public async Task CorruptConfigurationCanBeResetWithItsOriginalPreserved()
    {
        var app = App; await app.Configuration.LoadAsync(); File.WriteAllText(app.Paths.ConfigFile, "invalid-json");
        await app.Configuration.ResetAsync();
        Assert.Equal(2, (await app.Configuration.LoadAsync()).SchemaVersion);
        Assert.Contains(Directory.EnumerateFiles(Path.Combine(app.Paths.Home, "migration")), x => File.ReadAllText(x) == "invalid-json");
    }
    [Fact] public async Task CancelledAssetRestoreLeavesCurrentAssetsIntact()
    {
        var app = App; var marker = Path.Combine(Project, "Assets", "marker.txt"); File.WriteAllText(marker, "original");
        var plan = new ProjectPlan { Path = Project, Name = "test", ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [], ExtraImports = ["test"] };
        var backup = await app.Projects.BackupAsync(plan, default); File.WriteAllText(marker, "current");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Projects.RestoreAsync(Project, backup, null, cancellation.Token));
        Assert.Equal("current", File.ReadAllText(marker));
    }
    [Fact] public async Task ResolveFailureRollsBackTheRestoreItself()
    {
        var app = App; var marker = Path.Combine(Project, "Assets", "marker.txt"); File.WriteAllText(marker, "original");
        var plan = new ProjectPlan { Path = Project, Name = "test", ExpectedFingerprint = "", DesiredPolicy = new Dictionary<string, string>(), Changes = [], ExtraImports = ["test"] };
        var backup = await app.Projects.BackupAsync(plan, default); File.WriteAllText(marker, "current");
        app.Projects.Resolve = (_, _, _) => Task.FromResult(new ToolResult(1, "failed", ""));
        await Assert.ThrowsAsync<IOException>(() => app.Projects.RestoreAsync(Project, backup, null, default));
        Assert.Equal("current", File.ReadAllText(marker));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
