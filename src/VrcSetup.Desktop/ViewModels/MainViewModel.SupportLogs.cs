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
    [RelayCommand] private async Task ExportLogs() => await Guard(async () =>
    {
        if (await ui.SaveFileAsync(SupportLogs.SuggestedName, ".zip") is not { } path) return;
        await SupportLogs.ExportAsync(app.Paths, path, lifetime.Token);
        RevealLog(path); Status = "Logs ready to share";
    });
}
