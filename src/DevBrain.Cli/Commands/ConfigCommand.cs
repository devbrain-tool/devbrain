using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class ConfigCommand : Command
{
    public ConfigCommand() : base("config", "Show or update settings")
    {
        var setCommand = new Command("set", "Set a configuration value");
        var keyArgument = new Argument<string>("key")
        {
            Description = "Setting key"
        };
        var valueArgument = new Argument<string>("value")
        {
            Description = "Setting value"
        };
        setCommand.Add(keyArgument);
        setCommand.Add(valueArgument);
        setCommand.SetAction(async (pr) =>
        {
            var key = pr.GetValue(keyArgument)!;
            var value = pr.GetValue(valueArgument)!;
            var client = new DevBrainHttpClient();

            if (!await client.IsHealthy())
            {
                ConsoleFormatter.PrintError("Daemon is not running.");
                return;
            }

            try
            {
                var response = await client.Put("/api/v1/settings", new { key, value });
                if (response.IsSuccessStatusCode)
                {
                    ConsoleFormatter.PrintSuccess($"Setting '{key}' updated to '{value}'.");
                }
                else
                {
                    ConsoleFormatter.PrintError($"Failed to update setting: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                ConsoleFormatter.PrintError($"Failed to update setting: {ex.Message}");
            }
        });

        Add(setCommand);
        SetAction(ExecuteShow);
    }

    private static async Task ExecuteShow(ParseResult pr)
    {
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var json = await client.GetJson("/api/v1/settings");

            if (json.ValueKind == JsonValueKind.Object)
            {
                var lines = new List<string>();
                foreach (var prop in json.EnumerateObject())
                {
                    lines.Add($"{prop.Name}: {prop.Value}");
                }

                ConsoleFormatter.PrintBox("Settings", string.Join("\n", lines));
            }
            else
            {
                ConsoleFormatter.PrintWarning("No settings available.");
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch settings: {ex.Message}");
        }
    }
}
