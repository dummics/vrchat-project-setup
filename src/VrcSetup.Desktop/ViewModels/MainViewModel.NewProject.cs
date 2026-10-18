namespace VrcSetup_Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public async Task CreateEmptyProjectAsync(string name, string? preset, string platform) => await Guard(async () =>
    {
        if (!await FindEditorAsync(true)) return;
        var choices = config.Copy(); choices.UnityEditorPath = UnityEditorPath; choices.UnityProjectsRoot = ProjectsRoot;
        var plan = await Task.Run(() => app.PlanEmptyProjectAsync(name, choices, preset == DefaultPresetKey ? null : preset, platform, lifetime.Token));
        await app.Imports.EnqueueAsync(plan, RequestedParallelism, token: lifetime.Token);
        CurrentPage = "Create";
    });
}
