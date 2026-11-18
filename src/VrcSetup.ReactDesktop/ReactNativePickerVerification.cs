using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactNativePickerVerification
{
    internal static async Task RunAsync(CoreWebView2 core, string folder, CancellationToken token)
    {
        var archive = ReactImportVerification.MakeArchive(folder);
        var trace = Path.Combine(folder, "picker-progress.txt");
        const string picker = "document.querySelector('.drop')";
        await Program.RequireAsync(core, picker + "?.disabled === false");
        await OpenPickerAsync(null);
        await Program.RequireAsync(core, picker + "?.disabled === false && document.querySelector('.tabs') === null");
        await OpenPickerAsync(archive);
        await Program.RequireAsync(core, """
            document.querySelector('.tabs button')?.textContent === 'Probe.unitypackage'
            && document.querySelector('input[aria-label="Nome progetto"]')?.value === 'Probe'
            """, attempts: 400);
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "react-native-picker.json"), new
        {
            Passed = true, NativeDialogCancelledWithoutDraft = true, SelectedArchiveStagedThroughReact = true
        }, token);

        async Task OpenPickerAsync(string? selectedPath)
        {
            // UI Automation needs a separate thread when inspecting windows in this process.
            File.AppendAllText(trace, selectedPath is null ? "Requesting cancel.\n" : "Requesting file selection.\n");
            var native = Task.Run(() => CompletePickerAsync(selectedPath, trace, token), token);
            var click = core.ExecuteScriptAsync(picker + ".click()");
            await native;
            await click;
            File.AppendAllText(trace, "WebView completed the picker request.\n");
        }
    }

    private static async Task CompletePickerAsync(string? selectedPath, string trace, CancellationToken token)
    {
        var ownDialog = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, Environment.ProcessId),
            new PropertyCondition(AutomationElement.ClassNameProperty, "#32770"));
        var watch = Stopwatch.StartNew();
        AutomationElement? dialog = null;
        while (watch.Elapsed < TimeSpan.FromSeconds(25))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                dialog = AutomationElement.RootElement.FindFirst(TreeScope.Children, ownDialog);
                if (dialog is not null)
                {
                    var button = dialog.FindFirst(TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                            new PropertyCondition(AutomationElement.AutomationIdProperty, selectedPath is null ? "2" : "1")));
                    if (button?.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke) == true)
                    {
                        if (selectedPath is not null)
                        {
                            var host = dialog.FindFirst(TreeScope.Descendants,
                                new OrCondition(
                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "FileNameControlHost"),
                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1148")));
                            var edit = host?.Current.ControlType == ControlType.Edit ? host : host?.FindFirst(TreeScope.Descendants,
                                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                            if (edit?.TryGetCurrentPattern(ValuePattern.Pattern, out var value) != true)
                            {
                                await Task.Delay(200, token);
                                continue;
                            }
                            ((ValuePattern)value).SetValue(selectedPath);
                        }
                        File.AppendAllText(trace, "Invoking the hosted picker button.\n");
                        ((InvokePattern)invoke).Invoke();
                        File.AppendAllText(trace, "Hosted picker button returned.\n");
                        return;
                    }
                }
            }
            catch (ElementNotAvailableException) { /* The shell is still creating the picker controls. */ }
            await Task.Delay(200, token);
        }
        var controls = dialog?.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        var identifiers = controls is null ? "no dialog" : string.Join(", ", controls.Cast<AutomationElement>()
            .Take(30).Select(control => control.Current.AutomationId + " (" + control.Current.Name + ")"));
        throw new IOException("Could not operate the hosted file picker. Controls: " + identifiers);
    }
}
