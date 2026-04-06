using System.CommandLine;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class ExportCommand : Command
{
    private readonly Option<string> _formatOption = new("--format")
    {
        Description = "Export format (default: json)",
        DefaultValueFactory = _ => "json"
    };

    public ExportCommand() : base("export", "Export data to a file")
    {
        Add(_formatOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var format = pr.GetValue(_formatOption)!;
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        try
        {
            var response = await client.Post("/api/v1/export", new { format });
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var fileName = $"devbrain-export-{DateTime.Now:yyyyMMdd-HHmmss}.{format}";
            await File.WriteAllTextAsync(fileName, content);

            ConsoleFormatter.PrintSuccess($"Exported to: {fileName}");
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Export failed: {ex.Message}");
        }
    }
}
