using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup.ReactDesktop;

internal sealed class ImportBridge(Window window, VrcSetupApplication engine) : IDisposable
{
    internal const string Page = "https://app.vrcsetup.test/index.html";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, string> sources = [];
    private readonly CancellationTokenSource lifetime = new();
    private CoreWebView2? web;
    private bool handlingRequest;
    private OperationPlan? reviewed;
    private SourceDraft[] initial = [];
    private readonly string candidateProjects = Path.Combine(engine.Paths.Home, "candidate-projects");
    internal bool IsBusy => handlingRequest;

    internal static bool IsPage(string? uri) => string.Equals(uri, Page, StringComparison.Ordinal);

    internal void Attach(CoreWebView2 core)
    {
        web = core;
        core.WebMessageReceived += Receive;
    }

    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsPage(args.Source) || web is null) return;
        string? id = null;
        var ownsRequest = false;
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var root = message.RootElement;
            id = root.GetProperty("id").GetString();
            if (handlingRequest) throw new InvalidOperationException("Attendi l’operazione in corso.");
            handlingRequest = ownsRequest = true;
            var result = await Dispatch(root.GetProperty("method").GetString(), root.GetProperty("data"), args);
            Send(new { id, result });
        }
        catch (Exception error) when (!lifetime.IsCancellationRequested)
        {
            engine.Diagnostics.Write("React candidate", error.Message, error);
            Send(new { id, error = UserErrors.Message(error) });
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        finally { if (ownsRequest) handlingRequest = false; }
    }

    private async Task<object> Dispatch(string? method, JsonElement data, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var token = lifetime.Token;
        switch (method)
        {
            case "state":
                return await State(token);
            case "pick":
                var picker = new OpenFileDialog { Filter = "UnityPackage|*.unitypackage", Multiselect = true };
                return picker.ShowDialog(window) == true ? await Stage(picker.FileNames, token) : [];
            case "drop":
                return await Stage(args.AdditionalObjects.OfType<CoreWebView2File>().Select(x => x.Path), token);
            case "workspace":
                var config = await engine.Configuration.LoadAsync(token);
                config.UnityEditorPath = (await Task.Run(UnityEditorDiscovery.FindEditors, token))
                    .FirstOrDefault(x => x.Version == UnityEditorDiscovery.PinnedVersion).Path ?? "";
                if (config.UnityEditorPath.Length == 0) throw new IOException("Unity 2022.3.22f1 non trovato.");
                Directory.CreateDirectory(candidateProjects);
                config.UnityProjectsRoot = candidateProjects;
                await engine.Configuration.SaveAsync(config, token);
                return await State(token);
            case "remove":
                sources.Remove(data.GetGuid());
                reviewed = null;
                return true;
            case "review":
                reviewed = null;
                var settings = await engine.Configuration.LoadAsync(token);
                if (!string.Equals(settings.UnityProjectsRoot, candidateProjects, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Configura la cartella isolata della candidata.");
                var plans = new List<ProjectPlan>();
                foreach (var choice in data.Deserialize<DraftChoice[]>(Json) ?? [])
                {
                    if (!ProjectRules.BuildTargets.Contains(choice.Target)) throw new ArgumentException("Piattaforma non valida.");
                    var plan = await engine.PlanCreationAsync(sources[choice.Id], settings, name: choice.Name,
                        preset: choice.Preset, packages: choice.Packages, buildTarget: choice.Target, cancellationToken: token);
                    plans.AddRange(plan.Projects);
                }
                if (plans.Count == 0 || plans.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
                    throw new ArgumentException("Scegli una destinazione distinta per ogni progetto.");
                reviewed = new(Guid.NewGuid().ToString("N"), DateTime.UtcNow, plans.ToArray());
                return new { reviewed.Id, destinations = plans.Select(x => x.Path).ToArray() };
            case "start":
                if (reviewed is not { } accepted || data.GetString() != accepted.Id) throw new InvalidOperationException("Rivedi l’importazione.");
                reviewed = null;
                await engine.Imports.EnqueueAsync(accepted, token: token);
                return await State(token);
            case "pause":
                await engine.Imports.PauseAsync(data.GetString() ?? "", token);
                return await State(token);
            case "log":
                var path = engine.Imports.Snapshot().Single(x => x.Id == data.GetString()).LogFile;
                if (!File.Exists(path)) return "";
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    input.Seek(Math.Max(0, input.Length - 65536), SeekOrigin.Begin);
                    using var reader = new StreamReader(input);
                    return await reader.ReadToEndAsync(token);
                }
            default:
                throw new ArgumentException("Operazione sconosciuta.");
        }
    }

    private async Task<object> State(CancellationToken token)
    {
        var settings = await engine.Configuration.LoadAsync(token);
        return new
        {
            root = settings.UnityProjectsRoot == candidateProjects ? candidateProjects : "",
            packages = settings.VpmPackages,
            presets = settings.PackagePresets,
            initial,
            jobs = engine.Imports.Snapshot().Select(x => new { x.Id, x.Name, x.State, x.Step, x.Progress, x.Active })
        };
    }

    internal async Task<SourceDraft[]> Stage(IEnumerable<string> paths, CancellationToken token)
    {
        var settings = await engine.Configuration.LoadAsync(token);
        var added = new List<(SourceDraft Draft, string Path)>();
        foreach (var file in paths.Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!file.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)
                || sources.ContainsValue(file)) continue;
            var report = await engine.Inspection.InspectForImportAsync(file, cancellationToken: token);
            var suggestions = report.Suggestions
                .Where(x => x.Confidence is DependencyConfidence.Declared or DependencyConfidence.Detected)
                .Select(x => new PackageChoice(x.Id, PackageCatalog.FriendlyName(x.Id), DependencyAdvisor.SuggestedVersion(x)))
                .Where(x => x.Version is not null).ToArray();
            var id = Guid.NewGuid();
            added.Add((new(id, Path.GetFileName(file), ProjectRules.SuggestName(file, settings),
                ProjectRules.SuggestBuildTarget(file), suggestions), file));
        }
        foreach (var item in added) sources.Add(item.Draft.Id, item.Path);
        reviewed = null;
        return added.Select(item => item.Draft).ToArray();
    }

    internal async Task SeedSmokeAsync(string folder)
    {
        var inputs = new List<string>();
        for (var i = 1; i <= 6; ++i)
        {
            var input = Path.Combine(folder, $"avatarquestv{i}.unitypackage");
            using (var gzip = new System.IO.Compression.GZipStream(File.Create(input), System.IO.Compression.CompressionMode.Compress))
            using (var tar = new System.Formats.Tar.TarWriter(gzip))
            {
                var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/pathname")
                {
                    DataStream = new MemoryStream("Assets/Avatar.prefab"u8.ToArray())
                };
                tar.WriteEntry(entry);
            }
            inputs.Add(input);
        }
        initial = await Stage(inputs, lifetime.Token);
    }

    private void Send(object response)
    {
        if (!lifetime.IsCancellationRequested && web is { } core && IsPage(core.Source))
            core.PostWebMessageAsJson(JsonSerializer.Serialize(response, Json));
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    internal sealed record SourceDraft(Guid Id, string File, string Name, string Target, PackageChoice[] Suggestions);
    internal sealed record PackageChoice(string Id, string Name, string? Version);
    private sealed record DraftChoice(Guid Id, string Name, string Target, string? Preset, Dictionary<string, string> Packages);
}
