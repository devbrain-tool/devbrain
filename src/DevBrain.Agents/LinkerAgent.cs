namespace DevBrain.Agents;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class LinkerAgent : IIntelligenceAgent
{
    public string Name => "linker";

    public AgentSchedule Schedule => new AgentSchedule.OnEvent(
        EventType.ToolCall, EventType.FileChange, EventType.Decision,
        EventType.Error, EventType.Conversation);

    public Priority Priority => Priority.High;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var outputs = new List<AgentOutput>();

        var recent = await ctx.Observations.Query(new ObservationFilter
        {
            Limit = 50,
            After = DateTime.UtcNow.AddMinutes(-10)
        });

        foreach (var obs in recent)
        {
            if (obs.FilesInvolved.Count == 0)
                continue;

            // Create or find observation node (Decision or Bug based on EventType)
            var obsNodeType = obs.EventType == EventType.Error ? "Bug" : "Decision";
            var obsNode = await FindOrCreateNodeBySourceId(
                ctx.Graph, obsNodeType, obs.Summary ?? obs.RawContent, obs.Id);

            foreach (var file in obs.FilesInvolved)
            {
                // Create or find File node (no duplicates by name)
                var fileNode = await FindOrCreateFileNode(ctx.Graph, file);

                // Add "references" edge
                var edge = await ctx.Graph.AddEdge(obsNode.Id, fileNode.Id, "references");

                outputs.Add(new AgentOutput(
                    AgentOutputType.EdgeCreated,
                    $"{obsNodeType} '{obsNode.Name}' references '{file}'",
                    new { EdgeId = edge.Id, ObsNodeId = obsNode.Id, FileNodeId = fileNode.Id }));
            }
        }

        return outputs;
    }

    private static async Task<GraphNode> FindOrCreateFileNode(IGraphStore graph, string filePath)
    {
        var existingNodes = await graph.GetNodesByType("File");
        var existing = existingNodes.FirstOrDefault(n => n.Name == filePath);
        if (existing is not null)
            return existing;

        return await graph.AddNode("File", filePath);
    }

    private static async Task<GraphNode> FindOrCreateNodeBySourceId(
        IGraphStore graph, string type, string name, string sourceId)
    {
        var existingNodes = await graph.GetNodesByType(type);
        var existing = existingNodes.FirstOrDefault(n => n.SourceId == sourceId);
        if (existing is not null)
            return existing;

        return await graph.AddNode(type, name, sourceId: sourceId);
    }
}
