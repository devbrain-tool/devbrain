using System.CommandLine;
using System.Text.Json;
using System.Web;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class DeadEndsCommand : Command
{
    private readonly Option<string?> _projectOption = new("--project")
    {
        Description = "Filter by project name"
    };

    private readonly Option<string?> _fileOption = new("--file")
    {
        Description = "Filter by file path"
    };

    public DeadEndsCommand() : base("dead-ends", "Show dead ends")
    {
        Add(_projectOption);
        Add(_fileOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var project = pr.GetValue(_projectOption);
        var file = pr.GetValue(_fileOption);
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var url = "/api/v1/dead-ends";
            var queryParts = new List<string>();
            if (!string.IsNullOrEmpty(project))
                queryParts.Add($"project={HttpUtility.UrlEncode(project)}");
            if (!string.IsNullOrEmpty(file))
                queryParts.Add($"file={HttpUtility.UrlEncode(file)}");
            if (queryParts.Count > 0)
                url += "?" + string.Join("&", queryParts);

            var json = await client.GetJson(url);

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning("No dead ends found.");
                return;
            }

            Console.WriteLine($"Found {json.GetArrayLength()} dead end(s):\n");

            foreach (var item in json.EnumerateArray())
            {
                var description = item.GetPropertyOrDefault("description", "(no description)");
                var approach = item.GetPropertyOrDefault("approach", "");
                var reason = item.GetPropertyOrDefault("reason", "");

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  X ");
                Console.ResetColor();
                Console.WriteLine(description);

                if (!string.IsNullOrEmpty(approach))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    Approach: {approach}");
                    Console.ResetColor();
                }

                if (!string.IsNullOrEmpty(reason))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    Reason:   {reason}");
                    Console.ResetColor();
                }

                if (item.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    foreach (var f in files.EnumerateArray())
                    {
                        Console.WriteLine($"    File:     {f.GetString()}");
                    }
                    Console.ResetColor();
                }

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch dead ends: {ex.Message}");
        }
    }
}
