using VrcSetup.Infrastructure;

namespace VrcSetup.Cli;

public static class LifecycleCommand
{
    public static async Task<int> RunAsync(VrcSetupApplication app, string action, CommandLine command, CancellationToken token)
    {
        var lifecycle = new InstallationManager(app.Paths);
        switch (action)
        {
            case "install":
                var installed = await lifecycle.InstallAsync(command.Value("root"), token);
                Console.WriteLine("Installed: " + installed.Root);
                if (installed.PreviousInstall is not null) Console.WriteLine("Previous install preserved: " + installed.PreviousInstall);
                return 0;
            case "repair": await lifecycle.RepairAsync(command.Value("root"), token); Console.WriteLine("Installation and shortcuts verified."); return 0;
            case "uninstall":
                var confirmed = command.Has("yes") || Program.Confirm("Uninstall VRCSETUP? Your projects and settings will be preserved.");
                if (!confirmed) return 2;
                await lifecycle.UninstallAsync(command.Value("root"), confirmed, token); Console.WriteLine("Program removed; settings and Unity projects preserved."); return 0;
            default: throw new ArgumentException("Unknown maintenance action.");
        }
    }
}
