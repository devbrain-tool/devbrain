using System.CommandLine;
using System.Diagnostics;
using DevBrain.Cli.Output;

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
