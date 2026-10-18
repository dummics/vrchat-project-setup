using System.Net;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.Verification;

public sealed record UpdateSmokeSpec(string Root, string Installer, string Version, string SettingsHash, bool UseRealNetwork = false);

// Explicit developer entry point. Only its HTTP transport is replaced; the GUI
// commands, hash verification, installer handoff and restart are production paths.
public static class UpdateSmokeTest
{
    public static async Task<UpdateSmokeSpec> ReadSpecAsync(string path)
    {
        var spec = await AtomicFile.ReadJsonAsync<UpdateSmokeSpec>(path) ?? throw new IOException("Update test specification missing.");
        var root = ProjectRules.NormalizePath(spec.Root);
        if (root != spec.Root || !File.Exists(Path.Combine(root, ".vrcsetup-update-smoke"))) throw new IOException("Unrecognized update test folder.");
        ProjectRules.EnsureContained(root, ProjectRules.NormalizePath(path));
        var expectedInstall = Path.Combine(root, "install");
        if (!new AppPaths(Path.Combine(root, "home")).PayloadRoot.Equals(expectedInstall, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Update verification must run from its isolated installed copy.");
        _ = new SemanticVersion(spec.Version);
        if (!File.Exists(spec.Installer)) throw new IOException("Update test installer missing.");
        return spec;
    }

    public static VrcSetupApplication CreateApplication(UpdateSmokeSpec spec, string path) => new(Path.Combine(spec.Root, "home"), ["--no-catalog-refresh", "--no-update-check", "--no-editor-discovery"])
    {
        UpdateSmokeSpec = path,
        UpdateHttpClientFactory = spec.UseRealNetwork ? null : () => new HttpClient(new FileReleaseHandler(spec))
    };

    public static async Task RunAsync(MainWindow window, UpdateSmokeSpec spec)
    {
        await window.Page.Initialized;
        var vm = window.Page.ViewModel;
        if (vm.ErrorMessage.Length > 0) throw new IOException(vm.ErrorMessage);
        await vm.CheckApplicationUpdatesCommand.ExecuteAsync(null);
        if (!vm.HasApplicationUpdate) throw new IOException("Update test did not discover a newer release: " + vm.UpdateStatus);
        await UiSmokeTest.Render(window.Page, Path.Combine(spec.Root, "before-update.png"));
        await AtomicFile.WriteJsonAsync(Path.Combine(spec.Root, "handoff.json"), new { Version = vm.AppVersion, Target = spec.Version, Process = Environment.ProcessId, HttpTransport = spec.UseRealNetwork ? "Public GitHub Releases" : "Isolated file fixture", UsesProductionInstallCommand = true });
        await vm.InstallApplicationUpdateCommand.ExecuteAsync(null);
        if (!vm.IsInstallingUpdate) throw new IOException("Update handoff did not start: " + vm.UpdateStatus);
    }

    public static async Task PrepareBackgroundAsync(MainWindow window, UpdateSmokeSpec spec)
    {
        await window.Page.Initialized;
        var vm = window.Page.ViewModel;
        var settings = AtomicFile.Hash(vm.Application.Paths.ConfigFile);
        await vm.RunBackgroundUpdateCheckAsync();
        var ready = (await new AutomaticUpdates(vm.Application.Paths).ReadAsync()).Ready;
        if (!vm.IsUpdateReady || ready?.Version != spec.Version) throw new IOException("Background update was not prepared: " + vm.UpdateStatus);
        if (settings != AtomicFile.Hash(vm.Application.Paths.ConfigFile)) throw new IOException("Background preparation changed settings.");
        await new InstallationManager(vm.Application.Paths).ValidatePayloadAsync(vm.Application.Paths.PayloadRoot);
        await UiSmokeTest.Render(window.Page, Path.Combine(spec.Root, "background-ready.png"));
        await AtomicFile.WriteJsonAsync(Path.Combine(spec.Root, "prepared.json"), new { Passed = true, ready.Version, SettingsPreserved = true, OldPayloadHashesVerified = true, UsesProductionBackgroundCheck = true });
    }

    public static async Task VerifyAsync(MainWindow window, UpdateSmokeSpec spec)
    {
        await window.Page.Initialized;
        var vm = window.Page.ViewModel;
        if (vm.AppVersion != spec.Version) throw new IOException("Relaunched application has the wrong version: " + vm.AppVersion);
        if (AtomicFile.Hash(vm.Application.Paths.ConfigFile) != spec.SettingsHash) throw new IOException("Update changed the fixture settings.");
        await new InstallationManager(vm.Application.Paths).ValidatePayloadAsync(vm.Application.Paths.PayloadRoot);
        var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(vm.Application.Paths.PayloadRoot, ".vrcsetup-install.json"));
        if (record?.Version != spec.Version || record.PreviousInstall is null || !Directory.Exists(record.PreviousInstall)) throw new IOException("Updated installation or previous-program backup missing.");
        var result = await AtomicFile.ReadJsonAsync<JsonElement>(Path.Combine(vm.Application.Paths.Home, "updates", "last-result.json"));
        if (!result.GetProperty("Success").GetBoolean()) throw new IOException("Installer did not report success.");
        await vm.CheckApplicationUpdatesCommand.ExecuteAsync(null);
        if (vm.HasApplicationUpdate) throw new IOException("The same update was offered again after restart.");
        await UiSmokeTest.Render(window.Page, Path.Combine(spec.Root, "after-update.png"));
        await AtomicFile.WriteJsonAsync(Path.Combine(spec.Root, "passed.json"), new
        {
            Passed = true, Version = vm.AppVersion, RelaunchedProcess = Environment.ProcessId,
            SettingsPreserved = true, PayloadHashesVerified = true, PreviousInstallPreserved = true,
            NoRepeatedUpdate = true, RealNetworkUsed = spec.UseRealNetwork, RealInstallerUsed = true,
            Root = vm.Application.Paths.PayloadRoot, Home = vm.Application.Paths.Home
        });
    }

    private sealed class FileReleaseHandler(UpdateSmokeSpec spec) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = ApplicationUpdates.Repository + "/releases/download/v" + spec.Version + "/";
            var name = "Install-VrcSetup-" + spec.Version + "-win-x64.exe";
            HttpContent content;
            if (request.RequestUri!.AbsoluteUri == "https://api.github.com/repos/dummics/vrchat-project-setup/releases?per_page=100")
                content = new StringContent(JsonSerializer.Serialize(new[] { new { tag_name = "v" + spec.Version, draft = false, prerelease = true, assets = new[] { new { name, browser_download_url = root + name }, new { name = "SHA256SUMS.txt", browser_download_url = root + "SHA256SUMS.txt" }, new { name = "components.json", browser_download_url = root + "components.json" } } } }));
            else if (request.RequestUri.AbsoluteUri == root + "SHA256SUMS.txt")
                content = new StringContent(File.ReadAllText(Path.Combine(Path.GetDirectoryName(spec.Installer)!, "SHA256SUMS.txt")));
            else if (request.RequestUri.AbsoluteUri == root + "components.json")
                content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(spec.Installer)!, "components.json")));
            else if (request.RequestUri.AbsoluteUri == root + name)
            {
                content = new StreamContent(File.OpenRead(spec.Installer));
                content.Headers.ContentLength = new FileInfo(spec.Installer).Length;
            }
            else throw new IOException("Unexpected request in isolated update test: " + request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
