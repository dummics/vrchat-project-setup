using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VrcSetup.Core;

namespace VrcSetup.Infrastructure;

public sealed class UnityProjectDriver(AppPaths paths, ToolRunner runner, ProjectWorkspace workspace) : IProjectDriver
{
    public async Task PrepareAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        if (plan.Creation is not null)
        {
            var file = Path.Combine(plan.Path, "Packages", "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))?.AsObject() ?? throw new IOException("Unity package manifest is invalid.");
            var dependencies = manifest["dependencies"]?.AsObject() ?? new JsonObject();
            if (dependencies["com.unity.test-framework"] is null)
            {
                var snippetFile = Path.Combine(paths.Resources, "unity-test-framework.dependencies.json");
                var version = "1.1.33";
                if (File.Exists(snippetFile))
                {
                    using var snippet = JsonDocument.Parse(await File.ReadAllTextAsync(snippetFile, cancellationToken));
                    if (snippet.RootElement.TryGetProperty("dependencies", out var deps) && deps.TryGetProperty("com.unity.test-framework", out var value)) version = value.GetString()!;
                }
                dependencies["com.unity.test-framework"] = version; manifest["dependencies"] = dependencies;
                await AtomicFile.WriteJsonAsync(file, manifest, cancellationToken);
            }
        }
        if (!plan.NeedsEasyLoginRepository) return;
        const string repository = "https://foxscore.dev/vpm/index.json";
        var repositories = await runner.VpmAsync(["list", "repos"], progress: progress, cancellationToken: cancellationToken);
        if (ProjectRules.ToolFailed(repositories)) throw new IOException("Cannot check the Easy Login repository: " + repositories.Output);
        if (!repositories.Output.Contains(repository, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new(DateTime.UtcNow, "Repository", "Registering Vulpine Vault for Easy Login.", Project: plan.Path));
            var add = await runner.VpmAsync(["add", "repo", repository], progress: progress, cancellationToken: cancellationToken);
            if (ProjectRules.ToolFailed(add)) throw new IOException("Cannot register Vulpine Vault: " + add.Output);
        }
        var legacy = Path.Combine(plan.Path, "Assets", "EASY LOGIN");
        if (!Directory.Exists(legacy)) return;
        ProjectRules.EnsureContained(plan.Path, legacy);
        // Only move the known Easy Login folder, after the workspace has backed it up.
        if (!Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories).Any(x => Path.GetFileName(x).Contains("EasyLogin", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).Contains("Easy Login", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Assets/EASY LOGIN was not recognized as the legacy package. Inspect it before migration.");
        var backup = Path.Combine(plan.Path, ".vrcsetup", "backups", "easy-login-assets-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        Directory.Move(legacy, backup);
        if (File.Exists(legacy + ".meta")) File.Move(legacy + ".meta", backup + ".meta");
        progress?.Report(new(DateTime.UtcNow, "Migration", "Legacy Easy Login moved to " + backup, EventSeverity.Warning, plan.Path));
    }

    public async Task CreateAsync(CreationRequest request, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        ValidateEditor(request.UnityEditorPath);
        foreach (var target in request.PublishingPlatforms.Append(request.BuildTarget).Distinct())
            if (!HasBuildSupport(request.UnityEditorPath, target)) throw new IOException($"Unity build support is missing for {target}. Install it in Unity Hub.");
        if (Directory.Exists(request.TargetPath)) throw new IOException("Destination already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath)!);
        var stage = request.TargetPath + ".create-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            await AtomicFile.WriteJsonAsync(Path.Combine(stage, ".vrcsetup", "state.json"), new SetupState
            { ProjectName = request.Name, UnityPackagePath = request.UnityPackagePath, StartedAt = DateTime.UtcNow, LastUpdatedAt = DateTime.UtcNow, Steps = new() { ["created"] = false } }, cancellationToken);
            Directory.Move(stage, request.TargetPath);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        var log = NewUnityLog("create");
        var result = await runner.RunAsync(request.UnityEditorPath, ["-createProject", request.TargetPath, "-buildTarget", request.BuildTarget, "-quit", "-batchmode", "-logFile", log],
            progress: progress, phase: "Unity create", cancellationToken: cancellationToken, timeout: TimeSpan.FromMinutes(15), logFile: log);
        if (result.ExitCode != 0 || !Directory.Exists(Path.Combine(request.TargetPath, "Assets")) || !Directory.Exists(Path.Combine(request.TargetPath, "Packages")))
            throw new IOException("Unity could not create the project. Log: " + log);
        await AtomicFile.WriteJsonAsync(Path.Combine(request.TargetPath, ".vrcsetup", "state.json"), new SetupState
        { ProjectName = request.Name, UnityPackagePath = request.UnityPackagePath, StartedAt = DateTime.UtcNow, LastUpdatedAt = DateTime.UtcNow, Steps = new() { ["created"] = true } }, cancellationToken);
    }

    public async Task<ToolResult> ChangePackageAsync(string project, PackageChange change, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        ProjectRules.ValidatePackageId(change.Id);
        ToolResult result;
        if (change.Kind == ChangeKind.Remove)
            result = await runner.VpmAsync(["remove", "package", change.Id, "-p", project], project, cancellationToken: cancellationToken);
        else if (change.ResolvedVersion?.Contains('-') == true)
        {
            var executable = paths.VrcGetPath ?? throw new IOException("The bundled vrc-get tool is required for exact prerelease versions.");
            var current = (await workspace.ReadAsync(project, cancellationToken)).LockedPackages.GetValueOrDefault(change.Id) ?? change.InstalledVersion;
            var action = "install";
            if (!string.IsNullOrWhiteSpace(current))
            {
                try { action = new SemanticVersion(change.ResolvedVersion).CompareTo(new(current)) < 0 ? "downgrade" : "upgrade"; } catch (ArgumentException) { }
            }
            result = await runner.RunAsync(executable, [action, change.Id, change.ResolvedVersion, "--prerelease", "-p", project, "-y"], project, cancellationToken: cancellationToken);
        }
        else
            result = await runner.VpmAsync(["add", "package", change.Id + "@" + change.ResolvedVersion, "-p", project], project, cancellationToken: cancellationToken);
        await EmitPackageOutput(result, project, change.Id, progress, cancellationToken);
        return result;
    }
    private async Task EmitPackageOutput(ToolResult result, string project, string package, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        var snapshot = await workspace.ReadAsync(project, cancellationToken);
        var added = result.ExitCode == 0 && Regex.IsMatch(result.Output, @"(?im)Added\s+package\s+" + Regex.Escape(package) + @"\b");
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"(?i)Failed to remove package ([a-zA-Z0-9._-]+)");
            if (added && match.Success && !snapshot.Packages.ContainsKey(match.Groups[1].Value) && !snapshot.LockedPackages.ContainsKey(match.Groups[1].Value) && !Directory.Exists(Path.Combine(project, "Packages", match.Groups[1].Value)))
                progress?.Report(new(DateTime.UtcNow, "VPM", $"Legacy cleanup: {match.Groups[1].Value} is already absent; nothing to remove.", EventSeverity.Info, project, line));
            else progress?.Report(new(DateTime.UtcNow, "VPM", line.TrimEnd('\r'), Regex.IsMatch(line, @"(?i)\b(ERR|ERROR)\b") ? EventSeverity.Error : EventSeverity.Info, project));
        }
    }
    public Task<ToolResult> ResolveAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken) => runner.VpmAsync(["resolve", "project", project], project, progress, cancellationToken);

