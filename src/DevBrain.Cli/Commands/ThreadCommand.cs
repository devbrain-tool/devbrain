using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class ThreadCommand : Command
{
    public ThreadCommand() : base("thread", "Show threads or active thread")
    {
        var listCommand = new Command("list", "List all threads");
        listCommand.SetAction(ExecuteList);
        Add(listCommand);

        SetAction(ExecuteActive);
    }

    private static async Task ExecuteActive(ParseResult pr)
    {
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var json = await client.GetJson("/api/v1/threads");

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning("No threads found.");
                return;
            }

            // Find active thread
            JsonElement? active = null;
            foreach (var item in json.EnumerateArray())
            {
                var state = item.GetPropertyOrDefault("state", "");
                if (string.Equals(state, "active", StringComparison.OrdinalIgnoreCase))
                {
                    active = item;
                    break;
                }
            }

            if (active is null)
            {
                ConsoleFormatter.PrintWarning("No active thread. Use 'thread list' to see all threads.");
                return;
            }

            PrintThread(active.Value, "Active Thread");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch threads: {ex.Message}");
        }
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
            var json = await client.GetJson("/api/v1/threads");

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning("No threads found.");
                return;
            }

            Console.WriteLine($"Found {json.GetArrayLength()} thread(s):\n");

            foreach (var item in json.EnumerateArray())
            {
                var title = item.GetPropertyOrDefault("title", "(untitled)");
                var state = item.GetPropertyOrDefault("state", "unknown");
                var project = item.GetPropertyOrDefault("project", "");
                var observations = item.GetPropertyOrDefault("observationCount", 0L);
                var created = item.GetPropertyOrDefault("createdAt", "");

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  [{state}]");
                Console.ResetColor();
                Console.Write($" {title}");

                if (!string.IsNullOrEmpty(project))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  ({project})");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  observations: {observations}");
                if (!string.IsNullOrEmpty(created))
                    Console.Write($"  {created}");
                Console.ResetColor();

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch threads: {ex.Message}");
        }
    }

    private static void PrintThread(JsonElement item, string boxTitle)
    {
        var title = item.GetPropertyOrDefault("title", "(untitled)");
        var state = item.GetPropertyOrDefault("state", "unknown");
        var project = item.GetPropertyOrDefault("project", "");
        var observations = item.GetPropertyOrDefault("observationCount", 0L);
        var created = item.GetPropertyOrDefault("createdAt", "");
        var updated = item.GetPropertyOrDefault("updatedAt", "");

        var lines = new List<string>
        {
            $"Title:         {title}",
            $"State:         {state}",
            $"Project:       {project}",
            $"Observations:  {observations}",
            $"Created:       {created}",
            $"Updated:       {updated}"
        };

        ConsoleFormatter.PrintBox(boxTitle, string.Join("\n", lines));
    }
}
