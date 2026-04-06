using System.CommandLine;
using DevBrain.Cli.Output;

namespace DevBrain.Cli.Commands;

public class ServiceCommand : Command
{
    public ServiceCommand() : base("service", "Manage DevBrain as a system service")
    {
        var installCommand = new Command("install", "Show instructions to install as a service");
        installCommand.SetAction(ExecuteInstall);

        var uninstallCommand = new Command("uninstall", "Show instructions to uninstall the service");
        uninstallCommand.SetAction(ExecuteUninstall);

        Add(installCommand);
        Add(uninstallCommand);
    }

    private static Task ExecuteInstall(ParseResult pr)
    {
        if (OperatingSystem.IsWindows())
        {
            ConsoleFormatter.PrintBox("Install as Windows Service", string.Join("\n", new[]
            {
                "Use Task Scheduler to run DevBrain at login:",
                "",
                "1. Open Task Scheduler (taskschd.msc)",
                "2. Create a new task named 'DevBrain'",
                "3. Set trigger: At log on",
                "4. Set action: Start a program",
                $"   Program: {Path.Combine(AppContext.BaseDirectory, "devbrain-daemon.exe")}",
                "5. Check 'Run whether user is logged on or not'",
                "6. Click OK"
            }));
        }
        else if (OperatingSystem.IsMacOS())
        {
            var plistPath = "~/Library/LaunchAgents/com.devbrain.daemon.plist";
            ConsoleFormatter.PrintBox("Install as launchd Service", string.Join("\n", new[]
            {
                $"Create a plist at: {plistPath}",
                "",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\"",
                "  \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
                "<plist version=\"1.0\">",
                "<dict>",
                "  <key>Label</key>",
                "  <string>com.devbrain.daemon</string>",
                "  <key>ProgramArguments</key>",
                "  <array>",
                $"    <string>{Path.Combine(AppContext.BaseDirectory, "devbrain-daemon")}</string>",
                "  </array>",
                "  <key>RunAtLoad</key>",
                "  <true/>",
                "  <key>KeepAlive</key>",
                "  <true/>",
                "</dict>",
                "</plist>",
                "",
                $"Then run: launchctl load {plistPath}"
            }));
        }
        else
        {
            ConsoleFormatter.PrintBox("Install as systemd Service", string.Join("\n", new[]
            {
                "Create a unit file at: ~/.config/systemd/user/devbrain.service",
                "",
                "[Unit]",
                "Description=DevBrain Daemon",
                "",
                "[Service]",
                $"ExecStart={Path.Combine(AppContext.BaseDirectory, "devbrain-daemon")}",
                "Restart=on-failure",
                "",
                "[Install]",
                "WantedBy=default.target",
                "",
                "Then run:",
                "  systemctl --user daemon-reload",
                "  systemctl --user enable devbrain",
                "  systemctl --user start devbrain"
            }));
        }

        return Task.CompletedTask;
    }

    private static Task ExecuteUninstall(ParseResult pr)
    {
        if (OperatingSystem.IsWindows())
        {
            ConsoleFormatter.PrintBox("Uninstall Windows Service", string.Join("\n", new[]
            {
                "1. Open Task Scheduler (taskschd.msc)",
                "2. Find the 'DevBrain' task",
                "3. Right-click and select Delete"
            }));
        }
        else if (OperatingSystem.IsMacOS())
        {
            var plistPath = "~/Library/LaunchAgents/com.devbrain.daemon.plist";
            ConsoleFormatter.PrintBox("Uninstall launchd Service", string.Join("\n", new[]
            {
                $"launchctl unload {plistPath}",
                $"rm {plistPath}"
            }));
        }
        else
        {
            ConsoleFormatter.PrintBox("Uninstall systemd Service", string.Join("\n", new[]
            {
                "systemctl --user stop devbrain",
                "systemctl --user disable devbrain",
                "rm ~/.config/systemd/user/devbrain.service",
                "systemctl --user daemon-reload"
            }));
        }

        return Task.CompletedTask;
    }
}
