using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using VrcSetup.Core;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.Verification;

// Explicit, opt-in verification on a real UI and VPM queue. Never owner projects.
internal static class ProjectsFlowVerification
{
    internal static async Task<VrcSetupApplication> PrepareAsync(string editor, string evidence)
    {
        if (Directory.Exists(evidence)) throw new IOException("Choose unused verification evidence.");
        Directory.CreateDirectory(evidence);
        var app = new VrcSetupApplication(Path.Combine(evidence, "home"), ["--no-catalog-refresh", "--no-update-check", "--no-editor-discovery"]);
        var config = await app.Configuration.LoadAsync();
        config.UnityEditorPath = editor; config.UnityProjectsRoot = Path.Combine(evidence, "projects"); config.Language = "en";
        Directory.CreateDirectory(config.UnityProjectsRoot);
        var ids = new[] { ProjectRules.Resolver, ProjectRules.BaseSdk, ProjectRules.AvatarSdk, "com.poiyomi.toon", "com.vrcfury.vrcfury", "nadena.dev.modular-avatar", "com.llealloo.audiolink", "vrchat.blackstartx.gesture-manager", "com.vrcfury.temp" };
        config.PackagePresets["Regression fixture"] = ids.ToDictionary(x => x, _ => "latest");
        config.FavoritePackages.Add("nadena.dev.ndmf");
        await app.Configuration.SaveAsync(config);
        await app.Catalog.RememberAsync([new("nadena.dev.ndmf", "NDMF", "latest")]);
        return app;
    }

