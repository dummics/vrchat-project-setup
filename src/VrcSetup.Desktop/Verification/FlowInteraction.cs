using VrcSetup.Core;
using VrcSetup_Desktop.ViewModels;
namespace VrcSetup_Desktop.Verification;

// Native verification supplies confirmation decisions without desktop input.
internal sealed class FlowInteraction(IDesktopInteraction inner) : IDesktopInteraction
{
    public bool ConfirmResult { get; set; }
    public int Confirmations { get; private set; }
    public int Restarts { get; private set; }
    public bool? ReviewResult { get; set; }
    public OperationPlan? LastReview { get; private set; }
    public void ClearReview() => LastReview = null;
    public Task<bool> RestartApplicationAsync() { Restarts++; return Task.FromResult(true); }
    public string? SettingsChoice { get; set; }
    public string? ChangesChoice { get; set; }
    public Task<string?> ResolveChangesAsync(string title, string action) =>
        Task.FromResult<string?>(ChangesChoice == "Cancel" ? null : ChangesChoice ?? (ConfirmResult ? "Discard" : null));
    public string? FileChoice { get; set; }
    public Task<string?> ResolveSettingsAsync() => Task.FromResult(SettingsChoice);
    public int SaveFileRequests { get; private set; }
    public Task<string?> SaveFileAsync(string name, string extension) { SaveFileRequests++; return Task.FromResult(FileChoice); }
    public Task<bool> ConfirmAsync(string title, string content, string action) { Confirmations++; return Task.FromResult(ConfirmResult); }
    public Task<string?> PickFileAsync(string extension) => FileChoice is null ? inner.PickFileAsync(extension) : Task.FromResult<string?>(FileChoice);
    public Task<string?> PickFolderAsync() => inner.PickFolderAsync();
    public Task<bool> ReviewAsync(OperationPlan plan) { LastReview = plan; return ReviewResult is { } result ? Task.FromResult(result) : inner.ReviewAsync(plan); }
    public Task<string?> TextAsync(string title, string initial = "") => inner.TextAsync(title, initial);
    public Task<string?> ChooseAsync(string title, string[] items) => inner.ChooseAsync(title, items);
    public Task<string[]?> ChooseManyAsync(string title, string[] items) => inner.ChooseManyAsync(title, items);
    public Task<string?> VersionAsync(string package, string[] versions, string current) => inner.VersionAsync(package, versions, current);
    public Task<string[]?> ChooseProjectsAsync(string title, ProjectSnapshot[] projects, string[] selected) => inner.ChooseProjectsAsync(title, projects, selected);
    public Task ShowErrorAsync(string message) => inner.ShowErrorAsync(message);
    public void CloseApplication() => inner.CloseApplication();
    public Task<Dictionary<string, string>?> EditPackagesAsync(string title, Dictionary<string, string> packages, AppConfiguration config, PackageDefinition[] known, Func<string, CancellationToken, Task<PackageDefinition[]>> search, Func<string, CancellationToken, Task<string[]>> versions, PackageInspectionReport? inspection = null) => inner.EditPackagesAsync(title, packages, config, known, search, versions, inspection);
}
