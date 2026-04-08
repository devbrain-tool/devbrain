namespace DevBrain.Agents;

using DevBrain.Core;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class DecisionChainAgent : IIntelligenceAgent
{
    public string Name => "decision-chain";

    public AgentSchedule Schedule => new AgentSchedule.OnEvent(EventType.Decision);

    public Priority Priority => Priority.High;

    private static readonly HashSet<string> CausalEdgeTypes = ["caused_by", "supersedes", "resolved_by"];

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        var recentDecisions = await ctx.Observations.Query(new ObservationFilter
        {
            EventType = EventType.Decision,
            After = DateTime.UtcNow.AddMinutes(-10),
            Limit = 20
        });

        foreach (var decision in recentDecisions)
        {
            if (ct.IsCancellationRequested) break;
            if (decision.FilesInvolved.Count == 0) continue;

            var decisionNode = await FindOrCreateDecisionNode(ctx, decision);
            var candidates = await FindCandidateNodes(ctx, decision);

            foreach (var candidate in candidates)
            {
                if (candidate.Id == decisionNode.Id) continue;
                if (candidate.SourceId == decision.Id) continue;

                var existingNeighbors = await ctx.Graph.GetNeighbors(
                    decisionNode.Id, hops: 1, edgeTypes: CausalEdgeTypes.ToList());
                if (existingNeighbors.Any(n => n.Id == candidate.Id)) continue;

                var edgeType = await ClassifyRelationship(ctx, decision, candidate, ct);
                if (edgeType is null) continue;

                await ctx.Graph.AddEdge(decisionNode.Id, candidate.Id, edgeType);

                outputs.Add(new AgentOutput(
                    AgentOutputType.DecisionChainBuilt,
                    $"Linked '{decisionNode.Name}' --{edgeType}--> '{candidate.Name}'"));
            }

            var resolvedDeadEnds = await CheckDeadEndResolution(ctx, decision, decisionNode, ct);
            outputs.AddRange(resolvedDeadEnds);
        }

        return outputs;
    }

    private static async Task<GraphNode> FindOrCreateDecisionNode(AgentContext ctx, Observation decision)
    {
        var found = await ctx.Graph.GetNodeBySourceId(decision.Id);
        if (found is not null) return found;

        return await ctx.Graph.AddNode("Decision", decision.Summary ?? decision.RawContent, sourceId: decision.Id);
    }

    private static async Task<List<GraphNode>> FindCandidateNodes(AgentContext ctx, Observation decision)
    {
        var candidates = new List<GraphNode>();
        foreach (var file in decision.FilesInvolved)
        {
            var related = await ctx.Graph.GetRelatedToFile(file);
            foreach (var node in related)
            {
                if (node.Type is "Decision" or "Bug" && !candidates.Any(c => c.Id == node.Id))
                    candidates.Add(node);
            }
        }
        return candidates;
    }

    private static async Task<string?> ClassifyRelationship(
        AgentContext ctx, Observation decision, GraphNode candidate, CancellationToken ct)
    {
        var prompt = Prompts.Fill(Prompts.DecisionClassification,
            ("DECISION_A", candidate.Name),
            ("DECISION_B", decision.Summary ?? decision.RawContent),
            ("SHARED_FILES", string.Join(", ", decision.FilesInvolved)));

        var task = new LlmTask
        {
            AgentName = "decision-chain",
            Priority = Priority.High,
            Type = LlmTaskType.Classification,
            Prompt = prompt,
            Preference = LlmPreference.PreferLocal
        };

        LlmResult result;
        try
        {
            result = await ctx.Llm.Submit(task, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (!result.Success || string.IsNullOrEmpty(result.Content))
            return null;

        var label = result.Content.Trim().ToLowerInvariant();
        return CausalEdgeTypes.Contains(label) ? label : null;
    }

    private static async Task<List<AgentOutput>> CheckDeadEndResolution(
        AgentContext ctx, Observation decision, GraphNode decisionNode, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        var matchingDeadEnds = await ctx.DeadEnds.FindByFiles(decision.FilesInvolved);
        foreach (var deadEnd in matchingDeadEnds)
        {
            var deNode = await ctx.Graph.GetNodeBySourceId(deadEnd.Id);
            if (deNode is null)
                deNode = await ctx.Graph.AddNode("Bug", deadEnd.Description, sourceId: deadEnd.Id);

            var neighbors = await ctx.Graph.GetNeighbors(deNode.Id, hops: 1, edgeTypes: ["resolved_by"]);
            if (neighbors.Any(n => n.Id == decisionNode.Id)) continue;

            await ctx.Graph.AddEdge(decisionNode.Id, deNode.Id, "resolved_by");

            outputs.Add(new AgentOutput(
                AgentOutputType.DecisionChainBuilt,
                $"Decision '{decisionNode.Name}' may resolve dead end: '{deadEnd.Description}'"));
        }

        return outputs;
    }
}
