using Spectre.Console;
using VrcSetup.Cli;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class UsabilityContractsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-usability-" + Guid.NewGuid().ToString("N"));
    [Fact] public void LegacyTerminalPanelsAndWorkspaceRenderWithSpecialNames()
    {
        using var writer = new StringWriter(); var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) }); console.Profile.Width = 100;
        console.Write(LegacyTerminal.Menu("VRChat Project Setup", LegacyTerminal.HomeChoices, 0));
        console.Write(LegacyTerminal.Workspace("Avatar [PC] & café", [new("tool", "Tool [PC]", "1.0.0", "latest", "Will be changed", false, true, 1)], 0, true));
        var text = writer.ToString(); Assert.Contains("IMPORT AVATAR OR ASSET", text); Assert.Contains("My projects", text); Assert.Contains("Tool [PC]", text); Assert.Contains("After saving", text); Assert.Contains("Save changes", text);
        Assert.Contains("Activity and recovery", LegacyTerminal.HomeChoices);
    }
    [Fact] public void DeveloperFlagsAreExplicitAndDoNotMutateSavedPreferences()
    {
        var config = new AppConfiguration(); var app = new VrcSetupApplication(root, ["--developer", "--trace-tools"]); app.ConfigureDiagnostics(config);
        Assert.True(app.Diagnostics.DeveloperMode); Assert.True(app.Diagnostics.TraceTools); Assert.False(config.Diagnostics.DeveloperMode);
        Assert.True(new CommandLine(["projects", "--trace-tools", "--verbose"]).Has("tracetools"));
    }
    [Fact] public void StartupErrorsArePersistedWithoutEnablingDeveloperMode()
    {
        var log = new DiagnosticLog(new AppPaths(root)); log.Write("Startup", "Quiet normal startup"); Assert.False(File.Exists(log.FilePath));
        log.Write("Startup", "A real failure", new IOException("Missing project metadata"));
        var text = File.ReadAllText(log.FilePath); Assert.Contains("IOException", text); Assert.Contains("Missing project metadata", text);
    }
    [Fact] public async Task BrokenProjectDoesNotHideTheOtherProjects()
    {
        var app = new VrcSetupApplication(Path.Combine(root, "home"));
        var good = Path.Combine(root, "projects", "Good"); var bad = Path.Combine(root, "projects", "Bad");
        foreach (var project in new[] { good, bad }) { Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "Packages", "tool")); }
        await File.WriteAllTextAsync(Path.Combine(bad, "Packages", "tool", "package.json"), "{\"name\":\"tool\",\"version\":{}}");
        var snapshots = await app.Projects.ScanAsync(Path.Combine(root, "projects"));
        Assert.Equal(2, snapshots.Length); Assert.Equal("Needs attention", snapshots.Single(x => x.Path == bad).Status); Assert.Contains(snapshots, x => x.Path == good);
    }
    [Fact] public async Task CompletedStateIsRecognizedWithBothSerializerCasings()
    {
        var app = new VrcSetupApplication(Path.Combine(root, "home")); var project = Path.Combine(root, "project");
        Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "Packages"));
        await AtomicFile.WriteJsonAsync(Path.Combine(project, ".vrcsetup", "state.json"), new SetupState { Completed = true });
        Assert.False((await app.Projects.ReadAsync(project)).Incomplete);
        await File.WriteAllTextAsync(Path.Combine(project, ".vrcsetup", "state.json"), "{\"completed\":true}");
        Assert.False((await app.Projects.ReadAsync(project)).Incomplete);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
