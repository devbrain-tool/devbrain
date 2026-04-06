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

        // Export all observations as JSON
        app.MapPost("/api/v1/export", async (IObservationStore store) =>
        {
            var all = await store.Query(new ObservationFilter { Limit = int.MaxValue });
            return Results.Ok(all);
        });

        // Delete observations with optional project/before filters
        app.MapDelete("/api/v1/data", async (string? project, DateTime? before, IObservationStore store) =>
        {
            if (project is not null)
            {
                await store.DeleteByProject(project);
            }
            else if (before is not null)
            {
                await store.DeleteBefore(before.Value);
            }
            else
            {
                return Results.BadRequest(new { error = "Provide 'project' or 'before' query parameter" });
            }

            return Results.Ok(new { status = "deleted" });
        });
    }
}
