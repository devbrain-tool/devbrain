namespace DevBrain.Agents;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class DejaVuAgent : IIntelligenceAgent
{
    private readonly IAlertStore _alertStore;
    private readonly IAlertSink? _alertSink;

    public DejaVuAgent(IAlertStore alertStore, IAlertSink? alertSink = null)
    {
        _alertStore = alertStore;
        _alertSink = alertSink;
    }

    public string Name => "deja-vu";

    public AgentSchedule Schedule => new AgentSchedule.OnEvent(EventType.FileChange, EventType.Error);

    public Priority Priority => Priority.Critical;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        var recent = await ctx.Observations.Query(new ObservationFilter
        {
            After = DateTime.UtcNow.AddMinutes(-10),
            Limit = 50
        });

        var threadGroups = recent
            .Where(o => !string.IsNullOrEmpty(o.ThreadId))
            .GroupBy(o => o.ThreadId!);

        foreach (var group in threadGroups)
        {
            if (ct.IsCancellationRequested) break;

            var threadId = group.Key;
            var currentFiles = group
                .SelectMany(o => o.FilesInvolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (currentFiles.Count == 0) continue;

            var matchingDeadEnds = await ctx.DeadEnds.FindByFiles(currentFiles);
            var currentFileSet = currentFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var deadEnd in matchingDeadEnds)
            {
                if (ct.IsCancellationRequested) break;
                if (deadEnd.FilesInvolved.Count == 0) continue;

                var overlap = deadEnd.FilesInvolved.Count(f => currentFileSet.Contains(f));
                var confidence = (double)overlap / deadEnd.FilesInvolved.Count;

                if (confidence < 0.5) continue;

                if (await _alertStore.Exists(threadId, deadEnd.Id)) continue;

                var message = $"You may be heading toward a known dead end: {deadEnd.Description}. " +
                    $"Approach tried before: {deadEnd.Approach}. " +
                    $"Why it failed: {deadEnd.Reason}";

                var alert = new DejaVuAlert
                {
                    Id = Guid.NewGuid().ToString(),
                    ThreadId = threadId,
                    MatchedDeadEndId = deadEnd.Id,
                    Confidence = Math.Round(confidence, 2),
                    Message = message,
                    Strategy = MatchStrategy.FileOverlap
                };

                await _alertStore.Add(alert);

                if (_alertSink is not null)
                    await _alertSink.Send(alert, ct);

                outputs.Add(new AgentOutput(
                    AgentOutputType.AlertFired,
                    message,
                    alert));
            }
        }

        return outputs;
    }
}
