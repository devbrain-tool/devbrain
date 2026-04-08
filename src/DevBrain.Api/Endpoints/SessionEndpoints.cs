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
        group.MapGet("/{sessionId}/story", async (string sessionId, ISessionStore sessionStore) =>
        {
            var summary = await sessionStore.GetBySessionId(sessionId);
            return summary is not null
                ? Results.Ok(summary)
                : Results.NotFound(new { error = $"No story for session '{sessionId}'" });
        });

        // Get session detail with observations
        group.MapGet("/{sessionId}", async (string sessionId,
            IObservationStore obsStore, ISessionStore sessionStore) =>
        {
            var observations = await obsStore.GetSessionObservations(sessionId);
            var story = await sessionStore.GetBySessionId(sessionId);

            return Results.Ok(new
            {
                sessionId,
                observations,
                story
            });
        });

        // Validate whether a session can have a story generated.
        // Actual generation runs via StorytellerAgent on idle schedule.
        // Known limitation: no way to trigger immediate generation (v1).
        group.MapPost("/{sessionId}/story", async (string sessionId,
            IObservationStore obsStore, ISessionStore sessionStore) =>
        {
            var existing = await sessionStore.GetBySessionId(sessionId);
            if (existing is not null)
                return Results.Ok(new { status = "already_generated", story = existing });

            var observations = await obsStore.GetSessionObservations(sessionId);
            if (observations.Count < 3)
                return Results.BadRequest(new { error = "Session has fewer than 3 observations" });

            return Results.Ok(new { status = "pending", message = "Session is eligible. Story will be generated when the storyteller agent runs next." });
        });
    }
}
