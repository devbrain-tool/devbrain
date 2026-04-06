namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Agents;

public static class ObservationEndpoints
{
    public record CreateObservationRequest(
        string SessionId,
        string EventType,
        string Source,
        string RawContent,
        string? Project = null,
        string? Branch = null);

    public static void MapObservationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/observations");

        group.MapPost("/", async (CreateObservationRequest req, IObservationStore store, EventBus eventBus) =>
        {
            if (!Enum.TryParse<EventType>(req.EventType, true, out var eventType))
                return Results.BadRequest(new { error = $"Invalid event type: {req.EventType}" });

            if (!Enum.TryParse<CaptureSource>(req.Source, true, out var source))
                return Results.BadRequest(new { error = $"Invalid source: {req.Source}" });

            var obs = new Observation
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = req.SessionId,
                Timestamp = DateTime.UtcNow,
                Project = req.Project ?? "unknown",
                Branch = req.Branch,
                EventType = eventType,
                Source = source,
                RawContent = req.RawContent
            };

            var saved = await store.Add(obs);
            eventBus.Publish(saved);
            return Results.Created($"/api/v1/observations/{saved.Id}", saved);
        });

        group.MapGet("/", async (
            string? project,
            string? eventType,
            string? threadId,
            int? limit,
            int? offset,
            IObservationStore store) =>
        {
            EventType? parsedType = null;
            if (eventType is not null && Enum.TryParse<EventType>(eventType, true, out var et))
                parsedType = et;

            var filter = new ObservationFilter
            {
                Project = project,
                EventType = parsedType,
                ThreadId = threadId,
                Limit = limit ?? 50,
                Offset = offset ?? 0
            };

            var results = await store.Query(filter);
            return Results.Ok(results);
        });

        group.MapGet("/{id}", async (string id, IObservationStore store) =>
        {
            var obs = await store.GetById(id);
            return obs is not null ? Results.Ok(obs) : Results.NotFound();
        });
    }
}
