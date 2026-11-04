using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ApplyOverlayVisibility)), NotifyPropertyChangedFor(nameof(ShellInteractionEnabled)), NotifyPropertyChangedFor(nameof(MainInteractionEnabled))]
    public partial bool IsApplyingPackages { get; set; }
    [ObservableProperty] public partial string ApplyProject { get; set; } = "";
    [ObservableProperty] public partial string ApplyPhase { get; set; } = "";
    [ObservableProperty] public partial string ApplyPosition { get; set; } = "";
    [ObservableProperty] public partial string ApplyPackage { get; set; } = "";
    [ObservableProperty] public partial string ApplyPackageVersion { get; set; } = "";
    internal Func<OperationPlan, Task>? BeforePackageApplyForVerification { get; set; }
    partial void OnApplyPhaseChanged(string value) => NotifyApplicationStatus();
    partial void OnApplyPackageChanged(string value) => NotifyApplicationStatus();
    partial void OnApplyPackageVersionChanged(string value) => NotifyApplicationStatus();
    partial void OnApplyProjectChanged(string value) => NotifyApplicationStatus();
    [ObservableProperty] public partial bool CanCancelApply { get; set; }
    public Visibility ApplyOverlayVisibility => IsApplyingPackages ? Visibility.Visible : Visibility.Collapsed;
    public bool MainInteractionEnabled => !IsInstallingUpdate;
    public bool ShellInteractionEnabled => MainInteractionEnabled && !IsApplyingPackages;
    private PackageEditSession? applyingSession;
    private bool WorkspaceApplying => IsApplyingPackages && ReferenceEquals(session, applyingSession);
    private ProjectPlan[] applyingProjects = [];
    [RelayCommand] private void CancelApply()
    {
        if (!CanCancelApply) return;
        CanCancelApply = false; ApplyPhase = "Stopping safely · waiting for recovery"; operation?.Cancel();
    }
    internal void UpdateApplyProgress(OperationEvent value)
    {
        if (!IsApplyingPackages) return;
        if (value.Project is { } path && Array.FindIndex(applyingProjects, x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0)
        { ApplyProject = applyingProjects[index].Name; ApplyPosition = string.Format(UiText.Text("Project {0} of {1}"), index + 1, applyingProjects.Length); }
        if (!CanCancelApply && value.Phase != "Recovery") return;
        if (value.Package is { } package)
        {
            ApplyPhase = package.Kind switch { ChangeKind.Add => "Adding package", ChangeKind.Remove => "Removing package", _ => "Updating package" };
            ApplyPackage = PackageDisplayName(package.Id);
            ApplyPackageVersion = package.Kind == ChangeKind.Remove ? package.InstalledVersion ?? "" : package.ResolvedVersion ?? package.RequestedVersion ?? "";
            return;
        }
        // Tool output stays in logs; preserve the readable current package through VPM chatter.
        if (value.Phase == "VPM") return;
        ApplyPackage = ""; ApplyPackageVersion = "";
        ApplyPhase = value.Phase switch
        {
            "Backup" => "Creating backup", "Prepare" => "Preparing project", "Packages" or "VPM" => "Updating packages",
            "Verify" or "Validate" => "Checking installed packages", "Import" => "Importing assets", "Finalize" or "Finish" => "Finishing",
            "Recovery" => "Restoring backup", "Completed" => "Project updated", "Cancelled" => "Cancelled", _ => ApplyPhase
        };
    }
}
