namespace DevBrain.Agents;

using DevBrain.Core;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class GrowthAgent : IIntelligenceAgent
{
    private readonly IGrowthStore _growthStore;

    public GrowthAgent(IGrowthStore growthStore)
    {
        _growthStore = growthStore;
    }

    public string Name => "growth";

    public AgentSchedule Schedule => new AgentSchedule.Cron("0 8 * * 1");

    public Priority Priority => Priority.Low;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();
        var now = DateTime.UtcNow;
        var periodStart = now.AddDays(-7);
        var periodEnd = now;

        // Get all observations for the past week
        var weekObs = await ctx.Observations.Query(new ObservationFilter
        {
            After = periodStart,
            Before = periodEnd,
            Limit = 2000
        });

        if (weekObs.Count == 0)
            return outputs;

        // Compute metrics
        var metrics = new List<DeveloperMetric>();

        // 1. Debugging speed: avg minutes from Error to no-more-errors in thread
        var debuggingSpeed = ComputeDebuggingSpeed(weekObs);
        metrics.Add(CreateMetric("debugging_speed", debuggingSpeed, periodStart, periodEnd));

        // 2. Dead-end rate
        var deadEnds = await ctx.DeadEnds.Query(new DeadEndFilter { After = periodStart });
        var sessionIds = weekObs.Select(o => o.SessionId).Distinct().Count();
        var deadEndRate = sessionIds > 0 ? (double)deadEnds.Count / sessionIds : 0;
        metrics.Add(CreateMetric("dead_end_rate", Math.Round(deadEndRate, 2), periodStart, periodEnd));

        // 3. Exploration breadth: unique files per session
        var filesPerSession = weekObs
            .GroupBy(o => o.SessionId)
            .Select(g => g.SelectMany(o => o.FilesInvolved).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .DefaultIfEmpty(0)
            .Average();
        metrics.Add(CreateMetric("exploration_breadth", Math.Round(filesPerSession, 1), periodStart, periodEnd));

        // 4. Decision velocity: avg minutes from first FileChange to first Decision per thread
        var decisionVelocity = ComputeDecisionVelocity(weekObs);
        metrics.Add(CreateMetric("decision_velocity", Math.Round(decisionVelocity, 1), periodStart, periodEnd));

        // 5. Retry rate: sessions with 3+ edits to same file
        var retryRate = ComputeRetryRate(weekObs);
        metrics.Add(CreateMetric("retry_rate", Math.Round(retryRate, 2), periodStart, periodEnd));

        // 6. Tool repertoire: distinct ToolCall observations
        var toolCount = weekObs
            .Where(o => o.EventType == EventType.ToolCall)
            .SelectMany(o => o.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        metrics.Add(CreateMetric("tool_repertoire", toolCount, periodStart, periodEnd));

        // 7. Problem complexity (heuristic only — Llama enhancement deferred)
        var complexity = ComputeHeuristicComplexity(weekObs);
        metrics.Add(CreateMetric("problem_complexity", Math.Round(complexity, 2), periodStart, periodEnd));

        // 8. Code quality (heuristic — all errors count equally without Llama)
        var errorCount = weekObs.Count(o => o.EventType == EventType.Error);
        var quality = weekObs.Count > 0 ? 1.0 - ((double)errorCount / weekObs.Count) : 1.0;
        metrics.Add(CreateMetric("code_quality", Math.Round(quality, 3), periodStart, periodEnd));

        // Detect milestones BEFORE persisting metrics so history queries
        // don't include current week's data (avoids consuming a history slot)
        var milestones = await DetectMilestones(ctx, weekObs, metrics, periodStart);

        // Now persist metrics
        foreach (var metric in metrics)
            await _growthStore.AddMetric(metric);

        foreach (var milestone in milestones)
        {
            await _growthStore.AddMilestone(milestone);
            outputs.Add(new AgentOutput(AgentOutputType.MilestoneAchieved, milestone.Description));
        }

        // Generate LLM narrative
        string? narrative = null;
        try
        {
            var prompt = BuildNarrativePrompt(metrics, milestones);
            var task = new LlmTask
            {
                AgentName = Name,
                Priority = Priority.Low,
                Type = LlmTaskType.Synthesis,
                Prompt = prompt,
                Preference = LlmPreference.PreferLocal
            };
            var result = await ctx.Llm.Submit(task, ct);
            if (result.Success && !string.IsNullOrEmpty(result.Content))
                narrative = result.Content.Trim();
        }
        catch (OperationCanceledException) { throw; }
        catch { /* LLM failure is non-fatal for growth reports */ }

        // Persist report
        var report = new GrowthReport
        {
            Id = Guid.NewGuid().ToString(),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Metrics = metrics,
            Milestones = milestones,
            Narrative = narrative
        };
        await _growthStore.AddReport(report);

        outputs.Add(new AgentOutput(AgentOutputType.GrowthReportGenerated,
            $"Growth report generated: {metrics.Count} metrics, {milestones.Count} milestones"));

        return outputs;
    }

    internal static double ComputeDebuggingSpeed(IReadOnlyList<Observation> observations)
    {
        var threads = observations
            .Where(o => !string.IsNullOrEmpty(o.ThreadId))
            .GroupBy(o => o.ThreadId!);

        var durations = new List<double>();
        foreach (var thread in threads)
        {
            var sorted = thread.OrderBy(o => o.Timestamp).ToList();
            var firstError = sorted.FirstOrDefault(o => o.EventType == EventType.Error);
            if (firstError is null) continue;

            var lastError = sorted.LastOrDefault(o => o.EventType == EventType.Error);
            if (lastError is null) continue;

            // Resolution = first non-error observation after the last error
            var resolution = sorted
                .FirstOrDefault(o => o.Timestamp > lastError.Timestamp && o.EventType != EventType.Error);

            if (resolution is not null)
                durations.Add((resolution.Timestamp - firstError.Timestamp).TotalMinutes);
        }

        return durations.Count > 0 ? durations.Average() : 0;
    }

    internal static double ComputeDecisionVelocity(IReadOnlyList<Observation> observations)
    {
        var threads = observations
            .Where(o => !string.IsNullOrEmpty(o.ThreadId))
            .GroupBy(o => o.ThreadId!);

        var velocities = new List<double>();
        foreach (var thread in threads)
        {
            var sorted = thread.OrderBy(o => o.Timestamp).ToList();
            var firstChange = sorted.FirstOrDefault(o => o.EventType == EventType.FileChange);
            var firstDecision = sorted.FirstOrDefault(o => o.EventType == EventType.Decision);

            if (firstChange is not null && firstDecision is not null && firstDecision.Timestamp > firstChange.Timestamp)
                velocities.Add((firstDecision.Timestamp - firstChange.Timestamp).TotalMinutes);
        }

        return velocities.Count > 0 ? velocities.Average() : 0;
    }

    internal static double ComputeRetryRate(IReadOnlyList<Observation> observations)
    {
        var sessions = observations.GroupBy(o => o.SessionId);
        var totalSessions = 0;
        var retrySessions = 0;

        foreach (var session in sessions)
        {
            totalSessions++;
            var hasRetry = session
                .Where(o => o.EventType == EventType.FileChange)
                .SelectMany(o => o.FilesInvolved)
                .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() >= 3);
            if (hasRetry) retrySessions++;
        }

        return totalSessions > 0 ? (double)retrySessions / totalSessions : 0;
    }

    internal static double ComputeHeuristicComplexity(IReadOnlyList<Observation> observations)
    {
        var threads = observations
            .Where(o => !string.IsNullOrEmpty(o.ThreadId))
            .GroupBy(o => o.ThreadId!);

        var scores = new List<double>();
        foreach (var thread in threads)
        {
            var sorted = thread.OrderBy(o => o.Timestamp).ToList();
            var filesInvolved = sorted.SelectMany(o => o.FilesInvolved).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var decisions = sorted.Count(o => o.EventType == EventType.Decision);
            var durationHours = sorted.Count > 1
                ? (sorted[^1].Timestamp - sorted[0].Timestamp).TotalHours
                : 0;
            var crossProjectRefs = sorted.Select(o => o.Project).Distinct().Count();

            // Scale: 1 file + 0 decisions + 0 hours + 1 project = ~1.0
            // 5 files + 2 decisions + 1 hour + 1 project = ~3.0
            // 15+ files + 5+ decisions + 3+ hours + 3+ projects = ~5.0
            var raw = (filesInvolved * 0.3) + (decisions * 0.5) + (durationHours * 0.4) + (crossProjectRefs * 0.3);
            scores.Add(Math.Clamp(1.0 + raw, 1.0, 5.0));
        }

        return scores.Count > 0 ? scores.Average() : 1.0;
    }

    private async Task<IReadOnlyList<GrowthMilestone>> DetectMilestones(
        AgentContext ctx, IReadOnlyList<Observation> weekObs,
        IReadOnlyList<DeveloperMetric> currentMetrics, DateTime periodStart)
    {
        var milestones = new List<GrowthMilestone>();

        // "First" milestones: new projects
        var currentProjects = weekObs.Select(o => o.Project).Distinct().ToList();
        // Use targeted query for historical projects instead of loading 5000 observations
        var historicalObs = await ctx.Observations.Query(new ObservationFilter
        {
            Before = periodStart, Limit = 500
        });
        var historicalProjects = historicalObs.Select(o => o.Project).Distinct().ToHashSet();

        foreach (var project in currentProjects)
        {
            if (!historicalProjects.Contains(project))
            {
                milestones.Add(new GrowthMilestone
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = MilestoneType.First,
                    Description = $"First contribution to {project}",
                    AchievedAt = DateTime.UtcNow
                });
            }
        }

        // "Streak" milestones: zero dead ends this week
        var deadEndMetric = currentMetrics.FirstOrDefault(m => m.Dimension == "dead_end_rate");
        if (deadEndMetric is not null && deadEndMetric.Value == 0)
        {
            var deadEndHistory = await _growthStore.GetMetrics("dead_end_rate", 4);
            var priorWeeksWithDeadEnds = deadEndHistory.Count(m => m.Value > 0);

            if (priorWeeksWithDeadEnds > 0 || deadEndHistory.Count == 0)
            {
                milestones.Add(new GrowthMilestone
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = MilestoneType.Streak,
                    Description = "Zero dead ends this week",
                    AchievedAt = DateTime.UtcNow
                });
            }
        }

        // "Improvement" milestones: any metric > 20% better than 4-week average
        foreach (var metric in currentMetrics)
        {
            var history = await _growthStore.GetMetrics(metric.Dimension, weeks: 4);
            if (history.Count == 0) continue;

            var avg = history.Select(m => m.Value).Average();
            if (avg == 0) continue;

            // For rate metrics (dead_end_rate, retry_rate), lower is better
            var isLowerBetter = metric.Dimension is "dead_end_rate" or "retry_rate" or "debugging_speed";
            var improvement = isLowerBetter
                ? (avg - metric.Value) / avg
                : (metric.Value - avg) / avg;

            if (improvement > 0.20)
            {
                milestones.Add(new GrowthMilestone
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = MilestoneType.Improvement,
                    Description = $"{metric.Dimension} improved by {improvement:P0} this week",
                    AchievedAt = DateTime.UtcNow
                });
            }
        }

        // Composite: complexity up + quality holding
        var complexityMetric = currentMetrics.FirstOrDefault(m => m.Dimension == "problem_complexity");
        var qualityMetric = currentMetrics.FirstOrDefault(m => m.Dimension == "code_quality");
        if (complexityMetric is not null && qualityMetric is not null)
        {
            var complexityHistory = await _growthStore.GetMetrics("problem_complexity", 4);
            var qualityHistory = await _growthStore.GetMetrics("code_quality", 4);

            if (complexityHistory.Count >= 1 && qualityHistory.Count >= 1)
            {
                var complexityAvg = complexityHistory.Select(m => m.Value).Average();
                var qualityAvg = qualityHistory.Select(m => m.Value).Average();

                var complexityUp = complexityAvg > 0 && (complexityMetric.Value - complexityAvg) / complexityAvg > 0.10;
                var qualityStable = qualityAvg > 0 && Math.Abs(qualityMetric.Value - qualityAvg) / qualityAvg <= 0.05;

                if (complexityUp && qualityStable)
                {
                    milestones.Add(new GrowthMilestone
                    {
                        Id = Guid.NewGuid().ToString(),
                        Type = MilestoneType.Improvement,
                        Description = "Complexity up with quality holding steady — you're leveling up",
                        AchievedAt = DateTime.UtcNow
                    });
                }
            }
        }

        return milestones;
    }

    private static string BuildNarrativePrompt(
        IReadOnlyList<DeveloperMetric> metrics, IReadOnlyList<GrowthMilestone> milestones)
    {
        var metricsStr = string.Join(", ", metrics.Select(m => $"{m.Dimension}: {m.Value}"));
        var milestonesStr = milestones.Count > 0
            ? string.Join("; ", milestones.Select(m => m.Description))
            : "None this week";

        return Prompts.Fill(Prompts.GrowthNarrative,
            ("METRICS", metricsStr),
            ("MILESTONES", milestonesStr),
            ("TREND", "N/A"),
            ("COMPLEXITY", metrics.FirstOrDefault(m => m.Dimension == "problem_complexity")?.Value.ToString() ?? "N/A"),
            ("QUALITY", metrics.FirstOrDefault(m => m.Dimension == "code_quality")?.Value.ToString() ?? "N/A"),
            ("ERROR_BREAKDOWN", "N/A"));
    }

    private static DeveloperMetric CreateMetric(string dimension, double value, DateTime start, DateTime end) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Dimension = dimension,
        Value = value,
        PeriodStart = start,
        PeriodEnd = end
    };
}
