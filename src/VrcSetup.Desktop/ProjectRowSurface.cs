using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace VrcSetup_Desktop;

// The card owns its feedback: the stock presenter is behind its opaque surface.
// Selection always comes from IsSelected, never from a remembered click/press.
public sealed class ProjectRowSurface : Grid
{
    private readonly Border hover = Tint(255, 255, 255), selection = Tint(80, 205, 187);
    private ListViewItem? row;
    private long selectionToken;
    internal bool Hovered { get; private set; }
    internal bool SelectionHighlighted => row?.IsSelected == true;
    public ProjectRowSurface()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => LayoutColumns();
        ActualThemeChanged += (_, _) => UpdateFeedback(false);
    }
    private static Border Tint(byte r, byte g, byte b) => new()
    {
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b)),
        IsHitTestVisible = false, CornerRadius = new CornerRadius(4)
    };
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        LayoutColumns();
        if (!Children.Contains(hover))
        {
            hover.Margin = selection.Margin = new Thickness(1 - Padding.Left, 1 - Padding.Top, 1 - Padding.Right, 1 - Padding.Bottom);
            SetRowSpan(hover, 99); SetColumnSpan(hover, 99);
            SetRowSpan(selection, 99); SetColumnSpan(selection, 99);
            Children.Insert(0, selection); Children.Insert(0, hover);
        }
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem item) { row = item; break; }
        if (row is not null)
        {
            selectionToken = row.RegisterPropertyChangedCallback(ListViewItem.IsSelectedProperty, (_, _) => UpdateFeedback(true));
            row.PointerEntered += OnEntered; row.PointerExited += OnExited; row.PointerCanceled += OnExited;
        }
        Hovered = false; UpdateFeedback(false);
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (row is not null)
        {
            row.UnregisterPropertyChangedCallback(ListViewItem.IsSelectedProperty, selectionToken);
            row.PointerEntered -= OnEntered; row.PointerExited -= OnExited; row.PointerCanceled -= OnExited;
        }
        row = null; Hovered = false; UpdateFeedback(false);
    }

    private bool? stacked;
    private void LayoutColumns()
    {
        if (ColumnDefinitions.Count < 3) return;
        var narrow = ActualWidth < 640;
        if (stacked == narrow) return;
        stacked = narrow;
        ColumnSpacing = narrow ? 0 : 16;
        RowSpacing = narrow ? 8 : 0;
        ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 260);
        ColumnDefinitions[2].Width = new GridLength(narrow ? 0 : 200);
        foreach (var child in Children.OfType<FrameworkElement>())
        {
            if (child.Name == "ProjectDates") { SetColumn(child, narrow ? 0 : 1); SetRow(child, narrow ? 1 : 0); }
            if (child.Name == "ProjectActions") { SetColumn(child, narrow ? 0 : 2); SetRow(child, narrow ? 2 : 0); }
        }
    }
    private void OnEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => SetHovered(true);
    private void OnExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => SetHovered(false);
    internal void SetHovered(bool value) { Hovered = value; UpdateFeedback(true); }
    private void UpdateFeedback(bool animate)
    {
        hover.Background = new SolidColorBrush(ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 0, 0, 0) : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        Fade(hover, Hovered ? (highContrast ? .18f : .045f) : 0, animate);
        Fade(selection, SelectionHighlighted ? (highContrast ? .38f : .14f) : 0, animate);
    }
    private static void Fade(UIElement target, float opacity, bool animate)
    {
        var visual = ElementCompositionPreview.GetElementVisual(target);
        if (!animate || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        { visual.StopAnimation(nameof(Visual.Opacity)); visual.Opacity = opacity; return; }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, opacity);
        animation.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation(nameof(Visual.Opacity), animation);
    }
}
