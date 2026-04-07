namespace DevBrain.Api.Endpoints;

using DevBrain.Api.Services;
using DevBrain.Core.Interfaces;

public static class AlertEndpoints
{
    public static void MapAlertEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/alerts");

        group.MapGet("/", async (IAlertStore alertStore) =>
        {
            var alerts = await alertStore.GetActive();
            return Results.Ok(alerts);
        });

        group.MapGet("/all", async (IAlertStore alertStore, int? limit) =>
        {
            var capped = Math.Min(limit ?? 100, 1000);
            var alerts = await alertStore.GetAll(capped);
            return Results.Ok(alerts);
        });

        group.MapPost("/{id}/dismiss", async (string id, IAlertStore alertStore) =>
        {
            var found = await alertStore.Dismiss(id);
            return found
                ? Results.Ok(new { dismissed = true })
                : Results.NotFound(new { error = $"Alert '{id}' not found" });
        });

        group.MapGet("/stream", (AlertChannel channel, CancellationToken ct) =>
        {
            return Results.Stream(
                stream => WriteSSE(stream, channel, ct),
                contentType: "text/event-stream");
        });
    }

    private static async Task WriteSSE(Stream stream, AlertChannel channel, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream) { AutoFlush = true };

        await foreach (var alert in channel.ReadAllAsync(ct))
        {
            if (ct.IsCancellationRequested) break;

            var json = System.Text.Json.JsonSerializer.Serialize(alert);
            await writer.WriteLineAsync($"id: {alert.Id}");
            await writer.WriteLineAsync($"event: alert");
            await writer.WriteLineAsync($"data: {json}");
            await writer.WriteLineAsync();
        }
    }
}
