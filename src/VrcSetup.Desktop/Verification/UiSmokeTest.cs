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
        var application = new VrcSetupApplication(Path.Combine(artifacts, "home"));
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
        return application;
    }
    public static async Task RunAsync(MainWindow window, string artifacts)
    {
        var page = window.Page; await page.Initialized;
        var viewModel = page.ViewModel; var checks = new List<string>();
        Require(viewModel.Projects.Count == 1, "Native project library loaded");
        viewModel.ProjectFilter = "Quest"; Require(viewModel.Projects.Count == 0, "Project filter empty state");
        viewModel.ProjectFilter = "PC"; Require(viewModel.Projects.Count == 1, "Project filter recovers matches");
        viewModel.SelectedProjects.Add(viewModel.Projects[0]); await viewModel.OpenSelectedProjectsCommand.ExecuteAsync(null);
        Require(viewModel.Packages.Any(x => x.Id == "gogoloco"), "Native package workspace uses shared metadata");
        viewModel.SelectedPackage = viewModel.Packages.First(x => x.Id == "gogoloco");
        await viewModel.TogglePackageCommand.ExecuteAsync(null);
        Require(viewModel.Packages.First(x => x.Id == "gogoloco").Action == "Will be removed", "GUI command stages optional removal");
        var theme = viewModel.Theme;
        using (var editor = new PackageSetDialog("Creator packages", new() { ["custom.required"] = "1.0.0" }, new() { RequiredPackages = ["custom.required"] }, [],
            (_, _) => Task.FromResult(Array.Empty<PackageDefinition>()), (_, _) => Task.FromResult(Array.Empty<string>())))
        {
            editor.Dialog.XamlRoot = page.XamlRoot;
            var shown = editor.Dialog.ShowAsync(); await Task.Delay(180);
            await Render(editor.Dialog, Path.Combine(artifacts, "package-editor.png"));
            Require(editor.Result().ContainsKey("custom.required"), "Native preset working copy preserves custom foundation");
            editor.Dialog.Hide(); await shown;
        }
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(1180, 800), new Windows.Graphics.SizeInt32(1366, 768) })
        {
            window.AppWindow.Resize(size);
            foreach (var screen in new[] { "Projects", "Workspace", "Create", "Presets", "Settings", "Activity" })
            {
                viewModel.CurrentPage = screen;
                if (screen == "Activity") viewModel.AppendActivity(new(DateTime.UtcNow, "Verified", "Native renderer / shared viewmodel verification"));
                await Task.Delay(180);
                page.UpdateLayout();
                Require(page.ActualWidth > 0 && page.ActualHeight > 0, "Layout measured: " + screen + " " + size.Width);
                await Render(page, Path.Combine(artifacts, screen.ToLowerInvariant() + "-" + size.Width + ".png"));
            }
        }
        viewModel.Theme = "Dark"; viewModel.CurrentPage = "Workspace"; await Task.Delay(180);
        await Render(page, Path.Combine(artifacts, "workspace-dark.png")); viewModel.Theme = theme;
        await AtomicFile.WriteJsonAsync(Path.Combine(artifacts, "ui-smoke.json"), new { Passed = true, Checks = checks, page.ActualWidth, page.ActualHeight, Scale = page.XamlRoot.RasterizationScale, InputInjected = false });
        void Require(bool condition, string message) { if (!condition) throw new IOException(message); checks.Add(message); }
    }
    private static async Task Render(FrameworkElement element, string path)
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
