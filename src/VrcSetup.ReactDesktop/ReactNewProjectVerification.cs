using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactNewProjectVerification
{
    private const string Name = "React Empty Project";
    private const string Preset = "Fixture preset";
    private const string Package = "vrchat.blackstartx.gesture-manager";

    internal static async Task VerifyOpenAsync(CoreWebView2 core, VrcSetupApplication engine, string fixture, CancellationToken token)
    {
        var original = await engine.Configuration.LoadAsync(token);
        var root = Path.Combine(fixture, "candidate-projects");
        var project = Path.Combine(root, Name);
        if (!ProjectRules.NormalizePath(original.UnityProjectsRoot).Equals(root, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(project, "Library", "ScriptAssemblies", "VRC.SDKBase.dll")) || engine.Projects.IsBusy(project))
            throw new IOException("Use the closed, compiled new-project fixture.");
        ProjectRules.EnsureContained(root, project);
        var version = (await engine.Projects.ReadAsync(project, token)).UnityVersion;
        var receiptPath = Path.Combine(fixture, "open-from-projects-process.json");
        File.Delete(receiptPath);
        var launchConfig = original.Copy();
        launchConfig.UnityEditorPath = Path.Combine(fixture, "missing-editor", "Unity.exe");
        await engine.Configuration.SaveAsync(launchConfig, token);
        try
        {
            await ReactImportVerification.WaitForFixtureHeadroomAsync(token, minimumGiB: 3);
            await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.nav-item')).find(x => x.getAttribute('aria-label') === 'Progetti').click()");
            var row = $"Array.from(document.querySelectorAll('.project-table tbody tr')).find(x => x.querySelector('th')?.textContent.startsWith({JsonSerializer.Serialize(Name)}))";
            var button = $"Array.from({row}?.querySelectorAll('button') ?? []).find(x => x.textContent === 'Apri')";
            await Program.RequireAsync(core, button + "?.disabled === false", attempts: 200);
            await core.ExecuteScriptAsync(button + ".click()");
            await Program.RequireAsync(core, button + "?.disabled === true");
            await Program.RequireAsync(core, button + "?.disabled === false && document.querySelector('footer [role=status]').textContent === ''", attempts: 6000);
            using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath, token));
            var process = receipt.RootElement;
            if (process.GetProperty("ExitCode").GetInt32() != 0 || process.GetProperty("Project").GetString() != project ||
                UnityEditorDiscovery.ReadVersion(process.GetProperty("Editor").GetString() ?? "") != version ||
                (await engine.Configuration.LoadAsync(token)).UnityEditorPath != launchConfig.UnityEditorPath ||
                !File.Exists(Path.Combine(fixture, "open-from-projects.log")))
                throw new IOException("The Projects button did not launch the matching Unity editor with the selected project.");
            await AtomicFile.WriteJsonAsync(Path.Combine(fixture, "react-open-project.json"), new
            {
                Passed = true, Project = project, UnityVersion = version, Editor = process.GetProperty("Editor").GetString(),
                ProcessId = process.GetProperty("ProcessId").GetInt32(), LaunchedByProjectsButton = true,
                MissingConfiguredEditorRecovered = true, PreferencesUnchangedByLaunch = true, HeadlessFixture = true
            }, token);
        }
        finally
        {
            await engine.Configuration.MergeAsync(launchConfig, original, CancellationToken.None);
        }
    }

    internal static async Task RunAsync(CoreWebView2 core, VrcSetupApplication engine, string fixture, string editor, CancellationToken shutdown)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        timeout.CancelAfter(TimeSpan.FromMinutes(12));
        var token = timeout.Token;
        var original = await engine.Configuration.LoadAsync(token);

        await ClickAsync(".sidebar button", "Progetti");
        await ClickAsync(".project-header-actions button", "Nuovo progetto");
        await FillAsync("""input[aria-label="Nome nuovo progetto"]""", Name);
        await ClickAsync(".new-project-form button", "Personalizza pacchetti");
        await FillAsync(".new-project-packages input[type=search]", "Gesture Manager");
        await ClickAsync(".new-project-packages form button", "Cerca");
        await ClickAsync($$"""[aria-label="Risultati catalogo"] [data-package-id="{{Package}}"] button""", "Scegli versione");
        await ChooseVersionAsync("3.9.7");
        await ClickAsync(".new-project-packages button", "Salva come preset");
        await FillAsync("""input[aria-label="Nome preset"]""", Preset);
        await ClickAsync(".new-project-packages form button", "Salva");
        await RequireAsync($"""document.querySelector('.new-project-form select[aria-label="Preset"]')?.value === {JsonSerializer.Serialize(Preset)}""");

        await ClickAsync($$""".new-project-packages [data-package-id="{{Package}}"] button""", "Versione");
        await ChooseVersionAsync("3.9.9");
        await ClickAsync(".new-project-form button", "Rivedi progetto");
        await RequireAsync("""
            Array.from(document.querySelectorAll('.new-project-review-packages .package')).some(x =>
              x.querySelector('dt').textContent === 'Gesture Manager' && x.querySelector('dd').textContent === '3.9.9')
            """);
        await ClickAsync("""[aria-label="Nuovo progetto"] button""", "Crea progetto");

        var path = Path.Combine(fixture, "candidate-projects", Name);
        await RequireAsync("document.querySelector('.jobs article progress') !== null");
        engine.Imports.ConfigureParallelism(1);
        ImportJob job;
        while (true)
        {
            job = engine.Imports.Snapshot().Single();
            if (!job.Active) break;
            await Task.Delay(250, token);
        }
        if (job.State != "Completed" || !job.Source.EmptyProject || job.ProjectPath != path)
            throw new IOException($"Project creation ended at {job.State}/{job.Step}. {job.Message} Log: {job.LogFile}");
        var project = await engine.Projects.ReadAsync(path, token);
        var preferences = await engine.Configuration.LoadAsync(token);
        if (project.Incomplete || project.InstalledVersions.GetValueOrDefault(Package) != "3.9.9" ||
            project.LockedPackages.GetValueOrDefault(Package) != "3.9.9" ||
            preferences.PackagePresets.GetValueOrDefault(Preset)?.GetValueOrDefault(Package) != "3.9.7" ||
            !original.VpmPackages.OrderBy(x => x.Key).SequenceEqual(preferences.VpmPackages.OrderBy(x => x.Key)))
            throw new IOException("The created project or preserved preset did not match the review.");

        await ClickAsync(".sidebar button", "Progetti");
        await RequireAsync($"""Array.from(document.querySelectorAll('.project-table tbody th')).some(x => x.textContent.includes({JsonSerializer.Serialize(Name)}))""");

        await ReactImportVerification.WaitForFixtureHeadroomAsync(token);
        var log = Path.Combine(fixture, "new-project-unity.log");
        var compiled = await engine.Tools.RunAsync(editor, ["-batchmode", "-nographics", "-quit", "-projectPath", path, "-logFile", log],
            path, cancellationToken: token, timeout: TimeSpan.FromMinutes(6));
        var assembly = Path.Combine(path, "Library", "ScriptAssemblies", "VRC.SDKBase.dll");
        if (compiled.ExitCode != 0 || !File.Exists(assembly))
            throw new IOException("Unity did not compile the new project. Log: " + log);
        await AtomicFile.WriteJsonAsync(Path.Combine(fixture, "react-new-project.json"), new
        {
            Passed = true, Project = path, VersionInstalled = "3.9.9", PresetVersionPreserved = "3.9.7",
            DefaultsPreserved = true, ListedInProjects = true, CompiledInUnity = true, UnityLog = log
        }, token);

        Task RequireAsync(string expression) => Program.RequireAsync(core, expression, attempts: 600);

        async Task FillAsync(string selector, string value)
        {
            var input = $"document.querySelector({JsonSerializer.Serialize(selector)})";
            await RequireAsync(input + " !== null");
            await core.ExecuteScriptAsync($$"""
                (() => {
                  const input = {{input}};
                  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, {{JsonSerializer.Serialize(value)}});
                  input.dispatchEvent(new Event('input', { bubbles: true }));
                })()
                """);
        }

        async Task ClickAsync(string selector, string label)
        {
            var button = $"Array.from(document.querySelectorAll({JsonSerializer.Serialize(selector)})).find(x => x.textContent === {JsonSerializer.Serialize(label)})";
            await RequireAsync(button + "?.disabled === false");
            await core.ExecuteScriptAsync(button + ".click()");
        }

        async Task ChooseVersionAsync(string version)
        {
            const string picker = """document.querySelector('select[aria-label="Versione da installare"]')""";
            var value = JsonSerializer.Serialize(version);
            await RequireAsync($"Array.from({picker}?.options ?? []).some(x => x.value === {value})");
            await core.ExecuteScriptAsync($"(() => {{ const select = {picker}; select.value = {value}; select.dispatchEvent(new Event('change', {{ bubbles: true }})); }})()");
            await ClickAsync(".new-project-packages button", "Seleziona versione");
        }
    }
}
