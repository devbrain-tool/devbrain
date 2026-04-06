namespace DevBrain.Agents;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class DeadEndAgent : IIntelligenceAgent
{
    public string Name => "dead-end";

    public AgentSchedule Schedule => new AgentSchedule.OnEvent(EventType.Error, EventType.Conversation);

    public Priority Priority => Priority.High;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        var recentErrors = await ctx.Observations.Query(new ObservationFilter
        {
            EventType = EventType.Error,
            After = DateTime.UtcNow.AddHours(-1),
            Limit = 50
        });

        foreach (var error in recentErrors)
        {
            if (string.IsNullOrEmpty(error.ThreadId))
                continue;

            var threadObs = await ctx.Observations.Query(new ObservationFilter
            {
                ThreadId = error.ThreadId,
                Limit = 200
            });

            // Heuristic: same file edited 3+ times (FileChange events)
            var fileChanges = threadObs
                .Where(o => o.EventType == EventType.FileChange)
                .SelectMany(o => o.FilesInvolved)
                .GroupBy(f => f)
                .Where(g => g.Count() >= 3)
                .Select(g => g.Key)
                .ToList();

            if (fileChanges.Count > 0)
            {
                var description = $"Dead end detected in thread {error.ThreadId}: " +
                    $"files edited 3+ times: {string.Join(", ", fileChanges)}";

                outputs.Add(new AgentOutput(
                    AgentOutputType.DeadEndDetected,
                    description,
                    new { ThreadId = error.ThreadId, Files = fileChanges }));
            }
        }

        return outputs;
    }
}
