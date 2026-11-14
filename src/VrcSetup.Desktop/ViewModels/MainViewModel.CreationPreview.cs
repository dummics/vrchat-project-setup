using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Microsoft.UI.Xaml;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<UiCreationPackage> CreationPackages { get; } = [];
    public ObservableCollection<UiCreationPackage> SelectedCreationPackages { get; } = [];
    public ObservableCollection<UiCreationPackage> ExcludedCreationPackages { get; } = [];
    private readonly Dictionary<string, string> standaloneExcludedPackages = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> CurrentExcludedPackages => SelectedImportDraft?.ExcludedPackages ?? standaloneExcludedPackages;
    private void RememberExcludedSelection(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> next, bool samePreset)
    {
        if (!samePreset) CurrentExcludedPackages.Clear();
        else foreach (var entry in previous.Where(x => !next.ContainsKey(x.Key))) CurrentExcludedPackages[entry.Key] = entry.Value;
        foreach (var id in next.Keys) CurrentExcludedPackages.Remove(id);
    }
    public Visibility ExcludedCreationPackagesVisibility => ExcludedCreationPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string ExcludedCreationPackageSummary => string.Format(UiText.Text("Excluded from selection ({0})"), ExcludedCreationPackages.Count);
    public void ExcludeCreationPackage(UiCreationPackage row)
    {
        if (!row.Selected || row.IsRequired) return;
        var selection = CurrentCreationSelection();
        if (ProjectRules.Foundation(selection, config).Append(ProjectRules.AvatarSdk).Append(ProjectRules.WorldSdk).Contains(row.Id, StringComparer.OrdinalIgnoreCase)) return;
        if (!selection.Remove(row.Id, out var version)) return;
        CurrentExcludedPackages[row.Id] = version;
        creationOverrides = selection;
        if (SelectedImportDraft is { } draft) draft.Packages = new(selection);
        RememberSelectionSource(); RefreshCreationPackages();
    }
    public void IncludeExcludedCreationPackage(UiCreationPackage row)
    {
        if (!CurrentExcludedPackages.Remove(row.Id, out var version)) return;
        var selection = CurrentCreationSelection(); selection[row.Id] = version;
        creationOverrides = selection;
        if (SelectedImportDraft is { } draft) draft.Packages = new(selection);
        RememberSelectionSource(); RefreshCreationPackages();
    }
    public ObservableCollection<UiCreationPackage> DetectedCreationPackages { get; } = [];
    public ObservableCollection<UiCreationPackage> PossibleCreationPackages { get; } = [];
    public Visibility DetectedCreationPackagesVisibility => DetectedCreationPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PossibleCreationPackagesVisibility => PossibleCreationPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalCustomizeVisibility => Visibility.Visible;
    public Visibility SuggestedReviewOnlyVisibility => DetectedCreationPackages.Count > 0 || PossibleCreationPackages.Count > 0 || VersionReviewCandidates().Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    private Dictionary<string, DependencyProposal> VersionReviewCandidates() => CreationInspection is null
        ? new(StringComparer.OrdinalIgnoreCase)
        : DependencyAdvisor.Propose(CurrentCreationSelection(), CreationInspection).Where(x => x.Kind == DependencyProposalKind.ReviewVersion)
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> creationPreviewOrder = new(StringComparer.OrdinalIgnoreCase);
    private string creationPreviewContext = "";
    public string CreationPackageSummary
    {
        get
        {
            var missing = DetectedCreationPackages.Count + PossibleCreationPackages.Count;
            return string.Format(UiText.Text("Packages to install ({0})"), CreationPackages.Count(x => x.Selected))
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
        var versionReviews = VersionReviewCandidates();
        var rows = CreationPackages.Where(x => x.Data.Evidence is not null &&
            !CurrentExcludedPackages.ContainsKey(x.Id) && (versionReviews.ContainsKey(x.Id) || !x.Selected && x.Data.Evidence.Confidence is DependencyConfidence.Detected or DependencyConfidence.Declared or DependencyConfidence.Possible))
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
        var required = ProjectRules.Foundation(CurrentCreationSelection(), config).Append(ProjectRules.AvatarSdk).Append(ProjectRules.WorldSdk).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        foreach (var row in CreationPackages) row.IsRequired = required.Contains(row.Id);
        SelectedCreationPackages.Clear();
        foreach (var row in CreationPackages.Where(x => x.Selected).OrderByDescending(x => x.IsRequired).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) SelectedCreationPackages.Add(row);
        ExcludedCreationPackages.Clear();
        var currentSelection = CurrentCreationSelection();
        if (!switchingImportDraft)
            foreach (var id in CurrentExcludedPackages.Keys.Where(currentSelection.ContainsKey).ToArray()) CurrentExcludedPackages.Remove(id);
        foreach (var excluded in CurrentExcludedPackages)
            ExcludedCreationPackages.Add(new(new(excluded.Key, excluded.Value, false, null), PackageDisplayName(excluded.Key)));
        OnPropertyChanged(nameof(ExcludedCreationPackagesVisibility)); OnPropertyChanged(nameof(ExcludedCreationPackageSummary));
        var suggestions = CreationPackages.Where(x => !x.Selected && !CurrentExcludedPackages.ContainsKey(x.Id) && x.Data.Evidence?.Confidence is DependencyConfidence.Detected or DependencyConfidence.Declared).ToArray();
        for (var i = DetectedCreationPackages.Count - 1; i >= 0; i--) if (!suggestions.Contains(DetectedCreationPackages[i])) DetectedCreationPackages.RemoveAt(i);
        for (var i = 0; i < suggestions.Length; i++)
        {
            var position = DetectedCreationPackages.IndexOf(suggestions[i]);
            if (position < 0) DetectedCreationPackages.Insert(i, suggestions[i]);
            else if (position != i) DetectedCreationPackages.Move(position, i);
        }
        var possible = CreationPackages.Where(x => !x.Selected && !CurrentExcludedPackages.ContainsKey(x.Id) && x.Data.Evidence?.Confidence == DependencyConfidence.Possible).ToArray();
        for (var i = PossibleCreationPackages.Count - 1; i >= 0; i--) if (!possible.Contains(PossibleCreationPackages[i])) PossibleCreationPackages.RemoveAt(i);
        for (var i = 0; i < possible.Length; i++)
        {
            var position = PossibleCreationPackages.IndexOf(possible[i]);
            if (position < 0) PossibleCreationPackages.Insert(i, possible[i]);
            else if (position != i) PossibleCreationPackages.Move(position, i);
        }
        OnPropertyChanged(nameof(DetectedCreationPackagesVisibility));
        OnPropertyChanged(nameof(PossibleCreationPackagesVisibility));
        OnPropertyChanged(nameof(NormalCustomizeVisibility));
        OnPropertyChanged(nameof(SuggestedReviewOnlyVisibility));
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
    private bool isRequired;
    public bool IsRequired { get => isRequired; set { if (SetProperty(ref isRequired, value)) { OnPropertyChanged(nameof(ExcludeVisibility)); OnPropertyChanged(nameof(RequiredVisibility)); } } }
    public Visibility ExcludeVisibility => Selected && !IsRequired ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RequiredVisibility => IsRequired ? Visibility.Visible : Visibility.Collapsed;
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
        DependencyConfidence.Declared => Selected ? "Required" : "Required · not selected",
        DependencyConfidence.Detected => Selected ? "Detected" : "Detected · not selected", _ => ""
    };
    public string Action => Data.Evidence is { } hint && (hint.Confidence == DependencyConfidence.Possible || DependencyAdvisor.SuggestedVersion(hint) is not null) ? "Add" : "Review";
    public Visibility ActionVisibility => Selected ? Visibility.Collapsed : Visibility.Visible;
    public string Tooltip => UiText.Text(Version) + (Data.Version is "latest" ? ": " + UiText.Text(AvailableVersion) : "") + (Data.Evidence is { Confidence: DependencyConfidence.Declared, VersionConstraint: { Length: > 0 } constraint } ? "\n" + UiText.Text("Requires ") + constraint : "");
    public void Update(PackageSelectionPreview value, string displayName)
    { if (Data == value && Name == displayName) return; Data = value; Name = displayName; OnPropertyChanged(string.Empty); }
}
