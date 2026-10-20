using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private static void RevealLog(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/select," + Path.GetFullPath(path)); Process.Start(start);
    }
    private bool exportingLogs;
    [RelayCommand] private async Task ExportLogs()
    {
        // Diagnostics are read-only and must remain available while another action is busy.
        if (exportingLogs) return;
        exportingLogs = true;
        try
        {
            if (await ui.SaveFileAsync(SupportLogs.SuggestedName, ".zip") is not { } path) return;
            await Task.Run(() => SupportLogs.ExportAsync(app.Paths, path, lifetime.Token));
            RevealLog(path); Status = "Logs ready to share";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            app.Diagnostics.Write("Export diagnostics", error.Message, error);
            ErrorMessage = "Could not export diagnostics. Choose another folder and try again.";
        }
        finally { exportingLogs = false; }
    }
}
