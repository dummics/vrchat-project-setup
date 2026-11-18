using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactNativePickerVerification
{
    private const uint OwnerWindow = 4;
    private const uint CommandMessage = 0x0111;
    private const int CancelButton = 2;

    internal static async Task RunAsync(CoreWebView2 core, Window window, VrcSetupApplication engine, string folder, CancellationToken token)
    {
        var owner = new WindowInteropHelper(window).Handle;
        var thread = GetWindowThreadProcessId(owner, out _);
        var archive = ReactImportVerification.MakeArchive(folder);
        var trace = Path.Combine(folder, "picker-progress.txt");
        const string picker = "document.querySelector('.drop')";
        await Program.RequireAsync(core, picker + "?.disabled === false");
        await OpenPickerAsync(picker, null);
        await Program.RequireAsync(core, picker + "?.disabled === false && document.querySelector('.tabs') === null");
        await OpenPickerAsync(picker, archive);
        await Program.RequireAsync(core, """
            document.querySelector('.tabs button')?.textContent === 'Probe.unitypackage'
            && document.querySelector('input[aria-label="Nome progetto"]')?.value === 'Probe'
            """, attempts: 400);

        var originalRoot = (await engine.Configuration.LoadAsync(token)).UnityProjectsRoot;
        var selectedRoot = Path.Combine(folder, "Progetti selezionati");
        Directory.CreateDirectory(selectedRoot);
        var restoreFile = Path.Combine(folder, "ripristino-fixture.json");
        File.Copy(engine.Paths.ConfigFile, restoreFile);
        var sentinel = Path.Combine(originalRoot, "fixture-project.keep");
        await File.WriteAllTextAsync(sentinel, "The native settings operations preserve existing files.", token);
        var assetHash = AtomicFile.Hash(sentinel);
        const string settings = "Array.from(document.querySelectorAll('header button')).find(button => button.textContent === 'Impostazioni')";
        await Program.RequireAsync(core, settings + "?.disabled === false");
        await core.ExecuteScriptAsync(settings + ".click()");
        const string folderPicker = "document.getElementById('unityProjectsRoot')?.parentElement.querySelector('button')";
        await Program.RequireAsync(core, folderPicker + "?.disabled === false");
        await OpenPickerAsync(folderPicker, null);
        const string root = "document.getElementById('unityProjectsRoot')";
        await Program.RequireAsync(core, folderPicker + "?.disabled === false && " + root + "?.value === " + JsonSerializer.Serialize(originalRoot));
        await OpenPickerAsync(folderPicker, selectedRoot);
        await Program.RequireAsync(core, folderPicker + "?.disabled === false && " + root + "?.value === " + JsonSerializer.Serialize(selectedRoot), attempts: 400);
        await core.ExecuteScriptAsync("document.querySelector('.settings button[type=submit]').click()");
        const string footer = "document.querySelector('footer [role=status]')?.textContent";
        await Program.RequireAsync(core, footer + " === 'Impostazioni salvate.'");
        if (!string.Equals((await engine.Configuration.LoadAsync(token)).UnityProjectsRoot, selectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The folder selected with the native picker was not persisted.");
        var changedConfig = AtomicFile.Hash(engine.Paths.ConfigFile);

        await core.ExecuteScriptAsync("document.querySelector('.settings-maintenance summary').click()");
        const string restore = """document.querySelector('[data-maintenance-action="restoreSettings"]')""";
        const string confirm = "document.querySelector('dialog[open] .primary')";
        await core.ExecuteScriptAsync(restore + ".click()");
        await Program.RequireAsync(core, confirm + "?.disabled === false");
        await OpenPickerAsync(confirm, null);
        await Program.RequireAsync(core, restore + "?.disabled === false");
        if (AtomicFile.Hash(engine.Paths.ConfigFile) != changedConfig)
            throw new IOException("Cancelling the native settings file picker changed the configuration.");
        await core.ExecuteScriptAsync(restore + ".click()");
        await Program.RequireAsync(core, confirm + "?.disabled === false");
        await OpenPickerAsync(confirm, restoreFile);
        await Program.RequireAsync(core, footer + " === 'Impostazioni ripristinate.' && " + root + "?.value === " + JsonSerializer.Serialize(originalRoot), attempts: 400);
        var backups = Path.Combine(engine.Paths.Home, "migration");
        if (!string.Equals((await engine.Configuration.LoadAsync(token)).UnityProjectsRoot, originalRoot, StringComparison.OrdinalIgnoreCase) ||
            AtomicFile.Hash(sentinel) != assetHash || !Directory.GetFiles(backups, "config-*.json").Any(file => AtomicFile.Hash(file) == changedConfig))
            throw new IOException("Restoring settings did not preserve the previous configuration and project files.");
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "react-native-picker.json"), new
        {
            Passed = true, NativeDialogCancelledWithoutDraft = true, SelectedArchiveStagedThroughReact = true,
            FolderSelectionPersisted = true, CancelledRestorePreservedConfiguration = true,
            SettingsFileRestored = true, PriorConfigurationAndProjectFilesPreserved = true
        }, token);

        async Task OpenPickerAsync(string trigger, string? selectedPath)
        {
            // UI Automation needs a separate thread when inspecting windows in this process.
            File.AppendAllText(trace, selectedPath is null ? "Requesting cancel.\n" : "Requesting file selection.\n");
            var native = Task.Run(() => CompletePickerAsync(owner, thread, selectedPath, trace, token), token);
            var click = core.ExecuteScriptAsync(trigger + ".click()");
            await native;
            await click;
            File.AppendAllText(trace, "WebView completed the picker request.\n");
        }
    }

    private static async Task CompletePickerAsync(nint owner, uint thread, string? selectedPath, string trace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        nint handle = 0;
        var windowClasses = new HashSet<string>();
        AutomationElement? dialog = null;
        while (watch.Elapsed < TimeSpan.FromSeconds(25))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (handle == 0)
                {
                    EnumThreadWindows(thread, (candidate, _) =>
                    {
                        var className = new StringBuilder(128);
                        GetClassName(candidate, className, className.Capacity);
                        windowClasses.Add(className.ToString());
                        if (GetWindow(candidate, OwnerWindow) != owner || className.ToString() != "#32770") return true;
                        handle = candidate;
                        return false;
                    }, 0);
                    if (handle != 0) File.AppendAllText(trace, "Found the dialog owned by the fixture window.\n");
                }
                if (handle != 0 && selectedPath is null)
                {
                    if (!PostMessage(handle, CommandMessage, CancelButton, 0)) throw new IOException("Could not cancel the hosted picker.");
                    return;
                }
                dialog = handle == 0 ? null : AutomationElement.FromHandle(handle);
                if (dialog is not null)
                {
                    var button = dialog.FindFirst(TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "1")));
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
            catch (ElementNotAvailableException) { handle = 0; }
            await Task.Delay(200, token);
        }
        var controls = dialog?.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        var identifiers = controls is null ? "no dialog" : string.Join(", ", controls.Cast<AutomationElement>()
            .Take(30).Select(control => control.Current.AutomationId + " (" + control.Current.Name + ")"));
        if (handle != 0) PostMessage(handle, CommandMessage, CancelButton, 0);
        throw new IOException($"Could not operate the hosted file picker. Windows: {string.Join(", ", windowClasses)}. Controls: {identifiers}");
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int length);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nint parameter, nint data);
}
