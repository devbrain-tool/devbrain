namespace DevBrain.Storage;

using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class BlastRadiusCalculator
{
    private readonly IGraphStore _graph;
    private readonly IDeadEndStore _deadEnds;
    private readonly DecisionChainBuilder _chainBuilder;

    public BlastRadiusCalculator(IGraphStore graph, IDeadEndStore deadEnds, DecisionChainBuilder chainBuilder)
    {
        _graph = graph;
        _deadEnds = deadEnds;
        _chainBuilder = chainBuilder;
    }

    public async Task<BlastRadius> Calculate(string filePath, int maxHops = 3)
    {
        // Step 1: Find decisions connected to this file
        var related = await _graph.GetRelatedToFile(filePath);
        var seedDecisions = related
            .Where(n => n.Type is "Decision" or "Bug")
            .ToList();

        if (seedDecisions.Count == 0)
            return new BlastRadius { SourceFile = filePath };

        // Step 2: Traverse causal edges with depth tracking
        var nodesWithDepth = await _chainBuilder.TraverseCausalGraphWithDepth(seedDecisions, maxHops);

        // Step 3: For each downstream decision, find connected File nodes
        var affectedFiles = new Dictionary<string, BlastRadiusEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (nodeId, (node, depth)) in nodesWithDepth)
        {
            var fileNeighbors = await _graph.GetNeighbors(node.Id, hops: 1, edgeType: "references");
            foreach (var fileNode in fileNeighbors)
            {
                if (fileNode.Type != "File") continue;
                if (fileNode.Name.Equals(filePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (affectedFiles.ContainsKey(fileNode.Name)) continue;

                // Count dead ends relevant to this specific node's chain path
                var deadEndsForNode = node.Type == "Bug" ? 1 : 0;
                var chainLength = depth + 1; // depth 0 = direct connection = chain length 1
                var recency = ComputeRecencyDecay(node.CreatedAt);
                var risk = ComputeRiskScore(chainLength, deadEndsForNode, recency);

                affectedFiles[fileNode.Name] = new BlastRadiusEntry
                {
                    FilePath = fileNode.Name,
                    RiskScore = Math.Round(risk, 3),
                    ChainLength = chainLength,
                    Reason = $"Linked via decision: {node.Name}",
                    LinkedDecisionId = node.Id
                };
            }
        }

        // Step 4: Find dead ends at risk
        var deadEndsAtRisk = await _deadEnds.FindByFiles([filePath]);
        var deadEndIds = deadEndsAtRisk.Select(d => d.Id).ToList();

        var sortedFiles = affectedFiles.Values
            .OrderByDescending(f => f.RiskScore)
            .ToList();

        return new BlastRadius
        {
            SourceFile = filePath,
            AffectedFiles = sortedFiles,
            DeadEndsAtRisk = deadEndIds
        };
    }

    private static double ComputeRecencyDecay(DateTime createdAt)
    {
        var daysSince = (DateTime.UtcNow - createdAt).TotalDays;
        return Math.Max(0.1, 1.0 - (daysSince / 180.0));
    }

    internal static double ComputeRiskScore(int chainLength, int deadEndsInChain, double recencyDecay)
    {
        var deadEndMultiplier = 1.0 + (0.5 * deadEndsInChain);
        var raw = (1.0 / Math.Max(1, chainLength)) * deadEndMultiplier * recencyDecay;
        return Math.Min(1.0, raw);
    }
}
