using System.CommandLine;
using System.Text.Json;
using System.Web;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class SearchCommand : Command
{
    private readonly Argument<string> _queryArgument = new("query")
    {
        Description = "Search query string"
    };

    private readonly Option<bool> _exactOption = new("--exact")
    {
        Description = "Use FTS-only search (no semantic)"
    };

    public SearchCommand() : base("search", "Search observations")
    {
        Add(_queryArgument);
        Add(_exactOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var query = pr.GetValue(_queryArgument)!;
        var exact = pr.GetValue(_exactOption);
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var encodedQuery = HttpUtility.UrlEncode(query);
            var endpoint = exact ? "/api/v1/search/exact" : "/api/v1/search";
            var url = $"{endpoint}?q={encodedQuery}";

            var json = await client.GetJson(url);

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning("No results found.");
                return;
            }

            Console.WriteLine($"Found {json.GetArrayLength()} result(s):\n");

            foreach (var item in json.EnumerateArray())
            {
                var eventType = item.GetPropertyOrDefault("eventType", "unknown");
                var summary = item.GetPropertyOrDefault("summary", "(no summary)");
                var project = item.GetPropertyOrDefault("project", "");
                var timestamp = item.GetPropertyOrDefault("timestamp", "");

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  [{eventType}]");
                Console.ResetColor();
                Console.Write($" {summary}");

                if (!string.IsNullOrEmpty(project))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  ({project})");
                    Console.ResetColor();
                }

                if (!string.IsNullOrEmpty(timestamp))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  {timestamp}");
                    Console.ResetColor();
                }

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Search failed: {ex.Message}");
        }
    }
}
