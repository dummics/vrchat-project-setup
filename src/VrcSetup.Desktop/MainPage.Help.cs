using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private async void QuickGuideClicked(object sender, RoutedEventArgs args)
        => await ShowDialog(CreateQuickGuideDialog());

    internal ContentDialog CreateQuickGuideDialog()
    {
        var topics = new StackPanel { Spacing = 8, MaxWidth = 580 };
        foreach (var (title, description) in QuickGuideTopics)
        {
            topics.Children.Add(new Expander
            {
                Header = UiText.Text(title), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock { Text = UiText.Text(description), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            });
        }
        return new ContentDialog { Title = UiText.Text("Quick guide"), CloseButtonText = UiText.Text("Close"),
            Content = new ScrollViewer { Content = topics, MaxHeight = 480, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
    }

    internal static readonly (string Title, string Description)[] QuickGuideTopics =
    [
        ("Import an avatar", "Choose a UnityPackage, check the project name, destination and platform, then review the package choices before importing. Keep the original archive available until the import is complete."),
        ("Review suggested packages", "Suggestions help you choose packages; they never install or remove anything by themselves. Check the creator's instructions when a suggestion needs confirmation. A newer available version is not necessarily required by the avatar."),
        ("Pause and continue", "Pause keeps the project and your import request. Wait until the import has stopped. You can continue from the import row or the project warning, including after reopening the app."),
        ("Cancel an import", "Cancel and remove project deletes the project created for that import, including its files, after confirmation. Pause keeps it instead. Existing or unverified projects are preserved; the app explains when automatic removal is unavailable. A pending cancellation has not finished yet."),
        ("Find the original package", "If the source archive was moved, the recovery flow asks you to select it again. Choose the same original UnityPackage and review the proposed work. The app does not make a large automatic copy of your archive."),
        ("Work with Unity open", "Package changes can run with Unity open when supported. Imports that launch Unity and full backup restoration require the editor to be closed. Follow the message for the selected operation; never force-close an editor with unsaved work."),
        ("Resolve an error", "Use the action beside the affected import or project. The message explains what was kept and what to do next. Activity records what happened; Open log and Export diagnostics provide details when you need help.")
    ];
}
