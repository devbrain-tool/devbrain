using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class StatusCommand : Command
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StatusCommand() : base("status", "Show DevBrain daemon health status")
    {
        SetAction(Execute);
    }

    private static async Task Execute(ParseResult pr)
    {
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var json = await client.GetJson("/api/v1/health");
            var lines = new List<string>();

            var status = json.GetPropertyOrDefault("status", "unknown");
            var uptimeSeconds = json.GetPropertyOrDefault("uptimeSeconds", 0L);
            var uptime = TimeSpan.FromSeconds(uptimeSeconds);

            lines.Add($"Status:        {status}");
            lines.Add($"Uptime:        {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");

            if (json.TryGetProperty("storage", out var storage))
            {
                var sqliteSize = storage.GetPropertyOrDefault("sqliteSizeMb", 0L);
                var observations = storage.GetPropertyOrDefault("totalObservations", 0L);
                lines.Add($"SQLite size:   {sqliteSize} MB");
                lines.Add($"Observations:  {observations}");
            }

            if (json.TryGetProperty("llm", out var llm))
            {
                if (llm.TryGetProperty("local", out var local))
                {
                    var localStatus = local.GetPropertyOrDefault("status", "unknown");
                    lines.Add($"Ollama:        {localStatus}");
                }

                if (llm.TryGetProperty("cloud", out var cloud))
                {
                    var cloudStatus = cloud.GetPropertyOrDefault("status", "unknown");
                    lines.Add($"Anthropic:     {cloudStatus}");
                }
            }

            ConsoleFormatter.PrintBox("DevBrain Status", string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch status: {ex.Message}");
        }
    }
}

internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string name, string defaultValue)
    {
        return element.TryGetProperty(name, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
    }

    public static long GetPropertyOrDefault(this JsonElement element, string name, long defaultValue)
    {
        return element.TryGetProperty(name, out var prop) ? prop.GetInt64() : defaultValue;
    }
}
