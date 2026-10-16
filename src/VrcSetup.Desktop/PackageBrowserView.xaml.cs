using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup_Desktop.ViewModels;
namespace VrcSetup_Desktop;
public sealed partial class PackageBrowserView : UserControl
{
    public PackageBrowserView() => InitializeComponent();
    private void LibraryAddClicked(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) vm.AddLibraryPackageCommand.Execute((sender as FrameworkElement)?.DataContext as UiPackageRow); }
    private async void PackageStarClicked(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) await vm.TogglePackageFavoriteCommand.ExecuteAsync((sender as FrameworkElement)?.DataContext as UiPackageRow); }
}
