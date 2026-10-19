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
    private bool informationExpanded;
    private bool packageRecapAnimating;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? packageRecapStoryboard;
    private readonly TaskCompletionSource initializedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Initialized => initializedCompletion.Task;
    public MainViewModel ViewModel { get; }
    internal DelayedToolTips ToolTips { get; }
    public bool SuppressDialogs { get; set; }
    public void CloseApplication() => window.Close();
    public Task<bool> InstallApplicationUpdateAsync(string installer, string result) => ((MainWindow)window).InstallUpdateAsync(installer, result);
    public Task<bool> RestartApplicationAsync() => ((MainWindow)window).RestartForLanguageAsync();
    public MainPage(Window window, VrcSetupApplication app)
    {
        this.window = window;
        InitializeComponent(); ViewModel = new(app, this); DataContext = ViewModel;
        ToolTips = new(this);
        SizeChanged += (_, _) => ConstrainContent();
        Navigation.DisplayModeChanged += (_, _) => ConstrainContent();
        Navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => ConstrainContent());
        CreateView.SizeChanged += (_, _) => ConstrainForms(); SettingsView.SizeChanged += (_, _) => ConstrainForms();
        CreateForm.SizeChanged += (_, _) => ConstrainForms();
        ProjectsView.SizeChanged += (_, _) => ConstrainForms();
        PresetEditor.SizeChanged += (_, _) => ConstrainForms();
        ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewModel.CurrentPage)) ShowPage(ViewModel.CurrentPage);
            if (eventArgs.PropertyName == nameof(ViewModel.DeveloperMode)) SyncDiagnosticsTab();
            if (eventArgs.PropertyName == nameof(ViewModel.Theme)) RequestedTheme = ViewModel.Theme == "Dark" ? ElementTheme.Dark : ViewModel.Theme == "Light" ? ElementTheme.Light : ElementTheme.Default;
            if (eventArgs.PropertyName == nameof(ViewModel.CreationPackageSummary)) DispatcherQueue.TryEnqueue(ConstrainForms);
            if (eventArgs.PropertyName == nameof(ViewModel.ImportJobsVisibility)) DispatcherQueue.TryEnqueue(ConstrainForms);
            if (eventArgs.PropertyName == nameof(ViewModel.PresetCatalogVisibility)) DispatcherQueue.TryEnqueue(ConstrainForms);
            if (eventArgs.PropertyName == nameof(ViewModel.EditingPreset)) DispatcherQueue.TryEnqueue(AnimatePresetSelection);
            if (eventArgs.PropertyName == nameof(ViewModel.LibraryIsList)) DispatcherQueue.TryEnqueue(ConstrainForms);
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
        if (page != "Projects") ProjectList.SelectedItems.Clear();
        ProjectsView.Visibility = page == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        CreateView.Visibility = page == "Create" ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceView.Visibility = page == "Workspace" ? Visibility.Visible : Visibility.Collapsed;
        PresetsView.Visibility = page == "Presets" ? Visibility.Visible : Visibility.Collapsed;
        ActivityView.Visibility = page == "Activity" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ActivityNavigationMarker.Visibility = page == "Activity" ? Visibility.Visible : Visibility.Collapsed;
        SettingsNavigationMarker.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        syncingNavigation = true;
        try { Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(x => x.Tag?.ToString() == (page is "Workspace" or "Catalog" ? "Projects" : page)); }
        finally { syncingNavigation = false; }
    }
    private async void NavigationInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ViewModel is null || syncingNavigation) return;
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString() is { } tag)
        {
            if (tag == "About") { ToggleInformation(); return; }
            if (tag == "InfoGroup") return;
            if (tag.StartsWith("Info:", StringComparison.Ordinal))
            {
                if (tag == "Info:Releases") ViewModel.OpenReleasesCommand.Execute(null);
                else if (tag == "Info:Bug") ViewModel.ReportBugCommand.Execute(null);
                else if (tag == "Info:Contact") ViewModel.ContactSupportCommand.Execute(null);
                else if (tag == "Info:Logs") ViewModel.OpenLogsCommand.Execute(null);
                return;
            }
        }
        await ViewModel.NavigateAsync(args.IsSettingsInvoked ? "Settings" : (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString() ?? "Projects");
        ShowPage(ViewModel.CurrentPage);
    }
    private async void NewProjectClicked(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.EditorReady) { await ViewModel.FindEditorCommand.ExecuteAsync(null); if (!ViewModel.EditorReady) { await ViewModel.NavigateAsync("Settings"); return; } }
        var create = new NewProjectDialog(ViewModel, (DataTemplate)Resources["PresetChoice"]);
        if (await ShowDialog(create.Dialog) == ContentDialogResult.Primary)
            await ViewModel.CreateEmptyProjectAsync(create.Name.Text.Trim(), create.Preset.SelectedItem?.ToString(), create.Platform.SelectedValue?.ToString() ?? "StandaloneWindows64");
    }
    private async void ImportSettingsClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Settings");
    private async void ActivityClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Activity");
    private async void SettingsClicked(object sender, RoutedEventArgs args) => await ViewModel.NavigateAsync("Settings");
    private void ConstrainContent()
    {
        var expanded = ActualWidth >= 900;
        var mode = expanded ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
        if (Navigation.PaneDisplayMode != mode) { Navigation.PaneDisplayMode = mode; Navigation.IsPaneOpen = expanded; }
        Navigation.OpenPaneLength = Math.Clamp(ActualWidth * 0.16, 168, 192);
        var pane = Navigation.DisplayMode == NavigationViewDisplayMode.Expanded && Navigation.IsPaneOpen
            ? Navigation.OpenPaneLength : Navigation.CompactPaneLength;
        PageContent.MaxWidth = Math.Max(1, ActualWidth - pane);
        InformationLabel.Visibility = Navigation.IsPaneOpen && Navigation.DisplayMode == NavigationViewDisplayMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        ActivityNavigationLabel.Visibility = InformationLabel.Visibility;
        SettingsNavigationLabel.Visibility = InformationLabel.Visibility;
        if (informationExpanded && !CanExpandInformationInline()) SetInformationExpanded(false);
        ConstrainForms();
    }
    internal void ToggleInformation()
    {
        if (CanExpandInformationInline()) SetInformationExpanded(!informationExpanded);
        else { SetInformationExpanded(false); Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(InformationItem); }
    }
    private void InformationClicked(object sender, RoutedEventArgs args) => ToggleInformation();
    private bool CanExpandInformationInline()
    {
        if (!Navigation.IsPaneOpen || Navigation.DisplayMode != NavigationViewDisplayMode.Expanded) return false;
        var menus = Navigation.MenuItems.OfType<FrameworkElement>().Sum(x => x.ActualHeight);
        var footer = ActivityNavigationItem.ActualHeight + SettingsNavigationItem.ActualHeight + InformationItem.ActualHeight + 16;
        var extra = 4 * Math.Max(36, InformationItem.ActualHeight) + 14;
        return Navigation.ActualHeight >= menus + footer + extra + 96;
    }
    private void SetInformationExpanded(bool expanded)
    {
        informationExpanded = expanded;
        AnimateInformation(expanded);
    }
    private void ConstrainForms()
    {
        ProjectSearch.Width = Math.Clamp(ProjectsView.ActualWidth - (ViewModel.SelectedProjects.Count >= 2 ? ManageSelectionAction.ActualWidth + 16 : 0), 140, 320);
        var stackProjectColumns = ProjectsView.ActualWidth < 640;
        ProjectColumnHeaders.ColumnDefinitions[1].Width = new GridLength(stackProjectColumns ? 0 : 260);
        ProjectColumnHeaders.ColumnDefinitions[2].Width = new GridLength(stackProjectColumns ? 0 : 140);
        Grid.SetRow(ProjectDateSortHeader, stackProjectColumns ? 1 : 0); Grid.SetColumn(ProjectDateSortHeader, stackProjectColumns ? 0 : 1);
        Grid.SetColumnSpan(ProjectDateSortHeader, stackProjectColumns ? 3 : 1);
        if (PresetEditor.ActualWidth > 0) PresetSelector.Width = Math.Clamp(PresetEditor.ActualWidth * .3, 180, 320);
        var stackProjectHeader = ProjectsView.ActualWidth < 560;
        Grid.SetRow(ProjectHeaderActions, stackProjectHeader ? 1 : 0);
        Grid.SetColumn(ProjectHeaderActions, stackProjectHeader ? 0 : 1);
        Grid.SetColumnSpan(ProjectHeaderActions, stackProjectHeader ? 3 : 2);
        if (CreateView.ActualWidth > 0)
        {
            // WinUI scrollbars overlay content. Keep a stable gutter even before
            // scrolling becomes necessary, so actions never sit under the thumb.
            CreateLayout.Width = Math.Max(1, CreateView.ActualWidth - 24);
            CreateForm.Width = CreateLayout.Width;
            if (!packageRecapAnimating)
            {
                var recapWidth = PackageRecapWidth();
                CreationPackageDetails.Width = recapWidth;
                UpdateCreationPackageItemWidth(recapWidth);
            }
            DependencyObject? header = CreationPackageHeader;
            while (header is not null && header is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton) header = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(header);
            if (header is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle)
            { toggle.HorizontalAlignment = HorizontalAlignment.Stretch; toggle.Width = double.NaN; toggle.MaxWidth = CreateForm.Width; }
            // The stock template uses a StaticResource for this border, so local
            // resource overrides do not remove its oversized content outline.
            if (FindVisualChild<Border>(CreationPackageDetails, "ExpanderContent") is { } contentBorder)
                contentBorder.BorderThickness = new Thickness(0);
            ImportQueueSection.Width = CreateLayout.Width;
            ImportQueueList.MaxHeight = ViewModel.ImportJobs.Any(x => x.DetailsOpen) ? Math.Min(480, Math.Max(240, CreateView.ActualHeight - 180))
                : Math.Clamp(CreateView.ActualHeight - CreateForm.ActualHeight - ImportPageHeader.ActualHeight - 40, 120, 240);
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
            Grid.SetColumn(ChoosePackageButton, narrow ? 1 : 2); Grid.SetRow(ChoosePackageButton, narrow ? 1 : 0);
            ChoosePackageButton.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            Grid.SetColumnSpan(DropFileLabel, narrow ? 2 : 1);
        }
        if (SettingsView.ActualWidth > 0) SettingsForm.Width = Math.Max(1, SettingsView.ActualWidth - 24);
    }
    private void CreationPackagesLoaded(object sender, RoutedEventArgs args) => ConstrainForms();
    private void UpdateCreationPackageItemWidth(double recapWidth)
    {
        var listWidth = Math.Max(1, recapWidth - CreationPackageDetails.Padding.Left - CreationPackageDetails.Padding.Right);
        if (double.IsNaN(CreationPackageList.Width) || Math.Abs(CreationPackageList.Width - listWidth) > 0.5)
            CreationPackageList.Width = listWidth;
        if (CreationPackageList.ItemsPanelRoot is not ItemsWrapGrid items) return;
        var available = Math.Max(1, listWidth - CreationPackageList.Padding.Left - CreationPackageList.Padding.Right - 8);
        var itemWidth = available / (available >= 480 ? 2 : 1);
        if (double.IsNaN(items.ItemWidth) || Math.Abs(items.ItemWidth - itemWidth) > 0.5) items.ItemWidth = itemWidth;
    }
    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name) return element;
            if (FindVisualChild<T>(child, name) is { } match) return match;
        }
        return null;
    }
    private double PackageRecapWidth()
    {
        if (CreationPackageDetails.IsExpanded) return Math.Min(560, CreateForm.Width);
        var label = new TextBlock { Text = ViewModel.CreationPackageSummary, FontSize = 13 };
        label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Min(CreateForm.Width, Math.Max(180, label.DesiredSize.Width + 80));
    }
    private void AnimatePackageRecap()
    {
        var previousWidth = CreationPackageDetails.ActualWidth;
        packageRecapStoryboard?.Stop();
        packageRecapStoryboard = null;
        packageRecapAnimating = false;
        ConstrainForms();
        var targetWidth = PackageRecapWidth();
        UpdateCreationPackageItemWidth(targetWidth);
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled || Math.Abs(previousWidth - targetWidth) < 1)
        {
            packageRecapAnimating = false;
            CreationPackageDetails.Width = targetWidth;
            return;
        }
        packageRecapAnimating = true;
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = previousWidth, To = targetWidth, Duration = new Duration(TimeSpan.FromMilliseconds(380)),
            EnableDependentAnimation = true, FillBehavior = Microsoft.UI.Xaml.Media.Animation.FillBehavior.Stop,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, CreationPackageDetails);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Width");
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(packageRecapStoryboard, storyboard)) return;
            packageRecapStoryboard = null;
            packageRecapAnimating = false;
            ConstrainForms();
        };
        packageRecapStoryboard = storyboard;
        storyboard.Begin();
    }
    private void CreationPackagesExpanded(Expander sender, ExpanderExpandingEventArgs args) => AnimatePackageRecap();
    private void CreationPackagesCollapsed(Expander sender, ExpanderCollapsedEventArgs args) => AnimatePackageRecap();
    private async void AddCreationSuggestionClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiCreationPackage row) await ViewModel.AddCreationSuggestionAsync(row); }
    private async void HistoryLogClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.OpenHistoryLogAsync(row); }
    private async void HistoryCheckClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.CheckHistoryProjectAsync(row); }
    private void HistoryHeaderLoaded(object sender, RoutedEventArgs args) => LayoutHistoryHeader(sender);
    private void HistoryHeaderResized(object sender, SizeChangedEventArgs args) => LayoutHistoryHeader(sender);
    private static void LayoutHistoryHeader(object sender)
    {
        if (sender is not Grid grid || grid.FindName("HistoryDate") is not FrameworkElement date || grid.FindName("HistorySummary") is not FrameworkElement summary) return;
        var narrow = grid.ActualWidth < 560;
        Grid.SetRow(date, narrow ? 1 : 0); Grid.SetColumnSpan(summary, narrow ? 2 : 3);
    }
    private async void HistoryRepairClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.RepairHistoryImportAsync(row); }
    private async void HistoryRestoreClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is UiHistoryEntry row) await ViewModel.RestoreHistoryBackupAsync(row); }
    private void ProjectsSelected(object sender, SelectionChangedEventArgs args)
    {
        if (ViewModel is null) return;
        ViewModel.SelectedProjects.Clear(); foreach (var selected in ProjectList.SelectedItems.OfType<ProjectSnapshot>()) ViewModel.SelectedProjects.Add(selected); ViewModel.NotifyProjectSelection();
        ProjectColumnHeaders.Padding = new Thickness(ViewModel.HasSelectedProjects ? 48 : 18, 0, 42, 0);
        DispatcherQueue.TryEnqueue(ConstrainForms);
    }
    private void ProjectClicked(object sender, ItemClickEventArgs args) { }
    private void ManageSelectionResized(object sender, SizeChangedEventArgs args) => ConstrainForms();
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
        grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(260);
        grid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(140);
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            if (child.Name == "ProjectDates") { Grid.SetColumn(child, narrow ? 0 : 1); Grid.SetRow(child, narrow ? 1 : 0); }
            if (child.Name == "ProjectActions") { Grid.SetColumn(child, narrow ? 0 : 2); Grid.SetRow(child, narrow ? 2 : 0); }
        }
    }
    private async void PackageDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) => await ViewModel.ChooseVersionCommand.ExecuteAsync(null);
    private async void ProjectSortClicked(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string column || ViewModel.IsBusy) return;
        var selected = ProjectList.SelectedItems.OfType<ProjectSnapshot>().Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await ViewModel.CycleProjectSortAsync(column);
        foreach (var project in ViewModel.Projects.Where(x => selected.Contains(x.Path))) ProjectList.SelectedItems.Add(project);
    }
    private void ErrorClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.DismissErrorCommand.Execute(null);
    private void VersionSelectorLoaded(object sender, RoutedEventArgs args)
    { if (sender is PackageVersionSelector selector) { selector.VersionSource = ViewModel.LoadVersionsAsync; selector.CachedSource = ViewModel.CachedVersions; } }
    private void PresetVersionPicked(object? sender, string version)
    { if ((sender as FrameworkElement)?.DataContext is UiPackageRow row) PackageScrollStability.Run(sender as FrameworkElement, () => ViewModel.StagePresetVersion(row.Id, version)); }
    private void WorkspaceVersionPicked(object? sender, string version)
    { if ((sender as FrameworkElement)?.DataContext is PackageWorkspaceRow row) PackageScrollStability.Run(sender as FrameworkElement, () => ViewModel.StageWorkspaceVersion(row.Id, version)); }
    private void WorkspaceToggleClicked(object sender, RoutedEventArgs args)
    { if ((sender as FrameworkElement)?.DataContext is PackageWorkspaceRow row) PackageScrollStability.Run(sender as FrameworkElement, () => ViewModel.ToggleWorkspacePackage(row)); }
    private void PresetRemoveClicked(object sender, RoutedEventArgs args) => PackageScrollStability.Run(sender as FrameworkElement, () => ViewModel.RemovePresetPackageCommand.Execute((sender as FrameworkElement)?.DataContext as UiPackageRow));
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
        try { dialog.XamlRoot = XamlRoot; dialog.RequestedTheme = RequestedTheme; dialog.PrimaryButtonStyle ??= (Style)Resources["ActionButton"]; dialog.CloseButtonStyle ??= (Style)Resources["QuietButton"]; if (dialog.Title is string title) dialog.Title = UiText.Text(title); dialog.PrimaryButtonText = UiText.Text(dialog.PrimaryButtonText); dialog.SecondaryButtonText = UiText.Text(dialog.SecondaryButtonText); dialog.CloseButtonText = UiText.Text(dialog.CloseButtonText); return await dialog.ShowAsync(); }
        finally { dialogGate.Release(); }
    }
    public async Task<bool> ConfirmAsync(string title, string content, string action) => await ShowDialog(new()
    { Title = title, Content = new TextBlock { Text = UiText.Text(content), TextWrapping = TextWrapping.Wrap, MaxWidth = 500 }, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary;
    public async Task ShowErrorAsync(string message)
    { if (!SuppressDialogs) await ShowDialog(new() { Title = "Action could not complete", Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 }, CloseButtonText = "Back" }); }
    public async Task ShowUpdateInfoAsync(string details)
    {
        if (SuppressDialogs) return;
        var result = await ShowDialog(new() { Title = "Update details", Content = new ScrollViewer { MaxHeight = 320, Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 520 } }, PrimaryButtonText = "Open logs", SecondaryButtonText = "Report a bug", CloseButtonText = "Close" });
        if (result == ContentDialogResult.Primary) ViewModel.OpenLogsCommand.Execute(null);
        else if (result == ContentDialogResult.Secondary) ViewModel.ReportBugCommand.Execute(null);
    }
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
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(new TextBlock { Text = project.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(new TextBlock { Text = UiText.Text(project.RecoverySource is null ? "Create a new project" : "Complete existing project") + " · " + platform, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
                if (project.RecoverySource is not null) row.Children.Add(new TextBlock { Text = UiText.Text("Add missing assets from the package. Files already in the project stay unchanged."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
                ToolTipService.SetToolTip(row, project.Path); recap.Children.Add(row);
            }
            var existingOnly = plan.Projects.All(x => x.RecoverySource is not null);
            return await ShowDialog(new() { Title = UiText.Text(existingOnly ? "Complete existing project" : "Import project"), Content = new ScrollViewer { Content = recap, MaxHeight = 360 }, PrimaryButtonText = UiText.Text(existingOnly ? "Complete project" : "Import"), CloseButtonText = UiText.Text("Back"), DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary;
        }
        var stack = new StackPanel { Spacing = 14, MaxWidth = 600 };
        if (plan.Projects.All(x => !x.HasChanges)) stack.Children.Add(new TextBlock { Text = "Packages are already up to date. Save the selected version policies.", TextWrapping = TextWrapping.Wrap });
        foreach (var group in PackageReview.Group(plan))
        {
            var names = string.Join(", ", group.Projects.Select(x => x.Name));
            var section = new StackPanel { Spacing = 8 };
            var header = new TextBlock { Text = group.Projects.Length == plan.Projects.Length && plan.Projects.Length > 1 ? UiText.Text("All projects") + $" ({plan.Projects.Length})" : names, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            ToolTipService.SetToolTip(header, string.Join(Environment.NewLine, group.Projects.Select(x => x.Path))); section.Children.Add(header);
            foreach (var change in group.Changes)
            {
                var name = ViewModel.PackageDisplayName(change.Id);
                var text = change.Kind switch
                {
                    ChangeKind.Add => $"{UiText.Text("Add")} {name} · {change.ResolvedVersion}",
                    ChangeKind.Remove => $"{UiText.Text("Remove")} {name} · {change.InstalledVersion}",
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
