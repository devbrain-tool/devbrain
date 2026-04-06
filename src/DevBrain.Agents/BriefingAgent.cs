namespace DevBrain.Agents;

using DevBrain.Core;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class BriefingAgent : IIntelligenceAgent
{
    public string Name => "briefing";

    public AgentSchedule Schedule => new AgentSchedule.Cron("0 7 * * *");

    public Priority Priority => Priority.Normal;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var observations = await ctx.Observations.Query(new ObservationFilter
        {
            After = since,
            Limit = 200
        });

        if (observations.Count == 0)
        {
            return [new AgentOutput(AgentOutputType.BriefingGenerated, "No activity in the last 24 hours.")];
        }

        var prompt = BuildPrompt(observations);

        var task = new LlmTask
        {
            AgentName = Name,
            Priority = Priority.Normal,
            Type = LlmTaskType.Synthesis,
            Prompt = prompt,
            Preference = LlmPreference.PreferCloud
        };

        LlmResult result;
        try
        {
            result = await ctx.Llm.Submit(task, ct);
        }
        catch
        {
            return [new AgentOutput(AgentOutputType.BriefingGenerated, "generation failed")];
        }

        if (!result.Success || string.IsNullOrEmpty(result.Content))
        {
            return [new AgentOutput(AgentOutputType.BriefingGenerated, "generation failed")];
        }

        var content = result.Content;

        // Write briefing to file
        var dataPath = SettingsLoader.ResolveDataPath(ctx.Settings.Daemon.DataPath);
        var briefingsDir = Path.Combine(dataPath, "briefings");
        Directory.CreateDirectory(briefingsDir);

        var fileName = DateTime.UtcNow.ToString("yyyy-MM-dd") + ".md";
        var filePath = Path.Combine(briefingsDir, fileName);
        await File.WriteAllTextAsync(filePath, content, ct);

        return [new AgentOutput(AgentOutputType.BriefingGenerated, content)];
    }

    private static string BuildPrompt(IReadOnlyList<Observation> observations)
    {
        var lines = new List<string>
        {
            "Generate a daily development briefing based on the following observations from the last 24 hours.",
            "Summarize key decisions, errors encountered, files changed, and overall progress.",
            "Format as markdown with sections.",
            "",
            "Observations:"
        };

        foreach (var obs in observations)
        {
            lines.Add($"- [{obs.EventType}] {obs.Timestamp:HH:mm}: {obs.Summary ?? obs.RawContent}");
            if (obs.FilesInvolved.Count > 0)
                lines.Add($"  Files: {string.Join(", ", obs.FilesInvolved)}");
        }

        return string.Join("\n", lines);
    }
}
