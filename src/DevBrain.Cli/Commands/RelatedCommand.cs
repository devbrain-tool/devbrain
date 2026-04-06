using System.CommandLine;
using System.Text.Json;
using System.Web;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class RelatedCommand : Command
{
    private readonly Argument<string> _fileArgument = new("file")
    {
        Description = "File path to find related items for"
    };

    public RelatedCommand() : base("related", "Show related decisions, dead ends, and patterns for a file")
    {
        Add(_fileArgument);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var file = pr.GetValue(_fileArgument)!;
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var encodedFile = HttpUtility.UrlEncode(file);
            var json = await client.GetJson($"/api/v1/search?q={encodedFile}");

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning($"No related items found for: {file}");
                return;
            }

            var decisions = new List<JsonElement>();
            var deadEnds = new List<JsonElement>();
            var patterns = new List<JsonElement>();
            var other = new List<JsonElement>();

            foreach (var item in json.EnumerateArray())
            {
                var eventType = item.GetPropertyOrDefault("eventType", "unknown").ToLowerInvariant();
                switch (eventType)
                {
                    case "decision":
                        decisions.Add(item);
                        break;
                    case "dead_end":
                    case "dead-end":
                        deadEnds.Add(item);
                        break;
                    case "pattern":
                        patterns.Add(item);
                        break;
                    default:
                        other.Add(item);
                        break;
                }
            }

            var lines = new List<string> { $"Related items for: {file}", "" };

            if (decisions.Count > 0)
            {
                lines.Add($"Decisions ({decisions.Count}):");
                foreach (var d in decisions)
                    lines.Add($"  - {d.GetPropertyOrDefault("summary", "(no summary)")}");
                lines.Add("");
            }

            if (deadEnds.Count > 0)
            {
                lines.Add($"Dead Ends ({deadEnds.Count}):");
                foreach (var d in deadEnds)
                    lines.Add($"  - {d.GetPropertyOrDefault("summary", "(no summary)")}");
                lines.Add("");
            }

            if (patterns.Count > 0)
            {
                lines.Add($"Patterns ({patterns.Count}):");
                foreach (var p in patterns)
                    lines.Add($"  - {p.GetPropertyOrDefault("summary", "(no summary)")}");
                lines.Add("");
            }

            if (other.Count > 0)
            {
                lines.Add($"Other ({other.Count}):");
                foreach (var o in other)
                    lines.Add($"  - [{o.GetPropertyOrDefault("eventType", "unknown")}] {o.GetPropertyOrDefault("summary", "(no summary)")}");
                lines.Add("");
            }

            ConsoleFormatter.PrintBox("Related Items", string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to find related items: {ex.Message}");
        }
    }
}
