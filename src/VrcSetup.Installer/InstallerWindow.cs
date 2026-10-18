using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.Installer;

public sealed class InstallerWindow : Window
{
    private readonly string? home;
    private readonly Border surface;
    private readonly StackPanel folderFields, choices;
    private readonly TextBox target;
    private readonly TextBlock status;
    private readonly ProgressBar progress;
    private readonly Button install, finish;
    private readonly CheckBox start, desktop, launch;
    private string? installedRoot;
    private bool installing, finishing, closingAfterFinish;
    internal bool Complete => installedRoot is not null;
    internal bool DesktopShortcut { get => desktop.IsChecked == true; set => desktop.IsChecked = value; }
    internal bool LaunchApplication { get => launch.IsChecked == true; set => launch.IsChecked = value; }
    public InstallerWindow(string? root = null, string? home = null)
    {
        this.home = home;
        Icon = new BitmapImage(new Uri("pack://application:,,,/AppMark.png"));
        Title = "VRC Project Setup"; Width = 560; MinWidth = 520; MinHeight = 340; SizeToContent = SizeToContent.Height;
        // WPF's Fluent theme selector is still annotated experimental in .NET 10.
        // Keep its use local to this window rather than suppressing project warnings.
#pragma warning disable WPF0001
        ThemeMode = System.Windows.ThemeMode.Dark;
#pragma warning restore WPF0001
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(37, 39, 42)); Foreground = Brushes.WhiteSmoke;
        var body = new StackPanel(); surface = new Border { Background = Background, Padding = new Thickness(28), Child = body };
        body.Children.Add(new TextBlock { Text = "VRC Project Setup", FontSize = 24, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = ApplicationUpdates.CurrentVersion, FontSize = 13, Foreground = Brushes.Silver, Margin = new Thickness(0, 4, 0, 20) });
        folderFields = new StackPanel();
        folderFields.Children.Add(new TextBlock { Text = UiText.Text("Install folder"), Margin = new Thickness(0, 0, 0, 8) });
        target = new TextBox { Text = root ?? InstallationManager.DefaultRoot, Margin = new Thickness(0, 0, 0, 16) }; folderFields.Children.Add(target); body.Children.Add(folderFields);
        progress = new ProgressBar { Height = 12, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed }; body.Children.Add(progress);
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed }; body.Children.Add(status);
        choices = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 20) };
        start = Choice("Add to Start", true); desktop = Choice("Create desktop shortcut", false); launch = Choice("Open VRC Project Setup", true);
        choices.Children.Add(start); choices.Children.Add(desktop); choices.Children.Add(launch); body.Children.Add(choices);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        install = new Button { Padding = new Thickness(20, 9, 20, 9), Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        void UpdateAction() => install.Content = UiText.Text(File.Exists(Path.Combine(target.Text, ".vrcsetup-install.json")) ? "Update" : "Install");
        target.TextChanged += (_, _) => UpdateAction(); UpdateAction();
        finish = new Button { Content = UiText.Text("Cancel"), Padding = new Thickness(20, 9, 20, 9) };
        install.Click += async (_, _) => await InstallAsync();
        finish.Click += async (_, _) => { if (Complete) await FinishAsync(); else Close(); };
        actions.Children.Add(install); actions.Children.Add(finish); body.Children.Add(actions); Content = surface;
        Closed += (_, _) => Application.Current.Shutdown();
        Closing += async (_, args) =>
        {
            if (closingAfterFinish) return;
            if (installing || finishing) { args.Cancel = true; return; }
            if (Complete && !closingAfterFinish) { args.Cancel = true; await FinishAsync(false); }
        };
    }
    private static CheckBox Choice(string text, bool selected) => new() { Content = UiText.Text(text), IsChecked = selected, Margin = new Thickness(0, 5, 0, 5) };
    private void Status(string text) { status.Text = UiText.Text(text); status.Visibility = Visibility.Visible; }
    internal async Task<bool> InstallAsync(Func<InstallationChange, bool>? confirmation = null)
    {
        if (installing || Complete) return false;
        installing = true; install.IsEnabled = target.IsEnabled = finish.IsEnabled = false;
        try
        {
            var destination = ProjectRules.NormalizePath(target.Text);
            var record = await AtomicFile.ReadJsonAsync<InstallationRecord>(Path.Combine(destination, ".vrcsetup-install.json"));
            if (record is not null && !record.Root.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installation marker does not match this folder.");
            var change = InstallationVersions.Compare(record?.Version, ApplicationUpdates.CurrentVersion);
            if (change is InstallationChange.Reinstall or InstallationChange.Downgrade)
            {
                var message = change == InstallationChange.Reinstall
                    ? string.Format(UiText.Text("Version {0} is already installed. Reinstall it?"), record!.Version)
                    : string.Format(UiText.Text("Version {0} is newer than {1}. Install the older version?"), record!.Version, ApplicationUpdates.CurrentVersion);
                var confirmed = confirmation?.Invoke(change) ?? MessageBox.Show(this, message, Title, MessageBoxButton.YesNo,
                    change == InstallationChange.Downgrade ? MessageBoxImage.Warning : MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes;
                if (!confirmed) return false;
            }
            progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true; Status("Installing…");
            var installed = await App.Install(destination, home, new Progress<string>(Status), createStartMenu: false);
            ShowCompleted(installed.Root); return true;
        }
        catch (Exception error) { Status(error.Message); return false; }
        finally { installing = false; progress.IsIndeterminate = false; target.IsEnabled = finish.IsEnabled = install.IsEnabled = true; }
    }
    internal void ShowCompleted(string root)
    {
        installedRoot = root; folderFields.Visibility = progress.Visibility = install.Visibility = Visibility.Collapsed;
        Status("Installation complete"); status.FontSize = 18; status.FontWeight = FontWeights.SemiBold;
        choices.Visibility = Visibility.Visible; finish.Content = UiText.Text("Finish"); finish.IsDefault = true; install.IsDefault = false;
    }
    internal async Task<bool> FinishAsync(bool allowLaunch = true, bool close = true)
    {
        if (installedRoot is null || finishing) return false;
        finishing = true; finish.IsEnabled = choices.IsEnabled = false;
        try
        {
            await new InstallationManager(AppPaths.ForInstallation(home, installedRoot)).ConfigureShortcutsAsync(installedRoot, start.IsChecked == true, desktop.IsChecked == true);
            if (allowLaunch && launch.IsChecked == true) App.LaunchInstalled(installedRoot, home, false);
            closingAfterFinish = true; if (close) Close(); return true;
        }
        catch (Exception error) { Status(error.Message); return false; }
        finally { finishing = false; finish.IsEnabled = choices.IsEnabled = true; }
    }
    internal void RenderForVerification(string path)
    {
        surface.Measure(new Size(Width, double.PositiveInfinity)); var height = Math.Max(MinHeight, surface.DesiredSize.Height);
        surface.Arrange(new Rect(0, 0, Width, height)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
    }
}
