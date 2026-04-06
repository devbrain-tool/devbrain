using System.CommandLine;
using System.Reflection;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class UpdateCommand : Command
{
    private readonly Option<bool> _checkOption = new("--check")
    {
        Description = "Check for available updates"
    };

    public UpdateCommand() : base("update", "Check for or apply updates")
    {
        Add(_checkOption);
        SetAction(Execute);
    }

    private async Task Execute(ParseResult pr)
    {
        var check = pr.GetValue(_checkOption);

        if (check)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            Console.WriteLine($"Current version: {version}");
            Console.WriteLine("Update check via GitHub releases is not yet implemented.");
        }
        else
        {
            Console.WriteLine("Run the install script to update DevBrain:");
            Console.WriteLine();

            if (OperatingSystem.IsWindows())
            {
                Console.WriteLine("  irm https://raw.githubusercontent.com/devbrain-tool/devbrain/main/scripts/install.ps1 | iex");
            }
            else
            {
                Console.WriteLine("  curl -fsSL https://raw.githubusercontent.com/devbrain-tool/devbrain/main/scripts/install.sh | bash");
            }
        }

        await Task.CompletedTask;
    }
}
