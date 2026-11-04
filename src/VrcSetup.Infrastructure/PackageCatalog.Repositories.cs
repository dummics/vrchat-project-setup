using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed partial class PackageCatalog
{
    private readonly SemaphoreSlim repositoryGate = new(1);
    private readonly HashSet<string> preparedRepositories = new(StringComparer.OrdinalIgnoreCase);
    // Source hints are consulted only for a package the user has selected.
    // They do not install packages or add optional packages to fresh defaults.
    private static readonly Dictionary<string, string> KnownPackageSources = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProjectRules.EasyLogin] = "https://foxscore.dev/vpm/index.json",
        ["com.vrcfury.vrcfury"] = "https://vcc.vrcfury.com/",
        ["gogoloco"] = "https://spokeek.github.io/goloco/index.json",
        ["adjerry91.vrcft.templates"] = "https://adjerry91.github.io/VRCFaceTracking-Templates/index.json",
        ["com.poiyomi.toon"] = "https://poiyomi.github.io/vpm/index.json",
        ["com.poiyomi.pro"] = "https://poiyomi.github.io/vpm/index.json",
        ["com.poiyomi.thryeditor"] = "https://poiyomi.github.io/vpm/index.json"
    };
    private async Task<bool> EnsurePackageSourceAsync(string id, CancellationToken token)
    {
        if (!allowRepositorySetup || !KnownPackageSources.TryGetValue(id, out var url) || paths.VrcGetPath is null) return false;
        await repositoryGate.WaitAsync(token);
        try
        {
            if (preparedRepositories.Contains(url)) return true;
            var repos = await runner.VpmAsync(["list", "repos"], cancellationToken: token);
            if (repos.ExitCode != 0) throw new IOException("Could not load package sources. Check your connection and try again.");
            if (!repos.Output.Contains(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                var added = await runner.VpmAsync(["add", "repo", url], cancellationToken: token);
                if (added.ExitCode != 0) throw new IOException("Could not prepare the package source for " + FriendlyName(id) + ". Check your connection and try again.");
            }
            preparedRepositories.Add(url); return true;
        }
        finally { repositoryGate.Release(); }
    }
}
