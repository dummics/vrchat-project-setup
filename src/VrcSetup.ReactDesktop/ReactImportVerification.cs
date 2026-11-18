using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal static class ReactImportVerification
{
    internal static string MakeArchive(string folder)
    {
        var path = Path.Combine(folder, "Probe.unitypackage");
        using var gzip = new GZipStream(File.Create(path), CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip, TarEntryFormat.Ustar);
        Add("Assets/ReactProbe.txt", "React import succeeded");
        Add("Assets/ReactProbe.cs", "public sealed class ReactProbe {}\n");
        return path;

        void Add(string name, string content)
        {
            var id = Guid.NewGuid().ToString("N");
            Write("pathname", name);
            Write("asset", content);
            Write("asset.meta", $"fileFormatVersion: 2\nguid: {id}\n");

            void Write(string part, string text)
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"{id}/{part}") { DataStream = stream });
            }
        }
    }

    internal static async Task RunAsync(CoreWebView2 core, VrcSetupApplication engine, string folder, CancellationToken shutdown)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        timeout.CancelAfter(TimeSpan.FromMinutes(12));
        var token = timeout.Token;
        await core.ExecuteScriptAsync("document.querySelector('footer .primary').click()");
        await WaitForAsync("""document.querySelector('[aria-label="Rivedi importazione"] .primary') !== null""", TimeSpan.FromMinutes(2));
        await WaitForAsync("""document.querySelector('[aria-label="Rivedi importazione"] details ul').textContent.includes('3.10.6')""", TimeSpan.FromSeconds(10));
        await core.ExecuteScriptAsync("""document.querySelector('[aria-label="Rivedi importazione"] .primary').click()""");
        await WaitForAsync("document.querySelector('.jobs article') !== null", TimeSpan.FromSeconds(20));
        if (engine.Imports.EffectiveParallelism == 0)
        {
            var usage = engine.Imports.Resources;
            if (usage is not { AvailableGiB: >= 6, AvailableCommitGiB: >= 6 })
                throw new IOException("Not enough memory for the isolated Unity import fixture.");
            // Exercise a single owned project when the normal reserve would leave
            // it queued. The application continues to use automatic scheduling.
            engine.Imports.ConfigureParallelism(1);
        }

        ImportJob job;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            job = engine.Imports.Snapshot().Single();
            if (!job.Active) break;
            await Task.Delay(500, token);
        }
        if (job.State != "Completed" || job.UnityCompilationNeedsAttention || job.PackageContentsNeedRestore)
            throw new IOException($"Import ended at {job.Step}: {job.State}. {job.Message} Log: {job.LogFile}");
        await WaitForAsync("""document.querySelector('.jobs article')?.textContent.includes('Completata') === true""", TimeSpan.FromSeconds(10));
        var snapshot = await engine.Projects.ReadAsync(job.ProjectPath, token);
        if (snapshot.Incomplete || snapshot.InstalledVersions.GetValueOrDefault(ProjectRules.AvatarSdk) != "3.10.6" ||
            !File.Exists(Path.Combine(job.ProjectPath, "Library", "ScriptAssemblies", "Assembly-CSharp.dll")) ||
            await File.ReadAllTextAsync(Path.Combine(job.ProjectPath, "Assets", "ReactProbe.txt"), token) != "React import succeeded")
            throw new IOException("Imported files, compiled assembly or selected SDK were not verified.");
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "react-unity-import.json"), new
        {
            Passed = true, job.State, job.ProjectPath, job.LogFile,
            AvatarSdk = snapshot.InstalledVersions[ProjectRules.AvatarSdk],
            ReactReview = true, ReactQueueCompleted = true, AssetVerified = true, AssemblyVerified = true
        }, token);

        Task WaitForAsync(string condition, TimeSpan maximum) => WaitForPageAsync(core, condition, maximum, token);
    }

    internal static async Task VerifyRestoreAsync(CoreWebView2 core, VrcSetupApplication engine, string folder, CancellationToken shutdown)
    {
        var job = engine.Imports.Snapshot().Single(x => x.State == "Completed");
        var root = Path.Combine(folder, "candidate-projects");
        ProjectRules.EnsureContained(root, job.ProjectPath);
        var packageDir = Path.Combine(job.ProjectPath, "Packages", ProjectRules.AvatarSdk);
        ProjectRules.EnsureContained(root, packageDir);
        var package = Path.Combine(packageDir, "package.json");
        if (File.GetAttributes(package).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Fixture package is linked.");
        var asset = Path.Combine(job.ProjectPath, "Assets", "ReactProbe.txt");
        var originalAsset = AtomicFile.Hash(asset);
        if (originalAsset == "missing" || engine.Projects.IsBusy(job.ProjectPath)) throw new IOException("The import fixture is unavailable.");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(package, shutdown))?.AsObject()
            ?? throw new IOException("Fixture package metadata missing.");
        metadata["version"] = "0.0.0";
        await File.WriteAllTextAsync(package, metadata.ToJsonString(), shutdown);

        await ClickAsync("header button", "Progetti");
        await WaitAsync("document.querySelector('.project-table tbody th')?.textContent.includes('Probe') === true", TimeSpan.FromSeconds(15));
        await ClickAsync(".project-actions button", "Pacchetti");
        await WaitAsync("document.querySelector('.project-table tbody')?.textContent.includes('0.0.0') === true", TimeSpan.FromSeconds(15));
        await ClickAsync(".view button", "Ripristina pacchetti");
        await WaitAsync("""document.querySelector('[aria-label="Riepilogo pacchetti"]')?.textContent.includes('3.10.6') === true""", TimeSpan.FromSeconds(15));
        await ClickAsync(".view button", "Conferma ripristino");
        await WaitAsync("document.querySelector('footer [role=status]')?.textContent === 'Pacchetti ripristinati.'", TimeSpan.FromMinutes(4));
        var snapshot = await engine.Projects.ReadAsync(job.ProjectPath, shutdown);
        if (snapshot.InstalledVersions.GetValueOrDefault(ProjectRules.AvatarSdk) != "3.10.6" || AtomicFile.Hash(asset) != originalAsset)
            throw new IOException("The restore did not return to the locked version or changed avatar files.");
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "react-vpm-restore.json"),
            new { Passed = true, Restored = ProjectRules.AvatarSdk, Version = "3.10.6", AvatarFilesPreserved = true }, shutdown);

        async Task ClickAsync(string selector, string label) => await core.ExecuteScriptAsync(
            $"Array.from(document.querySelectorAll({JsonSerializer.Serialize(selector)})).find(x => x.textContent === {JsonSerializer.Serialize(label)}).click()");

        Task WaitAsync(string condition, TimeSpan maximum) => WaitForPageAsync(core, condition, maximum, shutdown);
    }

    internal static async Task VerifyPauseAsync(CoreWebView2 core, VrcSetupApplication engine, string folder, CancellationToken token)
    {
        await core.ExecuteScriptAsync("document.querySelector('footer .primary').click()");
        const string primary = """document.querySelector('[aria-label="Rivedi importazione"] .primary')""";
        await WaitForPageAsync(core, primary + "?.disabled === false", TimeSpan.FromMinutes(2), token);
        await WaitForFixtureHeadroomAsync(token);
        await core.ExecuteScriptAsync(primary + ".click()");
        await WaitForPageAsync(core, "document.querySelector('.jobs article') !== null", TimeSpan.FromSeconds(20), token);
        engine.Imports.ConfigureParallelism(1);
        var importing = await WaitForJobAsync(engine, Path.Combine(folder, "candidate-projects", "Probe"),
            job => !job.Active || job.Step == "Import" &&
                Directory.EnumerateFiles(engine.Paths.Logs, "unity-import-*.log").Any(log => new FileInfo(log).Length > 0),
            TimeSpan.FromMinutes(6), token);
        if (!importing.Active || importing.Source.CreationId is null || !Directory.Exists(importing.ProjectPath))
            throw new IOException($"The fixture never reached the Unity import: {importing.Step}, {importing.State}.");
        await ClickJobAsync(core, importing.Id, "Pausa", token);
        var paused = await WaitForJobAsync(engine, importing.ProjectPath, job => !job.Active, TimeSpan.FromMinutes(2), token);
        if (paused.State != "Paused" || !Directory.Exists(paused.ProjectPath) || paused.HistorySaveFailed)
            throw new IOException($"The import was not paused with its project retained: {paused.State}.");
        await WaitForPageAsync(core, "document.querySelector('.jobs article')?.textContent.includes('In pausa') === true",
            TimeSpan.FromSeconds(15), token);
        var journal = await AtomicFile.ReadJsonAsync<ImportJob[]>(Path.Combine(folder, "imports.json"), token);
        if (journal?.Single().State != "Paused") throw new IOException("The paused state was not persisted.");
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "paused-import.json"), new
        {
            paused.Id, paused.ProjectPath, paused.Source.CreationId, PausedDuringUnityImport = true, Process = Environment.ProcessId
        }, token);
    }

    internal static async Task VerifyRecoveryAsync(CoreWebView2 core, VrcSetupApplication engine, string folder, bool discard, CancellationToken token)
    {
        var path = Path.Combine(folder, "candidate-projects", "Probe");
        var journal = engine.Imports.Snapshot();
        var paused = ImportQueue.CurrentForProject(journal, path) ?? throw new IOException("Missing paused import.");
        using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "paused-import.json"), token));
        var before = evidence.RootElement;
        var originalId = before.GetProperty("Id").GetString();
        if (paused.State is not ("Paused" or "Interrupted") || paused.Source.CreationId is null ||
            paused.Source.CreationId != before.GetProperty("CreationId").GetString() ||
            !journal.Any(job => job.Id == originalId && job.State == "Paused" && job.ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase)) ||
            before.GetProperty("Process").GetInt32() == Environment.ProcessId || !Directory.Exists(path))
            throw new IOException("The reopened fixture does not match the paused import.");
        var status = JsonSerializer.Serialize(paused.State == "Paused" ? "In pausa" : "Interrotta");
        await WaitForPageAsync(core, $"document.querySelector('.jobs article')?.textContent.includes({status}) === true",
            TimeSpan.FromSeconds(12), token);
        if (discard)
        {
            var neighbor = Path.Combine(folder, "candidate-projects", "neighbor.txt");
            await File.WriteAllTextAsync(neighbor, "keep", token);
            await ClickJobAsync(core, paused.Id, "Annulla ed elimina progetto", token);
            await ClickJobAsync(core, paused.Id, "Indietro", token);
            if (!Directory.Exists(path)) throw new IOException("Leaving confirmation changed the project.");
            await ClickJobAsync(core, paused.Id, "Annulla ed elimina progetto", token);
            await ClickJobAsync(core, paused.Id, "Elimina progetto", token);
            var discarded = await WaitForJobAsync(engine, path, job => job.DiscardCompleted, TimeSpan.FromMinutes(2), token);
            var saved = await AtomicFile.ReadJsonAsync<ImportJob[]>(Path.Combine(folder, "imports.json"), token);
            if (Directory.Exists(path) || await File.ReadAllTextAsync(neighbor, token) != "keep" ||
                !File.Exists(paused.Source.UnityPackagePath) || saved?.SingleOrDefault(job => job.Id == discarded.Id)?.DiscardCompleted != true)
                throw new IOException("Cancellation left the project, lost its history or changed a source or sibling.");
            await WaitForPageAsync(core, "document.querySelector('.jobs article')?.textContent.includes('Annullata') === true",
                TimeSpan.FromSeconds(15), token);
            await AtomicFile.WriteJsonAsync(Path.Combine(folder, "discarded-import.json"), new
            { Passed = true, discarded.State, RetainedSource = true, RetainedSibling = true, ConfirmedInReact = true, ReopenedProfile = true }, token);
            return;
        }

        await ClickJobAsync(core, paused.Id, "Riprendi importazione", token);
        const string resume = """document.querySelector('[aria-label="Riprendi importazione"] .primary')""";
        await WaitForPageAsync(core, resume + "?.disabled === false", TimeSpan.FromMinutes(2), token);
        await WaitForFixtureHeadroomAsync(token);
        await core.ExecuteScriptAsync(resume + ".click()");
        var queued = await WaitForJobAsync(engine, path, job => job.Id != paused.Id, TimeSpan.FromSeconds(30), token);
        engine.Imports.ConfigureParallelism(1);
        var finished = await WaitForJobAsync(engine, path, job => job.Id == queued.Id && !job.Active, TimeSpan.FromMinutes(8), token);
        if (finished.State != "Completed" || finished.Source.CreationId != paused.Source.CreationId ||
            finished.UnityCompilationNeedsAttention || finished.PackageContentsNeedRestore ||
            await File.ReadAllTextAsync(Path.Combine(path, "Assets", "ReactProbe.txt"), token) != "React import succeeded" ||
            !File.Exists(Path.Combine(path, "Library", "ScriptAssemblies", "Assembly-CSharp.dll")))
            throw new IOException($"The paused import did not complete in its original project: {finished.Step}, {finished.State}. {finished.Message}");
        await WaitForPageAsync(core, "document.querySelector('.jobs article')?.textContent.includes('Completata') === true",
            TimeSpan.FromSeconds(15), token);
        await AtomicFile.WriteJsonAsync(Path.Combine(folder, "resumed-import.json"), new
        { Passed = true, FromState = paused.State, finished.State, finished.ProjectPath, finished.LogFile,
            OriginalProjectPreserved = true, ReopenedProfile = true }, token);
    }

    private static async Task WaitForFixtureHeadroomAsync(CancellationToken token)
    {
        var sampler = new SystemResourceSampler();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (sampler.Sample() is not { AvailableGiB: >= 6, AvailableCommitGiB: >= 6 })
        {
            if (wait.Elapsed >= TimeSpan.FromMinutes(2))
                throw new IOException("Not enough memory for the isolated Unity import fixture.");
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
    }

    private static async Task<ImportJob> WaitForJobAsync(VrcSetupApplication engine, string path, Func<ImportJob, bool> predicate,
        TimeSpan maximum, CancellationToken token)
    {
        var until = System.Diagnostics.Stopwatch.StartNew();
        while (until.Elapsed < maximum)
        {
            var job = ImportQueue.CurrentForProject(engine.Imports.Snapshot(), path);
            if (job is not null && predicate(job)) return job;
            await Task.Delay(150, token);
        }
        var last = ImportQueue.CurrentForProject(engine.Imports.Snapshot(), path);
        throw new IOException($"Import verification timed out at {last?.State}/{last?.Step}. {last?.Message}");
    }

    private static async Task ClickJobAsync(CoreWebView2 core, string id, string label, CancellationToken token)
    {
        var control = $"Array.from(document.getElementById({JsonSerializer.Serialize("job-" + id)})?.querySelectorAll('button') ?? [])" +
            $".find(button => button.textContent === {JsonSerializer.Serialize(label)})";
        await WaitForPageAsync(core, control + "?.disabled === false", TimeSpan.FromSeconds(15), token);
        await core.ExecuteScriptAsync(control + ".click()");
    }

    private static async Task WaitForPageAsync(CoreWebView2 core, string condition, TimeSpan maximum, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(maximum);
        try
        {
            while (await core.ExecuteScriptAsync(condition) != "true")
                await Task.Delay(250, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            var status = await core.ExecuteScriptAsync("document.querySelector('footer [role=status]')?.textContent");
            throw new IOException($"React assertion failed: {condition}. Status: {status}");
        }
    }
}
