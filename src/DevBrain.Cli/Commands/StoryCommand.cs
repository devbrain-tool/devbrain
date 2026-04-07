using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class StoryCommand : Command
{
    private readonly Option<string?> _sessionOption = new("--session")
    {
        Description = "Session ID (defaults to latest)"
    };

    public StoryCommand() : base("story", "Show session story narrative")
    {
        Add(_sessionOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var sessionId = pr.GetValue(_sessionOption);
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            JsonElement json;

            if (!string.IsNullOrEmpty(sessionId))
            {
                json = await client.GetJson($"/api/v1/sessions/{Uri.EscapeDataString(sessionId)}/story");
            }
            else
            {
                // Get latest
                var sessionsJson = await client.GetJson("/api/v1/sessions?limit=1");
                if (sessionsJson.ValueKind != JsonValueKind.Array || sessionsJson.GetArrayLength() == 0)
                {
                    ConsoleFormatter.PrintWarning("No session stories available yet.");
                    return;
                }
                json = sessionsJson[0];
            }

            var narrative = json.GetPropertyOrDefault("narrative", "");
            var outcome = json.GetPropertyOrDefault("outcome", "");
            var duration = json.TryGetProperty("duration", out var d)
                ? d.ToString()
                : json.GetPropertyOrDefault("durationSeconds", "?") + "s";
            var obsCount = json.TryGetProperty("observationCount", out var oc) ? oc.GetInt32() : 0;
            var filesCount = json.TryGetProperty("filesTouched", out var fc) ? fc.GetInt32() : 0;
            var deadEnds = json.TryGetProperty("deadEndsHit", out var de) ? de.GetInt32() : 0;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  Session Story");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {obsCount} observations | {filesCount} files | {deadEnds} dead ends");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine(narrative);
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  Outcome: ");
            Console.ResetColor();
            Console.WriteLine(outcome);
            Console.WriteLine();
        }
        catch (HttpRequestException)
        {
            ConsoleFormatter.PrintWarning("No story available for this session.");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch story: {ex.Message}");
        }
    }
}
