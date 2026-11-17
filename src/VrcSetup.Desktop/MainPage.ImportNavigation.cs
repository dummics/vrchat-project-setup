using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using VrcSetup.Core;
using Windows.System;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private enum ImportDetail { None, Packages, Preset }

    private ImportDetail importDetail;
    private bool importEditorOpen;

    private void UpdateImportDetailVisibility(string page)
    {
        var inImport = page == "Create";
        CreateHost.Visibility = inImport && importDetail == ImportDetail.None ? Visibility.Visible : Visibility.Collapsed;
        ImportDetailView.Visibility = inImport && importDetail != ImportDetail.None ? Visibility.Visible : Visibility.Collapsed;
        ImportPackagePanel.Visibility = importDetail == ImportDetail.Packages ? Visibility.Visible : Visibility.Collapsed;
        ImportPresetPanel.Visibility = importDetail == ImportDetail.Preset ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenImportPackagesClicked(object sender, RoutedEventArgs args)
    {
        OpenImportDetails(ImportDetail.Packages, "Packages");
        CreationPackageViewport.ChangeView(null, 0, null, true);
    }

    private void OpenImportPresetClicked(object sender, RoutedEventArgs args)
    {
        ImportPresetChoices.SelectedItem = ViewModel.SelectedPreset;
        OpenImportDetails(ImportDetail.Preset, "Choose a preset");
    }

    private void OpenImportDetails(ImportDetail detail, string title)
    {
        if (ViewModel.CurrentPage != "Create" || string.IsNullOrWhiteSpace(ViewModel.PackageFile)) return;
        importDetail = detail;
        ImportDetailTitle.Text = UiText.Text(title);
        UpdateImportDetailVisibility(ViewModel.CurrentPage);
        SlideImportSurface(ImportDetailView, forward: true);
        FocusImportControl(ImportDetailBack);
    }

    private void CloseImportDetails()
    {
        if (importDetail == ImportDetail.None) return;
        var origin = importDetail == ImportDetail.Preset ? PresetFields : CreationPackagesButton;
        importDetail = ImportDetail.None;
        UpdateImportDetailVisibility(ViewModel.CurrentPage);
        if (ViewModel.CurrentPage != "Create") return;
        SlideImportSurface(CreateHost, forward: false);
        FocusImportControl(origin);
    }

    private void ApplyImportPresetClicked(object sender, RoutedEventArgs args)
    {
        if (ImportPresetChoices.SelectedItem is string preset)
        {
            ViewModel.SelectedPreset = preset;
            CloseImportDetails();
        }
    }

    private void BackFromImportDetailsClicked(object sender, RoutedEventArgs args) => CloseImportDetails();

    private void ImportDetailKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        CloseImportDetails();
    }

    private async void CustomizeImportPackagesClicked(object sender, RoutedEventArgs args)
        => await ViewModel.CustomizeCreationPackagesCommand.ExecuteAsync(null);

    private async void BackFromImportEditorClicked(object sender, RoutedEventArgs args)
        => await ViewModel.NavigateAsync("Create");

    private async void ImportEditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (ViewModel.CurrentPage != "ImportPackages" || args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        await ViewModel.NavigateAsync("Create");
    }

    private void FocusImportControl(Control control)
        => DispatcherQueue.TryEnqueue(() => control.Focus(FocusState.Programmatic));

    private static void SlideImportSurface(FrameworkElement surface, bool forward)
    {
        const string property = "Translation";
        var visual = ElementCompositionPreview.GetElementVisual(surface);
        ElementCompositionPreview.SetIsTranslationEnabled(surface, true);
        visual.Properties.StopAnimation(property);
        visual.Properties.InsertVector3(property, Vector3.Zero);
        if (!MotionEnabled) return;

        var compositor = visual.Compositor;
        using var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(215);
        animation.InsertKeyFrame(0, new Vector3(forward ? 48 : -32, 0, 0));
        animation.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(new(.16f, 1), new(.3f, 1)));
        visual.Properties.StartAnimation(property, animation);
    }
}
