using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private Storyboard? informationMotion;
    private readonly Dictionary<Expander, (Storyboard Animation, FrameworkElement Content, SizeChangedEventHandler ClipHandler)> importMotions = [];
    internal static bool? MotionOverrideForVerification { get; set; }
    private static bool MotionEnabled => MotionOverrideForVerification ?? new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private void ImportExpanderUnloaded(object sender, RoutedEventArgs args)
    { if (sender is Expander expander) StopImportDetailsMotion(expander); }

    private void StopImportDetailsMotion(Expander expander)
    {
        if (!importMotions.Remove(expander, out var motion)) return;
        motion.Animation.Stop();
        motion.Content.SizeChanged -= motion.ClipHandler;
        motion.Content.ClearValue(FrameworkElement.HeightProperty);
        motion.Content.Clip = null;
    }

    private void AnimateImportDetails(Expander expander)
    {
        StopImportDetailsMotion(expander);
        if (!MotionEnabled || expander.Content is not FrameworkElement content || expander.ActualWidth <= 0) return;
        content.Measure(new Windows.Foundation.Size(expander.ActualWidth, double.PositiveInfinity));
        var target = content.DesiredSize.Height;
        if (!double.IsFinite(target) || target <= 0) return;
        void Clip(object _, SizeChangedEventArgs args) => content.Clip = new RectangleGeometry
        { Rect = new(0, 0, Math.Max(0, args.NewSize.Width), Math.Max(0, args.NewSize.Height)) };
        content.Clip = new RectangleGeometry { Rect = new(0, 0, expander.ActualWidth, 0) };
        content.SizeChanged += Clip;
        content.Height = target;
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation { From = 0, To = target, Duration = new(TimeSpan.FromMilliseconds(240)),
            EnableDependentAnimation = true, FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, content); Storyboard.SetTargetProperty(animation, "Height"); storyboard.Children.Add(animation);
        importMotions[expander] = (storyboard, content, Clip);
        storyboard.Completed += (_, _) => StopImportDetailsMotion(expander);
        storyboard.Begin();
    }
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
