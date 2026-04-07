using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class AlertsCommand : Command
{
    public AlertsCommand() : base("alerts", "Show and manage deja vu alerts")
    {
        var dismissCmd = new Command("dismiss", "Dismiss an alert");
        var idArg = new Argument<string>("id") { Description = "Alert ID to dismiss" };
        dismissCmd.Add(idArg);
        dismissCmd.SetAction(async (pr) =>
        {
            var id = pr.GetValue(idArg)!;
            var client = new DevBrainHttpClient();
            if (!await client.IsHealthy())
            {
                ConsoleFormatter.PrintError("Daemon is not running.");
                return;
            }
            var response = await client.Post($"/api/v1/alerts/{Uri.EscapeDataString(id)}/dismiss");
            if (response.IsSuccessStatusCode)
                ConsoleFormatter.PrintSuccess($"Alert {id} dismissed.");
            else
                ConsoleFormatter.PrintError("Failed to dismiss alert. It may not exist.");
        });

        var historyCmd = new Command("history", "Show all alerts including dismissed");
        historyCmd.SetAction(async (pr) =>
        {
            var client = new DevBrainHttpClient();
            if (!await client.IsHealthy())
            {
                ConsoleFormatter.PrintError("Daemon is not running.");
                return;
            }
            await PrintAlerts(client, "/api/v1/alerts/all");
        });

        Add(dismissCmd);
        Add(historyCmd);
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
        await PrintAlerts(client, "/api/v1/alerts");
    }

    private static async Task PrintAlerts(DevBrainHttpClient client, string url)
    {
        try
        {
            var json = await client.GetJson(url);

            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
            {
                ConsoleFormatter.PrintSuccess("No active alerts.");
                return;
            }

            Console.WriteLine($"Found {json.GetArrayLength()} alert(s):\n");

            foreach (var item in json.EnumerateArray())
            {
                var message = item.GetPropertyOrDefault("message", "(no message)");
                var confidence = item.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0;
                var strategy = item.GetPropertyOrDefault("strategy", "unknown");
                var dismissed = item.TryGetProperty("dismissed", out var d) && d.GetBoolean();

                if (dismissed)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("  - [DISMISSED] ");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("  ! ");
                }
                Console.ResetColor();

                Console.WriteLine(message);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Confidence: {confidence:P0}  Strategy: {strategy}");

                if (item.TryGetProperty("id", out var idProp))
                    Console.WriteLine($"    ID: {idProp.GetString()}");

                Console.ResetColor();
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to fetch alerts: {ex.Message}");
        }
    }
}
