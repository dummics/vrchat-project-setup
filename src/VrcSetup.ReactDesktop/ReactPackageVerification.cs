using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactPackageVerification
{
    private const string Package = "vrchat.blackstartx.gesture-manager";
    private const string Older = "3.9.7";
    private const string Newer = "3.9.9";

    internal static async Task VerifyBackupAsync(CoreWebView2 core, VrcSetupApplication engine, string fixture, CancellationToken token)
    {
        var project = Path.Combine(fixture, "candidate-projects", "Probe");
        var asset = Path.Combine(project, "Assets", "ReactProbe.txt");
        var assetHash = AtomicFile.Hash(asset);
        var current = await engine.Projects.ReadAsync(project, token);
        if (assetHash == "missing" || PackagePresence.Contains(current, Package) || engine.Projects.IsBusy(project) ||
            !File.Exists(Path.Combine(fixture, "react-vpm-edits-open.json")))
            throw new IOException("Use the closed VPM fixture after package removal to verify backup recovery.");

        var addedAsset = Path.Combine(project, "Assets", "AfterVpmChange.txt");
        ProjectRules.EnsureContained(project, addedAsset);
        const string change = "Keep this edit in the recovery copy.";
        await using (var stream = new FileStream(addedAsset, FileMode.CreateNew))
        await using (var writer = new StreamWriter(stream))
            await writer.WriteAsync(change.AsMemory(), token);

        await ClickAsync("Array.from(document.querySelectorAll('header button')).find(x => x.textContent === 'Progetti')");
        await OpenReviewAsync();
        await ClickAsync("document.querySelector('.backup-dialog button:not(.primary)')");
        await Program.RequireAsync(core, "document.querySelector('.backup-dialog') === null");
        if (!File.Exists(addedAsset) || PackagePresence.Contains(await engine.Projects.ReadAsync(project, token), Package))
            throw new IOException("Cancelling backup confirmation altered the fixture.");
        await OpenReviewAsync();
        await ClickAsync("document.querySelector('.backup-dialog .primary')");
        await Program.RequireAsync(core, "document.querySelector('footer [role=status]')?.textContent === 'Backup ripristinato.'", attempts: 1800);
        var restored = await engine.Projects.ReadAsync(project, token);
        var backupRoot = Path.Combine(project, ".vrcsetup", "backups");
        var displaced = Directory.EnumerateDirectories(backupRoot)
            .SelectMany(folder => Directory.EnumerateDirectories(folder, "before-restore-*"))
            .Select(folder => Path.Combine(folder, "Assets", "AfterVpmChange.txt"))
            .SingleOrDefault(File.Exists);
        if (restored.InstalledVersions.GetValueOrDefault(Package) != Older ||
            restored.LockedPackages.GetValueOrDefault(Package) != Older || AtomicFile.Hash(asset) != assetHash ||
            File.Exists(addedAsset) || displaced is null || await File.ReadAllTextAsync(displaced, token) != change)
            throw new IOException("The package version or displaced edit was not restored safely.");
        await AtomicFile.WriteJsonAsync(Path.Combine(fixture, "react-project-backup.json"), new
        { Passed = true, RestoredVersion = Older, DisplacedEditPreserved = true, ImportedAssetPreserved = true, ConfirmedThroughProjectOptions = true }, token);

        async Task ClickAsync(string selector)
        {
            await Program.RequireAsync(core, selector + " != null && " + selector + "?.disabled !== true", attempts: 400);
            await core.ExecuteScriptAsync(selector + ".click()");
        }

        async Task OpenReviewAsync()
        {
            await ClickAsync("document.querySelector('.project-options summary')");
            await ClickAsync("document.querySelector('.project-options[open] button')");
            await Program.RequireAsync(core, "document.querySelector('.backup-dialog')?.open === true", attempts: 400);
        }
    }

    internal static async Task RunAsync(CoreWebView2 core, VrcSetupApplication engine, string fixture, string? editor, CancellationToken token)
    {
        var root = Path.Combine(fixture, "candidate-projects");
        var project = Path.Combine(root, "Probe");
        ProjectRules.EnsureContained(root, project);
        var snapshot = await engine.Projects.ReadAsync(project, token);
        var asset = Path.Combine(project, "Assets", "ReactProbe.txt");
        var assetHash = AtomicFile.Hash(asset);
        if (assetHash == "missing" || snapshot.Incomplete || PackagePresence.Contains(snapshot, Package) || engine.Projects.IsBusy(project))
            throw new IOException("Use a completed, closed import fixture without Gesture Manager.");

        var marker = Path.Combine(project, ".vrcsetup", "react-vpm-editor.txt");
        var quit = Path.Combine(project, ".vrcsetup", "react-vpm-exit.txt");
        const string indicator = "document.querySelector('.project-table .open-indicator')";
        await ClickAsync("header button", "Progetti");
        await RequireAsync("document.querySelector('.project-actions button') !== null");
        await RequireAsync(indicator + " === null");
        using var process = editor is null ? null : await OpenEditorAsync(fixture, project, editor, token);
        var seen = new List<string>();
        try
        {
            if (process is not null)
            {
                await WaitForEditorVersionAsync(process, marker, "-", token);
                await RequireAsync(indicator + " !== null");
            }
            await ClickAsync(".project-actions button", "Pacchetti");
            await RequireAsync("document.querySelector('.view input[type=search]') !== null");
            if (process is not null)
                await RequireAsync("document.querySelector('#restore-editor-open') !== null && " +
                    "Array.from(document.querySelectorAll('.view button')).find(x => x.textContent === 'Ripristina pacchetti')?.disabled === true");

            await core.ExecuteScriptAsync("""
                (() => {
                    const query = document.querySelector('input[type=search]');
                    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(query, 'Gesture Manager');
                    query.dispatchEvent(new Event('input', { bubbles: true }));
                })()
                """);
            await ClickAsync(".view form button", "Cerca");
            await ClickAsync($"""[aria-label="Risultati catalogo"] [data-package-id="{Package}"] button""", "Scegli versione");
            await ChooseAsync(Older);
            await ApplyAsync(Older);

            await ClickAsync($"""tr[data-package-id="{Package}"] button""", "Versione");
            await ChooseAsync(Newer);
            await ApplyAsync(Newer);

            await ClickAsync($"""tr[data-package-id="{Package}"] button""", "Versione");
            await ChooseAsync(Older);
            await ApplyAsync(Older);

            await ClickAsync($"""tr[data-package-id="{Package}"] button""", "Rimuovi");
            await ApplyAsync(null);
            if (process is not null)
            {
                await ClickAsync("header button", "← Progetti");
                await RequireAsync(indicator + " !== null");
            }
        }
        finally
        {
            if (process is { HasExited: false })
            {
                await File.WriteAllTextAsync(quit, "close fixture", CancellationToken.None);
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(stop.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); }
            }
        }
        if (process is not null && process.ExitCode != 0)
            throw new IOException($"The fixture editor did not exit normally ({process.ExitCode}).");
        if (process is not null)
        {
            await RequireAsync(indicator + " === null && Array.from(document.querySelectorAll('.project-actions button'))" +
                ".find(x => x.textContent === 'Apri')?.disabled === false");
            await CheckFinalCompilationAsync(Path.Combine(fixture, "open-editor-vpm.log"), token);
        }

        var report = process is null ? "react-vpm-edits.json" : "react-vpm-edits-open.json";
        await AtomicFile.WriteJsonAsync(Path.Combine(fixture, report), new
        {
            Passed = true, Package, VersionsObserved = seen, EditorOpen = process is not null,
            EditorPid = process?.Id, AssetPreserved = true, LiveIndicatorVerified = process is not null
        }, token);

        Task RequireAsync(string condition) => Program.RequireAsync(core, condition, attempts: 2400);

        async Task ClickAsync(string selector, string label)
        {
            var control = $"Array.from(document.querySelectorAll({JsonSerializer.Serialize(selector)}))" +
                $".find(x => x.textContent === {JsonSerializer.Serialize(label)})";
            await RequireAsync(control + "?.disabled === false");
            await core.ExecuteScriptAsync(control + ".click()");
        }

        async Task ChooseAsync(string version)
        {
            var value = JsonSerializer.Serialize(version);
            const string picker = """document.querySelector('select[aria-label="Versione da installare"]')""";
            await RequireAsync($"Array.from({picker}?.options ?? []).some(x => x.value === {value})");
            await core.ExecuteScriptAsync($"(() => {{ const picker = {picker}; picker.value = {value}; picker.dispatchEvent(new Event('change', {{ bubbles: true }})); }})()");
            await ClickAsync(".view button", "Seleziona versione");
        }

        async Task ApplyAsync(string? version)
        {
            await ClickAsync(".view button", "Rivedi modifiche");
            await ClickAsync("""[aria-label="Riepilogo pacchetti"] button""", "Applica modifiche");
            var row = $"""document.querySelector('tr[data-package-id="{Package}"]')""";
            var installed = version is null ? row + " === null" : row + $"?.querySelector('td')?.textContent === {JsonSerializer.Serialize(version)}";
            await RequireAsync($"document.querySelector('footer [role=status]')?.textContent === 'Pacchetti aggiornati.' && ({installed})");
            var fresh = await engine.Projects.ReadAsync(project, token);
            if (fresh.InstalledVersions.GetValueOrDefault(Package) != version ||
                fresh.LockedPackages.GetValueOrDefault(Package) != version || AtomicFile.Hash(asset) != assetHash)
                throw new IOException($"Installed package and lock did not match {version ?? "removal"} or the imported asset changed.");
            if (process is not null)
            {
                if (process.HasExited || !engine.Projects.IsBusy(project))
                    throw new IOException("The fixture editor closed during a package edit.");
                await WaitForEditorVersionAsync(process, marker, version ?? "-", token);
            }
            seen.Add(version ?? "removed");
        }
    }

    private static async Task CheckFinalCompilationAsync(string log, CancellationToken token)
    {
        var built = false;
        await foreach (var line in File.ReadLinesAsync(log, token))
        {
            if (line.Contains("Tundra build success", StringComparison.Ordinal)) built = true;
            else if (line.Contains("error CS", StringComparison.Ordinal) ||
                line.Contains("Tundra build failed", StringComparison.Ordinal)) built = false;
        }
        if (!built) throw new IOException("Unity did not compile successfully after the last package change. See " + log);
    }

    private static async Task<Process> OpenEditorAsync(string fixture, string project, string editor, CancellationToken token)
    {
        UnityProjectDriver.ValidateEditor(editor);
        await ReactImportVerification.WaitForFixtureHeadroomAsync(token);
        var directory = Path.Combine(project, "Assets", "Editor");
        ProjectRules.EnsureContained(project, directory);
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, "ReactVpmEditorProbe.cs");
        ProjectRules.EnsureContained(project, probe);
        await File.WriteAllTextAsync(probe, EditorProbe, token);
        foreach (var file in new[] { "react-vpm-editor.txt", "react-vpm-exit.txt" })
        {
            var path = Path.Combine(project, ".vrcsetup", file);
            ProjectRules.EnsureContained(project, path);
            File.Delete(path);
        }
        var start = new ProcessStartInfo(editor) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixture };
        foreach (var argument in new[] { "-batchmode", "-nographics", "-projectPath", project, "-logFile", Path.Combine(fixture, "open-editor-vpm.log") })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not open the fixture in Unity.");
    }

    private static async Task WaitForEditorVersionAsync(Process process, string marker, string version, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromMinutes(3))
        {
            if (process.HasExited) throw new IOException($"The fixture editor exited with code {process.ExitCode}.");
            try { if (File.Exists(marker) && await File.ReadAllTextAsync(marker, token) == version) return; }
            catch (IOException) { /* The fixture editor can be writing its snapshot. */ }
            await Task.Delay(250, token);
        }
        throw new IOException($"Unity did not register {Package} at {version}.");
    }

    private const string EditorProbe = """
        using System;
        using System.IO;
        using System.Linq;
        using UnityEditor;
        using UnityEngine;

        [InitializeOnLoad]
        internal static class ReactVpmEditorProbe
        {
            static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".vrcsetup"));
            static double next;

            static ReactVpmEditorProbe() => EditorApplication.update += Poll;

            static void Poll()
            {
                if (EditorApplication.timeSinceStartup < next) return;
                next = EditorApplication.timeSinceStartup + .5;
                try
                {
                    if (File.Exists(Path.Combine(Root, "react-vpm-exit.txt"))) { EditorApplication.Exit(0); return; }
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                        File.Exists(Path.Combine(Root, "editor-package-lease.json")) ||
                        File.Exists(Path.Combine(Root, "editor-package-ack.txt"))) return;
                    var version = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                        .FirstOrDefault(x => x.name == "vrchat.blackstartx.gesture-manager")?.version ?? "-";
                    File.WriteAllText(Path.Combine(Root, "react-vpm-editor.txt"), version);
                }
                catch (IOException) { }
            }
        }
        """;
}
