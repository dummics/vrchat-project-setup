using Microsoft.UI.Xaml;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop;

public sealed partial class MainWindow : Window
{
    public MainPage Page { get; }
    private bool closingConfirmed, closePromptPending;
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
            if (closingConfirmed) { page.ViewModel.Dispose(); return; }
            // Closing is synchronous at the native boundary; cancel before awaiting.
            args.Cancel = true;
            if (closePromptPending) return;
            closePromptPending = true;
            try
            {
                if (page.ViewModel.IsBusy || page.ViewModel.HasActiveImports)
                {
                    if (await page.ConfirmAsync("Imports running", "Cancel running operations and preserve recovery information? Close again after they stop.", "Cancel operations"))
                    { page.ViewModel.CancelOperationCommand.Execute(null); page.ViewModel.CancelAllImports(); }
                }
                else if (await page.ViewModel.ConfirmCloseAsync() && !page.ViewModel.IsBusy && !page.ViewModel.HasActiveImports)
                {
                    await page.ViewModel.FinishImportsAsync();
                    closingConfirmed = true;
                    DispatcherQueue.TryEnqueue(() => Close());
                }
            }
            finally { closePromptPending = false; }
        };
    }
}
