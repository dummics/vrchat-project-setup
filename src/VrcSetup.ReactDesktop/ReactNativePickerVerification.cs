using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactNativePickerVerification
{
    private const uint OwnerWindow = 4;
    private const uint CommandMessage = 0x0111;
    private const int AcceptButton = 1;
    private const int CancelButton = 2;
    private const int YesButton = 6;
    private const int NoButton = 7;
    private const string RecoveryId = "hosted-source-relocation";
    private const string RecoveryProjectName = "Source relocation";

    internal static async Task SeedRecoveryAsync(VrcSetupApplication engine, string folder, AppConfiguration configuration, CancellationToken token)
    {
        var sandbox = Directory.CreateDirectory(Path.Combine(folder, "relocation")).FullName;
        var moved = Path.Combine(sandbox, "MOVED.unitypackage");
        File.Move(ReactImportVerification.MakeArchive(sandbox), moved);
        // A separate archive is structurally valid but has different asset GUIDs and bytes.
        ReactImportVerification.MakeArchive(sandbox);
        var original = Path.Combine(sandbox, "ORIGINAL.unitypackage");
        var editor = Path.Combine(sandbox, "Unity.exe");
        // The verifier stops at review, so this file is never executed.
        await File.WriteAllBytesAsync(editor, [], token);
        var project = Path.Combine(configuration.UnityProjectsRoot, RecoveryProjectName);
        var manifest = await MakeProjectAsync(project, token);
        var policy = new Dictionary<string, string> { [ProjectRules.BaseSdk] = "3.10.6", [ProjectRules.Resolver] = "0.1.29" };
        await AtomicFile.WriteJsonAsync(manifest, new { dependencies = policy }, token);
        await AtomicFile.WriteJsonAsync(Path.Combine(project, ".vrcsetup", "state.json"), new SetupState { UnityPackagePath = original }, token);
        var source = new CreationRequest(original, RecoveryProjectName, project, editor, "StandaloneWindows64", ["StandaloneWindows64"], [])
            { SourceIdentity = await PackageSourceIdentity.ReadAsync(moved, token) };
        var plan = new OperationPlan(RecoveryId, DateTime.UtcNow, [new()
        {
            Path = project, Name = RecoveryProjectName, Creation = source, DesiredPolicy = policy, Changes = [],
            ExtraImports = [original], ExpectedFingerprint = ""
        }]);
        var journal = Path.Combine(engine.Paths.Home, "imports.json");
        var jobs = await AtomicFile.ReadJsonAsync<ImportJob[]>(journal, token) ?? [];
        if (jobs.Any(job => job.Id == RecoveryId)) throw new IOException("The relocation fixture must be fresh.");
        await AtomicFile.WriteJsonAsync(journal, jobs.Append(new ImportJob(RecoveryId, plan, source, "Paused", "Paused", 30, "", "")).ToArray(), token);
    }

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

        var original = await engine.Configuration.LoadAsync(token);
        var originalRoot = original.UnityProjectsRoot;
        const string resume = "Array.from(document.querySelectorAll('#job-" + RecoveryId + " button')).find(button => button.textContent === 'Riprendi importazione')";
        const string recoveryReview = """document.querySelector('section[aria-label="Riprendi importazione"]')""";
        const string footer = "document.querySelector('footer [role=status]')?.textContent";
        await Program.RequireAsync(core, resume + "?.disabled === false");
        await OpenPickerAsync(resume, null);
        await Program.RequireAsync(core, resume + "?.disabled === false && " + recoveryReview + " === null");
        await OpenPickerAsync(resume, Path.Combine(folder, "relocation", "Probe.unitypackage"));
        await Program.RequireAsync(core, footer + " === 'Questo pacchetto è diverso dall’originale. Scegli il file UnityPackage originale per continuare.' && " + recoveryReview + " === null", attempts: 400);
        await OpenPickerAsync(resume, Path.Combine(folder, "relocation", "MOVED.unitypackage"));
        await Program.RequireAsync(core, recoveryReview + "?.textContent.includes('Continua nel progetto esistente') && " +
            recoveryReview + "?.querySelector('.path')?.textContent === " + JsonSerializer.Serialize(Path.Combine(originalRoot, RecoveryProjectName)), attempts: 400);
        await core.ExecuteScriptAsync(recoveryReview + ".querySelector('button').click()");
        if (engine.Imports.Snapshot().Single(job => job.Id == RecoveryId) is not { State: "Paused", Active: false })
            throw new IOException("Reviewing the relocated archive changed the paused job.");

        var selectedRoot = Path.Combine(folder, "Progetti selezionati");
        Directory.CreateDirectory(selectedRoot);
        var extras = Directory.CreateDirectory(Path.Combine(folder, "Pacchetti aggiuntivi")).FullName;
        // Selecting this inert file exercises path validation without launching Unity.
        var editor = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "Editor fixture")).FullName, "Unity.exe");
        await File.WriteAllBytesAsync(editor, [], token);
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
        await SelectSettingAsync("unityEditorPath", original.UnityEditorPath, editor);
        await SelectSettingAsync("unityPackagesFolder", original.UnityPackagesFolder ?? "", extras);
        await core.ExecuteScriptAsync("document.querySelector('.settings button[type=submit]').click()");
        await Program.RequireAsync(core, footer + " === 'Impostazioni salvate.'");
        var saved = await engine.Configuration.LoadAsync(token);
        if (!string.Equals(saved.UnityProjectsRoot, selectedRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(saved.UnityEditorPath, editor, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(saved.UnityPackagesFolder, extras, StringComparison.OrdinalIgnoreCase))
            throw new IOException("One or more paths selected with the native pickers were not persisted.");
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
        var restored = await engine.Configuration.LoadAsync(token);
        if (restored.UnityProjectsRoot != originalRoot || restored.UnityEditorPath != original.UnityEditorPath ||
            restored.UnityPackagesFolder != original.UnityPackagesFolder ||
            AtomicFile.Hash(sentinel) != assetHash || !Directory.GetFiles(backups, "config-*.json").Any(file => AtomicFile.Hash(file) == changedConfig))
            throw new IOException("Restoring settings did not preserve the previous configuration and project files.");

        const string fixturePreset = "Portable fixture";
        var shareSettings = restored.Copy();
        shareSettings.PackagePresets[fixturePreset] = new() { ["com.vrchat.avatars"] = "3.10.6" };
        shareSettings = await engine.Configuration.MergeAsync(restored, shareSettings, token);
        const string back = "Array.from(document.querySelectorAll('header button')).find(button => button.textContent === '← Importa')";
        await Program.RequireAsync(core, back + "?.disabled === false");
        await core.ExecuteScriptAsync(back + ".click()");
        const string presets = "Array.from(document.querySelectorAll('header button')).find(button => button.textContent === 'Preset')";
        const string exportPreset = "Array.from(document.querySelectorAll('.settings button')).find(button => button.textContent === 'Esporta preset')";
        const string importPreset = "Array.from(document.querySelectorAll('.settings button')).find(button => button.textContent === 'Importa preset')";
        await Program.RequireAsync(core, presets + "?.disabled === false");
        await core.ExecuteScriptAsync(presets + ".click()");
        const string presetChoice = "document.querySelector('select[aria-label=Preset]')";
        await Program.RequireAsync(core, presetChoice + "?.disabled === false");
        await core.ExecuteScriptAsync($$"""
            (() => {
              const preset = {{presetChoice}};
              preset.value = {{JsonSerializer.Serialize(fixturePreset)}};
              preset.dispatchEvent(new Event('change', { bubbles: true }));
            })()
            """);
        await Program.RequireAsync(core, exportPreset + "?.disabled === false");
        var presetFile = Path.Combine(folder, "preset condiviso.json");
        await OpenPickerAsync(exportPreset, null);
        await Program.RequireAsync(core, exportPreset + "?.disabled === false");
        await OpenPickerAsync(exportPreset, presetFile);
        await Program.RequireAsync(core, exportPreset + "?.disabled === false");
        if (!File.Exists(presetFile))
            throw new IOException("The preset export did not write the selected file. Status: " + await core.ExecuteScriptAsync(footer));
        var portable = PortablePreset.Read(await File.ReadAllTextAsync(presetFile, token));
        if (portable.Name != fixturePreset || portable.Packages.Count != 1 || portable.Packages.GetValueOrDefault("com.vrchat.avatars") != "3.10.6")
            throw new IOException("The exported preset did not preserve its name and version pin.");
        var presetName = portable.AvailableName(shareSettings.PackagePresets.Keys);
        var restoredHash = AtomicFile.Hash(engine.Paths.ConfigFile);
        await OpenPickerAsync(importPreset, presetFile);
        await Task.Run(() => CompletePresetConfirmationAsync(owner, thread, NoButton, token), token);
        await Program.RequireAsync(core, importPreset + "?.disabled === false");
        if (AtomicFile.Hash(engine.Paths.ConfigFile) != restoredHash)
            throw new IOException("Declining the preset confirmation changed the configuration.");
        await OpenPickerAsync(importPreset, presetFile);
        await Task.Run(() => CompletePresetConfirmationAsync(owner, thread, YesButton, token), token);
        await Program.RequireAsync(core, "document.querySelector('select[aria-label=Preset]')?.value === " + JsonSerializer.Serialize(presetName), attempts: 400);
        var imported = await engine.Configuration.LoadAsync(token);
        if (!imported.PackagePresets.TryGetValue(presetName, out var packages) ||
            packages.Count != portable.Packages.Count ||
            portable.Packages.Any(package => !packages.TryGetValue(package.Key, out var version) || version != package.Value))
            throw new IOException("The imported preset did not preserve the exported package choices.");

        await MakeProjectAsync(Path.Combine(originalRoot, "AA sort fixture"), token);
        await MakeProjectAsync(Path.Combine(originalRoot, "ZZ sort fixture"), token);
        var external = Path.Combine(folder, "Progetto esterno");
        var externalManifest = await MakeProjectAsync(external, token);
        var externalHash = AtomicFile.Hash(externalManifest);
        var ordered = imported.Copy();
        ordered.ProjectLibrarySort = "name-reverse";
        await engine.Configuration.MergeAsync(imported, ordered, token);

        await core.ExecuteScriptAsync(back + ".click()");
        const string projects = "Array.from(document.querySelectorAll('header button')).find(button => button.textContent === 'Progetti')";
        const string pickProject = "Array.from(document.querySelectorAll('.project-tools button')).find(button => button.textContent === 'Apri progetto esistente')";
        const string rows = "document.querySelectorAll('.project-table tbody th')";
        await Program.RequireAsync(core, projects + "?.disabled === false");
        await core.ExecuteScriptAsync(projects + ".click()");
        await Program.RequireAsync(core, rows + "[0]?.textContent.startsWith('ZZ sort fixture') && document.querySelector('th[aria-sort=descending]')?.textContent.startsWith('Nome')");
        await core.ExecuteScriptAsync("document.querySelector('th[aria-sort=descending] button').click()");
        await Program.RequireAsync(core, rows + "[0]?.textContent.startsWith('AA sort fixture') && document.querySelector('th[aria-sort]') === null");
        if ((await engine.Configuration.LoadAsync(token)).ProjectLibrarySort != "default")
            throw new IOException("Changing the Projects sort did not persist the selected order.");
        await OpenPickerAsync(pickProject, null);
        await Program.RequireAsync(core, pickProject + "?.disabled === false");
        await OpenPickerAsync(pickProject, extras);
        await Program.RequireAsync(core, footer + " === 'Scegli la cartella del progetto Unity, quella che contiene Assets e Packages.'");
        await OpenPickerAsync(pickProject, external);
        await Program.RequireAsync(core, "document.querySelector('h1')?.textContent === 'Progetto esterno' && document.querySelector('.project-table') === null", attempts: 400);
        if (AtomicFile.Hash(externalManifest) != externalHash)
            throw new IOException("Opening an existing project's packages modified its manifest.");

        await core.ExecuteScriptAsync("document.querySelector('header button').click()");
        await Program.RequireAsync(core, back + "?.disabled === false");
        await core.ExecuteScriptAsync(back + ".click()");
        const string activity = "Array.from(document.querySelectorAll('header button')).find(button => button.textContent === 'Attività')";
        const string diagnostics = "Array.from(document.querySelectorAll('.activity button')).find(button => button.textContent === 'Esporta diagnostica')";
        var diagnosticsPath = Path.Combine(folder, "diagnostica fixture.zip");
        await Program.RequireAsync(core, activity + "?.disabled === false");
        await core.ExecuteScriptAsync(activity + ".click()");
        await Program.RequireAsync(core, diagnostics + "?.disabled === false");
        await OpenPickerAsync(diagnostics, null);
        await Program.RequireAsync(core, diagnostics + "?.disabled === false");
        await OpenPickerAsync(diagnostics, diagnosticsPath);
        await Program.RequireAsync(core, footer + " === 'Diagnostica esportata.'", attempts: 400);
        using var zip = ZipFile.OpenRead(diagnosticsPath);
        if (zip.GetEntry("VRC-Project-Setup.txt") is null || zip.GetEntry("Archive-contents.txt") is null)
            throw new IOException("The native diagnostic export did not produce a valid support archive.");

        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "react-native-picker.json"), new
        {
            Passed = true, NativeDialogCancelledWithoutDraft = true, SelectedArchiveStagedThroughReact = true,
            ProjectEditorAndExtrasPathsPersisted = true, CancelledRestorePreservedConfiguration = true,
            SettingsFileRestored = true, PriorConfigurationAndProjectFilesPreserved = true,
            PortablePresetRoundTrip = true, DeclinedPresetPreservedConfiguration = true,
            ProjectSortReadAndPersisted = true, ExistingProjectSelectedWithoutChanges = true,
            InvalidFolderShowsAnAction = true, DiagnosticsExported = true,
            RelocatedSourceAcceptedForReview = true, DifferentArchiveRejected = true, PausedJobPreserved = true
        }, token);

        async Task SelectSettingAsync(string id, string previousPath, string path)
        {
            var input = "document.getElementById(" + JsonSerializer.Serialize(id) + ")";
            var button = input + "?.parentElement.querySelector('button')";
            await Program.RequireAsync(core, button + "?.disabled === false");
            await OpenPickerAsync(button, null);
            await Program.RequireAsync(core, button + "?.disabled === false && " + input + "?.value === " + JsonSerializer.Serialize(previousPath));
            await OpenPickerAsync(button, path);
            await Program.RequireAsync(core, button + "?.disabled === false && " + input + "?.value === " + JsonSerializer.Serialize(path), attempts: 400);
        }

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

    private static async Task<string> MakeProjectAsync(string path, CancellationToken token)
    {
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        Directory.CreateDirectory(Path.Combine(path, "Packages"));
        Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1\n", token);
        await File.WriteAllTextAsync(Path.Combine(path, "Packages", "manifest.json"), """{"dependencies":{}}""", token);
        var manifest = Path.Combine(path, "Packages", "vpm-manifest.json");
        await File.WriteAllTextAsync(manifest, """{"dependencies":{},"locked":{}}""", token);
        return manifest;
    }

    private static async Task CompletePresetConfirmationAsync(nint owner, uint thread, int button, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            token.ThrowIfCancellationRequested();
            nint dialog = 0;
            nint control = 0;
            EnumThreadWindows(thread, (candidate, _) =>
            {
                if (GetWindow(candidate, OwnerWindow) != owner) return true;
                control = GetDlgItem(candidate, button);
                if (control == 0) return true;
                dialog = candidate;
                return false;
            }, 0);
            if (dialog != 0 && PostMessage(dialog, CommandMessage, button, control)) return;
            await Task.Delay(100, token);
        }
        throw new IOException("The native preset confirmation did not accept the fixture's response.");
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
                    var host = dialog.FindFirst(TreeScope.Descendants,
                        new OrCondition(
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "FileNameControlHost"),
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "1148")));
                    var edits = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                    var edit = host?.Current.ControlType == ControlType.Edit ? host : host?.FindFirst(TreeScope.Descendants, edits);
                    edit ??= dialog.FindAll(TreeScope.Descendants, edits).Cast<AutomationElement>().FirstOrDefault(control =>
                        control.Current.AutomationId == "1001" ||
                        control.Current.Name.StartsWith("Folder", StringComparison.OrdinalIgnoreCase));
                    if (edit?.TryGetCurrentPattern(ValuePattern.Pattern, out var value) != true)
                    {
                        await Task.Delay(200, token);
                        continue;
                    }
                    var button = dialog.FindFirst(TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                            new OrCondition(
                                new PropertyCondition(AutomationElement.AutomationIdProperty, "1"),
                                new PropertyCondition(AutomationElement.NameProperty, "Select Folder"))));
                    ((ValuePattern)value).SetValue(selectedPath ?? throw new IOException("The fixture did not supply a path."));
                    // The shell's Save dialog updates the proposed name after an edit notification.
                    await Task.Delay(200, token);
                    File.AppendAllText(trace, $"Invoking the hosted picker button ({button?.Current.Name}) with value {((ValuePattern)value).Current.Value}.\n");
                    if (button?.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke) == true)
                        ((InvokePattern)invoke).Invoke();
                    else if (!PostMessage(handle, CommandMessage, AcceptButton, GetDlgItem(handle, AcceptButton)))
                        throw new IOException("Could not confirm the hosted picker.");
                    File.AppendAllText(trace, "Hosted picker button returned.\n");
                    return;
                }
            }
            catch (ElementNotAvailableException) { handle = 0; }
            await Task.Delay(200, token);
        }
        var controls = dialog?.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        var identifiers = controls is null ? "no dialog" : string.Join(", ", controls.Cast<AutomationElement>()
            .Where(control => control.Current.ControlType == ControlType.Edit ||
                control.Current.AutomationId is "1" or "2" ||
                control.Current.Name.Contains("Select", StringComparison.OrdinalIgnoreCase))
            .Select(control => control.Current.AutomationId + " (" + control.Current.Name + ")"));
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

    [DllImport("user32.dll")]
    private static extern nint GetDlgItem(nint window, int controlId);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint window, uint message, nint parameter, nint data);
}
