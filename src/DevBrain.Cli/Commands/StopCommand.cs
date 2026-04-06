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
        var pidPath = Path.Combine(SettingsLoader.ResolveDataPath("~/.devbrain"), "daemon.pid");

        if (!File.Exists(pidPath))
        {
            ConsoleFormatter.PrintWarning("No PID file found. Daemon may not be running.");
            return;
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
