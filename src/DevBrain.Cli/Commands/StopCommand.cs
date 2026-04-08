using System.CommandLine;
using System.Diagnostics;
using DevBrain.Cli.Output;
using DevBrain.Core;

namespace DevBrain.Cli.Commands;

public class StopCommand : Command
{
    public StopCommand() : base("stop", "Stop the DevBrain daemon")
    {
        SetAction(Execute);
    }

    private static async Task Execute(ParseResult pr)
    {
        var dataPath = SettingsLoader.ResolveDataPath("~/.devbrain");
        var pidPath = Path.Combine(dataPath, "daemon.pid");

        if (!File.Exists(pidPath))
        {
            ConsoleFormatter.PrintWarning("No PID file found. Daemon may not be running.");
            return;
        }

        // If tray app is running, write stopped sentinel so it doesn't auto-restart
        var trayLockPath = Path.Combine(dataPath, "tray.lock");
        if (File.Exists(trayLockPath))
        {
            var trayPidText = (await File.ReadAllTextAsync(trayLockPath)).Trim();
            if (int.TryParse(trayPidText, out var trayPid))
            {
                try
                {
                    Process.GetProcessById(trayPid);
                    // Tray is alive — write sentinel to prevent auto-restart
                    var sentinelPath = Path.Combine(dataPath, "stopped");
                    await File.WriteAllTextAsync(sentinelPath, "stopped by cli");
                }
                catch (ArgumentException)
                {
                    // Tray is dead — no sentinel needed
                }
            }
        }

        var pidText = (await File.ReadAllTextAsync(pidPath)).Trim();

        if (!int.TryParse(pidText, out var pid))
        {
            ConsoleFormatter.PrintError($"Invalid PID file content: {pidText}");
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            ConsoleFormatter.PrintSuccess($"Daemon (PID {pid}) stopped.");
        }
        catch (ArgumentException)
        {
            ConsoleFormatter.PrintWarning($"No process found with PID {pid}. Daemon may have already stopped.");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to stop daemon: {ex.Message}");
        }

        try
        {
            File.Delete(pidPath);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
