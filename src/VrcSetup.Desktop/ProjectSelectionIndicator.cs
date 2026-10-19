using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VrcSetup_Desktop;

// Explicit selection affordance: the stock template does not reliably reveal
// its built-in multi-select checkbox when enabled after the first selection.
public sealed class ProjectSelectionIndicator : CheckBox
{
    private ListView? owner;
    private ListViewItem? row;
    private bool syncing;
    public ProjectSelectionIndicator()
    {
        MinWidth = Width = 20; MinHeight = Height = 32; Padding = new Thickness(0);
        Visibility = Visibility.Collapsed; VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) =>
        {
            for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ListViewItem item) row = item;
                if (node is ListView list) { owner = list; break; }
            }
            if (owner is not null) owner.SelectionChanged += SelectionChanged;
            if (row is not null) { row.PointerEntered += RowEntered; row.PointerExited += RowExited; row.GotFocus += RowFocused; row.LostFocus += RowUnfocused; }
            Sync();
        };
        Unloaded += (_, _) => { if (owner is not null) owner.SelectionChanged -= SelectionChanged; if (row is not null) { row.PointerEntered -= RowEntered; row.PointerExited -= RowExited; row.GotFocus -= RowFocused; row.LostFocus -= RowUnfocused; } owner = null; row = null; };
        Checked += (_, _) => SetSelected(true); Unchecked += (_, _) => SetSelected(false);
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs args) => Sync();
    private bool hovered, focused;
    private void RowEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) { hovered = true; Sync(); }
    private void RowExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) { hovered = false; Sync(); }
    private void RowFocused(object sender, RoutedEventArgs args) { focused = true; Sync(); }
    private void RowUnfocused(object sender, RoutedEventArgs args) { focused = false; Sync(); }
    private void SetSelected(bool selected) { if (!syncing && row is not null) row.IsSelected = selected; }
    private void Sync()
    {
        syncing = true;
        try { IsChecked = row?.IsSelected == true; Visibility = hovered || focused || owner?.SelectedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
        finally { syncing = false; }
    }
}
