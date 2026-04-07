namespace DevBrain.Api.Endpoints;

using DevBrain.Storage;

public static class ReplayEndpoints
{
    public static void MapReplayEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/replay");

        // Decision chain for a file
        group.MapGet("/file/{*path}", async (string path, DecisionChainBuilder builder, int? hops) =>
        {
            var chain = await builder.BuildForFile(path, hops ?? 3);
            return chain is not null
                ? Results.Ok(chain)
                : Results.NotFound(new { error = $"No decision chain found for '{path}'" });
        });

        // Decision chain from a specific graph node
        group.MapGet("/decision/{nodeId}", async (string nodeId, DecisionChainBuilder builder, int? hops) =>
        {
            var chain = await builder.BuildForDecision(nodeId, hops ?? 4);
            return chain is not null
                ? Results.Ok(chain)
                : Results.NotFound(new { error = $"No decision chain found for node '{nodeId}'" });
        });
    }
}
