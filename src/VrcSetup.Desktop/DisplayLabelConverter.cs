using Microsoft.UI.Xaml.Data;

namespace VrcSetup_Desktop;

public sealed class DisplayLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value?.ToString() switch
    { "My default packages" => VrcSetup.Core.UiText.Text("Default"), "No recipe" => VrcSetup.Core.UiText.Text("None"), { } text => text, _ => "" };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value;
}
