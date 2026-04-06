namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin");

        group.MapPost("/rebuild/vectors", () =>
        {
            // Placeholder - vector rebuild not yet implemented
            return Results.Accepted();
        });

        group.MapPost("/rebuild/graph", async (IGraphStore graph) =>
        {
            await graph.Clear();
            return Results.Accepted();
        });
    }
}
