namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public interface IGraphStore
{
    Task<GraphNode> AddNode(string type, string name, object? data = null, string? sourceId = null);
    Task<GraphNode?> GetNode(string id);
    Task<IReadOnlyList<GraphNode>> GetNodesByType(string type);
    Task RemoveNode(string id);

    Task<GraphEdge> AddEdge(string sourceId, string targetId, string type, object? data = null);
    Task RemoveEdge(string id);

    Task<IReadOnlyList<GraphNode>> GetNeighbors(string nodeId, int hops = 1, string? edgeType = null);
    Task<IReadOnlyList<GraphPath>> FindPaths(string fromId, string toId, int maxDepth = 4);
    Task<IReadOnlyList<GraphNode>> GetRelatedToFile(string filePath);

    Task Clear();
}
