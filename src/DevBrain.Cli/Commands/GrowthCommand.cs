using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class GrowthCommand : Command
{
    public GrowthCommand() : base("growth", "Show developer growth report")
    {
        var milestonesCmd = new Command("milestones", "Show milestones");
        milestonesCmd.SetAction(async (pr) =>
        {
            var client = new DevBrainHttpClient();
            if (!await client.IsHealthy()) { ConsoleFormatter.PrintError("Daemon is not running."); return; }

            var json = await client.GetJson("/api/v1/growth/milestones");
            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning("No milestones yet.");
                return;
            }

            Console.WriteLine($"\n  Milestones ({json.GetArrayLength()}):\n");
            foreach (var item in json.EnumerateArray())
            {
                var type = item.GetPropertyOrDefault("type", "?");
                var desc = item.GetPropertyOrDefault("description", "");
                var color = type switch
                {
                    "First" => ConsoleColor.Cyan,
                    "Streak" => ConsoleColor.Yellow,
                    "Improvement" => ConsoleColor.Green,
                    _ => ConsoleColor.Gray
                };
                Console.ForegroundColor = color;
                Console.Write($"  [{type}] ");
                Console.ResetColor();
                Console.WriteLine(desc);
            }
            Console.WriteLine();
        });

        var resetCmd = new Command("reset", "Wipe all growth data");
        resetCmd.SetAction(async (pr) =>
        {
            var client = new DevBrainHttpClient();
            if (!await client.IsHealthy()) { ConsoleFormatter.PrintError("Daemon is not running."); return; }
            var response = await client.Delete("/api/v1/growth");
            if (response.IsSuccessStatusCode)
                ConsoleFormatter.PrintSuccess("All growth data has been cleared.");
            else
                ConsoleFormatter.PrintError("Failed to clear growth data.");
        });

        Add(milestonesCmd);
        Add(resetCmd);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var client = new DevBrainHttpClient();
        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var json = await client.GetJson("/api/v1/growth");

            if (json.TryGetProperty("status", out var status) && status.GetString() == "no_data")
            {
                ConsoleFormatter.PrintWarning("No growth reports yet. The growth agent runs weekly (Monday 8 AM).");
                return;
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Growth Report");
            Console.ResetColor();

            if (json.TryGetProperty("narrative", out var narr) && narr.ValueKind == JsonValueKind.String)
            {
                Console.WriteLine();
                Console.WriteLine($"  {narr.GetString()}");
            }

            Console.WriteLine();

            if (json.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in metrics.EnumerateArray())
                {
                    var dim = m.GetPropertyOrDefault("dimension", "?");
                    var val = m.TryGetProperty("value", out var v) ? v.GetDouble() : 0;
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  {dim,-25}");
                    Console.ResetColor();
                    Console.WriteLine($"{val:F2}");
                }
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch growth report: {ex.Message}");
        }
    }
}