    internal static async Task RunAsync(MainWindow window, string editor, string evidence)
    {
        await window.Page.Initialized;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var page = window.Page; var vm = page.ViewModel; var app = vm.Application;
        var original = JsonSerializer.Serialize((await app.Configuration.LoadAsync()).PackagePresets);
        await vm.BeginNewProjectAsync(); vm.EditingPreset = "Regression fixture";
        var name = (TextBox)page.FindName("NewProjectNameInput"); var button = (Button)page.FindName("NewProjectCreate");
        name.Text = "folder/invalid"; await Task.Delay(100);
        if (button.IsEnabled || vm.NewProjectNameError.Length == 0 || app.Imports.Snapshot().Length != 0) throw new IOException("Invalid name was not blocked at the field.");
        name.Text = "Native UI package mix"; await Task.Delay(100);
        vm.AddLibraryPackageCommand.Execute(vm.LibraryPackages.Single(x => x.Id == "nadena.dev.ndmf"));
        if (vm.PresetPackages.Any(x => x.Id == "com.vrcfury.temp") || !vm.PresetDirty) throw new IOException("Generated package or one-time selection regression.");
        if (!button.IsEnabled || !button.Command.CanExecute(button.CommandParameter)) throw new IOException("Valid native form did not enable creation.");
        await UiSmokeTest.Render(page, Path.Combine(evidence, "new-project-native.png"));
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        while (app.Imports.Snapshot().Length == 0)
        {
            if (vm.ErrorMessage.Length > 0) throw new IOException(vm.ErrorMessage);
            await Task.Delay(100, timeout.Token);
        }
        while (app.Imports.Snapshot().Any(x => x.Active)) await Task.Delay(150, timeout.Token);
        var job = app.Imports.Snapshot().Single();
        if (job.State != "Completed") throw new IOException("Real UI creation failed: " + job.Message);
        await Task.Delay(250);
        if (vm.CurrentPage != "Projects" || vm.ImportJobs.Any(x => x.Id == job.Id)) throw new IOException("Creation leaked into import presentation.");
        if (original != JsonSerializer.Serialize((await app.Configuration.LoadAsync()).PackagePresets)) throw new IOException("One-time package changes overwrote the saved preset.");
        if (Directory.EnumerateFiles(app.Paths.Logs, "unity-*.log").Any()) throw new IOException("Empty creation unexpectedly started Unity.");
        var project = job.ProjectPath; ProjectRules.EnsureContained(evidence, project);
        var snapshot = await app.Projects.ReadAsync(project, timeout.Token);
        foreach (var id in job.Plan.Projects[0].DesiredPolicy.Keys)
            if (!snapshot.InstalledVersions.TryGetValue(id, out var actual) || snapshot.LockedPackages.GetValueOrDefault(id) != actual)
                throw new IOException("Installed package/lock mismatch: " + id);
        if (snapshot.Packages.ContainsKey("com.vrcfury.temp")) throw new IOException("Generated package was requested from VPM.");
        if (!snapshot.Packages.ContainsKey("nadena.dev.ndmf")) throw new IOException("Selecting an installed dependency did not make it explicit in VPM.");

        // VPM's removal proposal reports a conflict. The wrapper must decline it.
        await vm.OpenProjectsAsync([snapshot]);
        vm.ToggleWorkspacePackage(vm.Packages.Single(x => x.Id == "nadena.dev.ndmf"));
        await vm.ApplyChangesCommand.ExecuteAsync(null);
        snapshot = await app.Projects.ReadAsync(project, timeout.Token);
        if (!snapshot.Packages.ContainsKey("nadena.dev.ndmf") || !snapshot.InstalledVersions.ContainsKey("nadena.dev.ndmf") || !vm.ErrorMessage.Contains("another package needs it"))
            throw new IOException("VPM dependency conflict was not safely explained: " + vm.ErrorMessage);
        vm.ToggleWorkspacePackage(vm.Packages.Single(x => x.Id == "nadena.dev.ndmf"));
        vm.ErrorMessage = "";

        // A second independently created project gives a mixed-present bulk case.
        await vm.NavigateAsync("Projects");
        await File.AppendAllTextAsync(Path.Combine(evidence, "steps.log"), "Second: navigated\n");
        await vm.BeginNewProjectAsync(); vm.EditingPreset = "Regression fixture";
        await File.AppendAllTextAsync(Path.Combine(evidence, "steps.log"), "Second: form opened\n");
        vm.RemovePresetPackageCommand.Execute(vm.PresetPackages.Single(x => x.Id == "com.llealloo.audiolink"));
        name.Text = "Native UI second project"; await Task.Delay(250); page.UpdateLayout();
        await File.AppendAllTextAsync(Path.Combine(evidence, "steps.log"), "Second: name=" + vm.NewProjectName + " enabled=" + button.IsEnabled + " creating=" + vm.IsCreatingProject + "\n");
        if (vm.NewProjectName != name.Text || !button.IsEnabled || !vm.CreateConfiguredProjectCommand.CanExecute(null)) throw new IOException("Second native form did not enable creation: " + vm.NewProjectName);
        await vm.CreateConfiguredProjectCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(45));
        await File.AppendAllTextAsync(Path.Combine(evidence, "steps.log"), "Second: creation command returned; jobs=" + app.Imports.Snapshot().Length + " error=" + vm.ErrorMessage + "\n");
        while (!app.Imports.Snapshot().Any(x => x.Name == "Native UI second project"))
        { if (vm.IsErrorVisible) throw new IOException(vm.ErrorMessage); await Task.Delay(100, timeout.Token); }
        while (app.Imports.Snapshot().Any(x => x.Active)) await Task.Delay(150, timeout.Token);
        var second = app.Imports.Snapshot().Single(x => x.Name == "Native UI second project");
        if (second.State != "Completed") throw new IOException("Second creation failed: " + second.Message);
        var implicitSnapshot = await app.Projects.ReadAsync(second.ProjectPath, timeout.Token);
        if (implicitSnapshot.Packages.ContainsKey("nadena.dev.ndmf") || !implicitSnapshot.InstalledVersions.ContainsKey("nadena.dev.ndmf")) throw new IOException("Fixture lacks an implicit dependency.");
        await vm.OpenProjectsAsync([implicitSnapshot]);
        vm.StageWorkspaceVersion("nadena.dev.ndmf", "latest"); await vm.ApplyChangesCommand.ExecuteAsync(null);
        if (vm.IsErrorVisible || !(await app.Projects.ReadAsync(second.ProjectPath, timeout.Token)).Packages.ContainsKey("nadena.dev.ndmf")) throw new IOException("Real dependency promotion failed: " + vm.ErrorMessage);
        await vm.OpenProjectsAsync([snapshot, await app.Projects.ReadAsync(second.ProjectPath, timeout.Token)]);
        vm.StageWorkspaceVersion("com.llealloo.audiolink", "latest"); await vm.ApplyChangesCommand.ExecuteAsync(null);
        var secondSnapshot = await app.Projects.ReadAsync(second.ProjectPath, timeout.Token);
        if (vm.IsErrorVisible || !secondSnapshot.Packages.ContainsKey("com.llealloo.audiolink") || !secondSnapshot.InstalledVersions.ContainsKey("com.llealloo.audiolink")) throw new IOException("Mixed-present bulk Apply failed: " + vm.ErrorMessage);
        await vm.NavigateAsync("Projects");
        Directory.CreateDirectory(Path.Combine(project, "Assets", "Editor"));
        await File.WriteAllTextAsync(Path.Combine(project, "Assets", "Editor", "VrcSetupCompileProbe.cs"),
            "public static class VrcSetupCompileProbe { public static void Run() { System.IO.File.WriteAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath, \"../compile-passed.txt\"), \"Compiled\"); UnityEditor.EditorApplication.Exit(0); } }");
        var log = Path.Combine(evidence, "unity-compile.log");
        var start = new ProcessStartInfo(editor) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-batchmode", "-nographics", "-projectPath", project, "-executeMethod", "VrcSetupCompileProbe.Run", "-logFile", log }) start.ArgumentList.Add(arg);
        using var unity = Process.Start(start) ?? throw new IOException("Fixture Unity did not start.");
        try { await unity.WaitForExitAsync(timeout.Token); }
        catch { if (!unity.HasExited) unity.Kill(true); throw; }
        var output = await SharedLog.ReadAsync(log, timeout.Token);
        if (unity.ExitCode != 0 || !File.Exists(Path.Combine(project, "compile-passed.txt")) || System.Text.RegularExpressions.Regex.IsMatch(output, @"error CS\d+|Scripts have compiler errors", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new IOException("Fixture compilation failed: " + log);
        await UiSmokeTest.Render(page, Path.Combine(evidence, "projects-complete.png"));
        await AtomicFile.WriteJsonAsync(Path.Combine(evidence, "projects-flow.json"), new { Passed = true, NativeButtonInvoked = true, PresetUnchanged = true, DependencyConflictBlocked = true, DependencyPromotion = true, MixedBulkApply = true, UnityExit = unity.ExitCode, job.State, Packages = snapshot.InstalledVersions });
    }
}
