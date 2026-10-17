using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

public sealed class PackageSetDialog : IDisposable
{
    public ContentDialog Dialog { get; }
    public bool Accepted { get; private set; }
    private readonly PackageEditSession session;
    private readonly AppConfiguration configuration;
    private readonly ListView list;
    private readonly TextBox query;
    private readonly InfoBar notice;
    private readonly CancellationTokenSource cancellation = new();
    private readonly AsyncVersionCache versionCache;
    private readonly Func<string, CancellationToken, Task<PackageDefinition[]>> search;
    private CancellationTokenSource? searching;
    private bool rendering;
    public PackageSetDialog(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions)
    {
        configuration = config; this.search = search;
        var working = new Dictionary<string, string>(packages);
        foreach (var id in ProjectRules.Foundation(working, config)) working.TryAdd(id, "latest");
        var snapshot = new ProjectSnapshot { Path = Path.GetFullPath("preset-working-copy"), Name = title, Packages = new(working), Policy = new(working) };
        session = new([snapshot], config, known);
        versionCache = new(async (id, token) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation.Token);
            if (!ProjectRules.SdkIds.Contains(id)) return await versions(id, linked.Token);
            HashSet<string>? common = null;
            foreach (var sdk in ProjectRules.SdkIds.Where(working.ContainsKey))
            {
                var available = await versions(sdk, linked.Token);
                if (common is null) common = new(available); else common.IntersectWith(available);
            }
            return SemanticVersion.Sort(common ?? []);
        });
        var panel = new StackPanel { Spacing = 12, MinWidth = 280, MaxWidth = 560 };
        query = new TextBox { PlaceholderText = "Search packages", MinHeight = 34 };
        panel.Children.Add(query);
        notice = new InfoBar { IsOpen = false, IsClosable = true }; panel.Children.Add(notice);
        list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 480, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.Resources["ListViewItemBackgroundSelected"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        list.Resources["ListViewItemBackgroundSelectedPointerOver"] = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(18, 128, 128, 128));
        list.Resources["ListViewItemBackgroundSelectedPressed"] = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(28, 128, 128, 128));
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="using:VrcSetup_Desktop">
              <Grid Padding="4,6" ColumnSpacing="12"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="100"/></Grid.ColumnDefinitions>
                <StackPanel><TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/><TextBlock Text="{Binding Id}" FontSize="11" Opacity="0.6" TextTrimming="CharacterEllipsis"/></StackPanel>
                <local:PackageVersionSelector Grid.Column="1" PackageId="{Binding Id}" Value="{Binding SelectedVersion}" MinHeight="30" Padding="8,4" VerticalAlignment="Center"/>
              </Grid>
            </DataTemplate>
            """);
        panel.Children.Add(list);
        Dialog = new() { Title = title, Content = panel };
        Dialog.TitleTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding}" FontSize="18" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/></DataTemplate>
            """);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinHeight = 32, Padding = new Thickness(14, 5, 14, 5) };
        var done = new Button { Content = "Done", MinHeight = 32, Padding = new Thickness(14, 5, 14, 5), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        cancel.Click += (_, _) => Dialog.Hide();
        done.Click += (_, _) => { Accepted = true; Dialog.Hide(); };
        actions.Children.Add(cancel); actions.Children.Add(done); panel.Children.Add(actions);
        void RootChanged(XamlRoot _, XamlRootChangedEventArgs __) => ResizeDialog();
        Dialog.Opened += (_, _) => { ResizeDialog(); Dialog.XamlRoot.Changed += RootChanged; };
        Dialog.Closed += (_, _) => Dialog.XamlRoot.Changed -= RootChanged;
        Dialog.Closed += (_, _) => { searching?.Cancel(); cancellation.Cancel(); };
        // Templates may materialize after the container-content callback, especially
        // in self-contained builds. Wire the realized visual tree after layout.
        list.LayoutUpdated += (_, _) => WireSelectors(list);
        list.SelectionChanged += (_, args) =>
        {
            if (rendering) return;
            foreach (var added in args.AddedItems.OfType<PackageWorkspaceRow>()) if (!added.Included) session.Toggle(added.Id);
            foreach (var removed in args.RemovedItems.OfType<PackageWorkspaceRow>())
            {
                if (removed.Required) ShowNotice("The required VRChat foundation stays included.");
                else if (removed.Included) session.Toggle(removed.Id);
            }
            Render();
        };
        Render();
        query.TextChanged += async (_, _) => await SearchAsync();
    }
    private async Task SearchAsync()
    {
        searching?.Cancel(); searching?.Dispose();
        var current = searching = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        var token = current.Token; var text = query.Text;
        Render();
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            await Task.Delay(350, token);
            var results = await Task.Run(() => search(text, token), token);
            if (token.IsCancellationRequested) return;
            session.AddDefinitions(results); Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) ShowNotice(error.Message); }
    }
    private void Render()
    {
        rendering = true;
        try
        {
            var rows = session.Rows(query.Text); list.ItemsSource = rows;
            foreach (var item in rows.Where(x => x.Included)) list.SelectedItems.Add(item);
            ResizeDialog();
        }
        finally { rendering = false; }
    }
    private void ResizeDialog()
    {
        if (Dialog.XamlRoot is not { } root) return;
        ((FrameworkElement)Dialog.Content).Width = Math.Max(280, Math.Min(500, root.Size.Width - 96));
        var available = Math.Max(100, Math.Min(480, root.Size.Height - 240));
        list.Height = Math.Min(available, Math.Max(240, list.Items.Count * 52));
    }
    private void WireSelectors(DependencyObject parent)
    {
        if (parent is PackageVersionSelector selector)
        {
            if (selector.VersionSource is null)
            {
                selector.VersionSource = versionCache.GetAsync;
                selector.VersionPicked += VersionPicked;
            }
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) WireSelectors(VisualTreeHelper.GetChild(parent, index));
    }
    private void VersionPicked(object? sender, string value)
    { if (sender is PackageVersionSelector selector) { session.SetVersion(selector.PackageId, value); Render(); } }
    private void ShowNotice(string message) { notice.Message = message; notice.IsOpen = true; }
    public Dictionary<string, string> Result()
    {
        var result = ProjectRules.Merge(session.Projects[0].Packages, session.Selected);
        foreach (var id in session.Removed) result.Remove(id);
        foreach (var id in ProjectRules.Foundation(result, configuration)) if (!result.ContainsKey(id)) result[id] = "latest";
        return result;
    }
    public void Dispose() { searching?.Cancel(); searching?.Dispose(); cancellation.Cancel(); cancellation.Dispose(); }
}
