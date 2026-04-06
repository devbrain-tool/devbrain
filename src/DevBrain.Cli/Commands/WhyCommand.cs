using System.CommandLine;
using System.Text.Json;
using System.Web;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class WhyCommand : Command
{
    private readonly Argument<string> _fileArgument = new("file")
    {
        Description = "File path to look up context for"
    };

    public WhyCommand() : base("why", "Show context for a file")
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
            // For v1, search observations by file path
            var encodedFile = HttpUtility.UrlEncode(file);
            var json = await client.GetJson($"/api/v1/search?q={encodedFile}");

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintWarning($"No context found for: {file}");
                return;
            }

            var lines = new List<string> { $"Context for: {file}", "" };

            foreach (var item in json.EnumerateArray())
            {
                var eventType = item.GetPropertyOrDefault("eventType", "unknown");
                var summary = item.GetPropertyOrDefault("summary", "(no summary)");
                var timestamp = item.GetPropertyOrDefault("timestamp", "");
                lines.Add($"[{eventType}] {summary}  {timestamp}");
            }

            ConsoleFormatter.PrintBox("File Context", string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to get file context: {ex.Message}");
        }
    }
}
