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
        var application = new VrcSetupApplication(Path.Combine(artifacts, "home"), ["--no-catalog-refresh"]);
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
        config.PackagePresets["Creator PC"] = packages; await application.Configuration.SaveAsync(config);
        config.CreationRecipes["PC template"] = new() { Preset = "creator pc", Packages = packages }; await application.Configuration.SaveAsync(config);
        return application;
    }
    public static async Task RunAsync(MainWindow window, string artifacts)
    {
        var page = window.Page; await page.Initialized;
        var viewModel = page.ViewModel; var checks = new List<string>();
        Require(viewModel.ErrorMessage.Length == 0, "Startup has no hidden errors");
        Require(viewModel.PresetLabel == "Default", "Default preset is the initial editing context");
        Require(((Microsoft.UI.Xaml.Controls.ComboBox)page.FindName("PresetSelector")).SelectedItem?.ToString() == "My default packages", "Preset selector has a visible selection after binding");
        Require(viewModel.WorkVisibility == Visibility.Collapsed, "Idle footer actions are hidden");
        Require(viewModel.PresetChangesVisibility == Visibility.Collapsed, "Preset save actions are contextual");
        var retries = 0;
        var retryCache = new AsyncVersionCache((_, _) => Task.FromResult(Interlocked.Increment(ref retries) == 1 ? Array.Empty<string>() : new[] { "1.0.0" }));
        Require((await retryCache.GetAsync("retry.package")).Length == 0 && (await retryCache.GetAsync("retry.package")).Length == 1, "Empty version responses can retry after recovery");
        var navigation = (Microsoft.UI.Xaml.Controls.NavigationView)page.FindName("Navigation");
        Require(navigation.FooterMenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().Any(x => x.Tag?.ToString() == "Activity"), "Activity is in the navigation footer");
        viewModel.SelectedRecipe = null!; viewModel.SelectedRecipe = "No recipe";
        Require(viewModel.ErrorMessage.Length == 0, "Transient cleared template selection is safe");
        viewModel.EditingPreset = "Creator PC";
        Require(viewModel.LibraryPackages.All(x => x.Included == viewModel.PresetPackages.Any(p => p.Id == x.Id)), "Library cards reflect the selected preset");
        var original = await viewModel.Application.Projects.ReadAsync(viewModel.Projects[0].Path);
        var optional = viewModel.PresetPackages.First(x => x.Id == "gogoloco");
        viewModel.RemovePresetPackageCommand.Execute(optional);
        Require(viewModel.PresetDirty && !viewModel.PresetPackages.Any(x => x.Id == "gogoloco"), "Preset edits are staged in the selected preset");
        await viewModel.SaveEditedPresetCommand.ExecuteAsync(null);
        var saved = await viewModel.Application.Configuration.LoadAsync();
        Require(!saved.PackagePresets["Creator PC"].ContainsKey("gogoloco") && saved.VpmPackages.ContainsKey("gogoloco"), "Saving named preset preserves Default");
        Require((await viewModel.Application.Projects.ReadAsync(original.Path)).Fingerprint == original.Fingerprint, "Preset edits do not mutate project files");
        var favorite = saved.FavoritePackages.Contains("gogoloco");
        await viewModel.TogglePackageFavoriteCommand.ExecuteAsync(viewModel.LibraryPackages.First(x => x.Id == "gogoloco"));
        Require((await viewModel.Application.Configuration.LoadAsync()).FavoritePackages.Contains("gogoloco") != favorite, "Package star persists the favorite choice");
        viewModel.AddLibraryPackageCommand.Execute(viewModel.LibraryPackages.First(x => x.Id == "gogoloco"));
        Require(viewModel.PresetDirty && viewModel.PresetPackages.Any(x => x.Id == "gogoloco"), "Saved library package is added without a repository search");
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
            Require(await flow.NavigateAsync("Projects") && !flow.WorkspaceDirty, "Confirmed navigation discards only staged project edits");
            await flow.OpenProjectPackagesCommand.ExecuteAsync(flow.Projects[0]); flow.OpenProjectCatalogCommand.Execute(null);
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
            Require(flow.LibraryPackages.Any(x => x.Id == "com.vrcfury.vrcfury"), "Local library search accepts multiple query words"); flow.LibraryFilter = "";
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
        using (var suggested = new PackageSetDialog("Packages for this project", new() { ["com.poiyomi.toon"] = "8.0.0" }, new(), viewModel.Registry.ToArray(),
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
        using (var editor = new PackageSetDialog("Creator packages", new() { ["custom.required"] = "1.0.0" }, new() { RequiredPackages = ["custom.required"] }, viewModel.Registry.ToArray(),
            (_, _) => Task.FromResult(Array.Empty<PackageDefinition>()), (_, _) => Task.FromResult(Array.Empty<string>())))
        {
            editor.Dialog.XamlRoot = page.XamlRoot;
            var shown = editor.Dialog.ShowAsync(); await Task.Delay(180);
            editor.Dialog.UpdateLayout();
            var inlineVersion = Descendants(editor.Dialog).OfType<PackageVersionSelector>().First(x => x.PackageId == "custom.required");
            Require(inlineVersion.VersionSource is not null, "Shared package editor wires inline version selector");
            inlineVersion.PickVersion("1.2.3"); await Task.Delay(100);
            Require(editor.Result()["custom.required"] == "1.2.3", "Shared editor stages version directly from its row");
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
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(520, 800), new Windows.Graphics.SizeInt32(760, 800), new Windows.Graphics.SizeInt32(1024, 760), new Windows.Graphics.SizeInt32(1180, 800), new Windows.Graphics.SizeInt32(1366, 768) })
        {
            window.AppWindow.Resize(size);
            foreach (var screen in new[] { "Projects", "Workspace", "Catalog", "Create", "Presets", "Settings", "Activity" })
            {
                if (screen == "Catalog") viewModel.OpenProjectCatalogCommand.Execute(null);
                else { if (viewModel.BrowsingProjectPackages) viewModel.BackToProjectCommand.Execute(null); viewModel.CurrentPage = screen; }
                if (screen == "Activity") viewModel.AppendActivity(new(DateTime.UtcNow, "Verified", "Native renderer / shared viewmodel verification"));
                await Task.Delay(180);
                page.UpdateLayout();
                Require(page.ActualWidth > 0 && page.ActualHeight > 0, "Layout measured: " + screen + " " + size.Width);
                if (screen is "Create" or "Settings")
                {
                    var form = (FrameworkElement)page.FindName(screen == "Create" ? "CreateForm" : "SettingsForm");
                    var viewport = (FrameworkElement)page.FindName(screen == "Create" ? "CreateView" : "SettingsView");
                    var origin = form.TransformToVisual(viewport).TransformPoint(new(0, 0));
                    Require(origin.X >= -1 && origin.X + form.ActualWidth <= viewport.ActualWidth + 1, "Form fits viewport: " + screen + " " + size.Width);
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
                if (screen == "Presets")
                {
                    if (size.Width == 1180)
                    {
                        var options = Descendants(page).OfType<Microsoft.UI.Xaml.Controls.Button>().First(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(x) == "Package options");
                        var menu = (Microsoft.UI.Xaml.Controls.MenuFlyout)options.Flyout; menu.ShowAt(options); await Task.Delay(120);
                        Require(menu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Last().DataContext is VrcSetup_Desktop.ViewModels.UiPackageRow { Required: true } && !menu.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Last().IsEnabled, "Package menu inherits row context and protects required packages");
                        await Render(page, Path.Combine(artifacts, "preset-menu.png")); menu.Hide();
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
                    var tabs = (Microsoft.UI.Xaml.Controls.Pivot)page.FindName("PackageTabs"); tabs.SelectedIndex = 1; await Task.Delay(180);
                    await Render(page, Path.Combine(artifacts, "my-packages-" + size.Width + ".png")); tabs.SelectedIndex = 0;
                }
            }
        }
        viewModel.Theme = "Dark"; viewModel.CurrentPage = "Workspace"; await Task.Delay(180);
        await Render(page, Path.Combine(artifacts, "workspace-dark.png")); viewModel.Theme = theme;
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
