using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class PackageCatalogNamesTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RepositoryDisplayNameRepairsPreviouslyIndexedTechnicalId(bool wrapped)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vrcsetup-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repos = Path.Combine(directory, "repos"); Directory.CreateDirectory(repos);
            const string id = "d4rkpl4y3r.d4rkavataroptimizer";
            var repository = new { packages = new Dictionary<string, object> { [id] = new { versions = new Dictionary<string, object> { ["3.9.0"] = new { displayName = "d4rkAvatarOptimizer" } } } } };
            await File.WriteAllTextAsync(Path.Combine(repos, "repository.json"), wrapped ? JsonSerializer.Serialize(new { repo = repository }) : JsonSerializer.Serialize(repository));
            var paths = new AppPaths(Path.Combine(directory, "home"));
            var catalog = new PackageCatalog(paths, new ToolRunner(), new ConfigurationStore(paths), repos);
            await catalog.RememberAsync([new(id, id, "latest")]);
            Assert.Equal("d4rkAvatarOptimizer", (await catalog.KnownAsync()).Single(x => x.Id == id).DisplayName);
            await catalog.RememberAsync([new(id, "d4rkAvatarOptimizer", "latest")]);
            await catalog.RememberAsync([new(id, id, "latest")]);
            Assert.Equal("d4rkAvatarOptimizer", (await catalog.KnownAsync()).Single(x => x.Id == id).DisplayName);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
