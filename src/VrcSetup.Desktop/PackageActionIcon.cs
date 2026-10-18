using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Shapes;

namespace VrcSetup_Desktop;

// Vector strokes stay centered and sharp at small sizes; no bitmap assets.
public sealed class PackageActionIcon : UserControl
{
    public static readonly DependencyProperty IsAddProperty = DependencyProperty.Register(nameof(IsAdd), typeof(bool), typeof(PackageActionIcon),
        new PropertyMetadata(false, (owner, _) => ((PackageActionIcon)owner).Update()));
    private readonly Rectangle vertical = new() { Width = 1.5, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    public bool IsAdd { get => (bool)GetValue(IsAddProperty); set => SetValue(IsAddProperty, value); }
    public PackageActionIcon()
    {
        Width = Height = 18; IsHitTestVisible = false;
        var grid = new Grid();
        var outline = new Rectangle { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, RadiusX = 3, RadiusY = 3, StrokeThickness = 1.25 };
        outline.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        var horizontal = new Rectangle { Width = 8, Height = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var line in new[] { horizontal, vertical }) line.SetBinding(Shape.FillProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        grid.Children.Add(outline); grid.Children.Add(horizontal); grid.Children.Add(vertical); Content = grid; Update();
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Opacity = IsEnabled ? 1 : 0.35);
    }
    private void Update() => vertical.Visibility = IsAdd ? Visibility.Visible : Visibility.Collapsed;
}
