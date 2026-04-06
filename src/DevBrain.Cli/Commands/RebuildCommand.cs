using System.CommandLine;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class RebuildCommand : Command
{
    private readonly Argument<string> _typeArgument = new("type")
    {
        Description = "What to rebuild: 'vectors' or 'graph'"
    };

    public RebuildCommand() : base("rebuild", "Rebuild vectors or graph index")
    {
        Add(_typeArgument);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var type = pr.GetValue(_typeArgument)!;

        if (type != "vectors" && type != "graph")
        {
            ConsoleFormatter.PrintError("Type must be 'vectors' or 'graph'.");
            return;
        }

        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            Console.WriteLine($"Rebuilding {type}...");
            var response = await client.Post($"/api/v1/admin/rebuild/{type}");
            if (response.IsSuccessStatusCode)
            {
                ConsoleFormatter.PrintSuccess($"Rebuild of {type} started successfully.");
            }
            else
            {
                ConsoleFormatter.PrintError($"Rebuild failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Rebuild failed: {ex.Message}");
        }
    }
}
