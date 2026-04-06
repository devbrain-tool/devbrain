using System.CommandLine;
using System.Diagnostics;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class DashboardCommand : Command
{
    public DashboardCommand() : base("dashboard", "Open the DevBrain dashboard in your browser")
    {
        SetAction(Execute);
    }

    private static Task Execute(ParseResult pr)
    {
        var url = $"http://localhost:{DevBrainHttpClient.DefaultPort}";

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }

            ConsoleFormatter.PrintSuccess($"Opening dashboard at {url}");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to open browser: {ex.Message}");
            Console.WriteLine($"Open manually: {url}");
        }

        return Task.CompletedTask;
    }
}
