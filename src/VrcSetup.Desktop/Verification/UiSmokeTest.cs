using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;

namespace VrcSetup_Desktop.Verification;

// Runs the real WinUI dispatcher and renderer without mouse/keyboard injection.
public static class UiSmokeTest
{
    private static int updateRequests;
    private sealed class RateLimitedUpdateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref updateRequests);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
            return Task.FromResult(response);
        }
    }
    public static async Task<VrcSetupApplication> PrepareAsync(string artifacts)
    {
        artifacts = ProjectRules.NormalizePath(artifacts); Directory.CreateDirectory(artifacts);
        updateRequests = 0;
        var application = new VrcSetupApplication(Path.Combine(artifacts, "home"), ["--no-catalog-refresh", "--no-update-check", "--no-editor-discovery", "--no-vcc-registration"])
        { UpdateHttpClientFactory = () => new HttpClient(new RateLimitedUpdateHandler()) };
        var project = Path.Combine(artifacts, "projects", "Creator [PC] & café");
        Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        Dictionary<string, string> packages = new() { [ProjectRules.BaseSdk] = "3.10.5", [ProjectRules.AvatarSdk] = "3.10.5", [ProjectRules.Resolver] = "0.1.29", ["gogoloco"] = "1.8.6" };
        foreach (var package in packages)
        {
            var folder = Path.Combine(project, "Packages", package.Key); Directory.CreateDirectory(folder);
            await AtomicFile.WriteJsonAsync(Path.Combine(folder, "package.json"), new { name = package.Key, version = package.Value });
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), new { dependencies = packages, locked = packages });
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
        var config = await application.Configuration.LoadAsync(); config.UnityProjectsRoot = Path.GetDirectoryName(project)!;
        config.Language = "en";
        config.UnityEditorPath = Path.Combine(artifacts, "saved-editor", "Unity.exe");
        config.VpmPackages = new(packages); // Existing user configuration; fresh defaults have a separate contract test.
        config.PackagePresets["Creator PC"] = packages; await application.Configuration.SaveAsync(config);
        config.CreationRecipes["PC template"] = new() { Preset = "creator pc", Packages = packages }; await application.Configuration.SaveAsync(config);
        await application.Catalog.RememberAsync([new("com.llealloo.audiolink", "AudioLink", "2.0.0"), new("nadena.dev.modular-avatar", "Modular Avatar", "latest"),
            new("com.nanochip.avatarlimbscaling", "Avatar Limb Scaling", "1.2.0"), new("vrchat.blackstartx.gesture-manager", "Gesture Manager", "3.9.0"),
            new("jp.lilxyzw.liltoon", "lilToon", "latest"), new("com.example.package-with-a-long-technical-identifier", "A package with a longer display name", "latest")]);
        return application;
    }
    public static async Task RunAsync(MainWindow window, string artifacts)
    {
        var page = window.Page; await page.Initialized;
        var viewModel = page.ViewModel; var checks = new List<string>();
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(400, 240));
        Require(window.AppWindow.Size.Width >= 720 && window.AppWindow.Size.Height >= 580, "Native window enforces a usable minimum size");
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        Require(viewModel.ErrorMessage.Length == 0, "Startup has no hidden errors");
        Require(viewModel.UnityEditorPath == Path.Combine(artifacts, "saved-editor", "Unity.exe"), "Startup preserves a saved editor path even when its drive is unavailable");
        var installedEditor = UnityEditorDiscovery.FindEditors().FirstOrDefault(x => x.Version == UnityEditorDiscovery.PinnedVersion).Path;
        if (installedEditor is not null)
        {
            await viewModel.FindEditorCommand.ExecuteAsync(null);
            Require(viewModel.EditorReady && viewModel.UnityEditorPath == installedEditor, "Installed pinned editor is discovered");
            if (viewModel.SettingsDirty) await viewModel.SaveSettingsCommand.ExecuteAsync(null);
            Require((await viewModel.Application.Configuration.LoadAsync()).UnityEditorPath == installedEditor, "Discovered editor is saved with settings");
            await viewModel.ReloadConfigurationAsync();
            Require(viewModel.EditorReady && viewModel.UnityEditorPath == installedEditor, "Reload preserves the discovered editor");
        }
        await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null);
        Require(viewModel.LibraryIsList && (await viewModel.Application.Configuration.LoadAsync()).PackageLibraryView == "list", "List view is available and persisted");
        await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null);
        Require(((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Content?.ToString() == viewModel.AppVersion, "Application version is visible in the persistent version menu");
        Require(((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Command is not null && ((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Flyout is null, "Footer update is a direct action without a menu");
        viewModel.IsCheckingUpdate = true;
        Require(((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Content?.ToString() == "Checking for updates…", "Manual update checks have visible feedback on the clicked button");
        viewModel.IsCheckingUpdate = false; viewModel.UpdateStatus = "No published update available";
        Require(((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Content?.ToString() == "Up to date", "Manual no-update result stays visible even while imports occupy the status line");
        viewModel.UpdateStatus = "";
        var originalBeta = viewModel.IncludeBetaUpdates;
        viewModel.IncludeBetaUpdates = true; viewModel.HasApplicationUpdate = true; viewModel.IsUpdateReady = true;
        viewModel.IncludeBetaUpdates = false;
        Require(!viewModel.HasApplicationUpdate && !viewModel.IsUpdateReady, "Changing update channel invalidates the previously offered update");
        viewModel.IncludeBetaUpdates = originalBeta;
        viewModel.UpdateFailed = true; viewModel.UpdateStatus = new string('x', 400);
        Require(viewModel.UpdateSummary == "Update failed", "Technical update errors stay compact in the footer");
        viewModel.UpdateFailed = false; viewModel.UpdateStatus = "";
        await viewModel.CheckApplicationUpdatesCommand.ExecuteAsync(null);
        await viewModel.CheckApplicationUpdatesCommand.ExecuteAsync(null);
        Require(!viewModel.UpdateFailed && viewModel.UpdateSummary.Contains("temporarily paused") && updateRequests == 1,
            "Manual update rate limits show a quiet pause and repeated checks do not hit the server");
        viewModel.UpdateStatus = "";
        var skipped = new OperationReport("fixture-skipped", DateTime.UtcNow, [new("Fixture", "fixture", OperationStatus.Skipped, "Open in Unity")], []);
        Require(new VrcSetup_Desktop.ViewModels.UiHistoryEntry(skipped).State == "Not applied", "Skipped projects are not labelled failed");
        var partial = skipped with { Results = [new("Done", "done", OperationStatus.Completed, ""), new("Skipped", "skip", OperationStatus.Skipped, "Open in Unity")] };
        Require(new VrcSetup_Desktop.ViewModels.UiHistoryEntry(partial).State == "Partially applied", "Mixed batch outcomes remain explicit");
        Require(((Microsoft.UI.Xaml.Controls.Pivot)page.FindName("ActivityTabs")).Items.Count == 2, "Diagnostics tab is absent in normal mode");
        Require(viewModel.PresetLabel == "Default", "Default preset is the initial editing context");
        viewModel.CurrentPage = "Presets"; await Task.Delay(100); page.UpdateLayout();
        Require(((Microsoft.UI.Xaml.Controls.TextBlock)page.FindName("PresetWheelTitle")).Text == "Default", "Preset wheel has a visible selection after binding");
        Require(viewModel.ApplicationProgressVisibility == Visibility.Collapsed, "Idle application progress is hidden");
        Require(!((Microsoft.UI.Xaml.Controls.Expander)page.FindName("CreationPackageDetails")).IsExpanded, "Creation packages start as a compact summary");
        viewModel.IsInspectingPackage = true;
        Require(viewModel.ApplicationStatus.Contains("Checking packages") && viewModel.ApplicationProgressIndeterminate, "Background analysis appears in application status");
        viewModel.IsDownloadingUpdate = true; viewModel.UpdateDownloadPercent = 42;
        Require(viewModel.ApplicationStatus.Contains("42%") && viewModel.ApplicationStatus.Contains("Checking packages") && !viewModel.ApplicationProgressIndeterminate, "Application status preserves concurrent work and real download progress");
        viewModel.IsDownloadingUpdate = false; viewModel.IsInspectingPackage = false;
        viewModel.IsBusy = true; viewModel.Status = "technical fixture path and tool output";
        Require(viewModel.ApplicationStatus == "Working…" && viewModel.ApplicationStopVisibility == Visibility.Visible, "General work uses a short friendly status with stop");
        viewModel.IsBusy = false;
        Require(viewModel.PresetChangesVisibility == Visibility.Collapsed, "Preset save actions are contextual");
        var retries = 0;
        var retryCache = new AsyncVersionCache((_, _) => Task.FromResult(Interlocked.Increment(ref retries) == 1 ? Array.Empty<string>() : new[] { "1.0.0" }));
        Require((await retryCache.GetAsync("retry.package")).Length == 0 && (await retryCache.GetAsync("retry.package")).Length == 1, "Empty version responses can retry after recovery");
        var navigation = (Microsoft.UI.Xaml.Controls.NavigationView)page.FindName("Navigation");
        Require(page.FindName("ActivityNavigationItem") is Microsoft.UI.Xaml.Controls.Button, "Activity is in the navigation footer");
        foreach (var footerPage in new[] { "Activity", "Settings" })
        {
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer((Microsoft.UI.Xaml.Controls.Button)page.FindName(footerPage + "NavigationItem"));
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await Task.Delay(200);
            Require(viewModel.CurrentPage == footerPage && ((FrameworkElement)page.FindName(footerPage + "NavigationMarker")).Visibility == Visibility.Visible, "Footer button navigates and marks the active page: " + footerPage);
        }
        viewModel.CurrentPage = "Presets";
        viewModel.ErrorMessage = "Fixture error"; await Task.Delay(150);
        var errorBanner = (Microsoft.UI.Xaml.Controls.InfoBar)page.FindName("ErrorBanner");
        Require(errorBanner.ActionButton is Microsoft.UI.Xaml.Controls.Button errorAction && ReferenceEquals(errorAction.Command, viewModel.ExportLogsCommand), "Error banner exports a shareable diagnostic archive");
        await Render(page, Path.Combine(artifacts, "error-log-action.png")); viewModel.ErrorMessage = "";
        window.AppWindow.Resize(new(1180, 800)); await Task.Delay(100);
        var contact = page.CreateContactDialog(); contact.XamlRoot = page.XamlRoot;
        var contactTask = contact.ShowAsync(); await Task.Delay(200);
        try
        {
            var contactCopies = Descendants(contact).OfType<Microsoft.UI.Xaml.Controls.Button>().Select(x => x.Tag?.ToString()).ToArray();
            Require(contactCopies.Contains("contact@dummics.com") && contactCopies.Contains("dummics"), "Contact offers explicit email and Discord copy actions");
            await Render(contact, Path.Combine(artifacts, "contact-dialog.png"));
        }
        finally { contact.Hide(); await contactTask; }
        var information = (FrameworkElement)page.FindName("InformationItem");
        var informationMenu = (Microsoft.UI.Xaml.Controls.MenuFlyout)Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(information);
        informationMenu.ShowAt(information); await Task.Delay(100);
        Require(informationMenu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().All(x => x.Command is not null), "Information opens a contextual popup with bound support commands"); informationMenu.Hide();
        window.AppWindow.Resize(new(1180, 800)); navigation.IsPaneOpen = true; await Task.Delay(250);
        var informationHeights = new List<double>(); page.ToggleInformation();
        for (var i = 0; i < 6; i++) { await Task.Delay(40); informationHeights.Add(((FrameworkElement)page.FindName("InformationSurface")).ActualHeight); }
        Require(informationHeights.Zip(informationHeights.Skip(1), (a, b) => b >= a - 1).All(x => x), "Information expansion grows continuously without item-by-item jumps");
        if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            Require(informationHeights.Any(x => x > 0 && x < informationHeights[^1] - 1), "Information expansion has intermediate animation frames");
        await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "information-motion.json"), informationHeights);
        Require(((FrameworkElement)page.FindName("InformationPanel")).Visibility == Visibility.Visible && ((FrameworkElement)page.FindName("InformationSurface")).ActualHeight > 100, "Information expands upward as one surface when the sidebar has room");
        await Render(page, Path.Combine(artifacts, "information-inline.png"));
        window.AppWindow.Resize(new(720, 620)); await Task.Delay(450);
        Require(((FrameworkElement)page.FindName("InformationPanel")).Visibility == Visibility.Collapsed, "Short or compact sidebar collapses inline information before clipping");
        page.ToggleInformation(); await Task.Delay(100); await Render(page, Path.Combine(artifacts, "information-popup.png")); informationMenu.Hide();
        // Let native popup dismissal/focus restoration finish before simulating
        // a stationary hover; otherwise LostFocus cancels that synthetic hover.
        await Task.Delay(500);
        var tooltipOwner = Descendants(navigation).OfType<FrameworkElement>().FirstOrDefault(x => x.IsLoaded && x.ActualWidth > 0 && x.Visibility == Visibility.Visible && x is not Microsoft.UI.Xaml.Controls.Control { IsEnabled: false } && page.ToolTips.HasContent(x));
        Require(tooltipOwner is not null, "Built-in compact navigation tooltips are adopted by the app hover delay");
        page.ToolTips.Observe(tooltipOwner, new(1, 1)); await Task.Delay(1000);
        Require(!page.ToolTips.IsOpen, "Navigation tooltip stays hidden during a brief hover");
        await Task.Delay(1700);
        for (var tick = 0; !page.ToolTips.IsOpen && tick < 10; tick++) await Task.Delay(100);
        Require(page.ToolTips.IsOpen, "Navigation tooltip opens only after a 2.5-second hold");
        page.ToolTips.Dismiss(); Require(!page.ToolTips.IsOpen, "Navigation tooltip closes immediately when hover ends");
        page.ToolTips.Observe(tooltipOwner, new(1, 1)); await Task.Delay(400);
        Require(!page.ToolTips.IsOpen, "A second hover still waits instead of using native instant reshow"); page.ToolTips.Dismiss();
        window.AppWindow.Resize(new(1180, 800)); await Task.Delay(250);
        await viewModel.BeginNewProjectAsync(); await Task.Delay(100);
        var nameInput = (Microsoft.UI.Xaml.Controls.TextBox)page.FindName("NewProjectNameInput");
        Require(!viewModel.CreateConfiguredProjectCommand.CanExecute(null), "Empty project name prevents creation without an error banner");
        nameInput.Text = "folder/name"; await Task.Delay(80);
        Require(viewModel.NewProjectNameErrorVisibility == Visibility.Visible && !viewModel.CreateConfiguredProjectCommand.CanExecute(null) && !viewModel.IsErrorVisible, "Invalid native name input is explained beside the field");
        nameInput.Text = "Fresh project"; await Task.Delay(350); page.UpdateLayout();
        Require(viewModel.NewProjectName == "Fresh project" && viewModel.NewProjectNameErrorVisibility == Visibility.Collapsed && viewModel.CreateConfiguredProjectCommand.CanExecute(null), "Native input updates creation intent and clears field validation");
        Require(viewModel.PresetCarouselVisibility == Visibility.Visible, "New project retains the centered preset wheel");
        Require(viewModel.CurrentPage == "NewProject" && viewModel.NewProjectVisibility == Visibility.Visible && viewModel.NewProjectPlatform == "StandaloneWindows64", "New project has its own page with platform, preset and editable packages");
        Require(viewModel.UseImportPresetVisibility == Visibility.Collapsed && viewModel.PresetOptionsVisibility == Visibility.Collapsed, "New project packages are local to the creation form");
        Require(viewModel.NewProjectSavePresetVisibility == Visibility.Collapsed, "Unchanged creation packages do not offer a duplicate preset");
        Require(((FrameworkElement)page.FindName("NewProjectNameInput")).ActualWidth <= 400, "Project name remains compact on wide windows");
        await Render(page, Path.Combine(artifacts, "new-project.png"));
        window.AppWindow.Resize(new(720, 620)); await Task.Delay(350); page.UpdateLayout();
        var createButton = (FrameworkElement)page.FindName("NewProjectCreate");
        var createBounds = createButton.TransformToVisual(page).TransformBounds(new(0, 0, createButton.ActualWidth, createButton.ActualHeight));
        Require(createBounds.Bottom <= page.ActualHeight && createBounds.Right <= page.ActualWidth, "Creation actions stay reachable on narrow windows");
        await Render(page, Path.Combine(artifacts, "new-project-narrow.png"));
        window.AppWindow.Resize(new(1180, 800)); await Task.Delay(250);
        await viewModel.NavigateAsync("Projects");
        viewModel.SelectedRecipe = null!; viewModel.SelectedRecipe = "No recipe";
        Require(viewModel.ErrorMessage.Length == 0, "Transient cleared template selection is safe");
        viewModel.EditingPreset = "Creator PC";
        var currentPreset = viewModel.EditingPreset;
        var arrowButtons = Descendants((DependencyObject)page.FindName("PresetCarousel")).OfType<Microsoft.UI.Xaml.Controls.Button>().Where(x => x.Content is Microsoft.UI.Xaml.Controls.FontIcon).ToArray();
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(arrowButtons[1]).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        await Task.Delay(300);
        Require(viewModel.EditingPreset != currentPreset, "Preset next arrow changes the selection through the native button");
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(arrowButtons[0]).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        await Task.Delay(300);
        Require(viewModel.EditingPreset == currentPreset, "Preset arrows cycle and wrap without losing the selected preset");
        var stablePresetRows = viewModel.PresetPackages.ToArray();
        viewModel.StagePresetVersion("gogoloco", "1.8.7");
        Require(stablePresetRows.SequenceEqual(viewModel.PresetPackages) && viewModel.PresetPackages.First(x => x.Id == "gogoloco").Version == "1.8.7", "Changing one preset version preserves every row instance");
        viewModel.DiscardPresetChangesCommand.Execute(null);
        var originalVersion = viewModel.PresetPackages.First(x => x.Id == "gogoloco").Version;
        viewModel.StagePresetVersion("gogoloco", "1.8.7");
        viewModel.StagePresetVersion("gogoloco", originalVersion);
        Require(!viewModel.PresetDirty, "Restoring the saved package version clears preset changes");
        var roundTripPackage = viewModel.LibraryPackages.First(x => !x.Required);
        viewModel.AddLibraryPackageCommand.Execute(roundTripPackage);
        viewModel.RemovePresetPackageCommand.Execute(viewModel.PresetPackages.First(x => x.Id == roundTripPackage.Id));
        Require(!viewModel.PresetDirty, "Adding then removing a package restores a clean preset");
        Require(viewModel.LibraryPackages.All(x => !x.Included && !viewModel.PresetPackages.Any(p => p.Id == x.Id)), "Preset catalogue excludes every already included package");
        var original = await viewModel.Application.Projects.ReadAsync(viewModel.Projects[0].Path);
        var optional = viewModel.PresetPackages.First(x => x.Id == "gogoloco");
        viewModel.RemovePresetPackageCommand.Execute(optional);
        Require(viewModel.PresetDirty && !viewModel.PresetPackages.Any(x => x.Id == "gogoloco"), "Preset edits are staged in the selected preset");
        await viewModel.SaveEditedPresetCommand.ExecuteAsync(null);
        Require(viewModel.ApplicationNotice == "Preset saved" && viewModel.ApplicationStatus.Contains("Preset saved"), "Preset save reports concise success in the application footer");
        var saved = await viewModel.Application.Configuration.LoadAsync();
        Require(!saved.PackagePresets["Creator PC"].ContainsKey("gogoloco") && saved.VpmPackages.ContainsKey("gogoloco"), "Saving named preset preserves Default");
        Require((await viewModel.Application.Projects.ReadAsync(original.Path)).Fingerprint == original.Fingerprint, "Preset edits do not mutate project files");
        var favorite = saved.FavoritePackages.Contains("gogoloco");
        await viewModel.TogglePackageFavoriteCommand.ExecuteAsync(viewModel.LibraryPackages.First(x => x.Id == "gogoloco"));
        Require((await viewModel.Application.Configuration.LoadAsync()).FavoritePackages.Contains("gogoloco") != favorite, "Package star persists the favorite choice");
        Require(viewModel.LibraryPackages.TakeWhile(x => x.Favorite).Count() == viewModel.LibraryPackages.Count(x => x.Favorite), "Changing favorites immediately pins all favorite packages above the other catalog rows");
        viewModel.AddLibraryPackageCommand.Execute(viewModel.LibraryPackages.First(x => x.Id == "gogoloco"));
        Require(viewModel.PresetDirty && viewModel.PresetPackages.Any(x => x.Id == "gogoloco"), "Saved library package is added without a repository search");
        Require(!viewModel.LibraryPackages.Any(x => x.Id == "gogoloco"), "Adding a package moves it into the preset without a duplicate catalogue card");
        foreach (var id in new[] { "com.llealloo.audiolink", "com.nanochip.avatarlimbscaling", "vrchat.blackstartx.gesture-manager" })
        {
            viewModel.AddLibraryPackageCommand.Execute(viewModel.LibraryPackages.First(x => x.Id == id));
            var row = viewModel.PresetPackages.First(x => x.Id == id);
            Require(row.Version == "latest", "Preset additions default to latest despite cached package version: " + id);
            viewModel.StagePresetVersion(id, "1.2.3");
            Require(viewModel.PresetPackages.First(x => x.Id == id).Version == "1.2.3", "Explicit preset pin remains supported: " + id);
            viewModel.RemovePresetPackageCommand.Execute(row);
        }
        viewModel.DiscardPresetChangesCommand.Execute(null); viewModel.EditingPreset = "My default packages";
        saved = await viewModel.Application.Configuration.LoadAsync();
        saved.PackagePresets["Creator PC"]["gogoloco"] = "1.8.6";
        await viewModel.Application.Configuration.SaveAsync(saved);
        viewModel.EditingPreset = "Creator PC";
        await viewModel.ReloadConfigurationAsync();
        Require(!viewModel.PresetDirty && viewModel.PresetPackages.Any(x => x.Id == "gogoloco"), "Reloaded configuration replaces the selected preset working copy");
        viewModel.SelectedPreset = "Creator PC";
        var creationField = typeof(VrcSetup_Desktop.ViewModels.MainViewModel).GetField("creationOverrides", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var creationSelection = new Dictionary<string, string> { ["custom.creation-package"] = "1.2.3" }; creationField.SetValue(viewModel, creationSelection);
        viewModel.RemovePresetPackageCommand.Execute(viewModel.PresetPackages.First(x => x.Id == "gogoloco"));
        await viewModel.RenameEditingPresetAsync("Creator renamed");
        saved = await viewModel.Application.Configuration.LoadAsync();
        Require(viewModel.PresetDirty && !viewModel.PresetPackages.Any(x => x.Id == "gogoloco") && saved.PackagePresets["Creator renamed"].ContainsKey("gogoloco"), "Rename preserves unsaved package edits without saving them");
        Require(saved.CreationRecipes["PC template"].Preset == "Creator renamed" && viewModel.SelectedPreset == "Creator renamed", "Renaming keeps template and creation preset references");
        Require(ReferenceEquals(creationField.GetValue(viewModel), creationSelection), "Renaming retains customized packages in the creation draft");
        viewModel.DiscardPresetChangesCommand.Execute(null);
        viewModel.EditingPreset = "My default packages";
        viewModel.PackageFile = Path.Combine(artifacts, "Avatar Quest.unitypackage");
        Require(viewModel.ProjectName.Length > 0 && viewModel.BuildTarget == "Android", "Dropped package suggests name and one platform");
        viewModel.CurrentPage = "Create"; await Task.Delay(150); page.UpdateLayout(); viewModel.ProjectName = "My avatar";
        ((Microsoft.UI.Xaml.Controls.ComboBox)page.FindName("PlatformSelector")).SelectedValue = "StandaloneWindows64";
        await Task.Delay(80);
        Require(viewModel.BuildTarget == "StandaloneWindows64" && viewModel.DestinationPreview.Contains("My avatar"), "Single platform and editable name bind to creation: " + viewModel.BuildTarget);
        Require(viewModel.Projects.Count == 1, "Native project library loaded");
        viewModel.ProjectFilter = "Quest"; Require(viewModel.Projects.Count == 0, "Project filter empty state");
        viewModel.ProjectFilter = "PC"; Require(viewModel.Projects.Count == 1, "Project filter recovers matches");
        await viewModel.OpenProjectPackagesCommand.ExecuteAsync(viewModel.Projects[0]);
        Require(viewModel.CurrentPage == "Workspace", "Project package action opens the shared workspace");
        Require(viewModel.Packages.Any(x => x.Id == "gogoloco"), "Native package workspace uses shared metadata");
        viewModel.SelectedPackage = viewModel.Packages.First(x => x.Id == "gogoloco");
        await viewModel.TogglePackageCommand.ExecuteAsync(null);
        Require(viewModel.Packages.First(x => x.Id == "gogoloco").Action == "Will be removed", "GUI command stages optional removal");
        viewModel.IsCreatingProject = true; viewModel.IsApplyingPackages = true;
        viewModel.ApplyProject = "Progress fixture"; viewModel.ApplyPhase = "Preparing";
        var operationsButton = (Microsoft.UI.Xaml.Controls.Button)page.FindName("OperationsStatusButton");
        Require(operationsButton.IsEnabled && viewModel.RunningOperations.Count >= 2 && !viewModel.CanUseUpdateAction, "Status exposes concurrent operations while update installation stays blocked");
        operationsButton.Flyout.ShowAt(operationsButton); await Task.Delay(180);
        await Render(page, Path.Combine(artifacts, "running-operations.png"));
        var operationsList = (Microsoft.UI.Xaml.Controls.ListView)page.FindName("RunningOperationsList");
        var originalOperations = operationsList.ItemsSource;
        operationsList.ItemsSource = Enumerable.Range(0, 40).Select(i => new VrcSetup_Desktop.ViewModels.RunningOperationRow("Creating project", "Long project name " + i + " with more text than the compact row can show", "Technical detail must stay hidden")).ToArray();
        await Task.Delay(150); page.UpdateLayout();
        Require(operationsList.Items.Count == 40 && operationsList.ActualHeight is > 0 and <= 201 && !Descendants(operationsList).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(x => x.Text == "Technical detail must stay hidden"), "Many operations remain compact, scroll within a bounded flyout and hide details");
        await Render((FrameworkElement)((Microsoft.UI.Xaml.Controls.Flyout)operationsButton.Flyout).Content, Path.Combine(artifacts, "running-operations-many.png"));
        operationsList.ItemsSource = originalOperations;
        operationsButton.Flyout.Hide();
        viewModel.IsCreatingProject = false; viewModel.IsApplyingPackages = false;
        viewModel.ApplyProject = ""; viewModel.ApplyPhase = "";
        var decisions = new FlowInteraction(page);
        Require(await decisions.PickFileAsync(".exe") is null, "Unattended file selection cancels without opening a native picker");
        using (var flow = new VrcSetup_Desktop.ViewModels.MainViewModel(viewModel.Application, decisions))
        {
            await flow.InitializeAsync();
            foreach (var partialRetry in new[] { false, true })
            {
                var retryName = partialRetry ? "Partial creation retry" : "Missing creation retry";
                var retryPath = Path.Combine(flow.ProjectsRoot, retryName);
                if (partialRetry)
                {
                    await AtomicFile.WriteJsonAsync(Path.Combine(retryPath, ".vrcsetup", "state.json"), new SetupState { ProjectName = retryName });
                    await File.WriteAllTextAsync(Path.Combine(retryPath, "preserve.txt"), "owner data");
                }
                var request = new CreationRequest("", retryName, retryPath, "", "StandaloneWindows64", [], [], true);
                var retryPlan = new ProjectPlan { Name = retryName, Path = retryPath, ExpectedFingerprint = "", Changes = [], Creation = request,
                    DesiredPolicy = new Dictionary<string, string> { ["gogoloco"] = "latest" } };
                var retryJob = new ImportJob("retry-" + partialRetry, new("retry-" + partialRetry, DateTime.UtcNow, [retryPlan]), request, "Failed", "Create", 0, "stopped", "");
                decisions.ConfirmResult = true;
                await flow.RepairHistoryImportAsync(new(retryJob)).WaitAsync(TimeSpan.FromSeconds(5));
                Require(flow.CurrentPage == "NewProject" && flow.PresetPackages.Any(x => x.Id == "gogoloco") && !flow.LibraryPackages.Any(x => x.Id == "gogoloco"), "Retry opens captured one-time choices without a nested action lock or duplicate catalogue entry: " + partialRetry);
                if (partialRetry) Require(flow.NewProjectName != retryName && await File.ReadAllTextAsync(Path.Combine(retryPath, "preserve.txt")) == "owner data", "Partial retry preserves the old folder and suggests a separate destination");
                await flow.NavigateAsync("Projects");
            }
            checks.AddRange(await ConcurrencyChecks.RunAsync(flow, decisions));
            var pickerRequests = decisions.SaveFileRequests; decisions.FileChoice = null; flow.IsBusy = true;
            await flow.ExportLogsCommand.ExecuteAsync(null);
            Require(decisions.SaveFileRequests == pickerRequests + 1 && flow.IsBusy, "Diagnostic export remains accessible while busy without changing operation state");
            flow.IsBusy = false;
            var initialConfigText = await File.ReadAllTextAsync(flow.Application.Paths.ConfigFile);
            var initialImportFile = flow.PackageFile;
            await flow.BeginNewProjectAsync(); flow.EditingPreset = "Creator renamed"; flow.NewProjectName = "Local creation fixture";
            flow.StagePresetVersion("gogoloco", "latest");
            Require(flow.NewProjectSavePresetVisibility == Visibility.Visible, "Customized creation packages offer saving a separate preset");
            Require(flow.PresetDirty && flow.NewProjectVisibility == Visibility.Visible && flow.PackageFile == initialImportFile, "New project custom packages do not change import drafts");
            OperationPlan? creationPlan = null;
            flow.EnqueueNewProjectForVerification = plan => { creationPlan = plan; return Task.CompletedTask; };
            decisions.ClearReview();
            await flow.CreateConfiguredProjectCommand.ExecuteAsync(null);
            if (flow.EditorReady)
                Require(creationPlan?.Projects.Single().Creation?.EmptyProject == true && creationPlan.Projects.Single().DesiredPolicy.GetValueOrDefault("gogoloco") == "latest" && flow.CurrentPage == "Projects" && !flow.PresetDirty && decisions.LastReview is null, "Custom preset directly queues one-time packages and returns to Projects without preset/review prompts");
            else Require(decisions.LastReview is null, "Missing editor selection does not proceed to a creation review");
            Require(!Directory.Exists(Path.Combine(flow.ProjectsRoot, flow.NewProjectName)), "Intercepted creation fixture creates no real project folder");
            flow.EnqueueNewProjectForVerification = null;
            Require(await File.ReadAllTextAsync(flow.Application.Paths.ConfigFile) == initialConfigText, "New project edits do not overwrite a saved preset");
            await flow.NavigateAsync("Projects"); await flow.BeginNewProjectAsync();
            flow.StagePresetVersion("gogoloco", "latest"); decisions.ChangesChoice = "Cancel";
            Require(await flow.NavigateAsync("Projects") && flow.NewProjectVisibility == Visibility.Collapsed, "Leaving disposable creation choices never asks to save a preset");
            await flow.BeginNewProjectAsync(); flow.NewProjectName = flow.Projects[0].Name;
            flow.StagePresetVersion("gogoloco", "latest"); decisions.ConfirmResult = false;
            await flow.CreateConfiguredProjectCommand.ExecuteAsync(null);
            Require(flow.CurrentPage == "NewProject", "Existing project is left untouched when package handoff is declined");
            decisions.ConfirmResult = true;
            await flow.CreateConfiguredProjectCommand.ExecuteAsync(null);
            Require(flow.CurrentPage == "Workspace" && flow.WorkspaceDirty && flow.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "latest", "Existing project opens staged package choices without recreation or applying them");
            decisions.ChangesChoice = "Discard"; await flow.NavigateAsync("Projects"); decisions.ConfirmResult = false;
            decisions.ChangesChoice = null;
            var languageBefore = flow.SelectedLanguage;
            flow.SelectedLanguage = UiText.CurrentLanguage == "en" ? "it" : "en";
            Require(flow.SettingsSaveLabel == "Save and restart", "Changing language uses the single settings save action");
            await flow.SaveSettingsCommand.ExecuteAsync(null);
            Require(decisions.Restarts == 1 && (await flow.Application.Configuration.LoadAsync()).Language == flow.SelectedLanguage, "Language is saved before the requested restart");
            flow.SelectedLanguage = languageBefore; await flow.SaveSettingsCommand.ExecuteAsync(null);
            await flow.NavigateAsync("Create"); flow.PackageFile = "";
            await flow.CreateProjectCommand.ExecuteAsync(null);
            Require(flow.ImportError.Contains("Projects → New project"), "Import without a UnityPackage directs users to New project");
            var invalidFolder = Path.Combine(artifacts, "Folder.unitypackage"); Directory.CreateDirectory(invalidFolder);
            flow.StageImportFiles([invalidFolder]);
            Require(flow.ImportDrafts.Count == 0 && flow.ImportError.Contains("not a folder"), "Folders named like UnityPackages are rejected by file staging");
            flow.ImportError = "";
            await flow.OpenProjectPackagesCommand.ExecuteAsync(flow.Projects[0]);
            flow.StageWorkspaceVersion("gogoloco", "1.8.7");
            Require(!await flow.NavigateAsync("Settings") && flow.WorkspaceDirty && flow.CurrentPage == "Workspace", "Declining discard keeps the project, pending versions and current page intact");
            decisions.ConfirmResult = true;
            Require(await flow.NavigateAsync("Projects") && !flow.WorkspaceDirty, "Navigation does not retain a hidden project draft");
            await flow.OpenProjectPackagesCommand.ExecuteAsync(flow.Projects[0]);
            Require(!flow.WorkspaceDirty && flow.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "1.8.6", "Reopening a project reloads the original version policy");
await flow.UndoChangesCommand.ExecuteAsync(null);
            Require(flow.CurrentPage == "Workspace" && flow.BrowsingProjectPackages, "Package catalog stays inside the project management page");
            foreach (var id in new[] { "com.llealloo.audiolink", "com.nanochip.avatarlimbscaling", "vrchat.blackstartx.gesture-manager" })
            {
                var row = flow.LibraryPackages.First(x => x.Id == id);
                flow.AddLibraryPackageCommand.Execute(row);
                Require(flow.Packages.First(x => x.Id == id).SelectedVersion == "latest" && ReferenceEquals(row, flow.LibraryPackages.First(x => x.Id == id)), "Project addition ignores cached pin and updates the same card: " + id);
                flow.AddLibraryPackageCommand.Execute(row);
            }
            Require(!flow.LibraryPackages.Any(x => x.Id == "gogoloco" || x.Id == ProjectRules.BaseSdk), "Add catalog excludes packages already included in the project");
            flow.AddLibraryPackageCommand.Execute(flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury"));
            Require(flow.WorkspaceDirty && flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury").Included && flow.Packages.Any(x => x.Id == "com.vrcfury.vrcfury" && x.Action == "Will be added"), "Staged additions stay visible as Added until Apply");
            flow.AddLibraryPackageCommand.Execute(flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury"));
            Require(!flow.WorkspaceDirty && !flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury").Included, "Clicking Added cancels the staged addition in place");
            flow.AddLibraryPackageCommand.Execute(flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury"));

            Require(flow.CurrentPage == "Workspace" && flow.WorkspaceDirty, "Returning from the library keeps the same pending batch");
            await flow.UndoChangesCommand.ExecuteAsync(null);
            Require(!flow.WorkspaceDirty && !flow.Packages.Any(x => x.Id == "com.vrcfury.vrcfury"), "Revert restores the installed project selection");
            flow.ToggleWorkspacePackage(flow.Packages.First(x => x.Id == "gogoloco")); flow.ToggleWorkspacePackage(flow.Packages.First(x => x.Id == "gogoloco"));
            Require(!flow.WorkspaceDirty, "Undoing a removal restores the original version policy without a new change");

            flow.ToggleWorkspacePackage(flow.Packages.First(x => x.Id == "gogoloco"));
            Require(!flow.LibraryPackages.Any(x => x.Id == "gogoloco"), "Installed packages stay in the managed section during staged removal");
            flow.ToggleWorkspacePackage(flow.Packages.First(x => x.Id == "gogoloco"));
            Require(!flow.WorkspaceDirty && flow.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "1.8.6" && !flow.LibraryPackages.Any(x => x.Id == "gogoloco"), "Readding a staged removal restores its original policy and hides it from the add catalog");
            flow.StageWorkspaceVersion("gogoloco", "1.8.7");
            var pendingVersion = flow.Packages.First(x => x.Id == "gogoloco");
            Require((string)new PackageRowConverter().Convert(pendingVersion, typeof(string), "Version", "en") == "1.8.7", "Version selector displays the pending choice instead of the old installed version");

            Require(flow.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "1.8.7", "Returning from Add packages retains the current operation's version selection");
            await flow.NavigateAsync("Settings");
            await flow.OpenProjectPackagesCommand.ExecuteAsync(flow.Projects[0]);
            Require(!flow.WorkspaceDirty && flow.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "1.8.6", "Leaving and reopening clears pending versions and reloads the installed policy");

            flow.LibraryFilter = "vrc fury";
            Require(flow.LibraryPackages.Concat(flow.PresetPackages).Any(x => x.Id == "com.vrcfury.vrcfury"), "Unified package search finds multi-word matches in the correct section"); flow.LibraryFilter = "";
            var secondProject = Path.Combine(artifacts, "multi-project-fixture");
            Directory.CreateDirectory(Path.Combine(secondProject, "Assets")); Directory.CreateDirectory(Path.Combine(secondProject, "ProjectSettings"));
            await File.WriteAllTextAsync(Path.Combine(secondProject, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
            var firstProject = flow.Projects[0]; var firstFingerprint = firstProject.Fingerprint;
            Directory.CreateDirectory(Path.Combine(secondProject, "Packages"));
            await AtomicFile.WriteJsonAsync(Path.Combine(secondProject, "Packages", "vpm-manifest.json"), new { dependencies = firstProject.Packages, locked = firstProject.Packages });
            await flow.OpenProjectsAsync([firstProject, await flow.Application.Projects.ReadAsync(secondProject)]);
            flow.AddLibraryPackageCommand.Execute(flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury"));
            Require(flow.Packages.First(x => x.Id == "com.vrcfury.vrcfury").TargetCount == 2 && flow.SelectedProjects.Count == 0, "Multi-project edits target the explicit batch without retaining library selections");
            decisions.ConfirmResult = false;
            Require(!await flow.NavigateAsync("Presets") && flow.WorkspaceDirty && flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury").Included, "Multi-project discard cancellation preserves the Added state and draft");
            decisions.ConfirmResult = true;
            Require(await flow.NavigateAsync("Presets") && !flow.WorkspaceDirty && flow.Packages.Count == 0, "Confirmed multi-project exit clears all staged edits");
            Require((await flow.Application.Projects.ReadAsync(firstProject.Path)).Fingerprint == firstFingerprint, "Managing and discarding a batch never writes the project");
            flow.FavoritesOnly = true;
            flow.LibraryFilter = "nonexistent-package-query";
            Require(flow.PresetPackages.Count > 0 && flow.PresetIncludedVisibility == Visibility.Collapsed && flow.IsSearchingPackages, "Search scopes results to addable packages and shows activity immediately");
            flow.ClearPackageFiltersCommand.Execute(null);
            Require(!flow.IsSearchingPackages && flow.PresetIncludedVisibility == Visibility.Visible, "Clearing search restores included packages without changing membership");
            await flow.NavigateAsync("Settings");
            var savedPrefix = flow.NamingPrefix; flow.NamingPrefix = "Unsaved fixture";
            Require(flow.SettingsDirty && !await flow.NavigateAsync("Projects") && !await flow.ConfirmCloseAsync(), "Settings cancellation protects edits on navigation and close");
            decisions.SettingsChoice = "Discard";
            Require(await flow.NavigateAsync("Projects") && !flow.SettingsDirty && flow.NamingPrefix == savedPrefix, "Discard reloads saved settings");
            await flow.NavigateAsync("Settings"); flow.UnityEditorPath = ""; flow.NamingPrefix = "Saved fixture"; decisions.SettingsChoice = "Save";
            Require(await flow.NavigateAsync("Projects") && !flow.SettingsDirty && (await flow.Application.Configuration.LoadAsync()).Naming.DefaultPrefix == "Saved fixture", "Saving before navigation persists the settings");
            await flow.NavigateAsync("Settings"); flow.NamingPatterns = "[";
            Require(!await flow.NavigateAsync("Projects") && flow.SettingsDirty, "Invalid settings stay editable and prevent accidental exit");
            Require((await flow.Application.Configuration.LoadAsync()).Naming.DefaultPrefix == "Saved fixture", "Failed validation never changes the saved configuration");
            decisions.SettingsChoice = "Discard"; await flow.NavigateAsync("Presets");
            decisions.FileChoice = Path.Combine(artifacts, "shared-preset.json");
            await flow.ExportPresetCommand.ExecuteAsync(null);
            var shared = PortablePreset.Read(await File.ReadAllTextAsync(decisions.FileChoice));
            var presetCount = flow.Presets.Count;
            await flow.ImportPresetCommand.ExecuteAsync(null);
            Require(flow.Presets.Count == presetCount + 1 && !flow.PresetDirty && flow.PresetPackages.Count >= shared.Packages.Count, "Exported preset imports as a saved new preset without overwriting existing ones");
            await File.WriteAllTextAsync(decisions.FileChoice, "{\"Name\":\"Broken\",\"Packages\":null}");
            await flow.ImportPresetCommand.ExecuteAsync(null);
            Require(flow.Presets.Count == presetCount + 1 && flow.ErrorMessage.Length > 0, "A broken preset leaves existing presets intact and reports a readable error");
            decisions.FileChoice = null;
            flow.ErrorMessage = "";
            flow.StagePresetVersion("gogoloco", "1.8.7"); decisions.ChangesChoice = "Cancel";
            Require(!await flow.NavigateAsync("Projects") && flow.PresetDirty, "Cancel preserves the unsaved preset and page");
            decisions.ChangesChoice = "Save"; var savedPresetName = flow.EditingPreset;
            Require(await flow.NavigateAsync("Projects") && !flow.PresetDirty, "Save and leave persists the preset before navigation");
            var savedPresetConfig = await flow.Application.Configuration.LoadAsync();
            Require(savedPresetConfig.PackagePresets.GetValueOrDefault(savedPresetName)?.GetValueOrDefault("gogoloco") == "1.8.7", "Save and leave writes the selected preset version");
            decisions.ChangesChoice = null;
            await flow.NavigateAsync("Create");
            var importFile = flow.PackageFile; var presetBefore = (await flow.Application.Configuration.LoadAsync()).VpmPackages.ToDictionary();
            await flow.CustomizeCreationPackagesCommand.ExecuteAsync(null);
            Require(flow.CurrentPage == "ImportPackages" && flow.PresetOptionsVisibility == Visibility.Collapsed, "Import customization reuses the package editor without editing saved presets");
            var importOptional = flow.LibraryPackages.First(); flow.AddLibraryPackageCommand.Execute(importOptional);
            await flow.SaveEditedPresetCommand.ExecuteAsync(null);
            Require((await flow.Application.Configuration.LoadAsync()).VpmPackages.OrderBy(x => x.Key).SequenceEqual(presetBefore.OrderBy(x => x.Key)), "Project-only customization cannot save over the shared preset");
            await flow.UsePresetForImportCommand.ExecuteAsync(null);
            Require(flow.CurrentPage == "Create" && flow.PackageFile == importFile && flow.CreationPackages.Any(x => x.Id == importOptional.Id && x.Selected), "Using a local package selection returns to the unchanged import draft");
            var queue = flow.Application.Imports;
            var queueSync = typeof(ImportQueue).GetField("sync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(queue)!;
            var queueJobs = (List<ImportJob>)typeof(ImportQueue).GetField("jobs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(queue)!;
            var protectedJob = new ImportJob("protected-fixture", new OperationPlan("protected", DateTime.UtcNow, []), new CreationRequest("", "Protected", "fixture", "", "StandaloneWindows64", [], []), "Running", "Import", 20, "", "", DateTime.UtcNow);
            lock (queueSync) queueJobs.Add(protectedJob);
            try
            {
                var confirmations = decisions.Confirmations; var configuration = await File.ReadAllTextAsync(flow.Application.Paths.ConfigFile);
                await flow.ResetSettingsCommand.ExecuteAsync(null); await flow.RepairInstallationCommand.ExecuteAsync(null);
                await flow.UninstallApplicationCommand.ExecuteAsync(null); await flow.ImportSettingsCommand.ExecuteAsync(null);
                await flow.SaveSettingsCommand.ExecuteAsync(null); await flow.RunBackgroundUpdateCheckAsync();
                Require(decisions.Confirmations == confirmations && await File.ReadAllTextAsync(flow.Application.Paths.ConfigFile) == configuration && flow.ErrorMessage.Length > 0, "Active imports block maintenance, settings restore/save and automatic update without touching configuration");
                Require(protectedJob.Active, "Protected imports remain active after blocked operations");
            }
            finally { lock (queueSync) queueJobs.Remove(protectedJob); }
        }
        Require((await new VrcSetupApplication(Path.Combine(artifacts, "seed-check-home")).Inspection.LoadIndexAsync()).Signatures.Length > 1000, "Published bundle includes the offline dependency signature seed");
        var inspectionFile = Path.Combine(artifacts, "Lullaby by Skittie - PC Package.unitypackage");
        using (var file = File.Create(inspectionFile))
        using (var compressed = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Compress))
        using (var tar = new System.Formats.Tar.TarWriter(compressed))
        using (var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Assets/Test.prefab")))
            tar.WriteEntry(new System.Formats.Tar.UstarTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "11111111111111111111111111111111/pathname") { DataStream = data });
        var delayed = new DelayedInspectionReader();
        var slowApp = new VrcSetupApplication(Path.Combine(artifacts, "slow-inspection-home"), ["--no-catalog-refresh"], delayed);
        using (var responsive = new VrcSetup_Desktop.ViewModels.MainViewModel(slowApp, page))
        {
            responsive.PackageFile = inspectionFile;
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            page.DispatcherQueue.TryEnqueue(() => dispatched.TrySetResult());
            await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Require(responsive.IsInspectingPackage, "Dispatcher remains usable while package analysis is pending");
            responsive.PackageFile = ""; delayed.Release.TrySetResult(); await Task.Delay(120);
            Require(responsive.CreationInspection is null && responsive.InspectionStatus.Length == 0, "Replacing a file rejects stale inspection results");
        }
        var proof = new DependencyEvidence("Assets/Test.prefab", "script");
        var advice = new PackageInspectionReport(new(1, 0, 0, true, true, [], [], 0), "fixture", "Avatar prefab", 0,
            [new("com.vrcfury.vrcfury", DependencyConfidence.Detected, null, [proof]), new("com.poiyomi.toon", DependencyConfidence.Detected, null, [proof]), new(ProjectRules.WorldSdk, DependencyConfidence.Detected, null, [proof]), new("example.range", DependencyConfidence.Declared, ">=2.0.0", [proof])], false);
        using (var suggested = new PackageSetDialog("Packages for this project", new() { ["com.poiyomi.toon"] = "8.0.0", [ProjectRules.AvatarSdk] = "3.10.5" }, new(), viewModel.Registry.ToArray(),
            (_, _) => Task.FromResult(Array.Empty<PackageDefinition>()), (_, _) => Task.FromResult(Array.Empty<string>()), advice))
        {
            suggested.Dialog.XamlRoot = page.XamlRoot; var shown = suggested.Dialog.ShowAsync(); await Task.Delay(180); suggested.Dialog.UpdateLayout();
            var add = Descendants(suggested.Dialog).OfType<Microsoft.UI.Xaml.Controls.Button>().First(x => x.Content is string text && text.StartsWith("Add detected"));
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(add);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke(); await Task.Delay(100);
            Require(suggested.Result()["com.vrcfury.vrcfury"] == "latest" && suggested.Result()["com.poiyomi.toon"] == "8.0.0", "Detected packages stage additions without replacing existing pins");
            Require(!suggested.Result().ContainsKey(ProjectRules.WorldSdk) && !suggested.Result().ContainsKey("example.range"), "Suggestions preserve SDK kind and do not convert declared ranges into latest");
            await Render(suggested.Dialog, Path.Combine(artifacts, "detected-packages.png")); suggested.Dialog.Hide(); await shown;
            Require(!suggested.Accepted, "Detected selection stays staged until the user accepts");
        }
        var theme = viewModel.Theme;
        viewModel.RemovePresetPackageCommand.Execute(viewModel.PresetPackages.First(x => x.Id == "gogoloco"));
        var librarySnapshot = viewModel.LibraryPackages.ToArray();
        var favoriteRow = librarySnapshot.First();
        var originalFavorite = favoriteRow.Favorite;
        var collectionReset = false;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler onCollection = (_, args) => collectionReset |= args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset;
        viewModel.LibraryPackages.CollectionChanged += onCollection;
        await viewModel.TogglePackageFavoriteCommand.ExecuteAsync(favoriteRow);
        Require(favoriteRow.Favorite != originalFavorite && !collectionReset && librarySnapshot.All(viewModel.LibraryPackages.Contains) && viewModel.LibraryPackages.TakeWhile(x => x.Favorite).Count() == viewModel.LibraryPackages.Count(x => x.Favorite), "Favorite pinning reorders existing cards without resetting their identity");
        await viewModel.TogglePackageFavoriteCommand.ExecuteAsync(favoriteRow);
        viewModel.LibraryPackages.CollectionChanged -= onCollection;
        viewModel.DiscardPresetChangesCommand.Execute(null);
        using (var editor = new PackageSetDialog("Creator packages", new() { ["custom.required"] = "1.0.0" }, new() { RequiredPackages = ["custom.required"] }, viewModel.Registry.ToArray(),
            (_, _) => Task.FromResult(Array.Empty<PackageDefinition>()), (_, _) => Task.FromResult(Array.Empty<string>())))
        {
            editor.Dialog.XamlRoot = page.XamlRoot;
            var shown = editor.Dialog.ShowAsync(); await Task.Delay(180);
            editor.Dialog.UpdateLayout();
            var inlineVersion = Descendants(editor.Dialog).OfType<PackageVersionSelector>().First(x => x.PackageId == "custom.required");
            Require(inlineVersion.VersionSource is not null, "Shared package editor wires inline version selector");
            var choiceList = Descendants(editor.Dialog).OfType<Microsoft.UI.Xaml.Controls.ListView>().First(x => x.Items.OfType<PackageSetDialog.PackageChoiceRow>().Any());
            var stableChoices = choiceList.Items.OfType<PackageSetDialog.PackageChoiceRow>().ToArray();
            var choiceSource = choiceList.ItemsSource;
            var optionalChoice = stableChoices.FirstOrDefault(x => !x.Required);
            if (optionalChoice is not null)
            {
                var included = optionalChoice.Included;
                editor.ToggleChoice(optionalChoice);
                Require(ReferenceEquals(choiceSource, choiceList.ItemsSource) && stableChoices.SequenceEqual(choiceList.Items.OfType<PackageSetDialog.PackageChoiceRow>()), "Package toggle preserves source, row instances and display order");
                Require(editor.Result().ContainsKey(optionalChoice.Id) != included, "Stable package row still stages the toggle");
                editor.ToggleChoice(optionalChoice);
            }
            Require(Descendants(editor.Dialog).OfType<Microsoft.UI.Xaml.Controls.CheckBox>().Where(x => x.DataContext is PackageSetDialog.PackageChoiceRow { Required: true }).All(x => !x.IsEnabled), "Foundation toggles are disabled without a warning");
            inlineVersion.PickVersion("1.2.3"); await Task.Delay(100);
            Require(editor.Result()["custom.required"] == "1.2.3", "Shared editor stages version directly from its row");
            Require(ReferenceEquals(choiceSource, choiceList.ItemsSource) && stableChoices.SequenceEqual(choiceList.Items.OfType<PackageSetDialog.PackageChoiceRow>()), "Version edit preserves package row instances and order");
            await Render(editor.Dialog, Path.Combine(artifacts, "package-editor.png"));
            Require(editor.Result().ContainsKey("custom.required"), "Native preset working copy preserves custom foundation");
            var buttons = Descendants((DependencyObject)editor.Dialog.Content).OfType<Microsoft.UI.Xaml.Controls.Button>().Where(x => x.Content is string text && text is "Done" or "Cancel").ToArray();
            Require(buttons.Length == 2 && buttons.All(x => x.ActualWidth < 120), "Shared editor uses compact contextual footer actions");
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 800)); await Task.Delay(180); page.UpdateLayout();
            await Render(editor.Dialog, Path.Combine(artifacts, "package-editor-520.png"));
            Require(((FrameworkElement)editor.Dialog.Content).ActualWidth <= page.ActualWidth - 48, "Shared editor fits narrow window");
            editor.Dialog.Hide(); await shown;
            Require(!editor.Accepted, "Closing shared editor discards the staged selection");
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800)); await Task.Delay(150);
        }
        var versionPicker = new VersionPickerDialog("gogoloco", ["1.8.6", "1.9.0-beta.1"], "1.8.6");
        Require(versionPicker.Result == "1.8.6" && !versionPicker.Advanced.IsExpanded, "Version picker preserves pinned choice with advanced controls collapsed");
        versionPicker.Dialog.XamlRoot = page.XamlRoot;
        versionPicker.Dialog.PrimaryButtonStyle = (Style)page.Resources["ActionButton"];
        var versionShown = versionPicker.Dialog.ShowAsync(); await Task.Delay(180);
        await Render(versionPicker.Dialog, Path.Combine(artifacts, "version-picker.png")); versionPicker.Dialog.Hide(); await versionShown;
        versionPicker.Choice.SelectedIndex = 0;
        Require(versionPicker.Result == "latest", "Automatic UI choice retains shared latest policy");
        versionPicker.Exact.Text = "1.9.0-beta.1";
        Require(versionPicker.Result == "1.9.0-beta.1", "Advanced exact prerelease remains available");
        viewModel.PackageFile = inspectionFile;
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(720, 620), new Windows.Graphics.SizeInt32(900, 760), new Windows.Graphics.SizeInt32(1024, 760), new Windows.Graphics.SizeInt32(1180, 800), new Windows.Graphics.SizeInt32(1920, 1080) })
        {
            window.AppWindow.Resize(size);
            foreach (var screen in new[] { "Projects", "Workspace", "Create", "Presets", "Settings", "Activity" })
            {
                viewModel.CurrentPage = screen;
                if (screen == "Activity") viewModel.AppendActivity(new(DateTime.UtcNow, "Verified", "Native renderer / shared viewmodel verification"));
                await Task.Delay(350);
                page.UpdateLayout();
                Require(page.ActualWidth > 0 && page.ActualHeight > 0, "Layout measured: " + screen + " " + size.Width);
                Require(navigation.DisplayMode != Microsoft.UI.Xaml.Controls.NavigationViewDisplayMode.Minimal, "Sidebar remains available: " + size.Width);
                var actionLists = screen switch
                {
                    "Projects" => new[] { (Microsoft.UI.Xaml.Controls.ListViewBase)page.FindName("ProjectList") },
                    "Workspace" => new[] { (Microsoft.UI.Xaml.Controls.ListViewBase)page.FindName("WorkspacePackages") },
                    "Presets" => new[] { (Microsoft.UI.Xaml.Controls.ListViewBase)page.FindName("PresetPackageList") },
                    _ => []
                };
                foreach (var list in actionLists)
                {
                    var right = list.TransformToVisual(page).TransformPoint(new(list.ActualWidth - list.Padding.Right, 0)).X;
                    var buttons = Descendants(list).OfType<Microsoft.UI.Xaml.Controls.Button>().Where(x => x.Visibility == Visibility.Visible && x.ActualWidth > 0).ToArray();
                    Require(list.Padding.Right >= 24 && buttons.All(x => x.TransformToVisual(page).TransformPoint(new(x.ActualWidth, 0)).X <= right + 2), "Trailing actions stay inside the scrollbar gutter: " + screen + " " + size.Width);
                }
                if (screen == "Projects")
                {
                    var projects = (FrameworkElement)page.FindName("ProjectList");
                    var open = Descendants(projects).OfType<Microsoft.UI.Xaml.Controls.Button>().First(x => x.Content as string == "Open");
                    var origin = open.TransformToVisual(page).TransformPoint(new(0, 0));
                    Require(origin.X >= 0 && origin.X + open.ActualWidth <= page.ActualWidth + 1, "Direct project action fits client: " + size.Width);
                    Require(Descendants(projects).OfType<FrameworkElement>().Any(x => x.Name == "ProjectModified"), "Project last-modified field is materialized: " + size.Width);
                    if (size.Width == 1180)
                    {
                        var list = (Microsoft.UI.Xaml.Controls.ListView)page.FindName("ProjectList");
                        var batch = (Microsoft.UI.Xaml.Controls.Button)page.FindName("ManageSelectionAction");
                        Require(batch.Visibility == Visibility.Collapsed && Descendants(list).OfType<ProjectSelectionIndicator>().All(x => x.Visibility == Visibility.Collapsed), "Batch action and checkboxes stay out of the way before selection");
                        var second = viewModel.Projects[0] with { Name = "Second fixture [PC]", Path = Path.Combine(artifacts, "selection-only") };
                        viewModel.Projects.Add(second); list.SelectedItems.Add(viewModel.Projects[0]);
                        Require(Descendants(list).OfType<ProjectSelectionIndicator>().Any(x => x.Visibility == Visibility.Visible) && batch.Visibility == Visibility.Collapsed, "First selection reveals checkboxes without a one-project batch action");
                        list.SelectedItems.Add(second); await Task.Delay(250); page.UpdateLayout();
                        list.ScrollIntoView(second); await Task.Delay(100); page.UpdateLayout();
                        Require(list.ContainerFromItem(second) is FrameworkElement { ActualHeight: > 0 }, "Both selected projects have visible native row containers");
                        var firstRow = (FrameworkElement)list.ContainerFromItem(viewModel.Projects[0]);
                        var secondRow = (FrameworkElement)list.ContainerFromItem(second);
                        await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "project-selection-bounds.json"), new { list.ActualHeight, FirstY = firstRow.TransformToVisual(list).TransformPoint(new(0, 0)).Y, FirstHeight = firstRow.ActualHeight, SecondY = secondRow.TransformToVisual(list).TransformPoint(new(0, 0)).Y, SecondHeight = secondRow.ActualHeight });
                        Require(secondRow.TransformToVisual(list).TransformPoint(new(0, 0)).Y >= firstRow.TransformToVisual(list).TransformPoint(new(0, firstRow.ActualHeight)).Y - 1, "Selected project rows remain separate without overlap");
                        Require(batch.Visibility == Visibility.Visible && Microsoft.UI.Xaml.Controls.Grid.GetRow(batch) == 0, "Multi-project action lives in the toolbar and covers no list rows");
                        await Render(page, Path.Combine(artifacts, "projects-multiple-selection.png"));
                        firstRow.Focus(FocusState.Pointer);
                        list.SelectedItems.Clear(); await Task.Delay(240);
                        Require(Descendants(list).OfType<ProjectSelectionIndicator>().All(x => x.IsChecked != true && x.Visibility == Visibility.Collapsed), "Pointer focus does not keep a deselected project checkbox active");
                        var surface = Descendants(firstRow).OfType<ProjectRowSurface>().Single();
                        Require(!surface.SelectionHighlighted, "Project highlight clears with the real selection");
                        surface.SetHovered(true); await Task.Delay(240);
                        await Render(page, Path.Combine(artifacts, "projects-hover-feedback.png"));
                        surface.SetHovered(false); await Task.Delay(240);
                        await Render(page, Path.Combine(artifacts, "projects-deselected-feedback.png"));
                        Require(!surface.Hovered && !surface.SelectionHighlighted, "Project hover exit leaves no remembered pressed state");
                        viewModel.Projects.Remove(second);
                        await viewModel.CycleProjectSortAsync("name"); await viewModel.CycleProjectSortAsync("name"); await viewModel.CycleProjectSortAsync("name");
                        Require(viewModel.ProjectSort == "default", "Native column sort cycles back to no explicit sort");
                    }
                }
                if (screen == "Workspace" && size.Width == 1180)
                {
                    Require(page.FindName("CatalogView") is null && page.FindName("WorkspaceCatalog") is PackageBrowserView, "Project management embeds the catalog without a separate add screen");
                    Require(viewModel.Packages.Take(3).Select(x => x.Id).SequenceEqual(new[] { ProjectRules.Resolver, ProjectRules.BaseSdk, ProjectRules.AvatarSdk }), "Foundation packages are consecutive before the removable Avatar SDK");
                    var available = viewModel.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury");
                    viewModel.AddLibraryPackageCommand.Execute(available);
                    Require(viewModel.LibraryPackages.First(x => x.Id == available.Id).AddLabel == "Added", "Inline catalog marks the staged addition in place");
                    var scroll = (Microsoft.UI.Xaml.Controls.ScrollViewer)page.FindName("WorkspacePageScroll");
                    page.UpdateLayout(); await Task.Delay(150);
                    var addedControl = Descendants((DependencyObject)page.FindName("WorkspaceCatalog")).OfType<Microsoft.UI.Xaml.Controls.Button>().First(x => x.DataContext is VrcSetup_Desktop.ViewModels.UiPackageRow row && row.Id == available.Id && x.Content is Microsoft.UI.Xaml.Controls.StackPanel);
                    Require(Descendants(addedControl).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().Any(x => x.Text == UiText.Text("Added")), "Native catalog control renders Added after staging");
                    var addedTop = addedControl.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
                    scroll.ChangeView(null, Math.Clamp(scroll.VerticalOffset + addedTop - scroll.ViewportHeight * .5, 0, scroll.ScrollableHeight), null, true); await Task.Delay(250);
                    await Render(page, Path.Combine(artifacts, "workspace-staged-addition.png"));
                    var resetCount = 0;
                    System.Collections.Specialized.NotifyCollectionChangedEventHandler resets = (_, args) => { if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resetCount++; };
                    viewModel.Packages.CollectionChanged += resets; viewModel.LibraryPackages.CollectionChanged += resets;
                    var stableRow = viewModel.LibraryPackages.First(x => x.Id == available.Id);
                    var untouched = viewModel.Packages.First(x => x.Id == ProjectRules.Resolver);
                    var anchorBefore = addedControl.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
                    PackageScrollStability.Run(addedControl, () => viewModel.AddLibraryPackageCommand.Execute(stableRow));
                    await Task.Delay(250); page.UpdateLayout();
                    var anchorAfter = addedControl.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
                    Require(resetCount == 0 && ReferenceEquals(stableRow, viewModel.LibraryPackages.First(x => x.Id == available.Id)) && ReferenceEquals(untouched, viewModel.Packages.First(x => x.Id == ProjectRules.Resolver)), "Package toggle preserves collection and untouched row identity without Reset");
                    Require(Math.Abs(anchorAfter - anchorBefore) < 3 && scroll.VerticalOffset > 0, "Removing an addition retains the clicked card position and scrolled viewport");
                    await Render(page, Path.Combine(artifacts, "workspace-scroll-preserved.png"));
                    viewModel.Packages.CollectionChanged -= resets; viewModel.LibraryPackages.CollectionChanged -= resets;
                    scroll.ChangeView(null, 0, null, true);
                }
                if (screen == "Create")
                    Require(((FrameworkElement)page.FindName("ImportQueueSection")).Visibility == Visibility.Collapsed && viewModel.ImportsIdle, "Empty imports reserve no workspace: " + size.Width);
                if (screen is "Create" or "Settings")
                {
                    var form = (FrameworkElement)page.FindName(screen == "Create" ? "CreateForm" : "SettingsForm");
                    if (size.Width >= 1920)
                    {
                        var view = (FrameworkElement)page.FindName(screen == "Create" ? "CreateView" : "SettingsView");
                        Require(form.ActualWidth >= view.ActualWidth - 24, $"Fullscreen form uses available width: {screen}, form={form.ActualWidth}, available={view.ActualWidth}, requested={size.Width}, window={page.ActualWidth}");
                    }
                    var viewport = (FrameworkElement)page.FindName(screen == "Create" ? "CreateView" : "SettingsView");
                    var origin = form.TransformToVisual(viewport).TransformPoint(new(0, 0));
                    Require(origin.X >= -1 && origin.X + form.ActualWidth <= viewport.ActualWidth - 23, "Form leaves a scrollbar gutter: " + screen + " " + size.Width);
                    var global = form.TransformToVisual(page).TransformPoint(new(0, 0));
                    Require(global.X >= 0 && global.X + form.ActualWidth <= page.ActualWidth + 1, "Form fits entire client window: " + screen + " " + size.Width);
                    if (screen == "Create" && size.Width < 800)
                    {
                        var name = (FrameworkElement)((Microsoft.UI.Xaml.Controls.Grid)page.FindName("ProjectFields")).Children[0];
                        var label = (FrameworkElement)page.FindName("DropFileLabel");
                        Require(name.ActualWidth > 200 && label.ActualHeight < 40, "Narrow Create keeps usable fields and single-line filename: " + size.Width);
                    }
                }
                await Render(page, Path.Combine(artifacts, screen.ToLowerInvariant() + "-" + size.Width + ".png"));
                if (screen == "Settings" && size.Width is 1180 or 1920)
                {
                    navigation.IsPaneOpen = false; await Task.Delay(450); page.UpdateLayout();
                    var content = (FrameworkElement)page.FindName("PageContent");
                    var origin = content.TransformToVisual(page).TransformPoint(new(0, 0));
                    Require(Math.Abs(origin.X - navigation.CompactPaneLength) <= 2 && Math.Abs(origin.X + content.ActualWidth - page.ActualWidth) <= 2,
                        "Collapsing the sidebar fills the released width without side bands: " + size.Width);
                    await Render(page, Path.Combine(artifacts, "settings-sidebar-collapsed-" + size.Width + ".png"));
                    navigation.IsPaneOpen = true; await Task.Delay(250);
                }
                if (screen == "Create" && size.Width is 720 or 1180)
                {
                    var details = (Microsoft.UI.Xaml.Controls.Expander)page.FindName("CreationPackageDetails");
                    var collapsedWidth = details.ActualWidth;
                    details.IsExpanded = true; viewModel.IsInspectingPackage = true;
                    await Task.Delay(650); page.UpdateLayout();
                    Require(Math.Abs(details.ActualWidth - collapsedWidth) < 2, "Package disclosure keeps a stable width while expanding: " + size.Width);
                    var importAction = (FrameworkElement)page.FindName("ImportPrimaryAction");
                    Require(importAction.TransformToVisual(details).TransformPoint(new(0, 0)).Y >= details.ActualHeight,
                        "Import action stays below package details: " + size.Width);
                    var footer = (FrameworkElement)page.FindName("StatusBar");
                    var text = (Microsoft.UI.Xaml.Controls.TextBlock)page.FindName("ApplicationStatusText");
                    Require(footer.ActualHeight <= 40 && text.Text.Contains("Checking packages"), "Analysis uses the fixed application footer: " + size.Width);
                    var creationList = (Microsoft.UI.Xaml.Controls.GridView)page.FindName("CreationPackageList");
                    var recapItems = (Microsoft.UI.Xaml.Controls.ItemsWrapGrid)creationList.ItemsPanelRoot;
                    var firstRecapItem = (FrameworkElement)creationList.ContainerFromIndex(0);
                    Require(double.IsPositiveInfinity(creationList.MaxHeight) && Microsoft.UI.Xaml.Controls.ScrollViewer.GetVerticalScrollMode(creationList) == Microsoft.UI.Xaml.Controls.ScrollMode.Disabled && details.ActualWidth <= ((FrameworkElement)page.FindName("CreateForm")).ActualWidth + 1, "Expanded package recap shows its full list within the form without nested scrolling: " + size.Width);
                    Require(creationList.ActualWidth >= details.ActualWidth - 32 && recapItems.ItemWidth >= 220 && firstRecapItem.ActualWidth >= 220, $"Expanded package rows fill the disclosure width: {size.Width}, list={creationList.ActualWidth}, surface={details.ActualWidth}, item={recapItems.ItemWidth}, container={firstRecapItem.ActualWidth}");
                    Require(creationList.ActualHeight >= 28 && creationList.Items.Count == viewModel.CreationPackages.Count && creationList.Items.Cast<object>().All(x => creationList.ContainerFromItem(x) is FrameworkElement { ActualHeight: > 0 }), "Every included import package is rendered without clipping: " + size.Width);
                    var chip = Descendants(details).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>().First(x => x.Name == "ExpanderHeader");
                    Require(Math.Abs(chip.ActualWidth - details.ActualWidth) < 2, "Package disclosure opens as one continuous surface: " + size.Width);
                    Require(!Descendants(details).OfType<Microsoft.UI.Xaml.Controls.Button>().Any(x => x.Command == viewModel.InspectDeeperCommand), "Package recap has no technical analysis action: " + size.Width);
                    Require(!((Microsoft.UI.Xaml.Controls.TextBlock)page.FindName("CreationPackageHeader")).IsTextTrimmed, "Compact package summary remains fully readable: " + size.Width);
                    await Render(page, Path.Combine(artifacts, "create-expanded-status-" + size.Width + ".png"));
                    details.IsExpanded = false; viewModel.IsInspectingPackage = false; await Task.Delay(200);
                }
                if (screen == "Presets")
                {
                    var carousel = (FrameworkElement)page.FindName("PresetCarousel");
                    var pageScroll = (Microsoft.UI.Xaml.Controls.ScrollViewer)page.FindName("PresetPageScroll");
                    var includedList = (Microsoft.UI.Xaml.Controls.ListView)page.FindName("PresetPackageList");
                    Require(double.IsPositiveInfinity(includedList.MaxHeight) && Microsoft.UI.Xaml.Controls.ScrollViewer.GetVerticalScrollMode(includedList) == Microsoft.UI.Xaml.Controls.ScrollMode.Disabled, "Included packages have no height cap or nested scrolling: " + size.Width);
                    Require(includedList.ActualHeight >= includedList.Items.Count * 56 && pageScroll.ExtentHeight >= includedList.ActualHeight, "One page scroll contains all included packages and the catalog: " + size.Width);
                    pageScroll.ChangeView(null, pageScroll.ScrollableHeight, null, true); await Task.Delay(150);
                    Require(pageScroll.ScrollableHeight == 0 || pageScroll.VerticalOffset > 0, "Catalog is reachable with the shared page scroll: " + size.Width);
                    await Render(page, Path.Combine(artifacts, "presets-scrolled-" + size.Width + ".png"));
                    pageScroll.ChangeView(null, 0, null, true); await Task.Delay(100);
                    Require(carousel.ActualWidth > 0 && page.FindName("PackageTabs") is null, "Preset editor is one unified page without duplicate tabs: " + size.Width);
                    var editorBounds = (FrameworkElement)page.FindName("PresetEditor");
                    var editorRight = editorBounds.TransformToVisual(page).TransformPoint(new(editorBounds.ActualWidth, 0)).X;
                    Require(Descendants((DependencyObject)page.FindName("PresetPackageList")).OfType<PackageActionIcon>().All(x => x.TransformToVisual(page).TransformPoint(new(x.ActualWidth, 0)).X <= editorRight + 1), "Preset minus icons fit without right-edge clipping: " + size.Width);
                    foreach (var button in Descendants((DependencyObject)page.FindName("PresetPackageList")).OfType<Microsoft.UI.Xaml.Controls.Button>().Where(x => x.Content is PackageActionIcon))
                    {
                        var icon = (FrameworkElement)button.Content; var point = icon.TransformToVisual(button).TransformPoint(new(0, 0));
                        Require(point.X >= 0 && point.Y >= 0 && point.X + icon.ActualWidth <= button.ActualWidth && point.Y + icon.ActualHeight <= button.ActualHeight, "Preset icon fits inside its own button padding: " + size.Width);
                    }
                    if (size.Width is 900 or 1180 or 1920)
                    {
                        await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null); await Task.Delay(250); page.UpdateLayout();
                        await Render(page, Path.Combine(artifacts, "presets-list-" + size.Width + ".png"));
                        await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null); await Task.Delay(250); page.UpdateLayout();
                    }
                    if (size.Width == 1180)
                    {
                        var selectorBefore = ((FrameworkElement)page.FindName("PresetSelector")).TransformToVisual(page).TransformPoint(new(0, 0)).X;
                        page.SetPresetActions(true); await Task.Delay(260); page.UpdateLayout();
                        var strip = (FrameworkElement)page.FindName("PresetActionsClip");
                        Require(strip.ActualWidth == 128 && Math.Abs(((FrameworkElement)page.FindName("PresetSelector")).TransformToVisual(page).TransformPoint(new(0, 0)).X - selectorBefore) < 1, "Inline preset actions expand without shifting the preset title");
                        await Render(page, Path.Combine(artifacts, "preset-inline-actions.png"));
                        var shareButton = (Microsoft.UI.Xaml.Controls.Button)page.FindName("PresetShareButton");
                        shareButton.Flyout.ShowAt(shareButton); await Task.Delay(250);
                        var shareMenu = (Microsoft.UI.Xaml.Controls.MenuFlyout)shareButton.Flyout;
                        Require(shareMenu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Count(x => x.Command is not null) == 2, "Preset share menu exposes working Import and Export actions");
                        await Render(page, Path.Combine(artifacts, "preset-sharing.png")); shareMenu.Hide();
                        page.SetPresetActions(false); await Task.Delay(260);
                        Require(strip.Visibility == Visibility.Collapsed, "Inline preset actions collapse cleanly");
                        var wheel = (Microsoft.UI.Xaml.Controls.Button)page.FindName("PresetSelector"); wheel.Flyout.ShowAt(wheel); await Task.Delay(500);
                        var choices = (Microsoft.UI.Xaml.Controls.ListView)page.FindName("PresetChoices");
                        Require(choices.ActualHeight >= 36 && choices.Items.Count == viewModel.Presets.Count, "Preset wheel opens the complete choice list");
                        var presenter = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(choices) as FrameworkElement ?? choices;
                        await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "preset-wheel-bounds.json"), new { choices.ActualWidth, choices.ActualHeight, DesiredHeight = choices.DesiredSize.Height, ParentWidth = presenter.ActualWidth, ParentHeight = presenter.ActualHeight, Items = choices.Items.Select(x => { var row = (FrameworkElement)choices.ContainerFromItem(x); return new { Name = x.ToString(), row.ActualHeight, Top = row.TransformToVisual(choices).TransformPoint(new(0, 0)).Y }; }).ToArray() });
                        await Render(presenter, Path.Combine(artifacts, "preset-wheel-choices.png")); wheel.Flyout.Hide();
                    }
                    if (size.Width == 1180)
                    {
                        var remove = Descendants((DependencyObject)page.FindName("PresetPackageList")).OfType<Microsoft.UI.Xaml.Controls.Button>().First(x => x.DataContext is VrcSetup_Desktop.ViewModels.UiPackageRow { Required: true } && x.Content is PackageActionIcon);
                        Require(!remove.IsEnabled, "Direct preset minus protects required packages");
                        var optionalRemove = Descendants((DependencyObject)page.FindName("PresetPackageList")).OfType<Microsoft.UI.Xaml.Controls.Button>().First(x => x.IsEnabled && x.Content is PackageActionIcon);
                        await Render((FrameworkElement)optionalRemove.Content, Path.Combine(artifacts, "minus-icon.png"));
                        var selector = Descendants((DependencyObject)page.FindName("PresetPackageList")).OfType<PackageVersionSelector>().First();
                        var uiThread = Environment.CurrentManagedThreadId; var worker = uiThread; var calls = 0;
                        var release = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var cache = new AsyncVersionCache(async (_, _) => { Interlocked.Increment(ref calls); worker = Environment.CurrentManagedThreadId; return await release.Task; });
                        selector.VersionSource = cache.GetAsync; selector.CachedSource = _ => ["1.0.1"];
                        for (var i = 0; i < 8; i++) { selector.Flyout.ShowAt(selector); await Task.Delay(20); selector.Flyout.Hide(); await Task.Delay(20); }
                        await Task.Delay(250);
                        Require(calls == 1 && worker != uiThread, "Repeated version opens share one off-dispatcher lookup");
                        release.SetResult(["1.0.1", "2.0.0-beta.1"]); await cache.GetAsync(selector.PackageId);
                        selector.Flyout.ShowAt(selector); await Task.Delay(100);
                        var flyoutPanel = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.Flyout)selector.Flyout).Content;
                        var releases = flyoutPanel.Children.OfType<Microsoft.UI.Xaml.Controls.ListView>().Single();
                        Require(releases.Items.Count >= 2 && releases.Items.Cast<object>().All(x => !x.ToString()!.Contains("beta")), "Version dropdown hides betas and keeps Latest: " + string.Join("; ", releases.Items.Cast<object>()) + " / selected=" + selector.Value);
                        selector.PickVersion("1.0.1");
                        Require(viewModel.PresetDirty, "Version dropdown stages a preset pin without a dialog");
                        viewModel.DiscardPresetChangesCommand.Execute(null);
                    }
                    await Render(page, Path.Combine(artifacts, "unified-packages-" + size.Width + ".png"));
                    await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null); await Task.Delay(120);
                    await Render(page, Path.Combine(artifacts, "unified-packages-list-" + size.Width + ".png"));
                    await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null);
                }
            }
        }
        viewModel.CurrentPage = "Create";
        var jobList = (Microsoft.UI.Xaml.Controls.ListView)page.FindName("ImportQueueList");
        var jobSection = (FrameworkElement)page.FindName("ImportQueueSection");
        var fixtureJobs = new[] { "First avatar", "Second avatar" }.Select((name, index) => new VrcSetup_Desktop.ViewModels.UiImportRow(
            new ImportJob("visual-" + index, new OperationPlan("visual", DateTime.UtcNow, []),
                new CreationRequest("fixture.unitypackage", name, Path.Combine(artifacts, name), "", "StandaloneWindows64", [], []),
                "Running", "Packages", 35 + index * 20, "", "", DateTime.UtcNow.AddMinutes(-2)))).ToArray();
        var failedEmpty = fixtureJobs[0].Job with { State = "Failed", Source = fixtureJobs[0].Job.Source with { UnityPackagePath = "", EmptyProject = true } };
        Require(new VrcSetup_Desktop.ViewModels.UiImportRow(failedEmpty) is { RecoverVisibility: Visibility.Visible, RetryLabel: "Try again" }, "Failed empty project exposes Try again");
        var failedImport = fixtureJobs[0].Job with { Id = "failed-import", State = "Failed", Plan = fixtureJobs[0].Job.Plan with { CreatedUtc = DateTime.UtcNow.AddMinutes(-5) } };
        var interruptedImport = failedImport with { State = "Interrupted" };
        Require(new VrcSetup_Desktop.ViewModels.UiImportRow(failedImport) is { RetryLabel: "Try again", AttentionVisibility: Visibility.Visible }, "Failed imports have a retry action and a quiet warning marker");
        Require(new VrcSetup_Desktop.ViewModels.UiImportRow(interruptedImport) is { RetryLabel: "Continue import", AttentionVisibility: Visibility.Visible }, "Interrupted imports retain Continue with a quiet warning marker");
        Require(new VrcSetup_Desktop.ViewModels.UiHistoryEntry(failedImport).RepairLabel == "Try again", "Activity uses the same retry label for failures");
        var attempts = new[] { failedImport, fixtureJobs[0].Job, fixtureJobs[1].Job };
        Require(VrcSetup_Desktop.ViewModels.MainViewModel.VisibleImportJobs(attempts, false).Length == 2, "Retry replaces the older failed row for the same destination");
        var hiddenNewest = fixtureJobs[0].Job with { State = "Completed", HiddenFromImports = true };
        Require(VrcSetup_Desktop.ViewModels.MainViewModel.VisibleImportJobs([failedImport, hiddenNewest], false).Length == 0, "Clearing the latest attempt does not resurrect older failures");
        Require(VrcSetup_Desktop.ViewModels.MainViewModel.VisibleImportJobs([failedImport, hiddenNewest with { HiddenFromImports = false }], true).Length == 0, "Auto-hiding a completed retry does not resurrect older failures");
        Require(new VrcSetup_Desktop.ViewModels.UiHistoryEntry(failedEmpty).ImportActionsVisibility == Visibility.Visible, "Empty project recovery is available in Activity");
        Require(new VrcSetup_Desktop.ViewModels.UiHistoryEntry(failedEmpty with { State = "Running" }).Summary.Length > 0, "Creation history summary works before its report exists");
        Require(new VrcSetup_Desktop.ViewModels.UiHistoryEntry(failedEmpty with { State = "Completed" }).ImportActionsVisibility == Visibility.Collapsed, "Completed empty project has no archive recovery action");
        viewModel.CurrentPage = "Activity";
        var historyList = Descendants((DependencyObject)page.FindName("ActivityView")).OfType<Microsoft.UI.Xaml.Controls.ListView>().First(x => ReferenceEquals(x.ItemsSource, viewModel.ImportHistory));
        var emptyHistory = Descendants((DependencyObject)page.FindName("ActivityView")).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().First(x => x.Text == "No import history yet");
        emptyHistory.Visibility = Visibility.Collapsed;
        historyList.ItemsSource = fixtureJobs.Select(x => new VrcSetup_Desktop.ViewModels.UiHistoryEntry(x.Job)).ToArray();
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(720, 620), new Windows.Graphics.SizeInt32(1180, 800) })
        {
            window.AppWindow.Resize(size); await Task.Delay(450); page.UpdateLayout();
            await Render(page, Path.Combine(artifacts, "activity-fixture-" + size.Width + ".png"));
        }
        historyList.ItemsSource = viewModel.ImportHistory; viewModel.CurrentPage = "Create";
        emptyHistory.SetBinding(UIElement.VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(viewModel.ImportHistoryEmptyVisibility)) });
        jobList.ItemsSource = fixtureJobs; jobList.Visibility = Visibility.Visible; jobSection.Visibility = Visibility.Visible;
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(720, 620), new Windows.Graphics.SizeInt32(1180, 800) })
        {
            window.AppWindow.Resize(size); await Task.Delay(600); page.UpdateLayout();
            var view = (FrameworkElement)page.FindName("CreateView");
            Require(Microsoft.UI.Xaml.Controls.Grid.GetRow(jobSection) == 2, "Imports remain below the form at every window size: " + size.Width);
            Require(jobList.ActualHeight >= 100, "Active job rows are visible in the adaptive import area: " + size.Width);
            var form = (FrameworkElement)page.FindName("CreateForm");
            var formOrigin = form.TransformToVisual(view).TransformPoint(new(0, 0));
            var jobsOrigin = jobSection.TransformToVisual(view).TransformPoint(new(0, 0));
            Require(jobsOrigin.Y >= formOrigin.Y + form.ActualHeight, "Import jobs never overlap the package recap: " + size.Width);
            var row = (FrameworkElement)jobList.ContainerFromIndex(0);
            var cancel = Descendants(row).OfType<Microsoft.UI.Xaml.Controls.Button>().Single(x => x.Name == "CancelImportAction");
            Require(!fixtureJobs[0].DetailsOpen && cancel.Visibility == Visibility.Visible && cancel.IsEnabled && cancel.ActualWidth >= 80,
                "Cancel import is directly visible on a collapsed active row: " + size.Width);
            Require(cancel.TransformToVisual(jobList).TransformPoint(new(cancel.ActualWidth, 0)).X <= jobList.ActualWidth,
                "Cancel import remains inside the usable list width: " + size.Width);
            await Render(page, Path.Combine(artifacts, "create-jobs-fixture-" + size.Width + ".png"));
        }
        fixtureJobs[0].Update(fixtureJobs[0].Job with { State = "Cancelling" });
        await Task.Delay(100);
        var firstCancel = Descendants((DependencyObject)jobList.ContainerFromIndex(0)).OfType<Microsoft.UI.Xaml.Controls.Button>().Single(x => x.Name == "CancelImportAction");
        Require(firstCancel.Visibility == Visibility.Visible && !firstCancel.IsEnabled && Equals(firstCancel.Content, "Cancelling…"),
            "Cancellation request stays visible with immediate disabled feedback");
        fixtureJobs[0].Update(fixtureJobs[0].Job with { State = "Completed" });
        await Task.Delay(100);
        Require(firstCancel.Visibility == Visibility.Collapsed, "Finished imports do not show a cancel action");
        fixtureJobs[0].Update(fixtureJobs[0].Job with { State = "Failed" });
        await Task.Delay(100);
        var retryImport = Descendants((DependencyObject)jobList.ContainerFromIndex(0)).OfType<Microsoft.UI.Xaml.Controls.Button>().Single(x => x.Name == "RetryImportAction");
        Require(retryImport.Visibility == Visibility.Visible && Equals(retryImport.Content, "Try again") && !fixtureJobs[0].DetailsOpen, "Failed import exposes Try again without opening details");
        await Render(page, Path.Combine(artifacts, "create-failed-retry.png"));
        fixtureJobs[0].Update(fixtureJobs[0].Job with { State = "Running" });
        fixtureJobs[1].DetailsOpen = true;
        await Task.Delay(600);
        fixtureJobs[1].LogText = string.Join("\n", Enumerable.Range(1, 160).Select(x => "Import log line " + x));
        page.UpdateLayout(); await Task.Delay(180);
        Require(Microsoft.UI.Xaml.Controls.ScrollViewer.GetVerticalScrollMode(jobList) == Microsoft.UI.Xaml.Controls.ScrollMode.Disabled, "Import rows have no intermediate scrolling viewport");
        var importLog = Descendants(jobList).OfType<Microsoft.UI.Xaml.Controls.ScrollViewer>().First(x => x.Height == 260);
        Require(importLog.ScrollableHeight > 0, "Expanded import log has its own readable scroll range");
        importLog.ChangeView(null, 160, null, true); await Task.Delay(180);
        Require(importLog.VerticalOffset >= 150, "Import log scrolls without resetting to the beginning");
        await Render(page, Path.Combine(artifacts, "create-log-expanded.png"));
        fixtureJobs[1].DetailsOpen = false;
        jobList.SetBinding(Microsoft.UI.Xaml.Controls.ItemsControl.ItemsSourceProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(viewModel.ImportJobs)) });
        jobList.SetBinding(UIElement.VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(viewModel.ImportJobsVisibility)) });
        jobSection.SetBinding(UIElement.VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(viewModel.ImportJobsVisibility)) });
        viewModel.Theme = "Dark"; viewModel.CurrentPage = "Workspace"; await Task.Delay(180);
        await Render(page, Path.Combine(artifacts, "workspace-dark.png"));
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(520, 800), new Windows.Graphics.SizeInt32(1180, 800) })
        {
            window.AppWindow.Resize(size); viewModel.CurrentPage = "Projects"; await Task.Delay(150);
            var projectList = (Microsoft.UI.Xaml.Controls.ListView)page.FindName("ProjectList");
            projectList.SelectedItems.Add(viewModel.Projects[0]); await Task.Delay(100);
            Require(viewModel.SelectedProjects.Count == 1, "Project checkbox selection updates managed targets: " + size.Width);
            await Render(page, Path.Combine(artifacts, "projects-selected-" + size.Width + ".png")); projectList.SelectedItems.Clear();
            viewModel.CurrentPage = "Workspace"; viewModel.ApplyProject = "Fixture project · no operation running";
            viewModel.ApplyPosition = "Project 1 of 4"; viewModel.ApplyPhase = "Updating package";
            viewModel.ApplyPackage = "Avatar Limb Scaling"; viewModel.ApplyPackageVersion = "1.2.0 → 1.3.0";
            viewModel.CanCancelApply = true; viewModel.IsApplyingPackages = true; await Task.Delay(150);
            Require(!navigation.IsEnabled && viewModel.ApplyOverlayVisibility == Visibility.Visible, "Package apply dims and protects the underlying editor while workers remain independent: " + size.Width);
            Require(!((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).IsEnabled, "Updates are blocked during sensitive package operations: " + size.Width);
            await Render(page, Path.Combine(artifacts, "apply-overlay-fixture-" + size.Width + ".png"));
            viewModel.CancelApplyCommand.Execute(null);
            Require(!viewModel.CanCancelApply && viewModel.ApplyPhase.Contains("recovery"), "Cancel keeps operation state until worker recovery: " + size.Width);
            viewModel.IsApplyingPackages = false;
        }
        await AtomicFile.WriteJsonAsync(Path.Combine(viewModel.Application.Paths.Logs, "operation-fixture-partial.json"), partial);
        viewModel.CurrentPage = "Activity"; await viewModel.RefreshSavedHistoryAsync(); await Task.Delay(200);
        var historyEntry = viewModel.OperationHistory.Single(x => x.Id == partial.Id); historyEntry.Expanded = true;
        var activityTabs = (Microsoft.UI.Xaml.Controls.Pivot)page.FindName("ActivityTabs"); activityTabs.SelectedIndex = 1;
        await Task.Delay(550); page.UpdateLayout(); await Render(page, Path.Combine(artifacts, "activity-outcome-fixture.png"));
        viewModel.DeveloperMode = true; Require(activityTabs.Items.Count == 3, "Developer mode adds diagnostics tab");
        viewModel.DeveloperMode = false; Require(activityTabs.Items.Count == 2, "Disabling developer mode removes diagnostics header");
        viewModel.Theme = "Light"; viewModel.CurrentPage = "Settings"; await Task.Delay(150);
        await Render(page, Path.Combine(artifacts, "settings-light.png"));
        viewModel.CurrentPage = "Workspace"; await Task.Delay(150); await Render(page, Path.Combine(artifacts, "workspace-light.png"));
        viewModel.CurrentPage = "Presets"; await Task.Delay(350); await Render(page, Path.Combine(artifacts, "presets-light.png"));
        viewModel.Theme = theme;
        saved = await viewModel.Application.Configuration.LoadAsync(); var availableRoot = saved.UnityProjectsRoot;
        saved.UnityProjectsRoot = Path.Combine(artifacts, "offline-drive"); await viewModel.Application.Configuration.SaveAsync(saved);
        await viewModel.ReloadConfigurationAsync(); viewModel.ProjectFilter = "does not exist"; viewModel.ProjectFilter = "";
        await viewModel.RefreshProjectsCommand.ExecuteAsync(null);
        Require(viewModel.EmptyProjectsVisibility == Visibility.Visible && viewModel.EmptyProjectsMessage.Contains("unavailable"), "Unavailable project root has a recovery folder prompt");
        saved.UnityProjectsRoot = availableRoot; await viewModel.Application.Configuration.SaveAsync(saved); await viewModel.ReloadConfigurationAsync(); await viewModel.RefreshProjectsCommand.ExecuteAsync(null);
        viewModel.CurrentPage = "Settings"; var savedSuffix = viewModel.NamingSuffix; viewModel.NamingSuffix += " draft";
        await Task.Delay(200); page.UpdateLayout();
        Require(viewModel.SettingsChangesVisibility == Visibility.Visible, "Settings actions stay outside the scrolling form when a draft exists");
        await Render(page, Path.Combine(artifacts, "settings-draft.png")); viewModel.NamingSuffix = savedSuffix;
        var firstRunApp = new VrcSetupApplication(Path.Combine(artifacts, "first-run-home"), ["--no-update-check", "--no-catalog-refresh", "--no-editor-discovery"]);
        await firstRunApp.Configuration.SaveAsync(new AppConfiguration { UnityEditorPath = "", UnityProjectsRoot = "", Language = "en" });
        var firstRunWindow = new MainWindow(firstRunApp); firstRunWindow.AppWindow.Show(false);
        await firstRunWindow.Page.Initialized; firstRunWindow.AppWindow.Resize(new(720, 620)); await Task.Delay(250);
        var firstRun = firstRunWindow.Page.ViewModel;
        Require(firstRun.CurrentPage == "Settings" && firstRun.SetupVisibility == Visibility.Visible && firstRun.SettingsOptionsVisibility == Visibility.Collapsed, "First launch shows only Unity and project folder setup");
        await firstRun.SaveSettingsCommand.ExecuteAsync(null);
        Require(firstRun.CurrentPage == "Settings" && firstRun.ErrorMessage.Length > 0 && (await firstRunApp.Configuration.LoadAsync()).UnityProjectsRoot == "", "Incomplete setup gives a useful prompt without saving invalid settings");
        await Render(firstRunWindow.Page, Path.Combine(artifacts, "first-run-720.png"));
        firstRunWindow.Close();
        await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "ui-smoke.json"), new { Passed = true, Checks = checks, page.ActualWidth, page.ActualHeight, Scale = page.XamlRoot.RasterizationScale, InputInjected = false });
        void Require(bool condition, string message) { if (!condition) throw new IOException(message); checks.Add(message); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class DelayedInspectionReader : IUnityPackageInspector
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ArchiveObservation> ReadAsync(string path, InspectionOptions options, CancellationToken token)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(token);
            var info = new FileInfo(path); return new(1, info.Length, info.LastWriteTimeUtc.Ticks, true, true, [], [], 0);
        }
    }
    public static async Task Render(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync(); CryptographicBuffer.CopyToByteArray(pixels, out var bytes);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new IOException("Native WinUI rendered an empty frame.");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}
