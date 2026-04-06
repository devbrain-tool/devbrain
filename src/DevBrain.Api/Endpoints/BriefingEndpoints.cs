namespace DevBrain.Api.Endpoints;

using DevBrain.Core;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Agents;

public static class BriefingEndpoints
{
    public static void MapBriefingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/briefings");

        group.MapGet("/", (Settings settings) =>
        {
            var dataPath = SettingsLoader.ResolveDataPath(settings.Daemon.DataPath);
            var briefingsDir = Path.Combine(dataPath, "briefings");

            if (!Directory.Exists(briefingsDir))
                return Results.Ok(Array.Empty<string>());

            var files = Directory.GetFiles(briefingsDir, "*.md")
                .Select(Path.GetFileName)
                .OrderByDescending(f => f)
                .ToList();

            return Results.Ok(files);
        });

        group.MapGet("/latest", async (Settings settings) =>
        {
            var dataPath = SettingsLoader.ResolveDataPath(settings.Daemon.DataPath);
            var briefingsDir = Path.Combine(dataPath, "briefings");

            var todayFile = Path.Combine(briefingsDir, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".md");
            if (!File.Exists(todayFile))
            {
                // Try the most recent file
                if (Directory.Exists(briefingsDir))
                {
                    var latest = Directory.GetFiles(briefingsDir, "*.md")
                        .OrderByDescending(f => f)
                        .FirstOrDefault();

                    if (latest is not null)
                    {
                        var content = await File.ReadAllTextAsync(latest);
                        return Results.Ok(new { file = Path.GetFileName(latest), content });
                    }
                }

                return Results.NotFound(new { error = "No briefings found" });
            }

            var todayContent = await File.ReadAllTextAsync(todayFile);
            return Results.Ok(new { file = Path.GetFileName(todayFile), content = todayContent });
        });

        group.MapPost("/generate", (
            IEnumerable<IIntelligenceAgent> agents,
            AgentContext ctx,
            ILoggerFactory loggerFactory) =>
        {
            var briefingAgent = agents.FirstOrDefault(a => a.Name == "briefing");
            if (briefingAgent is null)
                return Results.NotFound(new { error = "Briefing agent not found" });

            var logger = loggerFactory.CreateLogger("DevBrain.Api.Endpoints.BriefingEndpoints");

            // Fire-and-forget with logging
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("Starting briefing generation");
                    await briefingAgent.Run(ctx, CancellationToken.None);
                    logger.LogInformation("Briefing generation completed successfully");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Briefing generation failed");
                }
            });

            return Results.Accepted();
        });
    }
}
