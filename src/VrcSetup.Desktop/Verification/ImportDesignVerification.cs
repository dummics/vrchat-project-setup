using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VrcSetup_Desktop.Verification;

internal static class ImportDesignVerification
{
    internal static async Task RunAsync(MainWindow window, string artifacts, string sourceFixture,
        Action<bool, string> require, Func<FrameworkElement, string, Task> render)
    {
        var page = window.Page;
        var model = page.ViewModel;
        var originalFile = model.PackageFile;
        var originalName = model.ProjectName;
        var originalPlatform = model.BuildTarget;
        var originalPage = model.CurrentPage;
        // This runs against the smoke-test application's isolated home and tiny archive.
        model.PackageFile = "";
        var folder = Path.Combine(artifacts, "d1-files");
        Directory.CreateDirectory(folder);
        var paths = Enumerable.Range(1, 6).Select(i => Path.Combine(folder, $"Avatar {i}.unitypackage")).ToArray();
        foreach (var path in paths) File.Copy(sourceFixture, path, true);
        model.StageImportFiles(paths);
        model.CurrentPage = "Create";
        window.AppWindow.Resize(new(1180, 800));
        await Task.Delay(400);
        var tabs = (TabView)page.FindName("ImportDraftTabs");
        require(tabs.TabItems.Count == 6 && tabs.SelectedItem == model.SelectedImportDraft,
            "D1 exposes all six source files as native document tabs");
        var first = model.ImportDrafts[0];
        tabs.SelectedItem = first;
        await Task.Delay(180);
        ((TextBox)page.FindName("ImportProjectName")).Text = "My renamed avatar";
        model.BuildTarget = "Android";
        await Task.Delay(100);
        var optional = model.SelectedCreationPackages.First(x => !x.IsRequired);
        var version = optional.Data.Version;
        model.ExcludeCreationPackage(optional);
        tabs.SelectedItem = model.ImportDrafts[1];
        await Task.Delay(100);
        require(model.ProjectName != "My renamed avatar" && !model.ExcludedCreationPackages.Any(x => x.Id == optional.Id),
            "D1 switching tabs isolates the project name and explicit VPM exclusions");
        tabs.SelectedItem = first;
        await Task.Delay(200);
        require(model.ProjectName == "My renamed avatar" && model.BuildTarget == "Android" && first.Name == model.ProjectName,
            "D1 returning to a tab preserves its edited name and platform");
        require(model.ExcludedCreationPackages.Single(x => x.Id == optional.Id).Data.Version == version,
            "D1 returning to a tab preserves its excluded package version");
        require(model.ImportDraftSummary.Contains('6') && model.CreationSelectionSummary.Contains(model.SelectedCreationPackages.Count.ToString()),
            "D1 shows the whole batch count and the current file's package selection");
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(1180, 800), new(900, 620) })
        {
            window.AppWindow.Resize(size);
            await Task.Delay(250);
            page.UpdateLayout();
            var view = (FrameworkElement)page.FindName("CreateView");
            var origin = tabs.TransformToVisual(view).TransformPoint(new(0, 0));
            require(origin.X >= 0 && origin.X + tabs.ActualWidth <= view.ActualWidth,
                "Six document tabs stay within the page at " + size.Width);
            var name = (FrameworkElement)page.FindName("ImportProjectName");
            var platform = (FrameworkElement)page.FindName("PlatformSelector");
            var nameTop = name.TransformToVisual(page).TransformPoint(new(0, 0));
            var platformTop = platform.TransformToVisual(page).TransformPoint(new(0, 0));
            require(platformTop.X >= nameTop.X + name.ActualWidth - 1 || platformTop.Y >= nameTop.Y + name.ActualHeight,
                "D1 platform selection never overlaps the editable name at " + size.Width);
            await render(page, Path.Combine(artifacts, $"d1-six-files-{size.Width}.png"));
        }
        model.RemoveImportDraft(first);
        await Task.Delay(100);
        require(model.ImportDrafts.Count == 5 && model.SelectedImportDraft is not null && model.SelectedImportDraft != first,
            "Closing a source tab selects an available file without creating any project");
        model.PackageFile = originalFile;
        model.ProjectName = originalName;
        model.BuildTarget = originalPlatform;
        model.CurrentPage = originalPage;
    }
}
