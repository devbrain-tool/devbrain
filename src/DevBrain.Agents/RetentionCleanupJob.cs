namespace DevBrain.Agents;

using System.Text.Json;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class RetentionCleanupJob : IIntelligenceAgent
{
    public string Name => "retention-cleanup";
    public AgentSchedule Schedule => new AgentSchedule.Idle(TimeSpan.FromHours(24));
    public Priority Priority => Priority.Low;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        var trimmed = await TrimOldMetadata(ctx);
        var deleted = DeleteOldTranscripts();

        return
        [
            new AgentOutput(
                AgentOutputType.RetentionCleanup,
                $"Trimmed metadata on {trimmed} observations. Deleted {deleted} old transcripts."
            )
        ];
    }

    private static async Task<int> TrimOldMetadata(AgentContext ctx)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var totalCount = 0;

        while (true)
        {
            var batch = await ctx.Observations.Query(new ObservationFilter
            {
                Before = cutoff,
                Limit = 500,
            });

            if (batch.Count == 0) break;

            var batchTrimmed = 0;
            foreach (var obs in batch)
            {
                if (obs.Metadata is "{}" or "") continue;

                var trimmed = TrimMetadataFields(obs.Metadata);
                if (trimmed != obs.Metadata)
                {
                    await ctx.Observations.Update(obs with { Metadata = trimmed });
                    batchTrimmed++;
                }
            }

            totalCount += batchTrimmed;

            // If no observations in this batch needed trimming, we're done
            if (batchTrimmed == 0) break;
        }

        return totalCount;
    }

    private static string TrimMetadataFields(string metadata)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadata);
            if (dict is null) return metadata;

            var result = new Dictionary<string, object?>();
            foreach (var kv in dict)
            {
                switch (kv.Key)
                {
                    case "tool_output":
                        var outputStr = kv.Value.GetRawText();
                        result[kv.Key] = outputStr.Length > 1024
                            ? outputStr[..1024] + " [trimmed by retention]"
                            : (object)kv.Value;
                        break;

                    case "prompt" when kv.Value.ValueKind == JsonValueKind.String:
                        var prompt = kv.Value.GetString() ?? "";
                        result[kv.Key] = prompt.Length > 2000
                            ? prompt[..2000] + " [trimmed by retention]"
                            : prompt;
                        break;

                    case "last_message" when kv.Value.ValueKind == JsonValueKind.String:
                        var msg = kv.Value.GetString() ?? "";
                        result[kv.Key] = msg.Length > 1024
                            ? msg[..1024] + " [trimmed by retention]"
                            : msg;
                        break;

                    case "tool_input" when kv.Value.ValueKind == JsonValueKind.Object
                        && kv.Value.TryGetProperty("content", out _):
                        // Remove Write file content
                        var inputDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(kv.Value.GetRawText());
                        if (inputDict is not null)
                        {
                            inputDict.Remove("content");
                            result[kv.Key] = inputDict;
                        }
                        else
                        {
                            result[kv.Key] = kv.Value;
                        }
                        break;

                    default:
                        result[kv.Key] = kv.Value;
                        break;
                }
            }

            return JsonSerializer.Serialize(result);
        }
        catch
        {
            return metadata;
        }
    }

    private static int DeleteOldTranscripts()
    {
        var transcriptDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".devbrain", "transcripts");

        if (!Directory.Exists(transcriptDir))
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var count = 0;
        foreach (var file in Directory.GetFiles(transcriptDir, "*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                count++;
            }
        }
        return count;
    }
}
