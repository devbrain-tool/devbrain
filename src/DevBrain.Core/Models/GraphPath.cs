namespace DevBrain.Core.Models;

public record GraphPath
{
    public required IReadOnlyList<GraphNode> Nodes { get; init; }
    public required IReadOnlyList<GraphEdge> Edges { get; init; }
    public int Depth => Edges.Count;
}
