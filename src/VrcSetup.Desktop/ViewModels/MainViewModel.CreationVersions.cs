using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private AsyncVersionCache? creationVersionCache;
    private readonly HashSet<string> requestedCreationVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> creationVersionDisplays = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshCreationVersions()
    {
        // Preview lookup is lazy and cannot register repositories or edit package policy.
        if (CurrentPage != "Create" || lifetime.IsCancellationRequested) return;
        foreach (var row in CreationPackages.Where(x => x.Selected && x.Data.Version == "latest"))
        {
            if (creationVersionDisplays.TryGetValue(row.Id, out var display)) row.SetLatestVersion(display);
            else if (requestedCreationVersions.Add(row.Id))
            {
                row.SetLatestVersion("Checking version…");
                _ = LoadCreationVersionAsync(row.Id);
            }
        }
    }

    private async Task LoadCreationVersionAsync(string id)
    {
        var display = "Version unavailable";
        try
        {
            creationVersionCache ??= new(app.Catalog.GetVersionsForPreviewAsync, lifetime.Token);
            var versions = await creationVersionCache.GetAsync(id);
            display = SemanticVersion.Sort(versions, false).FirstOrDefault() ?? SemanticVersion.Sort(versions).FirstOrDefault() ?? display;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception error) { app.Diagnostics.Write("Package version preview", error.Message, error); }
        finally { requestedCreationVersions.Remove(id); }
        if (lifetime.IsCancellationRequested) return;
        if (display != "Version unavailable") creationVersionDisplays[id] = display;
        foreach (var row in CreationPackages.Where(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && x.Data.Version == "latest"))
            row.SetLatestVersion(display);
    }
}
