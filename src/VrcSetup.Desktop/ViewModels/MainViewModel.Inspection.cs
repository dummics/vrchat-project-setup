using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? inspectionRequest;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInspectDeeper))] public partial PackageInspectionReport? CreationInspection { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInspectDeeper))] public partial bool IsInspectingPackage { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InspectionVisibility))] public partial string InspectionStatus { get; set; } = "";
    public Visibility InspectionVisibility => InspectionStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool CanInspectDeeper => !IsInspectingPackage && CreationInspection is not null;
    public string InspectionReviewNote => IsInspectingPackage ? "Package analysis is still running. This review uses your current selection." : CreationInspection is { Observation.ArchiveComplete: false } or { Observation.TextComplete: false } ? "Package analysis is partial. Review your package selection." : "";
    [RelayCommand] private async Task InspectDeeper() => await InspectCreationPackageAsync(PackageFile, true);
    private async Task InspectCreationPackageAsync(string file, bool deep = false)
    {
        inspectionRequest?.Cancel();
        var request = inspectionRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CreationInspection = null; InspectionStatus = ""; IsInspectingPackage = false;
        try
        {
            if (string.IsNullOrWhiteSpace(file) || !file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) return;
            IsInspectingPackage = true;
            var pending = app.Inspection.InspectAsync(file, deep ? InspectionOptions.Extended : null, request.Token);
            var delay = Task.Delay(300, request.Token);
            if (await Task.WhenAny(pending, delay) == delay && inspectionRequest == request && !request.IsCancellationRequested)
            { IsInspectingPackage = true; InspectionStatus = "Checking packages…"; }
            var report = await pending;
            if (inspectionRequest != request || request.IsCancellationRequested) return;
            CreationInspection = report;
            var detected = report.Suggestions.Count(x => x.Confidence is DependencyConfidence.Detected or DependencyConfidence.Declared);
            InspectionStatus = !report.Observation.ArchiveComplete || !report.Observation.TextComplete ? (detected > 0 ? $"{detected} packages detected · partial analysis" : "Partial analysis · review packages") : detected > 0 ? $"{detected} packages detected" : "Analysis finished · review packages";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (inspectionRequest == request) { InspectionStatus = "Analysis unavailable · choose packages manually"; app.Diagnostics.Write("Package inspection", error.Message, error); }
        }
        finally { if (inspectionRequest == request) { IsInspectingPackage = false; inspectionRequest = null; } request.Dispose(); }
    }
}
