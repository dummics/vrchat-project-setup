using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public Visibility LibraryPendingVisibility => BrowsingProjectPackages && WorkspaceDirty ? Visibility.Visible : Visibility.Collapsed;
    private PackageWorkspaceRow[] PendingRows => session?.Rows().Where(x => x.Action.StartsWith("Will be", StringComparison.Ordinal)).ToArray() ?? [];
    public string WorkspaceFriendlySummary => string.Join(" · ", new[] { ("Will be added", "To add"), ("Will be changed", "To update"), ("Will be removed", "To remove") }
        .Select(x => (Label: x.Item2, Count: PendingRows.Count(row => row.Action == x.Item1))).Where(x => x.Count > 0).Select(x => UiText.Text(x.Label) + ": " + x.Count));
    public string WorkspaceFriendlyNames => string.Join(" · ", PendingRows.Take(5).Select(x => PackageDisplayName(x.Id))) + (PendingRows.Length > 5 ? " …" : "");
}
