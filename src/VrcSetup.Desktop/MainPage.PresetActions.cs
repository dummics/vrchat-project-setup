using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private bool presetActionsOpen;
    private Storyboard? presetActionsMotion;
    private DispatcherTimer? presetActionsClose;
    private void PresetActionsEntered(object sender, PointerRoutedEventArgs args) => SetPresetActions(true);
    private void PresetActionsExited(object sender, PointerRoutedEventArgs args) => DelayPresetActionsClose();
    private void PresetActionsClicked(object sender, RoutedEventArgs args) => SetPresetActions(!presetActionsOpen);
    private void PresetActionsKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Escape) return;
        SetPresetActions(false); PresetActionsToggle.Focus(FocusState.Programmatic); args.Handled = true;
    }
    private void PresetActionsFocused(object sender, RoutedEventArgs args)
    { if (FocusManager.GetFocusedElement(XamlRoot) is Control { FocusState: FocusState.Keyboard }) SetPresetActions(true); }
    private void PresetActionsUnfocused(object sender, RoutedEventArgs args) => DelayPresetActionsClose();
    private void PresetActionsResized(object sender, SizeChangedEventArgs args)
        => PresetActionsClip.Clip = new RectangleGeometry { Rect = new(0, 0, Math.Max(0, args.NewSize.Width), Math.Max(0, args.NewSize.Height)) };
    private void DelayPresetActionsClose()
    {
        presetActionsClose ??= CreateCloseTimer(); presetActionsClose.Stop(); presetActionsClose.Start();
    }
    private DispatcherTimer CreateCloseTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DependencyObject? focus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (focus is Control { FocusState: FocusState.Keyboard })
                for (var node = focus; node is not null; node = VisualTreeHelper.GetParent(node)) if (node == PresetActionsHost) return;
            SetPresetActions(false);
        };
        return timer;
    }
    internal void SetPresetActions(bool open)
    {
        presetActionsClose?.Stop();
        if (open == presetActionsOpen) return;
        presetActionsOpen = open;
        ((FontIcon)PresetActionsToggle.Content).Glyph = open ? "\uE711" : "\uE712";
        var from = PresetActionsClip.ActualWidth; presetActionsMotion?.Stop();
        PresetActionsClip.Visibility = Visibility.Visible;
        PresetActionsStrip.IsHitTestVisible = open;
        foreach (var action in PresetActionsStrip.Children.OfType<Button>()) action.IsTabStop = open;
        var to = open ? 96d : 0d; PresetActionsClip.Width = to;
        if (!MotionEnabled) { PresetActionsClip.Visibility = open ? Visibility.Visible : Visibility.Collapsed; return; }
        var motion = new Storyboard(); presetActionsMotion = motion;
        var width = new DoubleAnimation { From = from, To = to, Duration = new(TimeSpan.FromMilliseconds(220)), EnableDependentAnimation = true,
            FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(width, PresetActionsClip); Storyboard.SetTargetProperty(width, "Width"); motion.Children.Add(width);
        motion.Completed += (_, _) => { if (ReferenceEquals(presetActionsMotion, motion) && !presetActionsOpen) PresetActionsClip.Visibility = Visibility.Collapsed; };
        motion.Begin();
    }
}
