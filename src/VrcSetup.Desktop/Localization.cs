using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class Loc : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => UiText.Text(Key);
}
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => UiText.Text(value?.ToString());
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
