using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VrcSetup_Desktop;

// Package edits can change the height of the section above the clicked card.
// Keep that card in place instead of jumping to a newly selected managed row.
internal static class PackageScrollStability
{
    internal static void Run(FrameworkElement? source, Action edit)
    {
        ScrollViewer? scroll = null;
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer candidate && candidate.Name is "WorkspacePageScroll" or "PresetPageScroll") { scroll = candidate; break; }
        if (scroll is null || source is null) { edit(); return; }
        var offset = scroll.VerticalOffset;
        var before = source.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
        edit(); scroll.UpdateLayout();
        var target = offset;
        if (source.IsLoaded)
            target += source.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y - before;
        scroll.ChangeView(null, Math.Clamp(target, 0, scroll.ScrollableHeight), null, true);
    }
}
