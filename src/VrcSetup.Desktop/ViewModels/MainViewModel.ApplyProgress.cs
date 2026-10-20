using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ApplyOverlayVisibility)), NotifyPropertyChangedFor(nameof(MainInteractionEnabled))]
    public partial bool IsApplyingPackages { get; set; }
    [ObservableProperty] public partial string ApplyProject { get; set; } = "";
    [ObservableProperty] public partial string ApplyPhase { get; set; } = "";
    [ObservableProperty] public partial string ApplyPosition { get; set; } = "";
    [ObservableProperty] public partial bool CanCancelApply { get; set; }
    public Visibility ApplyOverlayVisibility => IsApplyingPackages ? Visibility.Visible : Visibility.Collapsed;
    public bool MainInteractionEnabled => !IsInstallingUpdate;
    private PackageEditSession? applyingSession;
    private bool WorkspaceApplying => IsApplyingPackages && ReferenceEquals(session, applyingSession);
    private ProjectPlan[] applyingProjects = [];
    [RelayCommand] private void CancelApply()
    {
        if (!CanCancelApply) return;
        CanCancelApply = false; ApplyPhase = "Stopping safely · waiting for recovery"; operation?.Cancel();
    }
    private void UpdateApplyProgress(OperationEvent value)
    {
        if (!IsApplyingPackages) return;
        if (value.Project is { } path && Array.FindIndex(applyingProjects, x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0)
        { ApplyProject = applyingProjects[index].Name; ApplyPosition = $"Project {index + 1} of {applyingProjects.Length}"; }
        if (!CanCancelApply && value.Phase != "Recovery") return;
        ApplyPhase = value.Phase switch
        {
            "Backup" => "Creating backup", "Prepare" => "Preparing project", "Packages" or "VPM" => "Updating packages",
            "Verify" or "Validate" => "Checking installed packages", "Import" => "Importing assets", "Finalize" or "Finish" => "Finishing",
            "Recovery" => "Restoring backup", "Completed" => "Project updated", "Cancelled" => "Cancelled", _ => ApplyPhase
        };
    }
}
