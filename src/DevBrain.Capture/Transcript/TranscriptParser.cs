namespace DevBrain.Capture.Transcript;

using System.Text.Json;

public record TurnMetrics(
    int TokensIn,
    int TokensOut,
    int CacheReadTokens,
    int CacheWriteTokens,
    int LatencyMs,
    string Model
);

public record SessionAggregates(
    int TotalTokensIn,
    int TotalTokensOut,
    int TotalTurns,
    IReadOnlyList<string> ModelsUsed,
    Dictionary<string, int> ToolUsage,
    int ErrorCount
);

public static class TranscriptParser
{
    public static TurnMetrics? ParseLastTurn(string transcriptPath)
    {
        if (!File.Exists(transcriptPath))
            return null;

        var lines = File.ReadAllLines(transcriptPath);
        string? lastLine = null;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                lastLine = lines[i];
                break;
            }
        }

        if (lastLine is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(lastLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("usage", out var usage))
                return null;

            return new TurnMetrics(
                TokensIn: usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                TokensOut: usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                CacheReadTokens: usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0,
                CacheWriteTokens: usage.TryGetProperty("cache_creation_input_tokens", out var cw) ? cw.GetInt32() : 0,
                LatencyMs: root.TryGetProperty("latency_ms", out var lat) ? lat.GetInt32() : 0,
                Model: root.TryGetProperty("model", out var m) ? m.GetString() ?? "unknown" : "unknown"
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static SessionAggregates ParseSessionAggregates(string transcriptPath)
    {
        var totalIn = 0;
        var totalOut = 0;
        var turns = 0;
        var models = new HashSet<string>();
        var tools = new Dictionary<string, int>();
        var errors = 0;

        if (!File.Exists(transcriptPath))
            return new SessionAggregates(0, 0, 0, [], tools, 0);

        foreach (var line in File.ReadLines(transcriptPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("usage", out var usage))
                {
                    turns++;
                    totalIn += usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                    totalOut += usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                }

                if (root.TryGetProperty("model", out var m))
                    models.Add(m.GetString() ?? "unknown");

                if (root.TryGetProperty("tool_use", out var tu) && tu.TryGetProperty("name", out var tn))
                {
                    var name = tn.GetString() ?? "unknown";
                    tools[name] = tools.GetValueOrDefault(name) + 1;
                }

                if (root.TryGetProperty("error", out _))
                    errors++;
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return new SessionAggregates(totalIn, totalOut, turns, models.ToList(), tools, errors);
    }
}
