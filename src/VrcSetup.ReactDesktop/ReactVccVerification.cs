using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactVccVerification
{
    private const string ProjectName = "ReactVccProbe";
    private const string JobId = "hosted-vcc-probe";

    internal static bool IsHostedFixture(string folder)
    {
        var temp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        return Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted" &&
            !string.IsNullOrWhiteSpace(temp) &&
            string.Equals(Path.GetDirectoryName(folder), Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task SeedAsync(VrcSetupApplication engine, string folder, AppConfiguration configuration, CancellationToken token)
    {
        var root = Path.Combine(folder, "projects");
        if (!configuration.UnityProjectsRoot.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            configuration.Language != "it" || configuration.AutoHideCompletedImports)
            throw new IOException("Unexpected settings in the hosted VCC fixture.");
        var project = Path.Combine(root, ProjectName);
        var journal = Path.Combine(engine.Paths.Home, "imports.json");
        if (Directory.Exists(project) || File.Exists(journal)) throw new IOException("Use a fresh VCC fixture.");
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(project, "Packages"));
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1\n", token);
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "manifest.json"), """{"dependencies":{}}""", token);
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), """{"dependencies":{},"locked":{}}""", token);
        var source = new CreationRequest("", ProjectName, project, "", "StandaloneWindows64", ["StandaloneWindows64"], []) { EmptyProject = true };
        var plan = new OperationPlan(JobId, DateTime.UtcNow, [new()
        {
            Path = project, Name = ProjectName, DesiredPolicy = new Dictionary<string, string>(), Changes = [], ExpectedFingerprint = ""
        }]);
        var job = new ImportJob(JobId, plan, source, "Completed", "Finish", 100, "", "") { CreatorCompanionPending = true };
        await AtomicFile.WriteJsonAsync(journal, new[] { job }, token);
    }

    internal static async Task RunAsync(CoreWebView2 core, VrcSetupApplication engine, string folder, CancellationToken token)
    {
        const string row = "document.getElementById('job-" + JobId + "')";
        const string retry = """Array.from(""" + row + """?.querySelectorAll('button') ?? []).find(button => button.textContent === 'Aggiungi a Creator Companion')""";
        await Program.RequireAsync(core, retry + "?.disabled === false");
        await core.ExecuteScriptAsync(retry + ".click()");
        await Program.RequireAsync(core, """
            document.querySelector('footer [role=status]')?.textContent.includes('Puoi aggiungerlo lì come progetto esistente.')
            """, attempts: 400);
        var pending = engine.Imports.Snapshot().Single(job => job.Id == JobId);
        if (pending.State != "Completed" || !pending.CreatorCompanionPending)
            throw new IOException("VCC rejection changed the completed import.");

        var manifest = Path.Combine(pending.ProjectPath, "Packages", "vpm-manifest.json");
        var originalManifest = JsonNode.Parse(await File.ReadAllTextAsync(manifest, token));
        Directory.CreateDirectory(Path.Combine(pending.ProjectPath, "Assets"));
        var asset = Path.Combine(pending.ProjectPath, "Assets", "keep.txt");
        await File.WriteAllTextAsync(asset, "Fixture asset to preserve during registration.", token);
        var assetHash = AtomicFile.Hash(asset);
        await Program.RequireAsync(core, retry + "?.disabled === false");
        await core.ExecuteScriptAsync(retry + ".click()");
        await Program.RequireAsync(core, retry + " === undefined && " + row + "?.textContent.includes('Completata')", attempts: 400);
        var persisted = await AtomicFile.ReadJsonAsync<ImportJob[]>(Path.Combine(engine.Paths.Home, "imports.json"), token);
        var listed = await new ToolRunner().VpmAsync(["list", "projects"], cancellationToken: token);
        if (persisted?.Single(job => job.Id == JobId) is not { State: "Completed", CreatorCompanionPending: false })
            throw new IOException("The completed status and successful registration were not persisted.");
        if (listed.ExitCode != 0 || !listed.Output.Contains(ProjectName, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"VPM did not list the registered fixture (exit {listed.ExitCode}). {listed.Output}");
        var currentManifest = JsonNode.Parse(await File.ReadAllTextAsync(manifest, token));
        if (!JsonNode.DeepEquals(currentManifest?["dependencies"], originalManifest?["dependencies"]) ||
            !JsonNode.DeepEquals(currentManifest?["locked"], originalManifest?["locked"]))
            throw new IOException($"VPM changed the fixture dependencies. Before: {originalManifest}. After: {currentManifest}.");
        if (AtomicFile.Hash(asset) != assetHash)
            throw new IOException("Registration changed the fixture asset.");
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "react-vcc-registration.json"), new
        {
            Passed = true, RecoverableRejection = true, CompletedImportPreserved = true,
            RetryPersisted = true, ListedByVpm = true, PackagePolicyPreserved = true
        }, token);
    }
}
