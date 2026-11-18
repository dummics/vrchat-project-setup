using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal sealed class ReactUpdateVerification(string root)
{
    private readonly string specification = Path.Combine(root, "spec.json");
    private readonly string delivery = Path.Combine(root, "delivery");
    private readonly string legacyFeed = ApplicationUpdateSource.Default.Feed.AbsoluteUri;
    private string installerName = "";
    private string version = "";
    private bool corruptInstaller = true;
    private readonly List<string> requests = [];

    internal HttpClient CreateClient() => new(new FixtureHandler(Respond));

    internal async Task VerifyAsync(CoreWebView2 core, VrcSetupApplication engine, CancellationToken token)
    {
        if (!File.Exists(Path.Combine(root, ".vrcsetup-update-smoke")))
            throw new IOException("Update fixture marker missing.");
        using var spec = JsonDocument.Parse(await File.ReadAllTextAsync(specification, token));
        if (!root.Equals(spec.RootElement.GetProperty("Root").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Update specification does not match the fixture.");
        version = spec.RootElement.GetProperty("Version").GetString() ?? "";
        var build = new ApplicationBuild(version);
        if (build.CompareTo(new(ApplicationUpdates.CurrentVersion)) <= 0 || version != Path.GetFileName(version))
            throw new IOException("Choose a newer fixture build.");
        installerName = $"Install-VrcSetup-{build.AssetId}-win-x64.exe";
        var folder = Path.Combine(engine.Paths.Home, "updates", version);
        var installer = Path.Combine(folder, installerName);
        if (Directory.Exists(folder) || (await new AutomaticUpdates(engine.Paths).ReadAsync(token)).Ready is not null)
            throw new IOException("Run the feed verifier on a fresh fixture.");

        var configuration = await engine.Configuration.LoadAsync(token);
        if (configuration.IncludeAlphaUpdates || engine.Imports.HasActiveJobs)
            throw new IOException("Begin with alpha opt-in off and no running imports.");

        const string check = """document.querySelector('[data-update-action="check"]')""";
        const string prepare = """document.querySelector('[data-update-action="prepare"]')""";
        const string apply = """document.querySelector('[data-update-action="apply"]')""";
        Task Require(string condition, int attempts = 30) => Program.RequireAsync(core, condition, attempts);
        Task Click(string selector) => core.ExecuteScriptAsync(selector + ".click()");

        var settings = JsonSerializer.Serialize(UiText.Text("Settings"));
        await Click($"Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === {settings})");
        await Require(check + "?.disabled === false");
        await Click(check);
        await Require(check + "?.disabled === false && document.querySelector('footer [role=status]')?.textContent.length > 0");
        if (requests.Count != 1 || requests[0] != "builds")
            throw new IOException("The default channel did not use the React release feed.");
        await Require(prepare + " === null");

        await Click("document.querySelector('#includeAlphaUpdates')");
        await Require(check + "?.disabled === true");
        await Click("document.querySelector('.view form button[type=submit]')");
        await Require(check + "?.disabled === false");
        await Click(check);
        await Require(prepare + "?.disabled === false");
        if (requests.Count != 2 || requests[1] != "builds")
            throw new IOException("Alpha opt-in did not use the React release feed.");

        await Click(prepare);
        await Require(prepare + "?.disabled === false && document.querySelector('footer [role=status]')?.textContent.length > 0", 240);
        if (requests.Count(x => x == installerName) != 1 || File.Exists(installer) ||
            File.Exists(Path.Combine(folder, "prefetch-result.json")) ||
            (await new AutomaticUpdates(engine.Paths).ReadAsync(token)).Ready is not null)
            throw new IOException("The corrupt installer was not safely rejected.");
        corruptInstaller = false;
        await Click(prepare);
        await Require(apply + "?.disabled === false", 1800);

        var ready = (await new AutomaticUpdates(engine.Paths).ReadAsync(token)).Ready;
        var downloadedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installer, token)));
        var sourceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(delivery, installerName), token)));
        if (ready?.Version != version || downloadedHash != sourceHash || ready.Sha256 != sourceHash ||
            requests.Count(x => x == installerName) != 2 || !File.Exists(Path.Combine(folder, "prefetch-result.json")))
            throw new IOException("The real bootstrapper did not prepare the downloaded installer.");

        var configHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(engine.Paths.ConfigFile, token)));
        await AtomicFile.WriteJsonAsync(Path.Combine(root, "feed-handoff.json"), new
        {
            FromVersion = ApplicationUpdates.CurrentVersion, TargetVersion = version, ConfigHash = configHash,
            CorruptInstallerRejected = true, PreparedInstallerHash = downloadedHash, Requests = requests,
            ComponentTransport = "Hash-verified fixture cache"
        }, token);
        Environment.SetEnvironmentVariable("VRCSETUP_UPDATE_SMOKE_SPEC", specification);
        Environment.SetEnvironmentVariable("VRCSETUP_SKIP_PATH_UPDATE", "1");
        Environment.SetEnvironmentVariable("VRCSETUP_START_MENU_ROOT", Path.Combine(root, "start-menu"));
        Environment.SetEnvironmentVariable("VRCSETUP_DESKTOP_ROOT", Path.Combine(root, "desktop"));
        try { await Click(apply); }
        catch (InvalidOperationException) when (token.IsCancellationRequested) { }
    }

    private HttpContent Respond(Uri uri)
    {
        var numberedFeed = new Uri(new Uri(legacyFeed), "releases-builds.json").AbsoluteUri;
        var prefix = ApplicationUpdateSource.Default.AssetPrefix + Uri.EscapeDataString("v" + version) + "/";
        if (uri.AbsoluteUri == numberedFeed)
        {
            requests.Add("builds");
            return new StringContent(JsonSerializer.Serialize(new[]
            {
                new { tag_name = "v" + version, draft = false, prerelease = true,
                    assets = new[] { installerName, "components.json", "SHA256SUMS.txt" }
                        .Select(name => new { name, browser_download_url = prefix + name }) }
            }));
        }
        foreach (var name in new[] { installerName, "components.json", "SHA256SUMS.txt" })
        {
            if (uri.AbsoluteUri != prefix + name) continue;
            requests.Add(name);
            if (name == installerName && corruptInstaller) return new ByteArrayContent([1, 2, 3]);
            return new StreamContent(File.OpenRead(Path.Combine(delivery, name)));
        }
        throw new IOException("Unexpected fixture request: " + uri);
    }

    private sealed class FixtureHandler(Func<Uri, HttpContent> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = respond(request.RequestUri ?? throw new IOException("Missing fixture request URI.")) });
        }
    }
}
