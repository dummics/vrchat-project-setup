using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using VrcSetup_Desktop.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace VrcSetup_Desktop;

public sealed partial class MainPage : Page, IDesktopInteraction
{
    private readonly Window window;
    private readonly SemaphoreSlim dialogGate = new(1, 1);
    private bool initialized;
    private bool syncingNavigation;
    private readonly TaskCompletionSource initializedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Initialized => initializedCompletion.Task;
    public MainViewModel ViewModel { get; }
    public bool SuppressDialogs { get; set; }
    public void CloseApplication() => window.Close();
    public MainPage(Window window, VrcSetupApplication app)
    {
        this.window = window;
        InitializeComponent(); ViewModel = new(app, this); DataContext = ViewModel;
        SizeChanged += (_, _) => ConstrainContent();
        Navigation.DisplayModeChanged += (_, _) => ConstrainContent();
        CreateView.SizeChanged += (_, _) => ConstrainForms(); SettingsView.SizeChanged += (_, _) => ConstrainForms();
        ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewModel.CurrentPage)) ShowPage(ViewModel.CurrentPage);
            if (eventArgs.PropertyName == nameof(ViewModel.Theme)) RequestedTheme = ViewModel.Theme == "Dark" ? ElementTheme.Dark : ViewModel.Theme == "Light" ? ElementTheme.Light : ElementTheme.Default;
        };
        Loaded += async (_, _) =>
        {
            if (initialized) return; initialized = true;
            try { await ViewModel.InitializeAsync(); ShowPage(ViewModel.CurrentPage); initializedCompletion.TrySetResult(); }
            catch (Exception error) { initializedCompletion.TrySetException(error); }
        };
    }
    private void ShowPage(string page)
    {
        ProjectsView.Visibility = page == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        CreateView.Visibility = page == "Create" ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceView.Visibility = page == "Workspace" ? Visibility.Visible : Visibility.Collapsed;
        PresetsView.Visibility = page == "Presets" ? Visibility.Visible : Visibility.Collapsed;
        CatalogView.Visibility = page == "Catalog" ? Visibility.Visible : Visibility.Collapsed;
        ActivityView.Visibility = page == "Activity" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        syncingNavigation = true;
        try { Navigation.SelectedItem = page == "Settings" ? Navigation.SettingsItem : Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(x => x.Tag?.ToString() == (page is "Workspace" or "Catalog" ? "Projects" : page)); }
        finally { syncingNavigation = false; }
    }
    private async void NavigationInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ViewModel is null || syncingNavigation) return;
        await ViewModel.NavigateAsync(args.IsSettingsInvoked ? "Settings" : (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString() ?? "Projects");
        ShowPage(ViewModel.CurrentPage);
    }
    private async void NewProjectClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Create");
    private async void ActivityClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Activity");
    private void ConstrainContent()
    {
        var pane = Navigation.DisplayMode == NavigationViewDisplayMode.Expanded ? Navigation.OpenPaneLength : Navigation.DisplayMode == NavigationViewDisplayMode.Compact ? Navigation.CompactPaneLength : 0;
        PageContent.MaxWidth = Math.Max(1, ActualWidth - pane);
        ConstrainForms();
    }
    private void ConstrainForms()
    {
        if (CreateView.ActualWidth > 0) CreateForm.Width = Math.Min(760, CreateView.ActualWidth);
        if (SettingsView.ActualWidth > 0) SettingsForm.Width = Math.Min(760, SettingsView.ActualWidth);
    }
    private void ProjectsSelected(object sender, SelectionChangedEventArgs args)
    {
        if (ViewModel is null) return;
        ViewModel.SelectedProjects.Clear(); foreach (var selected in ProjectList.SelectedItems.OfType<ProjectSnapshot>()) ViewModel.SelectedProjects.Add(selected); ViewModel.NotifyProjectSelection();
    }
    private void ProjectClicked(object sender, ItemClickEventArgs args) { }
    private async void ProjectPackagesClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is ProjectSnapshot project) await ViewModel.OpenProjectPackagesCommand.ExecuteAsync(project); }
    private async void PackageDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) => await ViewModel.ChooseVersionCommand.ExecuteAsync(null);
    private async void SortChanged(object sender, SelectionChangedEventArgs args) { if (initializedCompletion.Task.IsCompletedSuccessfully && ViewModel is not null && !ViewModel.IsBusy) await ViewModel.ChangeSortCommand.ExecuteAsync(null); }
    private void ErrorClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.DismissErrorCommand.Execute(null);
    private void VersionSelectorLoaded(object sender, RoutedEventArgs args)
    { if (sender is PackageVersionSelector selector) { selector.VersionSource = ViewModel.LoadVersionsAsync; selector.CachedSource = ViewModel.CachedVersions; } }
    private void PresetVersionPicked(object? sender, string version)
    { if ((sender as FrameworkElement)?.DataContext is UiPackageRow row) ViewModel.StagePresetVersion(row.Id, version); }
    private void WorkspaceVersionPicked(object? sender, string version)
    { if ((sender as FrameworkElement)?.DataContext is PackageWorkspaceRow row) ViewModel.StageWorkspaceVersion(row.Id, version); }
    private void WorkspaceToggleClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is PackageWorkspaceRow row) ViewModel.ToggleWorkspacePackage(row); }
    private void PresetRemoveClicked(object sender, RoutedEventArgs args) => ViewModel.RemovePresetPackageCommand.Execute((sender as FrameworkElement)?.DataContext as UiPackageRow);
    private void LibraryAddClicked(object sender, RoutedEventArgs args) => ViewModel.AddLibraryPackageCommand.Execute((sender as FrameworkElement)?.DataContext as UiPackageRow);
    private async void PackageStarClicked(object sender, RoutedEventArgs args) => await ViewModel.TogglePackageFavoriteCommand.ExecuteAsync((sender as FrameworkElement)?.DataContext as UiPackageRow);
    private void PackageDragLeave(object sender, DragEventArgs args) => PackageDropZone.BorderThickness = new Thickness(2);
    private void PackageDragOver(object sender, DragEventArgs args)
    { if (args.DataView.Contains(StandardDataFormats.StorageItems)) { args.AcceptedOperation = DataPackageOperation.Copy; PackageDropZone.BorderThickness = new Thickness(3); args.DragUIOverride.Caption = "Import UnityPackage"; args.DragUIOverride.IsCaptionVisible = true; } }
    private async void PackageDropped(object sender, DragEventArgs args)
    {
        PackageDropZone.BorderThickness = new Thickness(2);
        if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await args.DataView.GetStorageItemsAsync();
        var file = items.FirstOrDefault(x => x.Path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase));
        if (file is not null) ViewModel.PackageFile = file.Path;
    }
    public async Task<string?> PickFileAsync(string extension)
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFileAsync())?.Path;
    }
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        await dialogGate.WaitAsync();
        try { dialog.XamlRoot = XamlRoot; dialog.RequestedTheme = RequestedTheme; dialog.PrimaryButtonStyle = (Style)Resources["ActionButton"]; return await dialog.ShowAsync(); }
        finally { dialogGate.Release(); }
    }
    public async Task<bool> ConfirmAsync(string title, string content, string action) => await ShowDialog(new()
    { Title = title, Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 }, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary;
    public async Task ShowErrorAsync(string message)
    { if (!SuppressDialogs) await ShowDialog(new() { Title = "Action could not complete", Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 }, CloseButtonText = "Back" }); }
    public async Task<string?> TextAsync(string title, string initial = "")
    {
        var field = new TextBox { Text = initial, MinWidth = 360 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field, title);
        var dialog = new ContentDialog { Title = title, Content = field, PrimaryButtonText = "Continue", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) => { field.Focus(FocusState.Programmatic); field.SelectAll(); };
        return await ShowDialog(dialog) == ContentDialogResult.Primary ? field.Text.Trim() : null;
    }
    public async Task<string?> ChooseAsync(string title, string[] items)
    {
        if (items.Length == 0) { await ShowErrorAsync("No items available."); return null; }
        var list = new ListView { ItemsSource = items, SelectedIndex = 0, MaxHeight = 340, MinWidth = 400 };
        var filter = new TextBox { PlaceholderText = "Filter" };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(filter); panel.Children.Add(list);
        var dialog = new ContentDialog { Title = title, Content = panel, PrimaryButtonText = "Choose", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        filter.TextChanged += (_, _) => { list.ItemsSource = items.Where(x => x.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); if (list.Items.Count > 0) list.SelectedIndex = 0; };
        return await ShowDialog(dialog) == ContentDialogResult.Primary ? list.SelectedItem?.ToString() : null;
    }
    public async Task<string[]?> ChooseManyAsync(string title, string[] items)
    {
        var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 340, MinWidth = 400 };
        var dialog = new ContentDialog { Title = title, Content = list, PrimaryButtonText = "Add selected", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        return await ShowDialog(dialog) == ContentDialogResult.Primary ? list.SelectedItems.OfType<string>().ToArray() : null;
    }
    public async Task<string?> VersionAsync(string package, string[] versions, string current)
    {
        var picker = new VersionPickerDialog(package, versions, current);
        return await ShowDialog(picker.Dialog) == ContentDialogResult.Primary ? picker.Result : null;
    }
    public async Task<string[]?> ChooseProjectsAsync(string title, ProjectSnapshot[] projects, string[] selected)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, ItemsSource = projects.Select(x => x.Name + " · " + x.Path).ToArray(), MaxHeight = 340, MinWidth = 400 };
        for (var i = 0; i < projects.Length; i++) if (selected.Contains(projects[i].Path, StringComparer.OrdinalIgnoreCase)) list.SelectedItems.Add(list.Items[i]);
        if (await ShowDialog(new() { Title = title, Content = list, PrimaryButtonText = "Use projects", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return null;
        return list.SelectedItems.Cast<string>().Select(x => projects.First(p => p.Name + " · " + p.Path == x).Path).ToArray();
    }
    public async Task<bool> ReviewAsync(OperationPlan plan)
    {
        var stack = new StackPanel { Spacing = 14, MaxWidth = 600 };
        foreach (var project in plan.Projects)
        {
            stack.Children.Add(new TextBlock { Text = project.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 18 });
            stack.Children.Add(new TextBlock { Text = project.Path, TextWrapping = TextWrapping.Wrap, Opacity = 0.65 });
            if (project.Creation is { } create)
            {
                stack.Children.Add(new TextBlock { Text = "Create from " + create.UnityPackagePath, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(new TextBlock { Text = "Unity: " + create.BuildTarget + " · Publish: " + string.Join(", ", create.PublishingPlatforms), TextWrapping = TextWrapping.Wrap });
            }
            foreach (var change in project.Changes.Where(x => x.Kind != ChangeKind.Keep))
                stack.Children.Add(new TextBlock { Text = $"{change.Kind}  {PackageCatalog.FriendlyName(change.Id)}  {change.InstalledVersion ?? "—"} → {change.ResolvedVersion ?? "removed"}", TextWrapping = TextWrapping.Wrap });
            foreach (var import in project.ExtraImports.Concat(project.Creation?.ExtraImports ?? [])) stack.Children.Add(new TextBlock { Text = "Import: " + import, TextWrapping = TextWrapping.Wrap });
            if (project.NeedsEasyLoginRepository) stack.Children.Add(new TextBlock { Text = "Ensure Vulpine Vault repository; recognized legacy Easy Login assets will be backed up and migrated.", TextWrapping = TextWrapping.Wrap });
        }
        stack.Children.Add(new TextBlock { Text = "Projects open in Unity or changed after this review will be skipped. Backups are kept in each project's .vrcsetup folder.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { Title = "Review changes", Content = new ScrollViewer { Content = stack, MaxHeight = 430 }, PrimaryButtonText = plan.Projects.Any(x => x.Creation is not null) ? "Create project" : "Apply changes", CloseButtonText = "Back", DefaultButton = ContentDialogButton.Close };
        return await ShowDialog(dialog) == ContentDialogResult.Primary;
    }
    public async Task<Dictionary<string, string>?> EditPackagesAsync(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions)
    {
        using var editor = new PackageSetDialog(title, packages, config, known, search, versions);
        return await ShowDialog(editor.Dialog) == ContentDialogResult.Primary ? editor.Result() : null;
    }
}
