using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? inspectionRequest;
    partial void OnCreationInspectionChanged(PackageInspectionReport? value) => RefreshCreationPackages();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInspectDeeper))] public partial PackageInspectionReport? CreationInspection { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInspectDeeper)), NotifyPropertyChangedFor(nameof(ImportReviewLabel)), NotifyPropertyChangedFor(nameof(InspectionBusyVisibility)), NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))] public partial bool IsInspectingPackage { get; set; }
    public Visibility InspectionBusyVisibility => IsInspectingPackage ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InspectionVisibility))] public partial string InspectionStatus { get; set; } = "";
    public Visibility InspectionVisibility => InspectionStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool CanInspectDeeper => !IsInspectingPackage && CreationInspection is not null;
    public string InspectionReviewNote => IsInspectingPackage ? "Checking packages…" : CreationInspection is { Observation.ArchiveComplete: false } or { Observation.TextComplete: false } ? "Confirm the suggested packages before importing." : "";
    [RelayCommand] private async Task InspectDeeper() => await InspectCreationPackageAsync(PackageFile, true);
    private async Task InspectCreationPackageAsync(string file, bool deep = false)
    {
        inspectionRequest?.Cancel();
        var request = inspectionRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CreationInspection = null; InspectionStatus = ""; IsInspectingPackage = false;
        var completed = false;
        try
        {
            if (string.IsNullOrWhiteSpace(file) || !file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) return;
            IsInspectingPackage = true;
            InspectionStatus = "Checking packages…";
            var partial = new Progress<PackageInspectionReport>(report =>
            {
                if (inspectionRequest != request || request.IsCancellationRequested) return;
                CreationInspection = report;
                InspectionStatus = "Checking packages…";
            });
            var pending = app.Inspection.InspectForImportAsync(file, deep, partial, request.Token);
            var report = await pending;
            if (inspectionRequest != request || request.IsCancellationRequested) return;
            CreationInspection = report;
            completed = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (inspectionRequest == request) { InspectionStatus = "Choose packages manually to continue."; app.Diagnostics.Write("Package inspection", error.Message, error); }
        }
        finally { if (inspectionRequest == request) { IsInspectingPackage = false; inspectionRequest = null; if (completed) RefreshInspectionSummary(); } request.Dispose(); }
    }
    private void RefreshInspectionSummary()
    {
        if (IsInspectingPackage || CreationInspection is not { } report) return;
        var detected = DetectedCreationPackages.Count;
        var possible = PossibleCreationPackages.Count > 0;
        InspectionStatus = !report.Observation.ArchiveComplete || !report.Observation.TextComplete || report.UnresolvedReferences > 0
            ? detected > 0 || possible ? "Confirm the suggested packages before importing." : "Check the package selection before importing."
            : detected > 0 ? string.Format(UiText.Text("{0} packages to review"), detected) : possible ? "Check other possible packages below." : "No additional packages found.";
    }
}
