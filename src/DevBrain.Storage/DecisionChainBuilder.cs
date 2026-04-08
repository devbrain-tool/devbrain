namespace DevBrain.Storage;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class DecisionChainBuilder
{
    private readonly IGraphStore _graph;
    private readonly IObservationStore _observations;

    private static readonly IReadOnlyList<string> CausalEdgeTypes = ["caused_by", "supersedes", "resolved_by"];
    private static readonly HashSet<string> DecisionNodeTypes = ["Decision", "Bug"];

    public DecisionChainBuilder(IGraphStore graph, IObservationStore observations)
    {
        _graph = graph;
        _observations = observations;
    }

    /// <summary>
    /// Traverse causal edges from seed nodes outward. Reusable by Blast Radius.
    /// </summary>
    public async Task<Dictionary<string, GraphNode>> TraverseCausalGraph(
        IEnumerable<GraphNode> seedNodes, int maxHops,
        IReadOnlyList<string>? edgeTypes = null,
        HashSet<string>? nodeTypeFilter = null)
    {
        var types = edgeTypes ?? CausalEdgeTypes;
        var filter = nodeTypeFilter ?? DecisionNodeTypes;
        var allNodes = new Dictionary<string, GraphNode>();

        foreach (var node in seedNodes)
        {
            if (filter.Contains(node.Type))
                allNodes.TryAdd(node.Id, node);

            var neighbors = await _graph.GetNeighbors(node.Id, maxHops, types);
            foreach (var neighbor in neighbors)
            {
                if (filter.Contains(neighbor.Type))
                    allNodes.TryAdd(neighbor.Id, neighbor);
            }
        }

        return allNodes;
    }

    /// <summary>
    /// Traverse causal edges with depth tracking per node. Used by BlastRadiusCalculator
    /// to compute accurate chain lengths for risk scoring.
    /// </summary>
    public async Task<Dictionary<string, (GraphNode Node, int Depth)>> TraverseCausalGraphWithDepth(
        IEnumerable<GraphNode> seedNodes, int maxHops,
        IReadOnlyList<string>? edgeTypes = null,
        HashSet<string>? nodeTypeFilter = null)
    {
        var types = edgeTypes ?? CausalEdgeTypes;
        var filter = nodeTypeFilter ?? DecisionNodeTypes;
        var result = new Dictionary<string, (GraphNode, int)>();

        // BFS with depth tracking
        var seedList = seedNodes.ToList();
        var queue = new Queue<(GraphNode Node, int Depth)>();

        foreach (var seed in seedList)
        {
            if (filter.Contains(seed.Type) && result.TryAdd(seed.Id, (seed, 0)))
                queue.Enqueue((seed, 0));
        }

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= maxHops) continue;

            var neighbors = await _graph.GetNeighbors(current.Id, hops: 1, types);
            foreach (var neighbor in neighbors)
            {
                if (!filter.Contains(neighbor.Type)) continue;
                if (result.TryAdd(neighbor.Id, (neighbor, depth + 1)))
                    queue.Enqueue((neighbor, depth + 1));
            }
        }

        return result;
    }

    public async Task<DecisionChain?> BuildForFile(string filePath, int maxHops = 3)
    {
        var related = await _graph.GetRelatedToFile(filePath);
        var seedNodes = related.Where(n => DecisionNodeTypes.Contains(n.Type)).ToList();

        if (seedNodes.Count == 0)
            return null;

        var allNodes = await TraverseCausalGraph(seedNodes, maxHops);
        var steps = await BuildSteps(allNodes.Values);

        if (steps.Count == 0)
            return null;

        // Deterministic root: chronologically earliest step
        var rootNodeId = steps[0].ObservationId;

        return new DecisionChain
        {
            Id = Guid.NewGuid().ToString(),
            RootNodeId = rootNodeId,
            Narrative = BuildNarrativePlaceholder(filePath, steps),
            Steps = steps
        };
    }

    public async Task<DecisionChain?> BuildForDecision(string nodeId, int maxHops = 4)
    {
        var rootNode = await _graph.GetNode(nodeId);
        if (rootNode is null)
            return null;

        // Reject non-Decision/Bug root nodes
        if (!DecisionNodeTypes.Contains(rootNode.Type))
            return null;

        var allNodes = await TraverseCausalGraph([rootNode], maxHops);
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
        var nodeList = nodes.ToList();

        // Batch-fetch observations to avoid N+1 queries
        var sourceIds = nodeList
            .Where(n => n.SourceId is not null)
            .Select(n => n.SourceId!)
            .Distinct()
            .ToList();

        var obsMap = new Dictionary<string, Observation>();
        foreach (var id in sourceIds)
        {
            var obs = await _observations.GetById(id);
            if (obs is not null)
                obsMap[id] = obs;
        }

        var steps = new List<DecisionStep>();
        foreach (var node in nodeList)
        {
            // Skip non-Decision/Bug nodes
            if (!DecisionNodeTypes.Contains(node.Type))
                continue;

            Observation? obs = null;
            if (node.SourceId is not null)
                obsMap.TryGetValue(node.SourceId, out obs);

            var stepType = node.Type == "Bug"
                ? DecisionStepType.DeadEnd
                : DecisionStepType.Decision;

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
