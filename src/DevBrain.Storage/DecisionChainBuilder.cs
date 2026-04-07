namespace DevBrain.Storage;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class DecisionChainBuilder
{
    private readonly IGraphStore _graph;
    private readonly IObservationStore _observations;

    private static readonly List<string> CausalEdgeTypes = ["caused_by", "supersedes", "resolved_by"];

    public DecisionChainBuilder(IGraphStore graph, IObservationStore observations)
    {
        _graph = graph;
        _observations = observations;
    }

    public async Task<DecisionChain?> BuildForFile(string filePath, int maxHops = 3)
    {
        var related = await _graph.GetRelatedToFile(filePath);
        var decisionNodes = related
            .Where(n => n.Type is "Decision" or "Bug")
            .ToList();

        if (decisionNodes.Count == 0)
            return null;

        var allNodes = new Dictionary<string, GraphNode>();
        foreach (var node in decisionNodes)
        {
            allNodes.TryAdd(node.Id, node);

            var causalNeighbors = await _graph.GetNeighbors(node.Id, maxHops, CausalEdgeTypes);
            foreach (var neighbor in causalNeighbors)
            {
                if (neighbor.Type is "Decision" or "Bug")
                    allNodes.TryAdd(neighbor.Id, neighbor);
            }
        }

        var steps = await BuildSteps(allNodes.Values);
        if (steps.Count == 0)
            return null;

        return new DecisionChain
        {
            Id = Guid.NewGuid().ToString(),
            RootNodeId = decisionNodes[0].Id,
            Narrative = BuildNarrativePlaceholder(filePath, steps),
            Steps = steps
        };
    }

    public async Task<DecisionChain?> BuildForDecision(string nodeId, int maxHops = 4)
    {
        var rootNode = await _graph.GetNode(nodeId);
        if (rootNode is null)
            return null;

        var allNodes = new Dictionary<string, GraphNode> { [rootNode.Id] = rootNode };
        var causalNeighbors = await _graph.GetNeighbors(rootNode.Id, maxHops, CausalEdgeTypes);
        foreach (var neighbor in causalNeighbors)
        {
            if (neighbor.Type is "Decision" or "Bug")
                allNodes.TryAdd(neighbor.Id, neighbor);
        }

        var steps = await BuildSteps(allNodes.Values);
        if (steps.Count == 0)
            return null;

        return new DecisionChain
        {
            Id = Guid.NewGuid().ToString(),
            RootNodeId = rootNode.Id,
            Narrative = BuildNarrativePlaceholder(rootNode.Name, steps),
            Steps = steps
        };
    }

    private async Task<IReadOnlyList<DecisionStep>> BuildSteps(IEnumerable<GraphNode> nodes)
    {
        var steps = new List<DecisionStep>();

        foreach (var node in nodes)
        {
            Observation? obs = null;
            if (node.SourceId is not null)
                obs = await _observations.GetById(node.SourceId);

            var stepType = node.Type switch
            {
                "Decision" => DecisionStepType.Decision,
                "Bug" => DecisionStepType.DeadEnd,
                _ => DecisionStepType.Decision
            };

            steps.Add(new DecisionStep
            {
                ObservationId = node.SourceId ?? node.Id,
                Summary = node.Name,
                Timestamp = obs?.Timestamp ?? node.CreatedAt,
                StepType = stepType,
                FilesInvolved = obs?.FilesInvolved ?? []
            });
        }

        return steps.OrderBy(s => s.Timestamp).ToList();
    }

    private static string BuildNarrativePlaceholder(string root, IReadOnlyList<DecisionStep> steps)
    {
        var decisions = steps.Where(s => s.StepType == DecisionStepType.Decision).ToList();
        var deadEnds = steps.Where(s => s.StepType == DecisionStepType.DeadEnd).ToList();

        var parts = new List<string>
        {
            $"Decision chain for '{root}' spans {steps.Count} step(s)."
        };

        if (decisions.Count > 0)
            parts.Add($"{decisions.Count} decision(s): {string.Join("; ", decisions.Select(d => d.Summary))}.");

        if (deadEnds.Count > 0)
            parts.Add($"{deadEnds.Count} dead end(s) encountered along the way.");

        return string.Join(" ", parts);
    }
}
