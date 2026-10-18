using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup_Desktop.ViewModels;
namespace VrcSetup_Desktop;
public sealed partial class PackageBrowserView : UserControl
{
    public static readonly DependencyProperty ShowSearchProperty = DependencyProperty.Register(nameof(ShowSearch), typeof(bool), typeof(PackageBrowserView),
        new PropertyMetadata(true, (owner, _) => ((PackageBrowserView)owner).UpdateSearch()));
    public bool ShowSearch { get => (bool)GetValue(ShowSearchProperty); set => SetValue(ShowSearchProperty, value); }
    public PackageBrowserView() => InitializeComponent();
    private void UpdateSearch() { if (SearchToolbar is not null) SearchToolbar.Visibility = ShowSearch ? Visibility.Visible : Visibility.Collapsed; }
    private void CardsSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (LibraryCards.ItemsPanelRoot is ItemsWrapGrid panel && args.NewSize.Width > 0)
        {
            var available = Math.Max(1, args.NewSize.Width - LibraryCards.Padding.Left - LibraryCards.Padding.Right - 8);
            var columns = Math.Max(1, (int)(available / 240));
            panel.ItemWidth = Math.Floor(available / columns);
        }
    }
    private void LibraryAddClicked(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) vm.AddLibraryPackageCommand.Execute((sender as FrameworkElement)?.DataContext as UiPackageRow); }
    private async void PackageStarClicked(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) await vm.TogglePackageFavoriteCommand.ExecuteAsync((sender as FrameworkElement)?.DataContext as UiPackageRow); }
}
