using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

public sealed class PackageSetDialog : IDisposable
{
    public ContentDialog Dialog { get; }
    private readonly PackageEditSession session;
    private readonly AppConfiguration configuration;
    private readonly ListView list;
    private readonly TextBox query;
    private readonly ComboBox version;
    private readonly InfoBar notice;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<string, CancellationToken, Task<string[]>> getVersions;
    private bool rendering;
    private string? focused;
    public PackageSetDialog(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions)
    {
        var working = new Dictionary<string, string>(packages);
        foreach (var id in ProjectRules.Foundation(working, config)) working.TryAdd(id, "latest");
        var snapshot = new ProjectSnapshot { Path = Path.GetFullPath("preset-working-copy"), Name = title, Packages = new(working), Policy = new(working) };
        session = new([snapshot], config, known); configuration = config; getVersions = versions;
        var panel = new StackPanel { Spacing = 12, MinWidth = 480, MaxWidth = 640 };
        var searchRow = new Grid { ColumnSpacing = 10 }; searchRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        query = new TextBox { PlaceholderText = "Package name or ID" };
        var find = new Button { Content = "Search" }; Grid.SetColumn(find, 1); searchRow.Children.Add(query); searchRow.Children.Add(find); panel.Children.Add(searchRow);
        notice = new InfoBar { IsOpen = false, IsClosable = true }; panel.Children.Add(notice);
        list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 300 };
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Padding="4,8" ColumnSpacing="16"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="120"/></Grid.ColumnDefinitions>
                <StackPanel><TextBlock Text="{Binding Name}" FontWeight="SemiBold"/><TextBlock Text="{Binding Id}" FontSize="11" Opacity="0.6"/></StackPanel>
                <TextBlock Grid.Column="1" Text="{Binding SelectedVersion}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
              </Grid>
            </DataTemplate>
            """);
        panel.Children.Add(list);
        var row = new Grid { ColumnSpacing = 10 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        version = new ComboBox { Header = "Version for highlighted package", IsEditable = true, ItemsSource = new[] { "latest" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var useVersion = new Button { Content = "Use version", VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumn(useVersion, 1); row.Children.Add(version); row.Children.Add(useVersion); panel.Children.Add(row);
        Dialog = new() { Title = title, Content = panel, PrimaryButtonText = "Save selection", CloseButtonText = "Discard", DefaultButton = ContentDialogButton.Primary };
        Dialog.Closed += (_, _) => cancellation.Cancel();
        find.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(query.Text)) return;
            find.IsEnabled = false;
            try { var results = await search(query.Text, cancellation.Token); session.AddDefinitions(results); Render(); if (results.Length == 0) ShowNotice("No matching packages."); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OperationCanceledException) { ShowNotice(error.Message); }
            finally { find.IsEnabled = true; }
        };
        list.SelectionChanged += async (_, args) =>
        {
            if (rendering) return;
            foreach (var added in args.AddedItems.OfType<PackageWorkspaceRow>()) { focused = added.Id; if (!added.Included) session.Toggle(added.Id); }
            foreach (var removed in args.RemovedItems.OfType<PackageWorkspaceRow>())
            {
                if (removed.Required) ShowNotice("The required VRChat foundation stays included.");
                else if (removed.Included) session.Toggle(removed.Id);
                focused = removed.Id;
            }
            Render(); await LoadVersions();
        };
        useVersion.Click += (_, _) =>
        {
            if (focused is null) return;
            var selected = version.Text.Length > 0 ? version.Text : version.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(selected)) return;
            session.SetVersion(focused, selected); Render();
        };
        Render();
    }
    private void Render()
    {
        rendering = true;
        try
        {
            var rows = session.Rows(); list.ItemsSource = rows;
            foreach (var item in rows.Where(x => x.Included)) list.SelectedItems.Add(item);
            focused ??= rows.FirstOrDefault()?.Id;
            version.Text = rows.FirstOrDefault(x => x.Id == focused)?.SelectedVersion ?? "latest";
        }
        finally { rendering = false; }
    }
    private async Task LoadVersions()
    {
        var id = focused; if (id is null) return;
        try
        {
            string[] available;
            if (ProjectRules.SdkIds.Contains(id))
            {
                HashSet<string>? common = null;
                foreach (var sdk in session.Rows().Where(x => x.Included && ProjectRules.SdkIds.Contains(x.Id)).Select(x => x.Id))
                { var versions = await getVersions(sdk, cancellation.Token); if (common is null) common = new(versions); else common.IntersectWith(versions); }
                available = SemanticVersion.Sort(common ?? []);
            }
            else available = await getVersions(id, cancellation.Token);
            if (focused == id && !cancellation.IsCancellationRequested) version.ItemsSource = new[] { "latest" }.Concat(available).ToArray();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OperationCanceledException) { ShowNotice(error.Message); }
    }
    private void ShowNotice(string message) { notice.Message = message; notice.IsOpen = true; }
    public Dictionary<string, string> Result()
    {
        var result = ProjectRules.Merge(session.Projects[0].Packages, session.Selected);
        foreach (var id in session.Removed) result.Remove(id);
        foreach (var id in ProjectRules.Foundation(result, configuration)) if (!result.ContainsKey(id)) result[id] = "latest";
        return result;
    }
    public void Dispose() { cancellation.Cancel(); cancellation.Dispose(); }
}
