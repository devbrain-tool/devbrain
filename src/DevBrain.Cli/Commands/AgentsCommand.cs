using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class AgentsCommand : Command
{
    public AgentsCommand() : base("agents", "List agents or trigger an agent run")
    {
        var runCommand = new Command("run", "Trigger an agent to run");
        var nameArgument = new Argument<string>("name")
        {
            Description = "Name of the agent to run"
        };
        runCommand.Add(nameArgument);
        runCommand.SetAction(async (pr) =>
        {
            var name = pr.GetValue(nameArgument)!;
            var client = new DevBrainHttpClient();

            if (!await client.IsHealthy())
            {
                ConsoleFormatter.PrintError("Daemon is not running.");
                return;
            }

            try
            {
                var response = await client.Post($"/api/v1/agents/{name}/run");
                if (response.IsSuccessStatusCode)
                {
                    ConsoleFormatter.PrintSuccess($"Agent '{name}' triggered successfully.");
                }
                else
                {
                    ConsoleFormatter.PrintError($"Failed to trigger agent '{name}': {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                ConsoleFormatter.PrintError($"Failed to trigger agent: {ex.Message}");
            }
        });

        Add(runCommand);
        SetAction(ExecuteList);
    }

    private static async Task ExecuteList(ParseResult pr)
    {
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var json = await client.GetJson("/api/v1/agents");

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning("No agents found.");
                return;
            }

            Console.WriteLine($"Found {json.GetArrayLength()} agent(s):\n");

            foreach (var item in json.EnumerateArray())
            {
                var name = item.GetPropertyOrDefault("name", "(unnamed)");
                var status = item.GetPropertyOrDefault("status", "unknown");

                Console.ForegroundColor = status.Equals("running", StringComparison.OrdinalIgnoreCase)
                    ? ConsoleColor.Green
                    : ConsoleColor.Cyan;
                Console.Write($"  [{status}]");
                Console.ResetColor();
                Console.WriteLine($" {name}");
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch agents: {ex.Message}");
        }
    }
}
