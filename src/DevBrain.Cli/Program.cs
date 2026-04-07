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
root.Add(new ThreadCommand());
root.Add(new DeadEndsCommand());
root.Add(new AlertsCommand());
root.Add(new StoryCommand());
root.Add(new ReplayCommand());
root.Add(new RelatedCommand());
root.Add(new AgentsCommand());
root.Add(new ConfigCommand());
root.Add(new ExportCommand());
root.Add(new PurgeCommand());
root.Add(new RebuildCommand());
root.Add(new ServiceCommand());
root.Add(new UpdateCommand());

var result = root.Parse(args);
return await result.InvokeAsync();
