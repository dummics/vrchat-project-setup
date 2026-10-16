using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

// One native flyout per row. Loading never queues ContentDialogs or blocks the dispatcher.
public sealed class PackageVersionSelector : DropDownButton
{
    public static readonly DependencyProperty PackageIdProperty = DependencyProperty.Register(nameof(PackageId), typeof(string), typeof(PackageVersionSelector), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(PackageVersionSelector), new PropertyMetadata("latest", Changed));
    public static readonly DependencyProperty DisplayVersionProperty = DependencyProperty.Register(nameof(DisplayVersion), typeof(string), typeof(PackageVersionSelector), new PropertyMetadata("", Changed));
    public string PackageId { get => (string)GetValue(PackageIdProperty); set => SetValue(PackageIdProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string DisplayVersion { get => (string)GetValue(DisplayVersionProperty); set => SetValue(DisplayVersionProperty, value); }
    public Func<string, Task<string[]>>? VersionSource { get; set; }
    public Func<string, string[]>? CachedSource { get; set; }
    public event EventHandler<string>? VersionPicked;
    private readonly ListView choices = new() { MaxHeight = 260, MinWidth = 220, DisplayMemberPath = nameof(VersionItem.Label), IsItemClickEnabled = true };
    private readonly CheckBox betas = new() { Content = "Include betas" };
    private readonly TextBlock status = new() { Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
    private string[] versions = [];
    private bool rendering;
    private long request;
    public PackageVersionSelector()
    {
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(betas); panel.Children.Add(choices); panel.Children.Add(status);
        var flyout = new Flyout { Content = panel }; Flyout = flyout;
        flyout.Opening += async (_, _) =>
        {
            var id = PackageId; var generation = ++request;
            versions = CachedSource?.Invoke(id) ?? []; Render(); status.Text = versions.Length == 0 ? "Loading versions…" : "";
            try { if (VersionSource is not null) { var loaded = await VersionSource(id); if (generation != request || PackageId != id) return; versions = loaded; Render(); status.Text = loaded.Length == 0 ? "No saved releases available" : ""; } }
            catch (Exception) { if (generation == request) status.Text = "Versions unavailable. Saved choices are still usable."; }
        };
        flyout.Closed += (_, _) => request++;
        betas.Checked += (_, _) => Render(); betas.Unchecked += (_, _) => Render();
        choices.ItemClick += (_, args) => { if (args.ClickedItem is VersionItem picked) PickVersion(picked.Value); };
        choices.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter && choices.SelectedItem is VersionItem picked) { PickVersion(picked.Value); args.Handled = true; } };
        UpdateCaption();
    }
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs args) => ((PackageVersionSelector)value).UpdateCaption();
    private void UpdateCaption() => Content = string.IsNullOrWhiteSpace(DisplayVersion) ? Value == "latest" ? "Latest" : Value : DisplayVersion;
    internal void PickVersion(string value)
    { if (!rendering) { VersionPicked?.Invoke(this, value); Flyout.Hide(); } }
    private void Render()
    {
        rendering = true;
        try
        {
            var pins = SemanticVersion.Sort(versions, betas.IsChecked == true).ToList();
            if (Value != "latest" && !string.IsNullOrWhiteSpace(Value) && !Value.StartsWith("Mixed") && Value != "Not installed" && !pins.Contains(Value)) pins.Insert(0, Value);
            var rows = new[] { new VersionItem("latest", "Latest") }.Concat(pins.Select(x => new VersionItem(x, x))).ToArray();
            choices.ItemsSource = rows; choices.SelectedItem = rows.FirstOrDefault(x => x.Value == Value);
        }
        finally { rendering = false; }
    }
    private sealed record VersionItem(string Value, string Label);
}
