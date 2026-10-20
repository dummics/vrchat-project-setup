using VrcSetup_Desktop.ViewModels;
using VrcSetup.Infrastructure;

namespace VrcSetup_Desktop.Verification;

internal static class ConcurrencyChecks
{
    public static async Task<string[]> RunAsync(MainViewModel vm, FlowInteraction ui)
    {
        var checks = new List<string>();
        void Require(bool condition, string message)
        { if (!condition) throw new IOException(message); checks.Add(message); }
        var project = vm.Projects.First();
        var manifest = Path.Combine(project.Path, "Packages", "vpm-manifest.json");
        var before = await File.ReadAllTextAsync(manifest);
        await vm.OpenProjectsAsync([project]);
        vm.StageWorkspaceVersion("gogoloco", "latest");
        ui.PendingReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply = vm.ApplyChangesCommand.ExecuteAsync(null);
        try
        {
            for (var i = 0; i < 200 && ui.LastReview is null && !apply.IsCompleted; i++) await Task.Delay(25);
            Require(ui.LastReview is not null && !apply.IsCompleted && vm.IsBusy && vm.IsApplyingPackages, "Apply holds immutable reviewed intent while its worker is pending");
            vm.StageWorkspaceVersion("gogoloco", "1.0.0");
            Require(vm.Packages.First(x => x.Id == "gogoloco").SelectedVersion == "latest", "The editor being applied cannot mutate its captured intent");
            Require(await vm.NavigateAsync("Activity") && vm.CurrentPage == "Activity", "Activity remains reachable during package apply");
            Require(await vm.NavigateAsync("Presets") && vm.CurrentPage == "Presets", "Preset editor remains reachable during package apply");
            var original = vm.PresetPackages.First(x => x.Id == "gogoloco").Version;
            vm.StagePresetVersion("gogoloco", "latest");
            Require(vm.PresetDirty, "Independent preset edits are accepted during package apply");
            await vm.SaveEditedPresetCommand.ExecuteAsync(null);
            Require(!vm.PresetDirty && vm.IsBusy, "Independent preset saves finish before the package worker");
            vm.StagePresetVersion("gogoloco", original); await vm.SaveEditedPresetCommand.ExecuteAsync(null);
            await vm.OpenProjectPackagesCommand.ExecuteAsync(project);
            Require(vm.CurrentPage == "Presets" && vm.Status.Contains("being updated"), "A second manager cannot read a project being mutated by apply");
            var confirmations = ui.Confirmations;
            await vm.ResetSettingsCommand.ExecuteAsync(null);
            Require(ui.Confirmations == confirmations && vm.IsBusy && !vm.CanUseUpdateAction, "Reset and update remain excluded while package work is active");
            Require(ui.LastReview!.Projects.Single().DesiredPolicy.GetValueOrDefault("gogoloco") == "latest", "Concurrent preset changes do not rewrite the reviewed project policy");
        }
        finally
        {
            ui.PendingReview.TrySetResult(false);
            await apply; ui.PendingReview = null;
        }
        Require(!vm.IsBusy && !vm.IsApplyingPackages && vm.CurrentPage == "Presets", "Late package completion preserves the user's current page and clears busy state");
        Require(await File.ReadAllTextAsync(manifest) == before, "Concurrency review fixture never changes project files");
        await vm.OpenProjectsAsync([project]);
        // Hold the real catalogue lease so reload must yield before committing UI state.
        var catalogLease = await AtomicFile.LockAsync(Path.Combine(vm.Application.Paths.Cache, "catalog.lock"));
        Task reload;
        try
        {
            reload = vm.OpenProjectsAsync([project], reload: true);
            await Task.Delay(75);
            Require(!reload.IsCompleted, "Manager reload yields while its catalogue write is pending");
            Require(await vm.NavigateAsync("Activity"), "Navigation proceeds while a previous manager reload is pending");
        }
        finally { catalogLease.Dispose(); }
        await reload;
        Require(vm.CurrentPage == "Activity", "A stale manager reload cannot reopen its old page after navigation");
        Require(vm.ErrorMessage.Length == 0, "Concurrent navigation and reload finish without hidden errors");
        ui.ClearReview();
        return checks.ToArray();
    }
}