    public async Task ImportAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        if (workspace.IsBusy(plan.Path)) throw new IOException("The project is open in Unity. Close it before importing.");
        var editor = plan.UnityEditorPath;
        ValidateEditor(editor);
        var imports = (plan.Creation is null ? plan.ExtraImports : new[] { plan.Creation.UnityPackagePath }.Concat(plan.Creation.ExtraImports).Concat(plan.ExtraImports)).Select(x => ProjectRules.NormalizePath(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in imports) if (!File.Exists(file)) throw new IOException("UnityPackage not found: " + file);
        var editorFolder = Path.Combine(plan.Path, "Assets", "Editor"); Directory.CreateDirectory(editorFolder);
        var bridgeDestination = Path.Combine(editorFolder, "VrcSetupPostImport.cs");
        if (File.Exists(bridgeDestination) && AtomicFile.Hash(bridgeDestination) != AtomicFile.Hash(paths.BridgePath))
        {
            var backup = Path.Combine(plan.Path, ".vrcsetup", "backups", "bridge-" + Guid.NewGuid().ToString("N") + ".cs");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(bridgeDestination, backup);
        }
        File.Copy(paths.BridgePath, bridgeDestination, true);
        var stateDirectory = Path.Combine(plan.Path, ".vrcsetup"); Directory.CreateDirectory(stateDirectory);
        var importPlan = Path.Combine(stateDirectory, "import-plan.json");
        await AtomicFile.WriteJsonAsync(importPlan, new { packages = imports }, cancellationToken);
        if (plan.Creation is not null)
            await AtomicFile.WriteJsonAsync(Path.Combine(stateDirectory, "platforms.json"), new { targets = plan.Creation.PublishingPlatforms, pending = true }, cancellationToken);
        var arguments = new List<string> { "-projectPath", plan.Path, "-executeMethod", "VrcSetupPostImport.RunImport", "-vrcsetupImportPlan", importPlan, "-batchmode" };
        if (plan.Creation is not null) arguments.AddRange(["-buildTarget", plan.Creation.BuildTarget]);
        var log = NewUnityLog("import"); arguments.AddRange(["-logFile", log]);
        var result = await runner.RunAsync(editor, arguments, progress: progress, phase: "Unity import", cancellationToken: cancellationToken, timeout: TimeSpan.FromMinutes(45), logFile: log);
        VerifyUnityLog(result, log);
    }
    public async Task SettleAsync(ProjectPlan plan, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        var editor = plan.UnityEditorPath;
        ValidateEditor(editor);
        var log = NewUnityLog("settle");
        var arguments = new List<string> { "-projectPath", plan.Path, "-executeMethod", "VrcSetupPostImport.Run", "-batchmode", "-logFile", log };
        if (plan.Creation is not null) arguments.AddRange(["-buildTarget", plan.Creation.BuildTarget]);
        var result = await runner.RunAsync(editor, arguments, progress: progress, phase: "Unity settle", cancellationToken: cancellationToken, timeout: TimeSpan.FromMinutes(15), logFile: log);
        VerifyUnityLog(result, log);
    }
    private static void VerifyUnityLog(ToolResult result, string log)
    {
        var output = File.Exists(log) ? File.ReadAllText(log) : "";
        if (result.ExitCode != 0 || !output.Contains("[vrc-setup] Post-import settle complete", StringComparison.Ordinal) || Regex.IsMatch(output, @"(?im)(^.*error CS\d+|Script compilation failed|Import failed:|Import cancelled:|project remains incomplete|Fatal Error|Crash!!!)"))
            throw new IOException("Unity import or compilation did not complete. Project remains incomplete. Log: " + log);
    }
    public async Task<bool> RegisterAsync(string project, IProgress<OperationEvent>? progress, CancellationToken cancellationToken)
    {
        var result = await runner.VpmAsync(["add", "project", project], progress: progress, cancellationToken: cancellationToken);
        return !ProjectRules.ToolFailed(result) && !Regex.IsMatch(result.Output, @"(?im)\b(ERR|ERROR)\b");
    }
    private string NewUnityLog(string phase)
    {
        Directory.CreateDirectory(paths.Logs); return Path.Combine(paths.Logs, $"unity-{phase}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
    }
    public static void ValidateEditor(string editor)
    {
        if (!File.Exists(editor) || !Path.GetFileName(editor).Equals("Unity.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Select a valid Unity Editor executable in Settings.");
    }
    public static bool HasBuildSupport(string editor, string target) => target switch
    {
        "StandaloneWindows64" => File.Exists(editor),
        "Android" => Directory.Exists(Path.Combine(Path.GetDirectoryName(editor)!, "Data", "PlaybackEngines", "AndroidPlayer")),
        "iOS" => Directory.Exists(Path.Combine(Path.GetDirectoryName(editor)!, "Data", "PlaybackEngines", "iOSSupport")),
        _ => false
    };
    public static (string Version, string Path)[] FindEditors()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Select(x => (Version: Path.GetFileName(x), Path: Path.Combine(x, "Editor", "Unity.exe"))).Where(x => File.Exists(x.Path)).ToArray() : [];
    }
}
