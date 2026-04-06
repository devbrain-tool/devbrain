namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Models;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/settings");

        group.MapGet("/", (Settings settings) => Results.Ok(settings));

        group.MapPut("/", (Settings updated) =>
        {
            // For v1, just echo back the settings.
            // Persistence will be added in a future task.
            return Results.Ok(updated);
        });
    }
}
