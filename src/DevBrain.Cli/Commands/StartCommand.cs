using System.CommandLine;
using System.Diagnostics;
using DevBrain.Cli.Output;
using DevBrain.Core;

namespace DevBrain.Cli.Commands;

public class StartCommand : Command
{
    public StartCommand() : base("start", "Start the DevBrain daemon")
    {
        SetAction(Execute);
    }

    private static async Task Execute(ParseResult pr)
    {
        var client = new DevBrainHttpClient();

        if (await client.IsHealthy())
        {
            ConsoleFormatter.PrintWarning("Daemon is already running.");
            return;
        }

        // Check if tray app is managing the daemon
        var dataPath = SettingsLoader.ResolveDataPath("~/.devbrain");
        var trayLockPath = Path.Combine(dataPath, "tray.lock");

        if (File.Exists(trayLockPath))
        {
            var lockPidText = (await File.ReadAllTextAsync(trayLockPath)).Trim();
            if (int.TryParse(lockPidText, out var trayPid))
            {
                try
                {
                    Process.GetProcessById(trayPid);
                    ConsoleFormatter.PrintWarning(
                        "Daemon is managed by the tray app. Use the tray menu to start it.");
                    return;
                }
                catch (ArgumentException)
                {
                    // Tray process is dead — stale lock, continue normally
                }
            }
        }

        var cliDir = AppContext.BaseDirectory;
        var daemonName = OperatingSystem.IsWindows() ? "devbrain-daemon.exe" : "devbrain-daemon";
        var daemonPath = Path.Combine(cliDir, daemonName);

        if (!File.Exists(daemonPath))
        {
            ConsoleFormatter.PrintError($"Daemon binary not found at: {daemonPath}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = daemonPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to start daemon: {ex.Message}");
            return;
        }

        Console.Write("Starting daemon");

        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            Console.Write(".");

            if (await client.IsHealthy())
            {
                Console.WriteLine();
                ConsoleFormatter.PrintSuccess("Daemon started successfully.");
                return;
            }
        }

        Console.WriteLine();
        ConsoleFormatter.PrintError("Daemon did not become healthy within 10 seconds.");
    }
}
