namespace DevBrain.Agents;

using DevBrain.Core;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class CompressionAgent : IIntelligenceAgent
{
    public string Name => "compression";

    public AgentSchedule Schedule => new AgentSchedule.Idle(TimeSpan.FromMinutes(60));

    public Priority Priority => Priority.Low;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        var unenriched = await ctx.Observations.GetUnenriched(50);

        foreach (var obs in unenriched)
        {
            if (ct.IsCancellationRequested)
                break;

            var task = new LlmTask
            {
                AgentName = Name,
                Priority = Priority.Low,
                Type = LlmTaskType.Summarization,
                Prompt = string.Format(Prompts.CompressionSummarization, obs.RawContent),
                Preference = LlmPreference.PreferLocal
            };

            LlmResult result;
            try
            {
                result = await ctx.Llm.Submit(task, ct);
            }
            catch
            {
                continue;
            }

            if (!result.Success || string.IsNullOrEmpty(result.Content))
                continue;

            var updated = obs with { Summary = result.Content };
            await ctx.Observations.Update(updated);

            outputs.Add(new AgentOutput(
                AgentOutputType.ThreadCompressed,
                $"Compressed observation {obs.Id}: {result.Content}"));
        }

        return outputs;
    }
}
