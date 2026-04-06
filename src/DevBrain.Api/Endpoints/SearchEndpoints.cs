namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/search");

        // Semantic search (FTS for v1)
        group.MapGet("/", async (string? q, int? limit, IObservationStore store) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required" });

            var results = await store.SearchFts(q, limit ?? 10);
            return Results.Ok(results);
        });

        // Exact FTS search
        group.MapGet("/exact", async (string? q, int? limit, IObservationStore store) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required" });

            var results = await store.SearchFts(q, limit ?? 10);
            return Results.Ok(results);
        });
    }
}
