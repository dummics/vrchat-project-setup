using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.Foundation;

namespace VrcSetup_Desktop;

// WinUI has no per-app InitialShowDelay. Adopt tooltip content (including native
// NavigationView tooltips) and preserve bindings while replacing its hover timer.
internal sealed class DelayedToolTips : DependencyObject
{
    internal const int DelayMilliseconds = 2500;
    private static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached("DelayedContent", typeof(object), typeof(DelayedToolTips), new PropertyMetadata(null));
    private sealed class State { public bool Adopting; }
    private readonly ConditionalWeakTable<FrameworkElement, State> states = new();
    private readonly ConditionalWeakTable<FrameworkElement, object> hookedRoots = new();
    private readonly FrameworkElement root;
    private readonly DispatcherQueueTimer timer;
    private FrameworkElement? target;
    private ToolTip? openTip;
    private Point lastPoint;
    private long lastScan;
    internal bool IsOpen => openTip?.IsOpen == true;
    internal bool HasContent(DependencyObject element) => element.GetValue(ContentProperty) is not null;
    public DelayedToolTips(FrameworkElement root)
    {
        this.root = root;
        timer = root.DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(DelayMilliseconds); timer.IsRepeating = false;
        timer.Tick += (_, _) => Show();
        root.Loaded += (_, _) => Scan();
        root.LayoutUpdated += (_, _) => { if (Environment.TickCount64 - lastScan > 150) Scan(); };
        root.Unloaded += (_, _) => Dismiss();
        HookInput(root);
    }
    private void HookInput(FrameworkElement surface)
    {
        if (hookedRoots.TryGetValue(surface, out _)) return;
        hookedRoots.Add(surface, new());
        surface.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) => Observe(args.OriginalSource as DependencyObject, args.GetCurrentPoint(root).Position)), true);
        surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Dismiss()), true);
        surface.PointerExited += (_, args) => { var p = args.GetCurrentPoint(surface).Position; if (p.X < 0 || p.Y < 0 || p.X >= surface.ActualWidth || p.Y >= surface.ActualHeight) Dismiss(); };
        surface.GotFocus += (_, args) => { if (args.OriginalSource is Control { FocusState: FocusState.Keyboard } control) Observe(control, new()); };
        surface.LostFocus += (_, _) => Dismiss();
        surface.KeyDown += (_, args) => { if (args.Key is Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Control) Dismiss(); };
        surface.Unloaded += (_, _) => Dismiss();
    }
    private void Scan()
    {
        lastScan = Environment.TickCount64;
        Visit(root);
        if (root.XamlRoot is { } xamlRoot)
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
                if (popup.Child is FrameworkElement child && child is not ToolTip) { Visit(child); HookInput(child); }
    }
    private void Visit(DependencyObject element)
    {
        if (element is FrameworkElement owner && !states.TryGetValue(owner, out _))
        {
            var state = new State(); states.Add(owner, state);
            owner.RegisterPropertyChangedCallback(ToolTipService.ToolTipProperty, (_, _) => Adopt(owner, state));
            Adopt(owner, state);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
    }
    private static void Adopt(FrameworkElement owner, State state)
    {
        if (state.Adopting || ToolTipService.GetToolTip(owner) is not { } content) return;
        state.Adopting = true;
        try
        {
            if (owner.GetBindingExpression(ToolTipService.ToolTipProperty) is { } expression) BindingOperations.SetBinding(owner, ContentProperty, expression.ParentBinding);
            else owner.SetValue(ContentProperty, content);
            ToolTipService.SetToolTip(owner, null);
        }
        finally { state.Adopting = false; }
    }
    internal void Observe(DependencyObject? original, Point point)
    {
        FrameworkElement? owner = null;
        for (var current = original; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement element && element.GetValue(ContentProperty) is not null) { owner = element; break; }
        if (owner == target && Math.Abs(point.X - lastPoint.X) <= 4 && Math.Abs(point.Y - lastPoint.Y) <= 4) return;
        Dismiss(); lastPoint = point; target = owner;
        if (owner is not null) timer.Start();
    }
    private void Show()
    {
        if (target is not { IsLoaded: true } owner || owner is Control { IsEnabled: false } || owner.XamlRoot is null) return;
        var content = owner.GetValue(ContentProperty);
        // Native NavigationView owns its generated ToolTip instance. Reusing
        // that instance after detaching its service can retain a stale owner.
        if (content is ToolTip nativeTip) content = nativeTip.Content;
        if (content is null || content is string { Length: 0 }) return;
        openTip = new ToolTip { Content = content };
        var state = states.GetValue(owner, _ => new State()); state.Adopting = true;
        try { ToolTipService.SetToolTip(owner, openTip); openTip.IsOpen = true; }
        finally { state.Adopting = false; }
    }
    internal void Dismiss()
    {
        timer.Stop();
        if (openTip is not null)
        {
            openTip.IsOpen = false;
            if (target is not null && ReferenceEquals(ToolTipService.GetToolTip(target), openTip)) ToolTipService.SetToolTip(target, null);
        }
        openTip = null; target = null;
    }
}
