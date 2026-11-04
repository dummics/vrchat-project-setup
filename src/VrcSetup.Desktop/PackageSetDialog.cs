using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private readonly PackageInspectionReport? inspection;
    private readonly HashSet<string> originalSelection;
    private readonly ObservableCollection<PackageChoiceRow> visibleRows = [];
    private readonly Dictionary<string, int> displayOrder = new(StringComparer.OrdinalIgnoreCase);
    public PackageSetDialog(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions, PackageInspectionReport? inspection = null)
    {
        configuration = config; this.search = search; this.inspection = inspection;
        originalSelection = packages.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var working = new Dictionary<string, string>(packages);
        foreach (var id in ProjectRules.Foundation(working, config)) working.TryAdd(id, "latest");
        var snapshot = new ProjectSnapshot { Path = Path.GetFullPath("preset-working-copy"), Name = title, Packages = new(working), Policy = new(working) };
        var discovered = inspection?.Suggestions.Select(x => new PackageDefinition(x.Id, VrcSetup.Infrastructure.PackageCatalog.FriendlyName(x.Id), "latest")) ?? [];
        session = new([snapshot], config, known.Concat(discovered).DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase));
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
        query = new TextBox { PlaceholderText = UiText.Text("Search packages"), MinHeight = 34 };
        panel.Children.Add(query);
        notice = new InfoBar { IsOpen = false, IsClosable = true }; panel.Children.Add(notice);
        if (inspection is not null)
        {
            var recommended = inspection.Suggestions.Where(x => DependencyAdvisor.SuggestedVersion(x) is not null && !working.ContainsKey(x.Id)
                && !(x.Id == ProjectRules.WorldSdk && working.ContainsKey(ProjectRules.AvatarSdk)) && !(x.Id == ProjectRules.AvatarSdk && working.ContainsKey(ProjectRules.WorldSdk))).ToArray();
            if (recommended.Length > 0)
            {
            var addDetected = new Button { Content = string.Format(UiText.Text("Add detected ({0})"), recommended.Length), MinHeight = 30, Padding = new Thickness(10, 4, 10, 4) };
                addDetected.Click += (_, _) => { foreach (var item in recommended) if (!session.Rows().Any(x => x.Id == item.Id && x.Included)) session.SetVersion(item.Id, DependencyAdvisor.SuggestedVersion(item)!); Render(); addDetected.Visibility = Visibility.Collapsed; };
                panel.Children.Add(addDetected);
            }
            if (!inspection.Observation.ArchiveComplete || !inspection.Observation.TextComplete) ShowNotice("Analysis is partial. Other packages may still be required.");
            else if (inspection.Suggestions.Any(x => x.Confidence == DependencyConfidence.Included && working.ContainsKey(x.Id))) ShowNotice("This file includes packages also present in your selection. Review before importing.");
        }
        list = new ListView { SelectionMode = ListViewSelectionMode.None, MaxHeight = 480, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.ItemsSource = visibleRows;
        list.Resources["ListViewItemBackgroundSelected"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        list.Resources["ListViewItemBackgroundSelectedPointerOver"] = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(18, 128, 128, 128));
        list.Resources["ListViewItemBackgroundSelectedPressed"] = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(28, 128, 128, 128));
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="using:VrcSetup_Desktop">
              <Grid Padding="10,6" Margin="0,0,0,3" Background="{ThemeResource AppSurfaceBrush}" BorderBrush="{ThemeResource AppSubtleStrokeBrush}" BorderThickness="1" CornerRadius="4" ColumnSpacing="12"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="100"/></Grid.ColumnDefinitions>
                <CheckBox x:Name="PackageIncluded" IsChecked="{Binding Included, Mode=OneWay}" IsEnabled="{Binding CanToggle}" AutomationProperties.Name="{Binding Name}" MinWidth="20" VerticalAlignment="Center"/>
                <StackPanel Grid.Column="1"><TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/><TextBlock x:Name="PackageDetail" Text="{Binding Id}" FontSize="11" Opacity="0.6" TextTrimming="CharacterEllipsis"/></StackPanel>
                <local:PackageVersionSelector Grid.Column="2" PackageId="{Binding Id}" Value="{Binding SelectedVersion}" MinHeight="30" Padding="8,4" VerticalAlignment="Center"/>
              </Grid>
            </DataTemplate>
            """);
        panel.Children.Add(list);
        Dialog = new() { Title = title, Content = panel };
        Dialog.TitleTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding}" FontSize="18" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/></DataTemplate>
            """);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = UiText.Text("Cancel"), MinHeight = 32, Padding = new Thickness(14, 5, 14, 5) };
        var done = new Button { Content = UiText.Text("Done"), MinHeight = 32, Padding = new Thickness(14, 5, 14, 5), Style = (Style)Application.Current.Resources["AppPrimaryButton"] };
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
        catch (Exception error) { if (!token.IsCancellationRequested) ShowNotice(UserErrors.Message(error)); }
    }
    private void Render()
    {
        rendering = true;
        try
        {
            // Freeze ordering for this dialog lifetime. Search may change visibility,
            // but toggles/version edits must preserve containers, focus and scroll.
            var all = session.Rows();
            foreach (var row in all) if (!displayOrder.ContainsKey(row.Id)) displayOrder[row.Id] = displayOrder.Count;
            var rows = all.Where(x => string.IsNullOrWhiteSpace(query.Text) || (x.Id + " " + x.Name).Contains(query.Text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => displayOrder[x.Id]).ToArray();
            var ids = rows.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var index = visibleRows.Count - 1; index >= 0; index--) if (!ids.Contains(visibleRows[index].Id)) visibleRows.RemoveAt(index);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                var existing = visibleRows.FirstOrDefault(x => x.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null) visibleRows.Insert(index, new(row));
                else existing.Update(row);
            }
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
        if (parent is CheckBox { Name: "PackageIncluded" } toggle && toggle.Tag is null)
        {
            toggle.Tag = true;
            toggle.Click += (_, _) =>
            {
                if (toggle.DataContext is not PackageChoiceRow row) return;
                ToggleChoice(row);
                toggle.IsChecked = row.Included;
            };
        }
        if (parent is TextBlock { Name: "PackageDetail", DataContext: PackageChoiceRow row } detail && detail.Tag as string != row.Id)
        {
            detail.Tag = row.Id;
            var hint = Advice(row.Id);
            detail.Text = hint is null ? row.Id : hint.Confidence == DependencyConfidence.Declared ? "Requires " + hint.VersionConstraint : hint.Confidence.ToString();
            ToolTipService.SetToolTip(detail, hint is null ? row.Id : row.Id + "\n" + string.Join("\n", hint.Evidence.Take(3).Select(x => x.Kind + " · " + x.Asset)));
        }
        if (parent is PackageVersionSelector selector)
        {
            if (selector.VersionSource is null)
            {
                selector.VersionSource = versionCache.GetAsync;
                selector.VersionPicked += VersionPicked;
                if (inspection?.Suggestions.FirstOrDefault(x => x.Id.Equals(selector.PackageId, StringComparison.OrdinalIgnoreCase)) is { } advice)
                    ToolTipService.SetToolTip(selector, advice.Confidence + (advice.VersionConstraint is { Length: > 0 } constraint ? " · requires " + constraint : "") + "\n" + string.Join("\n", advice.Evidence.Take(3).Select(x => x.Kind + " · " + x.Asset)));
            }
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) WireSelectors(VisualTreeHelper.GetChild(parent, index));
    }
    private void VersionPicked(object? sender, string value)
    {
        if (sender is not PackageVersionSelector selector) return;
        if (NeedsExactVersion(selector.PackageId) && value == "latest") { ShowNotice("Choose an exact version. Required: " + Advice(selector.PackageId)!.VersionConstraint); return; }
        session.SetVersion(selector.PackageId, value); Render();
    }
    internal void ToggleChoice(PackageChoiceRow row)
    {
        if (rendering || row.Required) return;
        if (!row.Included && NeedsExactVersion(row.Id) && row.SelectedVersion is "latest" or "Not installed")
            ShowNotice("Choose an exact version for " + row.Name + ". Required: " + Advice(row.Id)!.VersionConstraint);
        else session.Toggle(row.Id);
        Render();
    }
    private DependencySuggestion? Advice(string id) => inspection?.Suggestions.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    private bool NeedsExactVersion(string id) => !originalSelection.Contains(id) && Advice(id) is { Confidence: DependencyConfidence.Declared } advice && DependencyAdvisor.SuggestedVersion(advice) is null;
    private void ShowNotice(string message) { notice.Message = message; notice.IsOpen = true; }
    public Dictionary<string, string> Result()
    {
        var result = ProjectRules.Merge(session.Projects[0].Packages, session.Selected);
        foreach (var id in session.Removed) result.Remove(id);
        foreach (var id in ProjectRules.Foundation(result, configuration)) if (!result.ContainsKey(id)) result[id] = "latest";
        return result;
    }
    public void Dispose() { searching?.Cancel(); searching?.Dispose(); cancellation.Cancel(); cancellation.Dispose(); }

    public sealed class PackageChoiceRow(PackageWorkspaceRow row) : ObservableObject
    {
        private PackageWorkspaceRow current = row;
        public string Id => current.Id;
        public string Name => current.Name;
        public string SelectedVersion => current.SelectedVersion;
        public bool Included => current.Included;
        public bool Required => current.Required;
        public bool CanToggle => !Required;
        public void Update(PackageWorkspaceRow value)
        {
            var previous = current; current = value;
            if (previous.Name != value.Name) OnPropertyChanged(nameof(Name));
            if (previous.SelectedVersion != value.SelectedVersion) OnPropertyChanged(nameof(SelectedVersion));
            if (previous.Included != value.Included) OnPropertyChanged(nameof(Included));
            if (previous.Required != value.Required) { OnPropertyChanged(nameof(Required)); OnPropertyChanged(nameof(CanToggle)); }
        }
    }
}
