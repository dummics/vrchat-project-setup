using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Microsoft.UI.Xaml;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<UiCreationPackage> CreationPackages { get; } = [];
    public ObservableCollection<UiCreationPackage> DetectedCreationPackages { get; } = [];
    public ObservableCollection<UiCreationPackage> PossibleCreationPackages { get; } = [];
    public Visibility DetectedCreationPackagesVisibility => DetectedCreationPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PossibleCreationPackagesVisibility => PossibleCreationPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    private UiCreationPackage[] BundledReviewCandidates()
    {
        var protectedIds = ProjectRules.Foundation(CurrentCreationSelection(), config).Append(ProjectRules.AvatarSdk).Append(ProjectRules.WorldSdk).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CreationPackages.Where(x => x.Selected && x.Data.Evidence?.Confidence == DependencyConfidence.Included && !protectedIds.Contains(x.Id)).ToArray();
    }
    public Visibility BundledReviewVisibility => BundledReviewCandidates().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalCustomizeVisibility => Visibility.Visible;
    public Visibility SuggestedReviewOnlyVisibility => BundledReviewVisibility == Visibility.Collapsed && (DetectedCreationPackages.Count > 0 || PossibleCreationPackages.Count > 0 || VersionReviewCandidates().Count > 0) ? Visibility.Visible : Visibility.Collapsed;
    private Dictionary<string, DependencyProposal> VersionReviewCandidates() => CreationInspection is null
        ? new(StringComparer.OrdinalIgnoreCase)
        : DependencyAdvisor.Propose(CurrentCreationSelection(), CreationInspection).Where(x => x.Kind == DependencyProposalKind.ReviewVersion)
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    public string BundledReviewText
    {
        get
        {
            var names = BundledReviewCandidates().Select(x => x.Name).ToArray();
            return string.Join(", ", names.Take(3)) + (names.Length > 3 ? ", …" : "");
        }
    }
    private readonly Dictionary<string, int> creationPreviewOrder = new(StringComparer.OrdinalIgnoreCase);
    private string creationPreviewContext = "";
    public string CreationPackageSummary
    {
        get
        {
            var missing = CreationPackages.Count(x => !x.Selected);
            return string.Format(UiText.Text("{0} packages included"), CreationPackages.Count(x => x.Selected))
                + (missing > 0 ? " · " + string.Format(UiText.Text("{0} suggested"), missing) : "");
        }
    }
    private Dictionary<string, string> CurrentCreationSelection()
    {
        var selected = new Dictionary<string, string>(creationOverrides ?? (SelectedPreset == DefaultPresetKey ? config.VpmPackages : config.PackagePresets.GetValueOrDefault(SelectedPreset) ?? config.VpmPackages), StringComparer.OrdinalIgnoreCase);
        foreach (var id in ProjectRules.Foundation(selected, config)) selected.TryAdd(id, "latest");
        return selected;
    }
    public SuggestionReviewSnapshot CreateSuggestionReview()
    {
        var selected = CurrentCreationSelection();
        var removable = BundledReviewCandidates().Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var versionReviews = VersionReviewCandidates();
        var rows = CreationPackages.Where(x => x.Data.Evidence is not null &&
            (removable.Contains(x.Id) || versionReviews.ContainsKey(x.Id) || !x.Selected && x.Data.Evidence.Confidence is DependencyConfidence.Detected or DependencyConfidence.Declared or DependencyConfidence.Possible))
            .Select(x => new SuggestionReviewItem(x.Id, x.Name, x.Selected, x.Data.Evidence!.Confidence,
                x.Data.Evidence.Confidence == DependencyConfidence.Possible ? "latest" : DependencyAdvisor.SuggestedVersion(x.Data.Evidence), x.AvailableVersion,
                versionReviews.GetValueOrDefault(x.Id)?.SelectedVersion, versionReviews.GetValueOrDefault(x.Id)?.Evidence.VersionConstraint)).ToArray();
        return new(PackageFile, SelectedImportDraft, SelectedImportDraft?.Revision, SelectedPreset, SelectedRecipe, CreationInspection, selected, rows);
    }
    public bool ApplySuggestionReview(SuggestionReviewSnapshot snapshot, IReadOnlyDictionary<string, bool> choices)
    {
        if (PackageFile != snapshot.File || SelectedImportDraft != snapshot.Draft || SelectedImportDraft?.Revision != snapshot.Revision || SelectedPreset != snapshot.Preset || SelectedRecipe != snapshot.Recipe || CreationInspection != snapshot.Inspection ||
            !CurrentCreationSelection().OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).SequenceEqual(snapshot.Selection.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))) return false;
        var selection = new Dictionary<string, string>(snapshot.Selection, StringComparer.OrdinalIgnoreCase);
        foreach (var row in snapshot.Items)
        {
            if (!choices.TryGetValue(row.Id, out var include) || include == row.Selected) continue;
            if (include && row.Version is not null) selection.TryAdd(row.Id, row.Version);
            else if (!include && row.Selected && row.Confidence == DependencyConfidence.Included) selection.Remove(row.Id);
        }
        creationOverrides = selection;
        if (SelectedImportDraft is { } draft) draft.Packages = new(selection);
        RememberSelectionSource(); RefreshCreationPackages();
        return true;
    }
    private void RefreshCreationPackages()
    {
        var context = PackageFile + "\0" + SelectedPreset;
        if (context != creationPreviewContext) { creationPreviewContext = context; creationPreviewOrder.Clear(); }
        var desired = DependencyAdvisor.CombineSelection(CurrentCreationSelection(), CreationInspection);
        foreach (var item in desired) if (!creationPreviewOrder.ContainsKey(item.Id)) creationPreviewOrder[item.Id] = creationPreviewOrder.Count;
        var ids = desired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = CreationPackages.Count - 1; i >= 0; i--) if (!ids.Contains(CreationPackages[i].Id)) CreationPackages.RemoveAt(i);
        var ordered = desired.OrderBy(x => creationPreviewOrder[x.Id]).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var item = ordered[i]; var name = PackageDisplayName(item.Id);
            var current = CreationPackages.FirstOrDefault(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (current is null) CreationPackages.Insert(i, new(item, name));
            else { current.Update(item, name); var old = CreationPackages.IndexOf(current); if (old != i) CreationPackages.Move(old, i); }
        }
        var suggestions = CreationPackages.Where(x => !x.Selected && x.Data.Evidence?.Confidence is DependencyConfidence.Detected or DependencyConfidence.Declared).ToArray();
        for (var i = DetectedCreationPackages.Count - 1; i >= 0; i--) if (!suggestions.Contains(DetectedCreationPackages[i])) DetectedCreationPackages.RemoveAt(i);
        for (var i = 0; i < suggestions.Length; i++)
        {
            var position = DetectedCreationPackages.IndexOf(suggestions[i]);
            if (position < 0) DetectedCreationPackages.Insert(i, suggestions[i]);
            else if (position != i) DetectedCreationPackages.Move(position, i);
        }
        var possible = CreationPackages.Where(x => !x.Selected && x.Data.Evidence?.Confidence == DependencyConfidence.Possible).ToArray();
        for (var i = PossibleCreationPackages.Count - 1; i >= 0; i--) if (!possible.Contains(PossibleCreationPackages[i])) PossibleCreationPackages.RemoveAt(i);
        for (var i = 0; i < possible.Length; i++)
        {
            var position = PossibleCreationPackages.IndexOf(possible[i]);
            if (position < 0) PossibleCreationPackages.Insert(i, possible[i]);
            else if (position != i) PossibleCreationPackages.Move(position, i);
        }
        OnPropertyChanged(nameof(DetectedCreationPackagesVisibility));
        OnPropertyChanged(nameof(PossibleCreationPackagesVisibility));
        OnPropertyChanged(nameof(BundledReviewVisibility));
        OnPropertyChanged(nameof(NormalCustomizeVisibility));
        OnPropertyChanged(nameof(SuggestedReviewOnlyVisibility));
        OnPropertyChanged(nameof(BundledReviewText));
        OnPropertyChanged(nameof(CreationPackageSummary));
        RefreshInspectionSummary();
        RefreshCreationVersions();
    }
    public async Task AddCreationSuggestionAsync(UiCreationPackage row)
    {
        var current = CreationPackages.FirstOrDefault(x => x.Id == row.Id);
        if (current is null || current.Selected) return;
        if (current.Data.Evidence is not { } hint)
        { await CustomizeCreationPackagesCommand.ExecuteAsync(null); return; }
        var version = hint.Confidence == DependencyConfidence.Possible ? "latest" : DependencyAdvisor.SuggestedVersion(hint);
        if (version is null) { await CustomizeCreationPackagesCommand.ExecuteAsync(null); return; }
        var selected = CurrentCreationSelection(); selected.TryAdd(row.Id, version);
        creationOverrides = selected;
        if (SelectedImportDraft is { } draft) draft.Packages = new(selected);
        RememberSelectionSource();
        RefreshCreationPackages();
    }
}

