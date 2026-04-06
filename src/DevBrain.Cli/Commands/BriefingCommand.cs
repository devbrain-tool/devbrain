using System.CommandLine;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class BriefingCommand : Command
{
    private readonly Option<bool> _generateOption = new("--generate")
    {
        Description = "Force regenerate the briefing"
    };

    public BriefingCommand() : base("briefing", "Show or generate your daily briefing")
    {
        Add(_generateOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var generate = pr.GetValue(_generateOption);
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            if (generate)
            {
                Console.WriteLine("Generating briefing...");
                var response = await client.Post("/api/v1/briefings/generate");
                if (!response.IsSuccessStatusCode)
                {
                    ConsoleFormatter.PrintError("Failed to generate briefing.");
                    return;
                }
            }

            var json = await client.GetJson("/api/v1/briefings/latest");

            var content = json.GetPropertyOrDefault("content", "");
            if (string.IsNullOrWhiteSpace(content))
            {
                ConsoleFormatter.PrintWarning("No briefing available. Try --generate to create one.");
                return;
            }

            var date = json.GetPropertyOrDefault("date", "today");
            ConsoleFormatter.PrintBox($"Briefing - {date}", content);
        }
        catch (HttpRequestException)
        {
            ConsoleFormatter.PrintWarning("No briefing available. Try --generate to create one.");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch briefing: {ex.Message}");
        }
    }
}
