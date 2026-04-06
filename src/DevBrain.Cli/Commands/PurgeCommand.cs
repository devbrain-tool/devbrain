using System.CommandLine;
using System.Web;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class PurgeCommand : Command
{
    private readonly Option<string?> _projectOption = new("--project")
    {
        Description = "Project name to purge"
    };

    private readonly Option<string?> _beforeOption = new("--before")
    {
        Description = "Purge data before this date (yyyy-MM-dd)"
    };

    public PurgeCommand() : base("purge", "Purge data from the database")
    {
        Add(_projectOption);
        Add(_beforeOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var project = pr.GetValue(_projectOption);
        var before = pr.GetValue(_beforeOption);
        var client = new DevBrainHttpClient();

        if (!await client.IsHealthy())
        {
            ConsoleFormatter.PrintError("Daemon is not running.");
            return;
        }

        Console.Write("Are you sure? (y/n) ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (answer != "y")
        {
            ConsoleFormatter.PrintWarning("Purge cancelled.");
            return;
        }

        try
        {
            var url = "/api/v1/data";
            var queryParts = new List<string>();
            if (!string.IsNullOrEmpty(project))
                queryParts.Add($"project={HttpUtility.UrlEncode(project)}");
            if (!string.IsNullOrEmpty(before))
                queryParts.Add($"before={HttpUtility.UrlEncode(before)}");
            if (queryParts.Count > 0)
                url += "?" + string.Join("&", queryParts);

            var response = await client.Delete(url);
            if (response.IsSuccessStatusCode)
            {
                ConsoleFormatter.PrintSuccess("Data purged successfully.");
            }
            else
            {
                ConsoleFormatter.PrintError($"Purge failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            ConsoleFormatter.PrintError($"Purge failed: {ex.Message}");
        }
    }
}
