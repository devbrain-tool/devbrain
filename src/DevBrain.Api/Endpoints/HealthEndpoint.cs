namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Agents;
using DevBrain.Llm;

public static class HealthEndpoint
{
    internal static readonly DateTime StartTime = DateTime.UtcNow;

    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/health", async (
            IObservationStore observations,
            IGraphStore graph,
            IVectorStore vectors,
            ILlmService llm,
            AgentScheduler scheduler,
            Settings settings) =>
        {
            var uptimeSeconds = (long)(DateTime.UtcNow - StartTime).TotalSeconds;
            var totalObservations = await observations.Count();
            var sqliteBytes = await observations.GetDatabaseSizeBytes();
            var vectorBytes = await vectors.GetSizeBytes();

            var lastRuns = scheduler.GetLastRunTimes();
            var agents = new Dictionary<string, AgentHealth>();
            foreach (var (name, lastRun) in lastRuns)
            {
                agents[name] = new AgentHealth
                {
                    LastRun = lastRun,
                    Status = "idle"
                };
            }

            var status = new HealthStatus
            {
                Status = "ok",
                UptimeSeconds = uptimeSeconds,
                Storage = new StorageHealth
                {
                    SqliteSizeMb = sqliteBytes / (1024 * 1024),
                    LanceDbSizeMb = vectorBytes / (1024 * 1024),
                    TotalObservations = totalObservations
                },
                Agents = agents,
                Llm = new LlmHealth
                {
                    Local = new LlmProviderHealth
                    {
                        Status = llm.IsLocalAvailable ? "available" : "unavailable",
                        Model = settings.Llm.Local.Model,
                        QueueDepth = llm.QueueDepth
                    },
                    Cloud = new LlmProviderHealth
                    {
                        Status = llm.IsCloudAvailable ? "available" : "unavailable",
                        Model = settings.Llm.Cloud.Model,
                        RequestsToday = llm.CloudRequestsToday,
                        Limit = settings.Llm.Cloud.MaxDailyRequests
                    }
                }
            };

            return Results.Ok(status);
        });
    }
}
