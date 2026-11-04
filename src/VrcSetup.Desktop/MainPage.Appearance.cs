namespace VrcSetup_Desktop;

public sealed partial class MainPage
{
    private bool appearanceUpdateQueued;

    private void QueueAppearanceUpdate()
    {
        if (!initialized || appearanceUpdateQueued) return;
        appearanceUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            appearanceUpdateQueued = false;
            ApplyAppearanceResources();
        })) appearanceUpdateQueued = false;
    }

    private void ApplyAppearanceResources()
    {
        if (ViewModel.IsLoadingSettings) return;
        try { AppearanceResources.Apply(ViewModel.CanvasColor, ViewModel.SurfaceColor, ViewModel.AccentColor); }
        catch (Exception error)
        {
            ViewModel.Application.Diagnostics.Write("Appearance resources", error.Message, error);
            ViewModel.ErrorNeedsDiagnostics = true;
            ViewModel.ErrorMessage = VrcSetup.Core.UserErrors.Message(error);
        }
    }
}
