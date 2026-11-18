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
        using var process = editor is null ? null : await OpenEditorAsync(fixture, project, editor, token);
        var seen = new List<string>();
        try
        {
            if (process is not null)
                await WaitForEditorVersionAsync(process, marker, "-", token);

            await ClickAsync("header button", "Progetti");
            await RequireAsync("document.querySelector('.project-actions button') !== null");
            if (process is not null)
                await RequireAsync("document.querySelector('.project-table .open-indicator') !== null");
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

        var report = process is null ? "react-vpm-edits.json" : "react-vpm-edits-open.json";
        await AtomicFile.WriteJsonAsync(Path.Combine(fixture, report), new
        {
            Passed = true, Package, VersionsObserved = seen, EditorOpen = process is not null,
            EditorPid = process?.Id, AssetPreserved = true
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
