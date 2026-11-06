using CommunityToolkit.Mvvm.ComponentModel;
using VrcSetup.Core;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPrepareImportReview)), NotifyCanExecuteChangedFor(nameof(CreateProjectCommand)),
        NotifyPropertyChangedFor(nameof(CanCreateConfiguredProject)), NotifyCanExecuteChangedFor(nameof(CreateConfiguredProjectCommand))]
    public partial bool IsClosingApp { get; set; }

    public async Task<bool> PrepareToCloseAsync()
    {
        if (IsClosingApp || IsApplyingPackages || IsInstallingUpdate) return false;
        IsClosingApp = true;
        noticeSequence++;
        ApplicationNotice = "";
        var ready = false;
        var acquired = false;
        try
        {
            // Reserve new actions, then let the current action release its own state.
            CancelOperation();
            inspectionRequest?.Cancel();
            using (var stopWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                stopWait.CancelAfter(TimeSpan.FromSeconds(20));
                await actionGate.WaitAsync(stopWait.Token);
                acquired = true;
                await app.Imports.PauseAllAsync(stopWait.Token);
                while (HasActiveImports || IsBusy)
                    await Task.Delay(100, stopWait.Token);
            }
            // Dirty choices are resolved only after imports have actually stopped.
            // These internal save/apply paths do not acquire actionGate again.
            if (!await ConfirmCloseAsync()) return false;
            using var finishWait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            finishWait.CancelAfter(TimeSpan.FromSeconds(20));
            await app.Imports.ShutdownAsync(finishWait.Token);
            ready = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            ApplicationNotice = UiText.Text("Stopping is taking longer. The app will stay open; your import is preserved.");
            return false;
        }
        catch (Exception error)
        {
            app.Diagnostics.Write("Closing", error.Message, error);
            ErrorMessage = UserErrors.Message(error);
            ApplicationNotice = UiText.Text("Could not finish closing. The app will stay open.");
            return false;
        }
        finally
        {
            if (acquired) actionGate.Release();
            if (!ready) IsClosingApp = false;
        }
    }
}
