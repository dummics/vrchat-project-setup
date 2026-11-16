using Microsoft.UI.Xaml.Data;

namespace VrcSetup_Desktop;

public sealed class ProjectDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTime date || date == default) return "—";
        var local = date.ToLocalTime();
        var it = VrcSetup.Core.UiText.CurrentLanguage == "it";
        var culture = System.Globalization.CultureInfo.GetCultureInfo(it ? "it-IT" : "en-GB");
        if (parameter?.ToString() == "Full") return (it ? "Data e ora: " : "Date and time: ") + local.ToString("g", culture);
        var elapsed = DateTime.UtcNow - date.ToUniversalTime();
        if (elapsed < TimeSpan.Zero) return local.ToString("d", culture);
        if (elapsed.TotalMinutes < 1) return it ? "Ora" : "Just now";
        if (elapsed.TotalHours < 1) return it ? $"{(int)elapsed.TotalMinutes} min fa" : $"{(int)elapsed.TotalMinutes} min ago";
        if (local.Date == DateTime.Today) return it ? $"{(int)elapsed.TotalHours} h fa" : $"{(int)elapsed.TotalHours} h ago";
        var days = (DateTime.Today - local.Date).Days;
        if (days == 1) return it ? "Ieri" : "Yesterday";
        if (days < 7) return it ? $"{days} giorni fa" : $"{days} days ago";
        return local.ToString("d", culture);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
