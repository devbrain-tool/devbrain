using System.CommandLine;
using System.Text.Json;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class BlastCommand : Command
{
    private readonly Argument<string> _pathArg = new("path")
    {
        Description = "File path to analyze blast radius for"
    };

    public BlastCommand() : base("blast", "Show blast radius for a file")
    {
        Add(_pathArg);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var path = pr.GetValue(_pathArg)!;
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var json = await client.GetJson($"/api/v1/blast-radius/{Uri.EscapeDataString(path)}");

            var sourceFile = json.GetPropertyOrDefault("sourceFile", path);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  Blast Radius: {sourceFile}");
            Console.ResetColor();
            Console.WriteLine();

            // Dead ends at risk
            if (json.TryGetProperty("deadEndsAtRisk", out var deadEnds) &&
                deadEnds.ValueKind == JsonValueKind.Array && deadEnds.GetArrayLength() > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  {deadEnds.GetArrayLength()} dead end(s) at risk of re-triggering");
                Console.ResetColor();
                Console.WriteLine();
            }

            // Affected files
            if (json.TryGetProperty("affectedFiles", out var files) &&
                files.ValueKind == JsonValueKind.Array)
            {
                if (files.GetArrayLength() == 0)
                {
                    ConsoleFormatter.PrintSuccess("No affected files found. Safe to change!");
                }
                else
                {
                    Console.WriteLine($"  {files.GetArrayLength()} affected file(s):\n");

                    foreach (var file in files.EnumerateArray())
                    {
                        var filePath = file.GetPropertyOrDefault("filePath", "?");
                        var risk = file.TryGetProperty("riskScore", out var r) ? r.GetDouble() : 0;
                        var chainLen = file.TryGetProperty("chainLength", out var cl) ? cl.GetInt32() : 0;
                        var reason = file.GetPropertyOrDefault("reason", "");

                        var color = risk > 0.7 ? ConsoleColor.Red
                            : risk > 0.3 ? ConsoleColor.Yellow
                            : ConsoleColor.Green;

                        Console.ForegroundColor = color;
                        Console.Write($"  {risk:F2} ");
                        Console.ResetColor();
                        Console.Write(filePath);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"  (chain: {chainLen})");
                        if (!string.IsNullOrEmpty(reason))
                            Console.WriteLine($"       {reason}");
                        Console.ResetColor();
                    }
                }
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Failed to compute blast radius: {ex.Message}");
        }
    }
}
