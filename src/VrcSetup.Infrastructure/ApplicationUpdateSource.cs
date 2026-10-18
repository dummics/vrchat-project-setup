using System.Reflection;

namespace VrcSetup.Infrastructure;

// The default provider is GitHub. An HTTPS static host can expose the same small
// releases JSON contract, independently of where component ZIPs are hosted.
public sealed record ApplicationUpdateSource
{
    public Uri Feed { get; }
    public Uri Repository { get; }
    public string AssetPrefix { get; }
    public ApplicationUpdateSource(string feed, string repository, string assets)
    {
        static Uri Https(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Fragment != "")
                throw new ArgumentException("Update hosting must use HTTPS without credentials.");
            return uri;
        }
        Feed = Https(feed); Repository = Https(repository); var assetUri = Https(assets);
        if (assetUri.Query != "" || !assetUri.AbsolutePath.EndsWith('/')) throw new ArgumentException("Asset prefix must be an HTTPS directory.");
        AssetPrefix = assetUri.AbsoluteUri;
    }
    public static ApplicationUpdateSource Default { get; } = CreateDefault();
    private static ApplicationUpdateSource CreateDefault()
    {
        var metadata = typeof(ApplicationUpdateSource).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        string Setting(string name, string fallback) => metadata.FirstOrDefault(x => x.Key == name)?.Value is { Length: > 0 } value ? value : fallback;
        return new(Setting("UpdateFeedUri", "https://raw.githubusercontent.com/dummics/vrchat-project-setup/main/.github/updates/releases.json"),
            ApplicationUpdates.Repository, Setting("UpdateAssetPrefix", ApplicationUpdates.Repository + "/releases/download/"));
    }
    public bool AllowsAsset(string? value, out Uri? uri)
    {
        uri = null;
        return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == ""
            && uri.Fragment == "" && uri.Query == "" && uri.AbsoluteUri.StartsWith(AssetPrefix, StringComparison.Ordinal);
    }
}
