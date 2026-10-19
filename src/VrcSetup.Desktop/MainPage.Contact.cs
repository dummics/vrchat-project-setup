using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;
using Windows.ApplicationModel.DataTransfer;

namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    internal ContentDialog CreateContactDialog()
    {
        var content = new StackPanel { Spacing = 20, MinWidth = 280 };
        foreach (var (label, value, action) in new[] { ("Email", "contact@dummics.com", "Copy email"), ("Discord", "dummics", "Copy Discord username") })
        {
            var section = new StackPanel { Spacing = 6 };
            section.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            section.Children.Add(new TextBlock { Text = value, IsTextSelectionEnabled = true });
            var copy = new Button { Content = UiText.Text(action), Tag = value };
            copy.Click += (_, _) =>
            {
                try
                {
                    var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data);
                    copy.Content = UiText.Text("Copied");
                }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
                { ViewModel.ErrorMessage = UiText.Text("Could not copy. Select and copy the contact instead."); }
            };
            section.Children.Add(copy); content.Children.Add(section);
        }
        return new ContentDialog { Title = UiText.Text("Contact"), Content = content, CloseButtonText = UiText.Text("Close") };
    }
    public async Task ShowContactAsync()
    { if (!SuppressDialogs) await ShowDialog(CreateContactDialog()); }
}
