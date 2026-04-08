namespace DevBrain.Api.Endpoints;

using System.Text.Json;
using DevBrain.Api.Services;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/events", async (JsonElement payload, EventIngestionService ingestion) =>
        {
            if (!payload.TryGetProperty("hookEvent", out var he) || !payload.TryGetProperty("session_id", out _))
                return Results.BadRequest(new { error = "hookEvent and session_id are required" });

            var observation = await ingestion.IngestEvent(payload);
            return observation is not null
                ? Results.Created($"/api/v1/observations/{observation.Id}", new { id = observation.Id })
                : Results.BadRequest(new { error = $"Unknown hook event: {he.GetString()}" });
        });
    }
}
