using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private int presetDirection = 1;
    private Storyboard? informationMotion;
    private static bool MotionEnabled => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private void PreviousPresetClicked(object sender, RoutedEventArgs args) { presetDirection = -1; ViewModel.PreviousPresetCommand.Execute(null); }
    private void NextPresetClicked(object sender, RoutedEventArgs args) { presetDirection = 1; ViewModel.NextPresetCommand.Execute(null); }
    private void PresetChoicePicked(object sender, ItemClickEventArgs args)
    { if (args.ClickedItem is string name) ViewModel.EditingPreset = name; PresetSelector.Flyout.Hide(); }
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
    private void PresetChoiceKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    { if (args.Key == Windows.System.VirtualKey.Enter) PresetSelector.Flyout.Hide(); }
    private void AnimatePresetSelection()
    {
        ConstrainForms();
        if (!MotionEnabled || PresetsView.Visibility != Visibility.Visible) return;
        foreach (var (element, distance) in new[] { ((UIElement)PresetWheelTitle, 12f), (PresetIncludedSurface, 5f), (PresetCatalogSurface, 5f) })
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element); var compositor = visual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
            var move = compositor.CreateVector3KeyFrameAnimation(); move.Duration = TimeSpan.FromMilliseconds(240);
            move.InsertKeyFrame(0, new Vector3(0, presetDirection * distance, 0)); move.InsertKeyFrame(1, Vector3.Zero, easing);
            var fade = compositor.CreateScalarKeyFrameAnimation(); fade.Duration = move.Duration;
            fade.InsertKeyFrame(0, .3f); fade.InsertKeyFrame(1, 1, easing);
            visual.StartAnimation("Translation", move); visual.StartAnimation("Opacity", fade);
            if (element == PresetWheelTitle)
            {
                visual.CenterPoint = new Vector3((float)PresetWheelTitle.ActualWidth / 2, (float)PresetWheelTitle.ActualHeight / 2, 0);
                visual.RotationAxis = new Vector3(1, 0, 0);
                var turn = compositor.CreateScalarKeyFrameAnimation(); turn.Duration = TimeSpan.FromMilliseconds(300);
                turn.InsertKeyFrame(0, presetDirection * 22); turn.InsertKeyFrame(1, 0, easing);
                visual.StartAnimation("RotationAngleInDegrees", turn);
            }
        }
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
