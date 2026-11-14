using System.Security.Cryptography;
using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

// Small public package in a fresh project; no Unity, VCC registration or user
// project writes. Uses the bundled tool and its existing cache without repo updates.
internal static class PackageRepairVerification
{
    public static async Task RunAsync(string evidence)
    {
        evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Choose a fresh repair fixture.");
        var cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VrcSetup", "app", "cli");
        var tool = Path.Combine(cli, "Tools", "vrc-get.exe");
        if (!File.Exists(tool)) throw new IOException("Installed bundled package tool is unavailable.");
        var project = Path.Combine(evidence, "P");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "manifest.json"), new { dependencies = new Dictionary<string, string>() });
        await AtomicFile.WriteJsonAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), new { dependencies = new Dictionary<string, object>(), locked = new Dictionary<string, object>() });
        var paths = new AppPaths(Path.Combine(evidence, "home"));
        var runner = new ToolRunner(new DiagnosticLog(paths));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await Task.Run(() =>
        {
            using var mutex = new Mutex(false, OperatingSystem.IsWindows() ? @"Local\VrcSetup.Vpm.Commands" : "VrcSetup.Vpm.Commands");
            var acquired = false;
            try
            {
                while (!acquired) { timeout.Token.ThrowIfCancellationRequested(); try { acquired = mutex.WaitOne(100); } catch (AbandonedMutexException) { acquired = true; } }
                ToolResult Run(params string[] args)
                {
                    var result = runner.RunAsync(tool, args, project, cancellationToken: timeout.Token, timeout: TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
                    if (result.ExitCode != 0) throw new IOException("Package repair fixture command failed: " + string.Join(' ', args.Take(2)) + "; " + result.Output);
                    return result;
                }
                ToolResult ObserveInstallOrResolve(params string[] args)
                {
                    var result = runner.RunAsync(tool, args, project, cancellationToken: timeout.Token).GetAwaiter().GetResult();
                    if (result.ExitCode != 0 && !(result.ExitCode == 1 && result.Output.Trim().Equals("nothing to do", StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("Unexpected tool failure while observing repair behavior: " + result.Output);
                    return result;
                }
                const string id = "com.vrchat.core.vpm-resolver", version = "0.1.29";
                var toolVersion = Run("--version").Output.Trim();
                Run("install", id, version, "-p", project, "-y", "--no-update");
                var package = Path.Combine(project, "Packages", id);
                var script = Directory.EnumerateFiles(package, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).First();
                var original = SHA256.HashData(File.ReadAllBytes(script));
                var meta = script + ".meta";
                var metaHash = File.Exists(meta) ? SHA256.HashData(File.ReadAllBytes(meta)) : null;
                File.Delete(script);
                var repeatedInstall = ObserveInstallOrResolve("install", id, version, "-p", project, "-y", "--offline");
                var installRestoredMissingFile = File.Exists(script) && SHA256.HashData(File.ReadAllBytes(script)).SequenceEqual(original);
                var resolved = ObserveInstallOrResolve("resolve", "-p", project, "--offline");
                var resolveRestoredMissingFile = File.Exists(script) && SHA256.HashData(File.ReadAllBytes(script)).SequenceEqual(original);
                Run("reinstall", id, "-p", project, "-y", "--offline");
                if (!File.Exists(script) || !SHA256.HashData(File.ReadAllBytes(script)).SequenceEqual(original)) throw new IOException("Explicit reinstall did not restore the missing package file.");
                File.AppendAllText(script, "\n// deliberately changed by isolated repair fixture\n");
                ObserveInstallOrResolve("resolve", "-p", project, "--offline");
                var resolveRestoredChangedContent = SHA256.HashData(File.ReadAllBytes(script)).SequenceEqual(original);
                File.AppendAllText(script, "\n// deliberately changed again before explicit reinstall\n");
                Run("reinstall", id, "-p", project, "-y", "--offline");
                if (!SHA256.HashData(File.ReadAllBytes(script)).SequenceEqual(original)) throw new IOException("Explicit reinstall did not restore changed package content.");
                if (metaHash is not null && !SHA256.HashData(File.ReadAllBytes(meta)).SequenceEqual(metaHash)) throw new IOException("Reinstall changed published meta identity.");
                var packageManifest = Path.Combine(package, "package.json");
                File.Delete(packageManifest);
                Run("resolve", "-p", project, "--offline");
                if (!File.Exists(packageManifest)) throw new IOException("Resolve did not restore missing package metadata.");
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "package.json")));
                if (manifest.RootElement.GetProperty("version").GetString() != version) throw new IOException("Repair changed the selected version.");
                AtomicFile.WriteJsonAsync(Path.Combine(evidence, "repair.json"), new
                {
                    Passed = true, Tool = toolVersion, Package = id, Version = version, InstallRestoredMissingFile = installRestoredMissingFile,
                    SameVersionInstallExitCode = repeatedInstall.ExitCode, ResolveExitCode = resolved.ExitCode,
                    ResolveRestoredMissingFile = resolveRestoredMissingFile, ExplicitReinstallRestoredMissingFile = true,
                    ResolveRestoredChangedContent = resolveRestoredChangedContent,
                    ResolveRestoredMissingMetadata = true,
                    ExplicitReinstallRestoredChangedContent = true, PublishedMetaPreserved = true, UnityLaunched = false,
                    PersonalProjectsChanged = false, CreatorCompanionRegistration = false
                }).GetAwaiter().GetResult();
                Console.WriteLine($"PASS: {toolVersion}; install restored={installRestoredMissingFile}; resolve restored={resolveRestoredMissingFile}; explicit reinstall restored missing/changed content at the same version.");
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }, timeout.Token);
        // Exercise the actual package-plan driver too, including the case that
        // used to issue an ineffective same-version install before validation.
        var applicationPaths = new AppPaths(Path.Combine(evidence, "home"), cli);
        var workspace = new ProjectWorkspace(applicationPaths);
        var applicationRunner = new ToolRunner(new DiagnosticLog(applicationPaths), tool);
        var driver = new UnityProjectDriver(applicationPaths, applicationRunner, workspace, registerProjects: false);
        const string packageId = "com.vrchat.core.vpm-resolver", packageVersion = "0.1.29";
        var metadataPath = Path.Combine(project, "Packages", packageId, "package.json");
        File.Delete(metadataPath);
        var snapshot = await workspace.ReadAsync(project, timeout.Token);
        if (snapshot.InstalledVersions.ContainsKey(packageId) || snapshot.LockedPackages.GetValueOrDefault(packageId) != packageVersion)
            throw new IOException("Missing-metadata fixture did not expose the intended locked package state.");
        var plan = new ProjectPlan
        {
            Name = snapshot.Name, Path = project, ExpectedFingerprint = snapshot.Fingerprint,
            DesiredPolicy = new Dictionary<string, string> { [packageId] = packageVersion },
            Changes = [new(packageId, null, packageVersion, packageVersion, ChangeKind.Update)]
        };
        var outcome = await driver.ApplyPackagePlanAsync(plan, null, timeout.Token);
        var repairedSnapshot = await workspace.ReadAsync(project, timeout.Token);
        if (ProjectRules.ToolFailed(outcome) || !PackagePresence.Matches(plan.Changes[0], repairedSnapshot) ||
            repairedSnapshot.Packages.GetValueOrDefault(packageId) != packageVersion)
            throw new IOException("Application package plan did not restore the locked version and retain its explicit root: " + outcome.Output);
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "package-plan-repair.json"), new
        { Passed = true, Driver = nameof(UnityProjectDriver), Package = packageId, Version = packageVersion, ExplicitRootPreserved = true, UnityLaunched = false });
        Console.WriteLine("PASS: application package plan restored missing metadata at the locked version and retained its declared root.");
        var packageFolder = Path.GetDirectoryName(metadataPath)!;
        var scriptPath = Directory.EnumerateFiles(packageFolder, "*.cs", SearchOption.AllDirectories).First();
        var scriptHash = SHA256.HashData(await File.ReadAllBytesAsync(scriptPath, timeout.Token));
        await File.AppendAllTextAsync(scriptPath, "\n// fixture damage before application reinstall\n", timeout.Token);
        var sentinel = Path.Combine(project, "Assets", "Keep.txt");
        await File.WriteAllTextAsync(sentinel, "avatar content", timeout.Token);
        var profile = Path.Combine(project, ".vrcsetup", "package-profile.json");
        var vpmFile = Path.Combine(project, "Packages", "vpm-manifest.json");
        var beforeManifest = await File.ReadAllBytesAsync(vpmFile, timeout.Token);
        var planner = new ProjectPlanner(workspace, new PackageCatalog(applicationPaths, applicationRunner, new ConfigurationStore(applicationPaths), allowRepositorySetup: false));
        var reinstall = await planner.PlanPackageReinstallAsync(project, timeout.Token);
        var report = await new ProjectExecutor(workspace, driver).ApplyAsync(reinstall, cancellationToken: timeout.Token);
        var afterManifest = await File.ReadAllBytesAsync(vpmFile, timeout.Token);
        if (!report.Success || !SHA256.HashData(await File.ReadAllBytesAsync(scriptPath, timeout.Token)).SequenceEqual(scriptHash) ||
            await File.ReadAllTextAsync(sentinel, timeout.Token) != "avatar content" || File.Exists(profile) ||
            !beforeManifest.SequenceEqual(afterManifest))
            throw new IOException("Explicit application reinstall failed to restore files while preserving project choices: " + report.Results.Single().Message);
        var backup = report.Results.Single().BackupPath ?? throw new IOException("Explicit reinstall did not preserve package files for recovery.");
        if (!File.Exists(Path.Combine(backup, "packages.zip")) || File.Exists(Path.Combine(backup, "assets.zip")))
            throw new IOException("Restore must back up package files without copying Assets.");
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "explicit-package-reinstall.json"), new
        { Passed = true, Package = packageId, Version = packageVersion, FileRestored = true, ManifestPreserved = true,
            PackageArchiveSaved = true, AssetsArchiveSaved = false, AvatarAssetPreserved = true, UnityLaunched = false });
        Console.WriteLine("PASS: app reinstall restored damaged files with the original package version and project assets.");
    }
}
