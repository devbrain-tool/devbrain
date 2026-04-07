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
            var alerts = await alertStore.GetAll(limit ?? 100);
            return Results.Ok(alerts);
        });

        group.MapPost("/{id}/dismiss", async (string id, IAlertStore alertStore) =>
        {
            await alertStore.Dismiss(id);
            return Results.Ok(new { dismissed = true });
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
        var writer = new StreamWriter(stream) { AutoFlush = true };

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
