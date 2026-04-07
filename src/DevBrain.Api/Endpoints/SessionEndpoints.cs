namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/sessions");

        // List all sessions with summaries
        group.MapGet("/", async (ISessionStore sessionStore, int? limit) =>
        {
            var capped = Math.Min(limit ?? 50, 200);
            var sessions = await sessionStore.GetAll(capped);
            return Results.Ok(sessions);
        });

        // Get session story by session ID
        group.MapGet("/{id}/story", async (string id, ISessionStore sessionStore) =>
        {
            var summary = await sessionStore.GetBySessionId(id);
            return summary is not null
                ? Results.Ok(summary)
                : Results.NotFound(new { error = $"No story for session '{id}'" });
        });

        // Get session detail with observations
        group.MapGet("/{id}", async (string id,
            IObservationStore obsStore, ISessionStore sessionStore) =>
        {
            var observations = await obsStore.GetSessionObservations(id);
            var story = await sessionStore.GetBySessionId(id);

            return Results.Ok(new
            {
                sessionId = id,
                observations,
                story
            });
        });

        // Trigger story generation on demand (fire-and-forget via agent)
        group.MapPost("/{id}/story", async (string id,
            IObservationStore obsStore, ISessionStore sessionStore) =>
        {
            var existing = await sessionStore.GetBySessionId(id);
            if (existing is not null)
                return Results.Ok(new { status = "already_generated", story = existing });

            var observations = await obsStore.GetSessionObservations(id);
            if (observations.Count < 3)
                return Results.BadRequest(new { error = "Session has fewer than 3 observations" });

            return Results.Accepted(new { status = "queued", message = "Story generation will run on next agent cycle" });
        });
    }
}
