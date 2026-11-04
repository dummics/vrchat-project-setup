using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private Storyboard? informationMotion;
    private static bool MotionEnabled => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private void OperationsActivityClicked(object sender, RoutedEventArgs args) => OperationsStatusButton.Flyout.Hide();
    private async void RetryCreationClicked(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.RunningOperationRow row)
        { OperationsStatusButton.Flyout.Hide(); await ViewModel.RetryCreationAsync(row.Id); }
    }
    private async void DismissCreationClicked(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.RunningOperationRow row) await ViewModel.DismissCreationAsync(row.Id);
    }
    private void InformationSurfaceResized(object sender, SizeChangedEventArgs args)
        => InformationSurface.Clip = new RectangleGeometry { Rect = new(0, 0, Math.Max(0, args.NewSize.Width), Math.Max(0, args.NewSize.Height)) };
    private void AnimateInformation(bool expand)
    {
        var from = InformationSurface.ActualHeight;
        informationMotion?.Stop();
        InformationPanel.Visibility = Visibility.Visible;
        InformationLinks.Measure(new(Math.Max(1, Navigation.OpenPaneLength - 8), double.PositiveInfinity));
        var to = expand ? InformationLinks.DesiredSize.Height : 0;
        InformationSurface.Height = to;
        if (!MotionEnabled) { InformationPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed; return; }
        var storyboard = new Storyboard(); informationMotion = storyboard;
        var animation = new DoubleAnimation { From = from, To = to, Duration = new(TimeSpan.FromMilliseconds(240)), EnableDependentAnimation = true,
            FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, InformationSurface); Storyboard.SetTargetProperty(animation, "Height"); storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => { if (ReferenceEquals(informationMotion, storyboard) && !informationExpanded) InformationPanel.Visibility = Visibility.Collapsed; };
        storyboard.Begin();
    }
}