public sealed record SuggestionReviewItem(string Id, string Name, bool Selected, DependencyConfidence Confidence, string? Version, string AvailableVersion, string? SelectedVersion, string? RequiredVersion);
public sealed record SuggestionReviewSnapshot(string File, UiImportDraft? Draft, int? Revision, string Preset, string Recipe, PackageInspectionReport? Inspection, Dictionary<string, string> Selection, SuggestionReviewItem[] Items);

public sealed class UiCreationPackage(PackageSelectionPreview data, string name) : ObservableObject
{
    public PackageSelectionPreview Data { get; private set; } = data;
    public string Id => Data.Id;
    public string Name { get; private set; } = name;
    public bool Selected => Data.Selected;
    private string latestVersion = "Checking version…";
    public string Version => Data.Version is "latest" ? "Latest" : string.IsNullOrWhiteSpace(Data.Version) ? "Choose version" : Data.Version;
    public string AvailableVersion => latestVersion;
    public Visibility AvailableVersionVisibility => Data.Version is "latest" ? Visibility.Visible : Visibility.Collapsed;
    public void SetLatestVersion(string value)
    {
        if (latestVersion == value) return;
        latestVersion = value;
        OnPropertyChanged(nameof(AvailableVersion)); OnPropertyChanged(nameof(Tooltip)); OnPropertyChanged(nameof(AccessibilityLabel));
    }
    public string AccessibilityLabel => Name + ", " + UiText.Text(Version) + (Data.Version is "latest" ? ", " + UiText.Text(AvailableVersion) : "");
    public string Hint => Data.Evidence?.Confidence switch
    {
        DependencyConfidence.Included => "In file", DependencyConfidence.Declared => Selected ? "Required" : "Required · not selected",
        DependencyConfidence.Detected => Selected ? "Detected" : "Detected · not selected", _ => ""
    };
    public string Action => Data.Evidence is { } hint && (hint.Confidence == DependencyConfidence.Possible || DependencyAdvisor.SuggestedVersion(hint) is not null) ? "Add" : "Review";
    public Visibility ActionVisibility => Selected ? Visibility.Collapsed : Visibility.Visible;
    public string Tooltip => UiText.Text(Version) + (Data.Version is "latest" ? ": " + UiText.Text(AvailableVersion) : "") + (Data.Evidence is { Confidence: DependencyConfidence.Declared, VersionConstraint: { Length: > 0 } constraint } ? "\n" + UiText.Text("Requires ") + constraint : "");
    public void Update(PackageSelectionPreview value, string displayName)
    { if (Data == value && Name == displayName) return; Data = value; Name = displayName; OnPropertyChanged(string.Empty); }
}
