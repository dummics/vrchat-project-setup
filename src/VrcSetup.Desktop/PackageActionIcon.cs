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
    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(nameof(IsRequired), typeof(bool), typeof(PackageActionIcon),
        new PropertyMetadata(false, (owner, _) => ((PackageActionIcon)owner).Update()));
    private readonly Rectangle vertical = new() { Width = 1.5, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid symbols = new();
    private readonly FontIcon requiredIcon = new() { Glyph = "\uE72E", FontSize = 16 };
    public bool IsAdd { get => (bool)GetValue(IsAddProperty); set => SetValue(IsAddProperty, value); }
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    public PackageActionIcon()
    {
        Width = Height = 18; IsHitTestVisible = false;
        var grid = new Grid();
        var outline = new Rectangle { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, RadiusX = 3, RadiusY = 3, StrokeThickness = 1.25 };
        outline.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        var horizontal = new Rectangle { Width = 8, Height = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var line in new[] { horizontal, vertical }) line.SetBinding(Shape.FillProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        symbols.Children.Add(outline); symbols.Children.Add(horizontal); symbols.Children.Add(vertical);
        grid.Children.Add(symbols); grid.Children.Add(requiredIcon); Content = grid; Update();
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Opacity = IsEnabled ? 1 : 0.35);
    }
    private void Update()
    {
        vertical.Visibility = IsAdd ? Visibility.Visible : Visibility.Collapsed;
        symbols.Visibility = IsRequired ? Visibility.Collapsed : Visibility.Visible;
        requiredIcon.Visibility = IsRequired ? Visibility.Visible : Visibility.Collapsed;
    }
}
