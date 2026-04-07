using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class ReplayCommand : Command
{
    private readonly Argument<string?> _pathArg = new("path")
    {
        Description = "File path to get the decision chain for",
        Arity = ArgumentArity.ZeroOrOne
    };

    private readonly Option<string?> _decisionOption = new("--decision")
    {
        Description = "Graph node ID of a specific decision to trace"
    };

    public ReplayCommand() : base("replay", "Show the decision chain for a file or decision")
    {
        Add(_pathArg);
        Add(_decisionOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var path = pr.GetValue(_pathArg);
        var decisionId = pr.GetValue(_decisionOption);
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            JsonElement json;

            if (!string.IsNullOrEmpty(decisionId))
            {
                json = await client.GetJson($"/api/v1/replay/decision/{Uri.EscapeDataString(decisionId)}");
            }
            else if (!string.IsNullOrEmpty(path))
            {
                json = await client.GetJson($"/api/v1/replay/file/{Uri.EscapeDataString(path)}");
            }
            else
            {
                ConsoleFormatter.PrintError("Provide a file path or --decision <id>.");
                return;
            }

            var narrative = json.GetPropertyOrDefault("narrative", "");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Decision Chain");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"  {narrative}");
            Console.WriteLine();

            if (json.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in steps.EnumerateArray())
                {
                    var stepType = step.GetPropertyOrDefault("stepType", "Decision");
                    var summary = step.GetPropertyOrDefault("summary", "(no summary)");
                    var timestamp = step.GetPropertyOrDefault("timestamp", "");

                    var color = stepType switch
                    {
                        "Decision" => ConsoleColor.Green,
                        "DeadEnd" => ConsoleColor.Red,
                        "Error" => ConsoleColor.Yellow,
                        "Resolution" => ConsoleColor.Blue,
                        _ => ConsoleColor.Gray
                    };

                    Console.ForegroundColor = color;
                    Console.Write($"  [{stepType}] ");
                    Console.ResetColor();

                    if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var dt))
                        Console.Write($"{dt:yyyy-MM-dd HH:mm} ");

                    Console.WriteLine(summary);

                    if (step.TryGetProperty("filesInvolved", out var files) && files.ValueKind == JsonValueKind.Array)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        foreach (var f in files.EnumerateArray())
                            Console.WriteLine($"    {f.GetString()}");
                        Console.ResetColor();
                    }
                }
            }

            Console.WriteLine();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ConsoleFormatter.PrintWarning("No decision chain found for this file.");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch decision chain: {ex.Message}");
        }
    }
}
