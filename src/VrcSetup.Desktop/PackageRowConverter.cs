using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using VrcSetup.Core;
namespace VrcSetup_Desktop;
public sealed class PackageRowConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not PackageWorkspaceRow row) return "";
        return parameter?.ToString() switch
        {
            "CanRemove" => !row.Required,
            "IsAdd" => !row.Included,
            "Glyph" => row.Included ? "−" : "+",
            "Action" => UiText.Text(row.Required ? "Required package" : row.Included ? "Remove package" : "Keep package"),
            "PendingVisibility" => row.Action.StartsWith("Will be") && !IsDowngrade(row) ? Visibility.Visible : Visibility.Collapsed,
            "DowngradeVisibility" => row.Action == "Will be changed" && IsDowngrade(row) ? Visibility.Visible : Visibility.Collapsed,
            "MissingVisibility" => row.Missing && !row.Action.StartsWith("Will be") ? Visibility.Visible : Visibility.Collapsed,
            "Version" => row.Action is "Will be added" or "Will be changed" ? UiText.Text(row.SelectedVersion == "latest" ? "Latest" : row.SelectedVersion) : row.InstalledVersion,
            _ => row.Action == "Will be changed" && IsDowngrade(row)
                ? string.Format(UiText.Text("Downgrade · {0} → {1}"), row.InstalledVersion, row.SelectedVersion)
                : UiText.Text(row.Action == "Will be removed" ? "Remove on apply" : row.Action.StartsWith("Will be") ? "On apply · " + (row.SelectedVersion == "latest" ? "Latest" : row.SelectedVersion) : "")
        };
    }
    private static bool IsDowngrade(PackageWorkspaceRow row) => PackageDowngrades.Classify(row.Id, row.InstalledVersion, row.SelectedVersion) is VersionChangeRisk.Downgrade or VersionChangeRisk.RiskyDowngrade;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
