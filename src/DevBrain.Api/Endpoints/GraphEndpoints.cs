namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;

public static class GraphEndpoints
{
    public static void MapGraphEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/graph");

        group.MapGet("/node/{id}", async (string id, IGraphStore graph) =>
        {
            var node = await graph.GetNode(id);
            return node is not null ? Results.Ok(node) : Results.NotFound();
        });

        group.MapGet("/neighbors", async (string nodeId, int? hops, string? edgeType, IGraphStore graph) =>
        {
            var neighbors = await graph.GetNeighbors(nodeId, hops ?? 1, edgeType);
            return Results.Ok(neighbors);
        });

        group.MapGet("/paths", async (string from, string to, int? maxDepth, IGraphStore graph) =>
        {
            var paths = await graph.FindPaths(from, to, maxDepth ?? 4);
            return Results.Ok(paths);
        });
    }
}
