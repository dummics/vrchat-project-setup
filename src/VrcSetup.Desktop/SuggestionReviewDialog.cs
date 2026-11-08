using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;
using VrcSetup_Desktop.ViewModels;

namespace VrcSetup_Desktop;

// This dialog edits a temporary selection; the caller commits only on Apply.
internal sealed class SuggestionReviewDialog
{
    public ContentDialog Dialog { get; }
    private readonly Dictionary<string, CheckBox> choices = new(StringComparer.OrdinalIgnoreCase);
    public SuggestionReviewDialog(SuggestionReviewSnapshot snapshot, Func<string, Task<string[]>>? versions = null)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
        foreach (var item in snapshot.Items)
        {
            var details = new StackPanel { Spacing = 2 };
            details.Children.Add(new TextBlock { Text = item.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var explanation = item.Confidence switch
            {
                DependencyConfidence.Included => "Also in this file · may still be needed",
                DependencyConfidence.Possible => "May be needed",
                _ => "Suggested for this avatar"
            };
            details.Children.Add(new TextBlock { Text = UiText.Text(explanation), Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (item.RequiredVersion is { } requirement)
            {
                var selectedVersion = item.SelectedVersion == "latest" ? UiText.Text("Latest") + " · " + UiText.Text(item.AvailableVersion) : item.SelectedVersion ?? "";
                var versionInfo = new TextBlock { Text = string.Format(UiText.Text("Selected: {0} · File requests: {1}"), selectedVersion, requirement), Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                details.Children.Add(versionInfo);
                if (item.SelectedVersion == "latest" && versions is not null) _ = UpdateSelectedVersionAsync(versionInfo, item.Id, requirement, versions);
                details.Children.Add(new TextBlock { Text = UiText.Text("Check the version in package settings"), Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }
            if (!item.Selected && item.Version is null)
                details.Children.Add(new TextBlock { Text = UiText.Text("Choose a version in package settings"), Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            else if (!item.Selected)
            {
                var version = new TextBlock { Text = item.Version == "latest" ? UiText.Text("Latest") + " · " + UiText.Text(item.AvailableVersion) : item.Version, Opacity = 0.7, FontSize = 12 };
                details.Children.Add(version);
                if (item.Version == "latest" && versions is not null) _ = UpdateVersionAsync(version, item.Id, versions);
            }
            if (item.RequiredVersion is not null || !item.Selected && item.Version is null)
            {
                details.Margin = new Thickness(28, 0, 0, 0);
                panel.Children.Add(details);
            }
            else
            {
                var choice = new CheckBox { Content = details, IsChecked = item.Selected, HorizontalAlignment = HorizontalAlignment.Stretch };
                choices.Add(item.Id, choice); panel.Children.Add(choice);
            }
        }
        if (snapshot.Items.Length == 0) panel.Children.Add(new TextBlock { Text = UiText.Text("No packages to review."), TextWrapping = TextWrapping.Wrap });
        Dialog = new ContentDialog
        {
            Title = UiText.Text("Review suggested packages"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = choices.Count > 0 ? UiText.Text("Apply choices") : "",
            CloseButtonText = UiText.Text(choices.Count > 0 ? "Cancel" : "Close"), DefaultButton = ContentDialogButton.Close
        };
    }
    public Dictionary<string, bool> Result() => choices.ToDictionary(x => x.Key, x => x.Value.IsChecked == true, StringComparer.OrdinalIgnoreCase);

    private static async Task UpdateVersionAsync(TextBlock target, string id, Func<string, Task<string[]>> versions)
    {
        var display = "Version unavailable";
        try
        {
            var available = await versions(id);
            display = SemanticVersion.Sort(available, false).FirstOrDefault() ?? SemanticVersion.Sort(available).FirstOrDefault() ?? display;
        }
        catch (Exception) { /* A preview failure never changes the package choice. */ }
        target.Text = UiText.Text("Latest") + " · " + UiText.Text(display);
    }
    private static async Task UpdateSelectedVersionAsync(TextBlock target, string id, string requirement, Func<string, Task<string[]>> versions)
    {
        var display = "Version unavailable";
        try
        {
            var available = await versions(id);
            display = SemanticVersion.Sort(available, false).FirstOrDefault() ?? SemanticVersion.Sort(available).FirstOrDefault() ?? display;
        }
        catch (Exception) { /* A preview failure never changes the package choice. */ }
        target.Text = string.Format(UiText.Text("Selected: {0} · File requests: {1}"), UiText.Text("Latest") + " · " + UiText.Text(display), requirement);
    }
}
