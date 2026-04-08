namespace DevBrain.Api.Endpoints;

using DevBrain.Storage;

public static class BlastRadiusEndpoints
{
    public static void MapBlastRadiusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/blast-radius");

        group.MapGet("/{*path}", async (string path, BlastRadiusCalculator calculator, int? hops) =>
        {
            var cappedHops = Math.Clamp(hops ?? 3, 1, 5);
            var result = await calculator.Calculate(path, cappedHops);
            return Results.Ok(result);
        });
    }
}
