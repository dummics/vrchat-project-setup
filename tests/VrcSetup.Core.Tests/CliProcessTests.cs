using System.Text.Json;
using VrcSetup.Core;
using VrcSetup.Infrastructure;
using Xunit;

namespace VrcSetup.Tests;

public sealed class CliProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vrcsetup-cli-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ToolRunner runner = new();
    private string Home => Path.Combine(root, "home");
    private string Cli => typeof(VrcSetup.Cli.Program).Assembly.Location;
    public CliProcessTests() => Directory.CreateDirectory(root);
    private Task<ToolResult> Run(params string[] args) => runner.RunAsync(ToolRunner.FindExecutable("dotnet")!, new[] { Cli, "--home", Home }.Concat(args), root);
    [Fact] public async Task RedirectedNoArgsReturnsHelpWithoutPrompt()
    {
        var result = await Run(); Assert.Equal(0, result.ExitCode); Assert.Contains("vrcsetup projects", result.StdOut);
    }
    [Fact] public async Task JsonPresetsEmptyResultIsAnArray()
    {
        var result = await Run("presets", "list", "-Json");
        Assert.Equal(0, result.ExitCode); using var json = JsonDocument.Parse(result.StdOut); Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind); Assert.Equal(0, json.RootElement.GetArrayLength());
    }
    [Fact] public async Task InvalidOptionFailsWithReadableStderr()
    {
        var result = await Run("projects", "--invalid-option"); Assert.Equal(1, result.ExitCode); Assert.Contains("Unknown option", result.StdErr);
    }
    [Fact] public async Task ReadonlyJsonProjectAndPackageContractsKeepTheirFields()
    {
        var project = Path.Combine(root, "Project [PC] & café"); Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "Packages")); Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(project, "Packages", "vpm-manifest.json"), "{\"dependencies\":{\"tool\":{\"version\":\"1.0.0\"}}}");
        await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.22f1");
        var configure = await Run("config", "set", "--root", root); Assert.Equal(0, configure.ExitCode);
        var result = await Run("projects", "-Json", "-Sort", "name"); Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StdOut); var row = document.RootElement[0];
        Assert.Equal(["Name", "Kind", "UnityVersion", "PackageCount", "LastUpdatedUtc", "Status", "Path"], row.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal(project, row.GetProperty("Path").GetString());
        var packages = await Run("packages", "list", project, "--json"); Assert.Equal(0, packages.ExitCode);
        using var list = JsonDocument.Parse(packages.StdOut); Assert.Equal("tool", list.RootElement[0].GetProperty("Package").GetString()); Assert.Equal("1.0.0", list.RootElement[0].GetProperty("Version").GetString());
    }
    [Theory][InlineData("pwsh")][InlineData("powershell")]
    public async Task CommandCanBeInvokedFromPowerShellHosts(string host)
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = host == "powershell" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe") : ToolRunner.FindExecutable("pwsh");
        Assert.NotNull(executable);
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = Path.Combine(root, host + ".ps1");
        await File.WriteAllTextAsync(script, "& " + Quote(ToolRunner.FindExecutable("dotnet")!) + " " + Quote(Cli) + " --home " + Quote(Path.Combine(Home, host)) + " presets list -Json\nexit $LASTEXITCODE\n");
        var result = await runner.RunAsync(executable!, ["-NoProfile", "-NonInteractive", "-File", script], root);
        Assert.Equal(0, result.ExitCode); using var document = JsonDocument.Parse(result.StdOut); Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
    }
    [Fact] public async Task CorruptConfigCanBeRecoveredThroughCliReset()
    {
        await Run("config", "show"); await File.WriteAllTextAsync(Path.Combine(Home, "config.json"), "invalid");
        var result = await Run("config", "reset", "--yes"); Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, (await Run("config", "show")).ExitCode);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
