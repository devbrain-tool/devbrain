namespace DevBrain.Agents;

using DevBrain.Core;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class StorytellerAgent : IIntelligenceAgent
{
    private readonly ISessionStore _sessionStore;

    public StorytellerAgent(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    public string Name => "storyteller";

    public AgentSchedule Schedule => new AgentSchedule.Idle(TimeSpan.FromMinutes(30));

    public Priority Priority => Priority.Normal;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        // Find sessions with recent observations that don't yet have a story
        var recent = await ctx.Observations.Query(new ObservationFilter
        {
            After = DateTime.UtcNow.AddHours(-4),
            Limit = 200
        });

        var sessionIds = recent
            .Select(o => o.SessionId)
            .Distinct()
            .ToList();

        foreach (var sessionId in sessionIds)
        {
            if (ct.IsCancellationRequested) break;

            // Skip if already generated
            var existing = await _sessionStore.GetBySessionId(sessionId);
            if (existing is not null) continue;

            var observations = await ctx.Observations.GetSessionObservations(sessionId);
            if (observations.Count < 3) continue;

            // Compute metrics
            var duration = observations[^1].Timestamp - observations[0].Timestamp;
            var filesTouched = observations
                .SelectMany(o => o.FilesInvolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            // Phase detection
            var phases = DetectPhases(observations);

            // Turning points
            var turningPoints = DetectTurningPoints(observations);

            // Dead ends in this session
            var sessionFiles = observations
                .SelectMany(o => o.FilesInvolved)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var deadEnds = sessionFiles.Count > 0
                ? await ctx.DeadEnds.FindByFiles(sessionFiles)
                : [];

            // Build LLM prompt
            var eventLines = observations.Select(o =>
                $"[{o.EventType}] {o.Timestamp:HH:mm}: {Truncate(o.Summary ?? o.RawContent, 100)}"
            );

            var prompt = Prompts.Fill(Prompts.StorytellerNarrative,
                ("DURATION", duration.ToString(@"h\h\ mm\m")),
                ("PHASES", string.Join(" -> ", phases)),
                ("TURNING_POINTS", string.Join("; ", turningPoints)),
                ("EVENTS", string.Join("\n", eventLines)));

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
            catch (OperationCanceledException) { throw; }
            catch
            {
                continue;
            }

            if (!result.Success || string.IsNullOrEmpty(result.Content))
                continue;

            // Parse narrative and outcome (last line is outcome)
            var lines = result.Content.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var outcome = lines.Length > 1 ? lines[^1].Trim() : "Session completed.";
            var narrative = lines.Length > 1
                ? string.Join("\n", lines[..^1]).Trim()
                : result.Content.Trim();

            var summary = new SessionSummary
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                Narrative = narrative,
                Outcome = outcome,
                Duration = duration,
                ObservationCount = observations.Count,
                FilesTouched = filesTouched,
                DeadEndsHit = deadEnds.Count,
                Phases = phases
            };

            await _sessionStore.Add(summary);

            outputs.Add(new AgentOutput(
                AgentOutputType.StoryGenerated,
                $"Story generated for session {sessionId}: {outcome}"));
        }

        return outputs;
    }

    internal static IReadOnlyList<string> DetectPhases(IReadOnlyList<Observation> observations)
    {
        if (observations.Count == 0) return [];

        var phases = new List<string>();
        var windowSize = TimeSpan.FromMinutes(10);
        var start = observations[0].Timestamp;
        var end = observations[^1].Timestamp;

        // Handle sessions shorter than one window (including same-timestamp)
        if (end - start < windowSize)
        {
            phases.Add(ClassifyPhase(observations.ToList()));
            return phases;
        }

        for (var windowStart = start; windowStart < end; windowStart += windowSize)
        {
            var windowEnd = windowStart + windowSize;
            var windowObs = observations
                .Where(o => o.Timestamp >= windowStart && o.Timestamp < windowEnd)
                .ToList();

            if (windowObs.Count == 0) continue;

            var phase = ClassifyPhase(windowObs);
            if (phases.Count == 0 || phases[^1] != phase)
                phases.Add(phase);
        }

        return phases;
    }

    private static string ClassifyPhase(List<Observation> windowObs)
    {
        var errorCount = windowObs.Count(o => o.EventType == EventType.Error);
        var fileChangeCount = windowObs.Count(o => o.EventType == EventType.FileChange);
        var conversationCount = windowObs.Count(o => o.EventType == EventType.Conversation);
        var hasRefactorTag = windowObs.Any(o => o.Tags.Any(t =>
            t.Contains("refactor", StringComparison.OrdinalIgnoreCase)));

        if (errorCount > 0 && fileChangeCount > 0) return "Debugging";
        if (hasRefactorTag && fileChangeCount > 0 && errorCount == 0) return "Refactoring";
        if (fileChangeCount > conversationCount) return "Implementation";
        return "Exploration";
    }

    internal static IReadOnlyList<string> DetectTurningPoints(IReadOnlyList<Observation> observations)
    {
        var points = new List<string>();

        for (int i = 0; i < observations.Count; i++)
        {
            var obs = observations[i];

            // Decision events are turning points
            if (obs.EventType == EventType.Decision)
                points.Add($"Decision: {Truncate(obs.Summary ?? obs.RawContent, 60)}");

            // Error followed by continued non-error activity = likely resolved
            if (obs.EventType == EventType.Error)
            {
                var nextError = observations.Skip(i + 1)
                    .FirstOrDefault(o => o.EventType == EventType.Error);
                var hasSubsequentActivity = observations.Skip(i + 1)
                    .Any(o => o.EventType != EventType.Error);

                if (hasSubsequentActivity &&
                    (nextError is null || (nextError.Timestamp - obs.Timestamp).TotalMinutes >= 10))
                    points.Add($"Error at {obs.Timestamp:HH:mm}, no recurrence after");
            }
        }

        return points;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
