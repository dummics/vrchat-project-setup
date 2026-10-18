using VrcSetup.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VrcSetup.Installer;

internal sealed class UpdateProgressWindow : Window
{
    private readonly TextBlock step = new() { Text = UiText.Text("Waiting for the app to close…"), FontSize = 14, Margin = new Thickness(0, 0, 0, 14) };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 5 };
    private readonly StackPanel panel = new() { Margin = new Thickness(24) };
    private bool canClose;
    public UpdateProgressWindow()
    {
        Title = "VRCSETUP · " + UiText.Text("Updating"); Width = 400; Height = 136;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(40, 42, 45)); Foreground = Brushes.WhiteSmoke;
        panel.Background = Background; progress.Foreground = new SolidColorBrush(Color.FromRgb(80, 205, 187));
        panel.Children.Add(step); panel.Children.Add(progress);
        Content = panel;
        Closing += (_, args) => args.Cancel = !canClose;
    }
    public void SetStep(string message, bool finished = false)
    { step.Text = UiText.Text(message); if (finished) { progress.IsIndeterminate = false; progress.Value = 100; } }
    public void ShowFailure(bool reopened, string? result)
    {
        canClose = true; Height = 210; progress.IsIndeterminate = false;
        step.Text = UiText.Text(reopened ? "Update failed. The previous app was reopened." : "Update failed. Your previous installation is preserved.");
        step.TextWrapping = TextWrapping.Wrap;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        if (result is not null)
        {
            var details = new Button { Content = UiText.Text("Open details"), Padding = new Thickness(12, 5, 12, 5) };
            details.Click += (_, _) => Process.Start(new ProcessStartInfo(result) { UseShellExecute = true }); actions.Children.Add(details);
        }
        var close = new Button { Content = UiText.Text("Close"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5) };
        close.Click += (_, _) => Application.Current.Shutdown(1); actions.Children.Add(close); panel.Children.Add(actions);
    }
    internal void RenderForVerification(string path)
    {
        panel.Measure(new Size(Width, Height)); panel.Arrange(new Rect(0, 0, Width, Height)); panel.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(path); encoder.Save(output);
    }
}
