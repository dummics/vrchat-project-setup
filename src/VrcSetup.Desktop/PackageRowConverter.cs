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
            "PendingVisibility" => row.Action.StartsWith("Will be") ? Visibility.Visible : Visibility.Collapsed,
            _ => UiText.Text(row.Action == "Will be removed" ? "Remove on apply" : row.Action.StartsWith("Will be") ? "On apply · " + (row.SelectedVersion == "latest" ? "Latest" : row.SelectedVersion) : "")
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
