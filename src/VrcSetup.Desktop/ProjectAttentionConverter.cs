using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

public sealed class ProjectAttentionConverter : IValueConverter
{
    public static string Description(ProjectSnapshot project)
    {
        var message = UiText.Text(project.IncompleteStep == "vpm" ? "The last package change did not finish. Check the packages and recent activity." : project.Incomplete ? "An operation on this project did not finish. Check its packages and recent activity." : "This project needs attention. Check its packages and recent activity.");
        if (project.MissingPackages.Length > 0)
            message += "\n" + string.Format(UiText.Text(project.PackageRetrySafe ? "Not installed: {0}. Try again to restore the missing packages." : "Packages not found: {0}"), string.Join(", ", project.MissingPackages.Select(VrcSetup.Infrastructure.PackageCatalog.FriendlyName)));
        if (project.MissingPackages.Contains("com.poiyomi.pro", StringComparer.OrdinalIgnoreCase))
            message += "\n" + UiText.Text("Poiyomi Pro also needs Patreon sign-in in Unity to download the shader.");
        return message;
    }
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ProjectSnapshot project) return Visibility.Collapsed;
        if (parameter?.ToString() == "Label") return project.Incomplete ? "Setup unfinished" : project.Status;
        return project.Incomplete || project.Status != "Ready" || project.MissingPackages.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
