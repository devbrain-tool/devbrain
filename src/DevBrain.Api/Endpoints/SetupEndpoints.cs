namespace DevBrain.Api.Endpoints;

using DevBrain.Api.Setup;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/setup");

        group.MapGet("/status", async (SetupValidator validator) =>
        {
            var status = await validator.RunAllChecks();
            return Results.Ok(status);
        });

        group.MapPost("/fix/{checkId}", async (string checkId, SetupValidator validator) =>
        {
            var result = await validator.Fix(checkId);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });
    }
}
