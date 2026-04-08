using System.CommandLine;
using System.Text.Json;

namespace DevBrain.Cli.Commands;

public class HookCommand : Command
{
    private readonly Argument<string> _eventArg = new("event")
    {
        Description = "The hook event name (e.g. PostToolUse)"
    };

    public HookCommand() : base("hook", "Forward Claude Code hook events to the DevBrain daemon")
    {
        Add(_eventArg);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        try
        {
            var eventName = pr.GetValue(_eventArg) ?? "";

            using var reader = new StreamReader(Console.OpenStandardInput());
            var stdin = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(stdin))
                return;

            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(stdin) ?? [];
            dict["hookEvent"] = eventName;

            using var client = new DevBrainHttpClient();
            await client.Post("/api/v1/events", dict);
        }
        catch
        {
            // Never fail — hooks must not block Claude Code
        }
    }
}
