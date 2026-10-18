using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop;

// A compact default choice with explicit access to preview and exact versions.
public sealed class VersionPickerDialog
{
    public ContentDialog Dialog { get; }
    public ComboBox Choice { get; }
    public Expander Advanced { get; }
    public TextBox Exact { get; }
    public TextBox Search { get; }
    public CheckBox Previews { get; }
    public string? Result => Exact.Text.Trim() is { Length: > 0 } exact ? exact : (Choice.SelectedItem as VersionChoice)?.Value;

    public VersionPickerDialog(string package, string[] versions, string current)
    {
        Choice = new() { Header = UiText.Text("Version"), DisplayMemberPath = nameof(VersionChoice.Label), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 360, MaxDropDownHeight = 280 };
        Search = new() { PlaceholderText = UiText.Text("Search versions") };
        Previews = new() { Content = UiText.Text("Include preview releases"), IsChecked = current.Contains('-') && current != "latest" };
        Exact = new() { Header = UiText.Text("Use an exact version"), PlaceholderText = "e.g. 1.2.3" };
        var advancedContent = new StackPanel { Spacing = 14 }; advancedContent.Children.Add(Search); advancedContent.Children.Add(Previews); advancedContent.Children.Add(Exact);
        Advanced = new() { Header = UiText.Text("Advanced"), Content = advancedContent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 20 }; content.Children.Add(Choice); content.Children.Add(Advanced);
        Dialog = new() { Title = PackageCatalog.FriendlyName(package), Content = content, PrimaryButtonText = UiText.Text("Apply"), CloseButtonText = UiText.Text("Cancel"), DefaultButton = ContentDialogButton.Primary };
        var selected = current;
        void Refresh()
        {
            var releases = versions.Append(current).Where(x => x != "latest" && !string.IsNullOrWhiteSpace(x));
            var rows = new[] { new VersionChoice("latest", "Automatic (latest)") }.Concat(SemanticVersion.Sort(releases, Previews.IsChecked == true).Select(x => new VersionChoice(x, x)))
                .Where(x => Search.Text.Length == 0 || x.Label.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            Choice.ItemsSource = rows; Choice.SelectedItem = rows.FirstOrDefault(x => x.Value == selected);
            Dialog.IsPrimaryButtonEnabled = Result is not null;
        }
        Choice.SelectionChanged += (_, _) => { if (Choice.SelectedItem is VersionChoice choice) selected = choice.Value; Dialog.IsPrimaryButtonEnabled = Result is not null; };
        Search.TextChanged += (_, _) => Refresh(); Previews.Checked += (_, _) => Refresh(); Previews.Unchecked += (_, _) => Refresh();
        Exact.TextChanged += (_, _) => { Choice.IsEnabled = string.IsNullOrWhiteSpace(Exact.Text); Dialog.IsPrimaryButtonEnabled = Result is not null; };
        Refresh();
    }
    public sealed record VersionChoice(string Value, string Label);
}
