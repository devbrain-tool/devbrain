using System.CommandLine;
using DevBrain.Cli.Commands;

var root = new RootCommand("DevBrain -- your developer second brain");

root.Add(new StartCommand());
root.Add(new StopCommand());
root.Add(new StatusCommand());
root.Add(new BriefingCommand());
root.Add(new SearchCommand());
root.Add(new WhyCommand());
root.Add(new DashboardCommand());

var result = root.Parse(args);
return await result.InvokeAsync();
