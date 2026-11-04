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
    public Visibility DetectedCreationPackagesVisibility => DetectedCreationPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        OnPropertyChanged(nameof(DetectedCreationPackagesVisibility));
        OnPropertyChanged(nameof(CreationPackageSummary));
    }
    public async Task AddCreationSuggestionAsync(UiCreationPackage row)
    {
        var current = CreationPackages.FirstOrDefault(x => x.Id == row.Id);
        if (current is null || current.Selected) return;
        if (current.Data.Evidence is not { } hint || DependencyAdvisor.SuggestedVersion(hint) is not { } version)
        { await CustomizeCreationPackagesCommand.ExecuteAsync(null); return; }
        var selected = CurrentCreationSelection(); selected.TryAdd(row.Id, version);
        creationOverrides = selected;
        if (SelectedImportDraft is { } draft) draft.Packages = new(selected);
        RefreshCreationPackages();
    }
}

public sealed class UiCreationPackage(PackageSelectionPreview data, string name) : ObservableObject
{
    public PackageSelectionPreview Data { get; private set; } = data;
    public string Id => Data.Id;
    public string Name { get; private set; } = name;
    public bool Selected => Data.Selected;
    public string Version => Data.Version is "latest" ? "Latest" : string.IsNullOrWhiteSpace(Data.Version) ? "Choose version" : Data.Version;
    public string AccessibilityLabel => Name + ", " + UiText.Text(Version);
    public string Hint => Data.Evidence?.Confidence switch
    {
        DependencyConfidence.Included => "In file", DependencyConfidence.Declared => Selected ? "Required" : "Required · not selected",
        DependencyConfidence.Detected => Selected ? "Detected" : "Detected · not selected", _ => ""
    };
    public string Action => Data.Evidence is { } hint && DependencyAdvisor.SuggestedVersion(hint) is not null ? "Add" : "Review";
    public Visibility ActionVisibility => Selected ? Visibility.Collapsed : Visibility.Visible;
    public string Tooltip => Id + "\n" + UiText.Text(Version) + (Data.Evidence?.VersionConstraint is { Length: > 0 } constraint ? "\nRequires " + constraint : "");
    public void Update(PackageSelectionPreview value, string displayName)
    { if (Data == value && Name == displayName) return; Data = value; Name = displayName; OnPropertyChanged(string.Empty); }
}
