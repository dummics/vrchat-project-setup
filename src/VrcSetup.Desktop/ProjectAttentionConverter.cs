using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

public sealed class ProjectAttentionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ProjectSnapshot project) return Visibility.Collapsed;
        if (parameter?.ToString() == "Label") return project.Incomplete ? "Setup unfinished" : project.Status;
        return project.Incomplete || project.Status != "Ready" ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
