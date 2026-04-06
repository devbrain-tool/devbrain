namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Agents;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/agents");

        group.MapGet("/", (
            IEnumerable<IIntelligenceAgent> agents,
            AgentScheduler scheduler) =>
        {
            var lastRuns = scheduler.GetLastRunTimes();
            var result = agents.Select(a =>
            {
                lastRuns.TryGetValue(a.Name, out var lastRun);
                return new
                {
                    name = a.Name,
                    schedule = a.Schedule.ToString(),
                    priority = a.Priority.ToString(),
                    lastRun = lastRun == default ? (DateTime?)null : lastRun,
                    status = "idle"
                };
            });

            return Results.Ok(result);
        });

        group.MapPost("/{name}/run", (
            string name,
            IEnumerable<IIntelligenceAgent> agents,
            AgentContext ctx) =>
        {
            var agent = agents.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

            if (agent is null)
                return Results.NotFound(new { error = $"Agent '{name}' not found" });

            // Fire-and-forget
            _ = Task.Run(async () =>
            {
                try
                {
                    await agent.Run(ctx, CancellationToken.None);
                }
                catch
                {
                    // Swallow - fire and forget
                }
            });

            return Results.Accepted();
        });
    }
}
