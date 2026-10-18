using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup_Desktop.ViewModels;
namespace VrcSetup_Desktop;
public sealed partial class PackageBrowserView : UserControl
{
    public PackageBrowserView() => InitializeComponent();
    private void CardsSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (LibraryCards.ItemsPanelRoot is ItemsWrapGrid panel && args.NewSize.Width > 0)
        {
            var available = Math.Max(1, args.NewSize.Width - 8);
            var columns = Math.Max(1, (int)(available / 240));
            panel.ItemWidth = Math.Floor(available / columns);
        }
    }
    private void LibraryAddClicked(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) vm.AddLibraryPackageCommand.Execute((sender as FrameworkElement)?.DataContext as UiPackageRow); }
    private async void PackageStarClicked(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) await vm.TogglePackageFavoriteCommand.ExecuteAsync((sender as FrameworkElement)?.DataContext as UiPackageRow); }
}
