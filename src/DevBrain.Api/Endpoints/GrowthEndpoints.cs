namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;

public static class GrowthEndpoints
{
    public static void MapGrowthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/growth");

        group.MapGet("/", async (IGrowthStore growthStore) =>
        {
            var report = await growthStore.GetLatestReport();
            if (report is null)
                return Results.Ok(new { status = "no_data", message = "No growth reports yet." });
            return Results.Ok(report);
        });

        group.MapGet("/history", async (IGrowthStore growthStore, string? dimension, int? weeks) =>
        {
            if (!string.IsNullOrEmpty(dimension))
            {
                var metrics = await growthStore.GetMetrics(dimension, weeks ?? 12);
                return Results.Ok(metrics);
            }

            var latest = await growthStore.GetLatestMetrics();
            return Results.Ok(latest);
        });

        group.MapGet("/milestones", async (IGrowthStore growthStore, int? limit) =>
        {
            var milestones = await growthStore.GetMilestones(Math.Min(limit ?? 50, 200));
            return Results.Ok(milestones);
        });
    }
}
