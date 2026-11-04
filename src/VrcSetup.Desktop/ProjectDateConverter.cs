using Microsoft.UI.Xaml.Data;

namespace VrcSetup_Desktop;

public sealed class ProjectDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTime date || date == default) return "—";
        var local = date.ToLocalTime();
        if (parameter?.ToString() == "Full") return "Last modified: " + local.ToString("g");
        var elapsed = DateTime.UtcNow - date.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours} h ago";
        if (elapsed.TotalDays < 2) return "Yesterday";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays} days ago";
        return local.ToString("d");
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
