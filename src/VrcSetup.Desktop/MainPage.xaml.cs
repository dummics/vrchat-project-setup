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
    public Task<bool> InstallApplicationUpdateAsync(string installer, string result) => ((MainWindow)window).InstallUpdateAsync(installer, result);
    public MainPage(Window window, VrcSetupApplication app)
    {
        this.window = window;
        InitializeComponent(); ViewModel = new(app, this); DataContext = ViewModel;
        SizeChanged += (_, _) => ConstrainContent();
        Navigation.DisplayModeChanged += (_, _) => ConstrainContent();
        CreateView.SizeChanged += (_, _) => ConstrainForms(); SettingsView.SizeChanged += (_, _) => ConstrainForms();
        ProjectsView.SizeChanged += (_, _) => ConstrainForms();
        ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewModel.CurrentPage)) ShowPage(ViewModel.CurrentPage);
            if (eventArgs.PropertyName == nameof(ViewModel.DeveloperMode)) SyncDiagnosticsTab();
            if (eventArgs.PropertyName == nameof(ViewModel.Theme)) RequestedTheme = ViewModel.Theme == "Dark" ? ElementTheme.Dark : ViewModel.Theme == "Light" ? ElementTheme.Light : ElementTheme.Default;
        };
        Loaded += async (_, _) =>
        {
            if (initialized) return; initialized = true;
            try { await ViewModel.InitializeAsync(); SyncDiagnosticsTab(); ShowPage(ViewModel.CurrentPage); initializedCompletion.TrySetResult(); }
            catch (Exception error) { initializedCompletion.TrySetException(error); }
        };
    }
    private void SyncDiagnosticsTab()
    {
        if (ViewModel.DeveloperMode && !ActivityTabs.Items.Contains(DiagnosticsTab)) ActivityTabs.Items.Add(DiagnosticsTab);
        else if (!ViewModel.DeveloperMode && ActivityTabs.Items.Contains(DiagnosticsTab)) ActivityTabs.Items.Remove(DiagnosticsTab);
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
    private async void ImportSettingsClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Settings");
    private async void ActivityClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Activity");
    private void ConstrainContent()
    {
        var pane = Navigation.DisplayMode == NavigationViewDisplayMode.Expanded ? Navigation.OpenPaneLength : Navigation.DisplayMode == NavigationViewDisplayMode.Compact ? Navigation.CompactPaneLength : 0;
        PageContent.MaxWidth = Math.Max(1, ActualWidth - pane);
        ConstrainForms();
    }
    private void ConstrainForms()
    {
        var stackProjectHeader = ProjectsView.ActualWidth < 560;
        Grid.SetRow(ProjectHeaderActions, stackProjectHeader ? 1 : 0);
        Grid.SetColumn(ProjectHeaderActions, stackProjectHeader ? 0 : 1);
        Grid.SetColumnSpan(ProjectHeaderActions, stackProjectHeader ? 3 : 2);
        if (CreateView.ActualWidth > 0)
        {
            CreateLayout.Width = Math.Min(880, CreateView.ActualWidth);
            CreateForm.Width = CreateLayout.Width;
            ImportQueueSection.Width = CreateLayout.Width;
            ImportQueueList.MaxHeight = ViewModel.ImportJobs.Any(x => x.DetailsOpen) ? Math.Min(520, Math.Max(260, CreateView.ActualHeight - 140)) : 220;
            var narrow = CreateForm.Width < 560;
            Grid.SetRow(ImportResourceSummary, narrow ? 1 : 0); Grid.SetColumn(ImportResourceSummary, narrow ? 0 : 1);
            Grid.SetColumnSpan(ImportResourceSummary, narrow ? 3 : 1);
            ProjectFields.Width = Math.Min(620, CreateForm.Width);
            var stackFields = CreateForm.Width < 440;
            ProjectFields.ColumnDefinitions[1].Width = stackFields ? new GridLength(0) : new GridLength(160);
            Grid.SetColumn(PlatformSelector, stackFields ? 0 : 1); Grid.SetRow(PlatformSelector, stackFields ? 1 : 0);
            var stackReview = CreateForm.Width < 720;
            PackageSetup.ColumnDefinitions[1].Width = stackReview ? new GridLength(0) : GridLength.Auto;
            Grid.SetColumn(ReviewAction, stackReview ? 0 : 1); Grid.SetRow(ReviewAction, stackReview ? 2 : 0);
            ReviewAction.Width = Math.Min(260, CreateForm.Width);
            var stackPreset = CreateForm.Width < 340;
            PresetFields.ColumnDefinitions[0].Width = new GridLength(Math.Min(220, stackPreset ? CreateForm.Width : Math.Max(120, CreateForm.Width - 120)));
            PresetFields.ColumnDefinitions[1].Width = stackPreset ? new GridLength(0) : GridLength.Auto;
            Grid.SetColumn(CustomizePackagesButton, stackPreset ? 0 : 1); Grid.SetRow(CustomizePackagesButton, stackPreset ? 1 : 0);
            CustomizePackagesButton.HorizontalAlignment = HorizontalAlignment.Left;
            if (CreationPackageList.ItemsPanelRoot is ItemsWrapGrid items)
                items.ItemWidth = Math.Max(1, (CreateForm.Width - 16) / (CreateForm.Width >= 720 ? 2 : 1));
            Grid.SetColumn(ChoosePackageButton, narrow ? 1 : 2); Grid.SetRow(ChoosePackageButton, narrow ? 1 : 0);
            ChoosePackageButton.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            Grid.SetColumnSpan(DropFileLabel, narrow ? 2 : 1);
        }
        if (SettingsView.ActualWidth > 0) SettingsForm.Width = Math.Min(760, SettingsView.ActualWidth);
    }
    private void CreationPackagesLoaded(object sender, RoutedEventArgs args) => ConstrainForms();
    private async void AddCreationSuggestionClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiCreationPackage row) await ViewModel.AddCreationSuggestionAsync(row); }
    private async void HistoryLogClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.OpenHistoryLogAsync(row); }
    private async void HistoryCheckClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.CheckHistoryProjectAsync(row); }
    private async void HistoryRepairClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.RepairHistoryImportAsync(row); }
    private async void HistoryRestoreClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.RestoreHistoryBackupAsync(row); }
    private void ProjectsSelected(object sender, SelectionChangedEventArgs args)
    {
        if (ViewModel is null) return;
        ViewModel.SelectedProjects.Clear(); foreach (var selected in ProjectList.SelectedItems.OfType<ProjectSnapshot>()) ViewModel.SelectedProjects.Add(selected); ViewModel.NotifyProjectSelection();
    }
    private void ProjectClicked(object sender, ItemClickEventArgs args) { }
    private async void ProjectPackagesClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is ProjectSnapshot project) await ViewModel.OpenProjectPackagesCommand.ExecuteAsync(project); }
    private async void ProjectOpenClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is ProjectSnapshot project) await ViewModel.LaunchProjectCommand.ExecuteAsync(project); }
    private void ProjectRowLoaded(object sender, RoutedEventArgs args) => LayoutProjectRow(sender);
    private void ProjectRowSizeChanged(object sender, SizeChangedEventArgs args) => LayoutProjectRow(sender);
    private static void LayoutProjectRow(object sender)
    {
        if (sender is not Grid grid) return;
        var narrow = grid.ActualWidth < 640;
        if (grid.Tag is bool previous && previous == narrow) return;
        grid.Tag = narrow; grid.ColumnSpacing = narrow ? 0 : 16; grid.RowSpacing = narrow ? 8 : 0;
        grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(130);
        grid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : GridLength.Auto;
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            if (child.Name == "ProjectModified") { Grid.SetColumn(child, narrow ? 0 : 1); Grid.SetRow(child, narrow ? 1 : 0); }
            if (child.Name == "ProjectActions") { Grid.SetColumn(child, narrow ? 0 : 2); Grid.SetRow(child, narrow ? 2 : 0); }
        }
    }
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
    private void PackageDragLeave(object sender, DragEventArgs args) => PackageDropZone.BorderThickness = new Thickness(1);
    private void PackageDragOver(object sender, DragEventArgs args)
    { if (args.DataView.Contains(StandardDataFormats.StorageItems)) { args.AcceptedOperation = DataPackageOperation.Copy; PackageDropZone.BorderThickness = new Thickness(3); args.DragUIOverride.Caption = "Import UnityPackage"; args.DragUIOverride.IsCaptionVisible = true; } }
    private async void PackageDropped(object sender, DragEventArgs args)
    {
        args.Handled = true;
        PackageDropZone.BorderThickness = new Thickness(1);
        if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await args.DataView.GetStorageItemsAsync();
        var files = items.Where(x => x.Path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)).ToArray();
        ViewModel.StageImportFiles(files.Select(x => x.Path));
    }
    private async void ImportFilesClicked(object sender, RoutedEventArgs args)
    {
        if (!await ViewModel.NavigateAsync("Create")) return;
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".unitypackage");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var files = await picker.PickMultipleFilesAsync();
        ViewModel.StageImportFiles(files.Select(x => x.Path));
    }
    private async void ImportFilesDropped(object sender, DragEventArgs args)
    {
        args.Handled = true;
        if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var files = await args.DataView.GetStorageItemsAsync();
        ViewModel.StageImportFiles(files.Where(x => x.Path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)).Select(x => x.Path));
    }
    private void RemoveImportDraftClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiImportDraft draft) ViewModel.RemoveImportDraft(draft); }
    private async void ImportDetailsExpanded(Expander sender, ExpanderExpandingEventArgs args)
    { if (sender.DataContext is UiImportRow row) { row.DetailsOpen = true; ConstrainForms(); await ViewModel.RefreshImportLogAsync(row, true); } }
    private void ImportDetailsCollapsed(object sender, object args)
    { if (sender is Expander { DataContext: UiImportRow row }) { row.DetailsOpen = false; ConstrainForms(); } }
    private void ImportCancelClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiImportRow row) ViewModel.CancelImport(row); }
    private void ImportLogClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiImportRow row) ViewModel.OpenImportLog(row); }
    private async void ImportRecoverClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiImportRow row) await ViewModel.RecoverImportAsync(row); }
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
        try { dialog.XamlRoot = XamlRoot; dialog.RequestedTheme = RequestedTheme; dialog.PrimaryButtonStyle ??= (Style)Resources["ActionButton"]; return await dialog.ShowAsync(); }
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
        if (plan.Projects.Length > 0 && plan.Projects.All(x => x.Creation is not null || x.RecoverySource is not null))
        {
            var recap = new StackPanel { Spacing = 14, MaxWidth = 440 };
            foreach (var project in plan.Projects)
            {
                var platform = (project.Creation ?? project.RecoverySource)!.BuildTarget switch { "Android" => "Android / Quest", "iOS" => "iOS", _ => "Windows" };
                var preset = project.PresetName is null or "My default packages" ? "Default" : project.PresetName;
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(new TextBlock { Text = project.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(new TextBlock { Text = (project.RecoverySource is null ? "" : "Resume · ") + $"{platform} · {preset} · {project.DesiredPolicy.Count} packages", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
                if (project.RecoverySource is not null) row.Children.Add(new TextBlock { Text = "Existing files preserved; backup before reimport.", Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
                ToolTipService.SetToolTip(row, project.Path); recap.Children.Add(row);
            }
            return await ShowDialog(new() { Title = plan.Projects.Length == 1 ? "Import project" : $"Import {plan.Projects.Length} projects", Content = new ScrollViewer { Content = recap, MaxHeight = 360 }, PrimaryButtonText = "Import", CloseButtonText = "Back", DefaultButton = ContentDialogButton.Primary }) == ContentDialogResult.Primary;
        }
        var stack = new StackPanel { Spacing = 14, MaxWidth = 600 };
        if (plan.Projects.All(x => !x.HasChanges)) stack.Children.Add(new TextBlock { Text = "Packages are already up to date. Save the selected version policies.", TextWrapping = TextWrapping.Wrap });
        foreach (var group in PackageReview.Group(plan))
        {
            var names = string.Join(", ", group.Projects.Select(x => x.Name));
            var section = new StackPanel { Spacing = 8 };
            var header = new TextBlock { Text = group.Projects.Length == plan.Projects.Length && plan.Projects.Length > 1 ? $"All {plan.Projects.Length} projects" : names, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            ToolTipService.SetToolTip(header, string.Join(Environment.NewLine, group.Projects.Select(x => x.Path))); section.Children.Add(header);
            foreach (var change in group.Changes)
            {
                var name = ViewModel.PackageDisplayName(change.Id);
                var text = change.Kind switch
                {
                    ChangeKind.Add => $"Add {name} · {change.ResolvedVersion}",
                    ChangeKind.Remove => $"Remove {name} · {change.InstalledVersion}",
                    _ when change.InstalledVersion == change.ResolvedVersion => $"Repair {name} · {change.ResolvedVersion}",
                    _ => $"{name} · {change.InstalledVersion} → {change.ResolvedVersion}"
                };
                section.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
            }
            stack.Children.Add(section);
        }
        foreach (var project in plan.Projects)
        {
            if (project.Creation is { } create)
            {
                stack.Children.Add(new TextBlock { Text = "Create from " + create.UnityPackagePath, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(new TextBlock { Text = "Unity: " + create.BuildTarget + " · Publish: " + string.Join(", ", create.PublishingPlatforms), TextWrapping = TextWrapping.Wrap });
            }
            foreach (var import in project.ExtraImports.Concat(project.Creation?.ExtraImports ?? [])) stack.Children.Add(new TextBlock { Text = project.Name + " · Import: " + Path.GetFileName(import), TextWrapping = TextWrapping.Wrap });
        }
        var legacy = plan.Projects.Where(x => x.NeedsEasyLoginRepository && Directory.Exists(Path.Combine(x.Path, "Assets", "EASY LOGIN"))).ToArray();
        if (legacy.Length > 0) stack.Children.Add(new TextBlock { Text = "Legacy Easy Login folder will be checked and backed up before migration: " + string.Join(", ", legacy.Select(x => x.Name)), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = "Projects open in Unity or changed after this review will be skipped. Backups are kept in each project's .vrcsetup folder.", TextWrapping = TextWrapping.Wrap });
        if (plan.Projects.Any(x => x.Creation is not null) && ViewModel.InspectionReviewNote.Length > 0) stack.Children.Add(new TextBlock { Text = ViewModel.InspectionReviewNote, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { Title = "Review changes", Content = new ScrollViewer { Content = stack, MaxHeight = 430 }, PrimaryButtonText = plan.Projects.Any(x => x.Creation is not null) ? "Create project" : "Apply changes", CloseButtonText = "Back", DefaultButton = ContentDialogButton.Close };
        return await ShowDialog(dialog) == ContentDialogResult.Primary;
    }
    public async Task<string?> PromptRecoveryAsync(string name, bool canResume)
    {
        var result = await ShowDialog(new() { Title = name, Content = canResume ? "This import is unfinished. Resume missing steps, or prepare a new copy." : "Unity project creation was interrupted. Prepare a new copy while keeping the partial folder.", PrimaryButtonText = canResume ? "Resume" : "New copy", SecondaryButtonText = canResume ? "New copy" : "", CloseButtonText = "Later", DefaultButton = ContentDialogButton.Close });
        return result == ContentDialogResult.Primary ? canResume ? "Resume" : "Copy" : result == ContentDialogResult.Secondary ? "Copy" : null;
    }
    public async Task<Dictionary<string, string>?> EditPackagesAsync(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known,
        Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions, PackageInspectionReport? inspection = null)
    {
        using var editor = new PackageSetDialog(title, packages, config, known, search, versions, inspection);
        await ShowDialog(editor.Dialog);
        return editor.Accepted ? editor.Result() : null;
    }
}
