using Microsoft.UI.Xaml;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop;

public sealed partial class MainWindow : Window
{
    public MainPage Page { get; }
    public MainWindow(VrcSetupApplication? application = null)
    {
        InitializeComponent(); Title = "VRChat Project Setup";
        ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar);
        AppTitleBar.Title = "VRChat Project Setup";
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        var page = Page = new MainPage(this, application ?? new VrcSetupApplication()); RootFrame.Content = page;
        AppWindow.Closing += async (_, args) =>
        {
            if (page.ViewModel.IsBusy)
            {
                args.Cancel = true;
                if (await page.ConfirmAsync("Operation running", "Cancel the running operation and preserve recovery information?", "Cancel operation")) page.ViewModel.CancelOperationCommand.Execute(null);
            }
            else page.ViewModel.Dispose();
        };
    }
}
