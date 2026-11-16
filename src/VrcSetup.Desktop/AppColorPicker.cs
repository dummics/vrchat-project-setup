using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VrcSetup_Desktop;

// Avoid ColorSpectrum's native resource-loading path in unpackaged flyouts.
public sealed class AppColorPicker : UserControl
{
    private readonly TextBox hex = new() { Header = "Hex", MaxLength = 7, MinWidth = 220 };
    private readonly Border preview = new() { Height = 32, CornerRadius = new(4) };
    private readonly TextBox[] channels = new TextBox[3];
    private bool updating;
    public event Action<AppColorPicker, Color>? ColorChanged;
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(AppColorPicker),
        new PropertyMetadata(Color.FromArgb(255, 255, 173, 102), (owner, args) => ((AppColorPicker)owner).Refresh((Color)args.NewValue)));
    public Color Color { get => (Color)GetValue(ColorProperty); set { if (Color != value) SetValue(ColorProperty, value); } }

    public AppColorPicker()
    {
        var panel = new StackPanel { Spacing = 10, Width = 240 };
        panel.Children.Add(preview);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var value in new[] { "#FFAD66", "#50CDBB", "#70B9F2", "#C49AF0", "#F07878", "#FFFFFF", "#181A1D" })
        {
            var color = AppearanceResources.ParseColor(value, "#FFAD66");
            var button = new Button { Width = 28, Height = 32, MinWidth = 0, MinHeight = 32, Padding = new Thickness(0), Background = new SolidColorBrush(color) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, value);
            ToolTipService.SetToolTip(button, value);
            button.Click += (_, _) => Color = color;
            swatches.Children.Add(button);
        }
        panel.Children.Add(swatches);
        panel.Children.Add(hex);
        var rgb = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (var i = 0; i < channels.Length; i++)
        {
            var slider = new TextBox { Header = new[] { "R", "G", "B" }[i], MaxLength = 3, Text = "0", Width = 74 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, new[] { "R", "G", "B" }[i]);
            channels[i] = slider;
            slider.TextChanged += (_, _) =>
            {
                if (!updating && channels.All(x => x is not null && byte.TryParse(x.Text, out _)))
                    Color = Color.FromArgb(255, byte.Parse(channels[0].Text), byte.Parse(channels[1].Text), byte.Parse(channels[2].Text));
            };
            rgb.Children.Add(slider);
        }
        panel.Children.Add(rgb);
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            var text = hex.Text.Trim().TrimStart('#');
            if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value))
                Color = Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        };
        Content = panel;
        Refresh(Color);
    }

    private void Refresh(Color color)
    {
        if (channels[2] is null) return;
        updating = true;
        try
        {
            var text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (hex.Text != text) hex.Text = text;
            if (channels[0].Text != color.R.ToString()) channels[0].Text = color.R.ToString();
            if (channels[1].Text != color.G.ToString()) channels[1].Text = color.G.ToString();
            if (channels[2].Text != color.B.ToString()) channels[2].Text = color.B.ToString();
            preview.Background = new SolidColorBrush(color);
        }
        finally { updating = false; }
        ColorChanged?.Invoke(this, color);
    }
}
