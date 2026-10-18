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
    public static async Task<VrcSetupApplication> PrepareAsync(string artifacts)
    {
        artifacts = ProjectRules.NormalizePath(artifacts); Directory.CreateDirectory(artifacts);
        var application = new VrcSetupApplication(Path.Combine(artifacts, "home"), ["--no-catalog-refresh", "--no-update-check", "--no-editor-discovery"]);
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
        config.PackagePresets["Creator PC"] = packages; await application.Configuration.SaveAsync(config);
        config.CreationRecipes["PC template"] = new() { Preset = "creator pc", Packages = packages }; await application.Configuration.SaveAsync(config);
        await application.Catalog.RememberAsync([new("com.llealloo.audiolink", "AudioLink", "latest"), new("nadena.dev.modular-avatar", "Modular Avatar", "latest"),
            new("com.nanochip.avatarlimbscaling", "Avatar Limb Scaling", "latest"), new("vrchat.blackstartx.gesture-manager", "Gesture Manager", "latest"),
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
            Require(viewModel.EditorReady && viewModel.UnityEditorPath == installedEditor && (await viewModel.Application.Configuration.LoadAsync()).UnityEditorPath == installedEditor, "Installed pinned editor is discovered and saved immediately");
            await viewModel.ReloadConfigurationAsync();
            Require(viewModel.EditorReady && viewModel.UnityEditorPath == installedEditor, "Reload preserves the discovered editor");
        }
        await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null);
        Require(viewModel.LibraryIsList && (await viewModel.Application.Configuration.LoadAsync()).PackageLibraryView == "list", "List view is available and persisted");
        await viewModel.ToggleLibraryLayoutCommand.ExecuteAsync(null);
        Require(((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Content?.ToString() == viewModel.AppVersion, "Application version is visible in the persistent version menu");
        Require(((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Command is not null && ((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).Flyout is null, "Footer update is a direct action without a menu");
        viewModel.UpdateFailed = true; viewModel.UpdateStatus = new string('x', 400);
        Require(viewModel.UpdateSummary == "Update failed", "Technical update errors stay compact in the footer");
        viewModel.UpdateFailed = false; viewModel.UpdateStatus = "";
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
        await Task.Delay(200);
        var tooltipOwner = Descendants(navigation).OfType<FrameworkElement>().FirstOrDefault(x => x.IsLoaded && x.ActualWidth > 0 && x.Visibility == Visibility.Visible && x is not Microsoft.UI.Xaml.Controls.Control { IsEnabled: false } && page.ToolTips.HasContent(x));
        Require(tooltipOwner is not null, "Built-in compact navigation tooltips are adopted by the app hover delay");
        page.ToolTips.Observe(tooltipOwner, new(1, 1)); await Task.Delay(1000);
        Require(!page.ToolTips.IsOpen, "Navigation tooltip stays hidden during a brief hover");
        await Task.Delay(1700); Require(page.ToolTips.IsOpen, "Navigation tooltip opens only after a 2.5-second hold");
        page.ToolTips.Dismiss(); Require(!page.ToolTips.IsOpen, "Navigation tooltip closes immediately when hover ends");
        page.ToolTips.Observe(tooltipOwner, new(1, 1)); await Task.Delay(400);
        Require(!page.ToolTips.IsOpen, "A second hover still waits instead of using native instant reshow"); page.ToolTips.Dismiss();
        window.AppWindow.Resize(new(1180, 800)); await Task.Delay(250);
        var freshProject = new NewProjectDialog(viewModel, (Microsoft.UI.Xaml.DataTemplate)page.Resources["PresetChoice"]);
        freshProject.Name.Text = "Fresh project"; freshProject.Dialog.XamlRoot = page.XamlRoot;
        var newProjectShown = freshProject.Dialog.ShowAsync(); await Task.Delay(350);
        Require(freshProject.Preset.SelectedItem?.ToString() == viewModel.EditingPreset && freshProject.Platform.SelectedValue?.ToString() == "StandaloneWindows64", "Empty project dialog selects preset and platform without a UnityPackage field");
        await Render(freshProject.Dialog, Path.Combine(artifacts, "new-project.png")); freshProject.Dialog.Hide(); await newProjectShown;
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
        viewModel.AddLibraryPackageCommand.Execute(viewModel.LibraryPackages.First(x => x.Id == "gogoloco"));
        Require(viewModel.PresetDirty && viewModel.PresetPackages.Any(x => x.Id == "gogoloco"), "Saved library package is added without a repository search");
        Require(!viewModel.LibraryPackages.Any(x => x.Id == "gogoloco"), "Adding a package moves it into the preset without a duplicate catalogue card");
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
        var decisions = new FlowInteraction(page);
        using (var flow = new VrcSetup_Desktop.ViewModels.MainViewModel(viewModel.Application, decisions))
        {
            await flow.InitializeAsync(); await flow.OpenProjectPackagesCommand.ExecuteAsync(flow.Projects[0]);
            flow.StageWorkspaceVersion("gogoloco", "1.8.7");
            Require(!await flow.NavigateAsync("Settings") && flow.CurrentPage == "Workspace" && flow.WorkspaceDirty, "Rejecting navigation keeps the active project draft");
            decisions.ConfirmResult = true;
            Require(await flow.NavigateAsync("Projects") && flow.WorkspaceDirty, "Confirmed navigation preserves the staged project draft");
            await flow.OpenProjectPackagesCommand.ExecuteAsync(flow.Projects[0]);
            Require(flow.WorkspaceDirty && flow.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "1.8.7", "Reopening the same project retains pending versions");
            await flow.UndoChangesCommand.ExecuteAsync(null); flow.OpenProjectCatalogCommand.Execute(null);
            Require(flow.CurrentPage == "Catalog" && flow.BrowsingProjectPackages, "Add package opens the shared in-page library");
            flow.AddLibraryPackageCommand.Execute(flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury"));
            Require(flow.WorkspaceDirty && flow.LibraryPackages.First(x => x.Id == "com.vrcfury.vrcfury").Included, "Local package addition is immediate and staged for the project");
            flow.BackToProjectCommand.Execute(null);
            Require(flow.CurrentPage == "Workspace" && flow.WorkspaceDirty, "Returning from the library keeps the same pending batch");
            await flow.UndoChangesCommand.ExecuteAsync(null);
            Require(!flow.WorkspaceDirty && !flow.Packages.Any(x => x.Id == "com.vrcfury.vrcfury"), "Revert restores the installed project selection");
            flow.ToggleWorkspacePackage(flow.Packages.First(x => x.Id == "gogoloco")); flow.ToggleWorkspacePackage(flow.Packages.First(x => x.Id == "gogoloco"));
            Require(!flow.WorkspaceDirty, "Undoing a removal restores the original version policy without a new change");
            flow.LibraryFilter = "vrc fury";
            Require(flow.LibraryPackages.Concat(flow.PresetPackages).Any(x => x.Id == "com.vrcfury.vrcfury"), "Unified package search finds multi-word matches in the correct section"); flow.LibraryFilter = "";
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
        var collectionChanges = 0;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler onCollection = (_, _) => collectionChanges++;
        viewModel.LibraryPackages.CollectionChanged += onCollection;
        await viewModel.TogglePackageFavoriteCommand.ExecuteAsync(favoriteRow);
        Require(favoriteRow.Favorite != originalFavorite && collectionChanges == 0 && librarySnapshot.SequenceEqual(viewModel.LibraryPackages), "Favorite updates in place without resetting or reordering cards");
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
            foreach (var screen in new[] { "Projects", "Workspace", "Catalog", "Create", "Presets", "Settings", "Activity" })
            {
                if (screen == "Catalog") viewModel.OpenProjectCatalogCommand.Execute(null);
                else { if (viewModel.BrowsingProjectPackages) viewModel.BackToProjectCommand.Execute(null); viewModel.CurrentPage = screen; }
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
                    details.IsExpanded = true; viewModel.IsInspectingPackage = true;
                    await Task.Delay(250); page.UpdateLayout();
                    var footer = (FrameworkElement)page.FindName("StatusBar");
                    var text = (Microsoft.UI.Xaml.Controls.TextBlock)page.FindName("ApplicationStatusText");
                    Require(footer.ActualHeight <= 40 && text.Text.Contains("Checking packages"), "Analysis uses the fixed application footer: " + size.Width);
                    Require(((FrameworkElement)page.FindName("CreationPackageList")).ActualHeight <= 128 && details.ActualWidth <= 560, "Expanded package recap remains bounded: " + size.Width);
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
            await Render(page, Path.Combine(artifacts, "create-jobs-fixture-" + size.Width + ".png"));
        }
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
            viewModel.ApplyPosition = "Project 1 of 4"; viewModel.ApplyPhase = "Updating packages";
            viewModel.CanCancelApply = true; viewModel.IsApplyingPackages = true; await Task.Delay(150);
            Require(!navigation.IsEnabled && viewModel.ApplyOverlayVisibility == Visibility.Visible, "Apply state blocks navigation: " + size.Width);
            Require(!((Microsoft.UI.Xaml.Controls.Button)page.FindName("ApplicationVersionButton")).IsEnabled, "Updates are blocked during sensitive package operations: " + size.Width);
            await Render(page, Path.Combine(artifacts, "apply-overlay-fixture-" + size.Width + ".png"));
            viewModel.CancelApplyCommand.Execute(null);
            Require(!viewModel.CanCancelApply && viewModel.ApplyPhase.Contains("recovery"), "Cancel keeps blocking state until worker recovery: " + size.Width);
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
