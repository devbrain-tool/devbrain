namespace DevBrain.Api.Services;

using System.Collections.Concurrent;
using System.Text.Json;
using DevBrain.Capture.Privacy;
using DevBrain.Capture.Transcript;
using DevBrain.Capture.Truncation;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class EventIngestionService
{
    private readonly IObservationStore _store;
    private readonly SecretPatternRedactor _secretRedactor;
    private readonly FieldAwareRedactor _fieldRedactor;
    private readonly ConcurrentDictionary<string, int> _turnCounters = new();
    private readonly string _transcriptArchiveDir;

    public EventIngestionService(
        IObservationStore store,
        SecretPatternRedactor secretRedactor,
        FieldAwareRedactor fieldRedactor)
    {
        _store = store;
        _secretRedactor = secretRedactor;
        _fieldRedactor = fieldRedactor;
        _transcriptArchiveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".devbrain", "transcripts");
    }

    public async Task<Observation?> IngestEvent(JsonElement payload)
    {
        var hookEvent = payload.GetProperty("hookEvent").GetString()!;
        var sessionId = payload.GetProperty("session_id").GetString()!;
        var cwd = payload.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() ?? "" : "";

        var eventType = MapEventType(hookEvent);
        if (eventType is null) return null;

        string? toolName = payload.TryGetProperty("tool_name", out var tn) ? tn.GetString() : null;
        string? outcome = null;
        JsonElement toolInput = default;
        JsonElement toolOutput = default;

        if (hookEvent == "PostToolUse")
        {
            outcome = "success";
            if (payload.TryGetProperty("tool_input", out toolInput) && payload.TryGetProperty("tool_response", out toolOutput))
            {
                var truncated = SmartTruncator.Truncate(toolName ?? "", toolInput, toolOutput);
                toolInput = truncated.Input;
                toolOutput = truncated.Output;
            }
        }
        else if (hookEvent == "PostToolUseFailure")
        {
            outcome = "failure";
            if (payload.TryGetProperty("tool_input", out toolInput))
            {
                var truncated = SmartTruncator.Truncate(toolName ?? "", toolInput, default);
                toolInput = truncated.Input;
            }
        }

        var metadata = BuildMetadata(hookEvent, payload, toolInput, toolOutput);

        int? turnNumber = null;
        int? durationMs = null;

        if (hookEvent == "SessionStart")
            _turnCounters[sessionId] = 0;

        if (hookEvent is "Stop" or "StopFailure")
        {
            var count = _turnCounters.AddOrUpdate(sessionId, 1, (_, v) => v + 1);
            turnNumber = count;

            if (payload.TryGetProperty("transcript_path", out var tp))
            {
                var transcriptPath = tp.GetString();
                if (transcriptPath is not null)
                {
                    var metrics = TranscriptParser.ParseLastTurn(transcriptPath);
                    if (metrics is not null)
                    {
                        durationMs = metrics.LatencyMs;
                        metadata = EnrichWithTurnMetrics(metadata, metrics);
                    }
                }
            }
        }
        else
        {
            turnNumber = _turnCounters.GetValueOrDefault(sessionId);
        }

        if (hookEvent == "SessionEnd" && payload.TryGetProperty("transcript_path", out var tpEnd))
        {
            var transcriptPath = tpEnd.GetString();
            if (transcriptPath is not null)
            {
                try
                {
                    TranscriptArchiver.Archive(transcriptPath, sessionId, _transcriptArchiveDir);
                    var aggregates = TranscriptParser.ParseSessionAggregates(transcriptPath);
                    metadata = EnrichWithSessionAggregates(metadata, aggregates);
                }
                catch { }
            }
            _turnCounters.TryRemove(sessionId, out _);
        }

        var rawContent = GenerateRawContent(eventType.Value, toolName, metadata);
        var project = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(project)) project = "unknown";
        var filesInvolved = ExtractFiles(toolInput);

        rawContent = _secretRedactor.Redact(rawContent);
        metadata = _secretRedactor.Redact(metadata);
        metadata = _fieldRedactor.Redact(toolName, metadata);

        var observation = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Timestamp = DateTime.UtcNow,
            Project = project,
            EventType = eventType.Value,
            Source = CaptureSource.ClaudeCode,
            RawContent = rawContent,
            Metadata = metadata,
            ToolName = toolName,
            Outcome = outcome,
            DurationMs = durationMs,
            TurnNumber = turnNumber,
            FilesInvolved = filesInvolved,
        };

        return await _store.Add(observation);
    }

    private static EventType? MapEventType(string hookEvent) => hookEvent switch
    {
        "PostToolUse" => EventType.ToolCall,
        "PostToolUseFailure" => EventType.ToolFailure,
        "UserPromptSubmit" => EventType.UserPrompt,
        "SessionStart" => EventType.SessionStart,
        "SessionEnd" => EventType.SessionEnd,
        "Stop" => EventType.TurnComplete,
        "StopFailure" => EventType.TurnError,
        "SubagentStart" => EventType.SubagentStart,
        "SubagentStop" => EventType.SubagentStop,
        "FileChanged" => EventType.FileChange,
        "CwdChanged" => EventType.CwdChange,
        "PostCompact" => EventType.ContextCompact,
        _ => null,
    };

    private static string BuildMetadata(string hookEvent, JsonElement payload,
        JsonElement toolInput, JsonElement toolOutput)
    {
        var dict = new Dictionary<string, object?>();

        switch (hookEvent)
        {
            case "PostToolUse":
                if (toolInput.ValueKind != JsonValueKind.Undefined) dict["tool_input"] = toolInput;
                if (toolOutput.ValueKind != JsonValueKind.Undefined) dict["tool_output"] = toolOutput;
                if (payload.TryGetProperty("tool_use_id", out var tuId)) dict["tool_use_id"] = tuId.GetString();
                break;
            case "PostToolUseFailure":
                if (toolInput.ValueKind != JsonValueKind.Undefined) dict["tool_input"] = toolInput;
                if (payload.TryGetProperty("error", out var err)) dict["error"] = err.GetString();
                if (payload.TryGetProperty("is_interrupt", out var intr)) dict["is_interrupt"] = intr.GetBoolean();
                if (payload.TryGetProperty("tool_use_id", out var tuId2)) dict["tool_use_id"] = tuId2.GetString();
                break;
            case "UserPromptSubmit":
                if (payload.TryGetProperty("prompt", out var prompt)) dict["prompt"] = prompt.GetString();
                break;
            case "SessionStart":
                if (payload.TryGetProperty("source", out var src)) dict["source"] = src.GetString();
                if (payload.TryGetProperty("model", out var model)) dict["model"] = model.GetString();
                if (payload.TryGetProperty("permission_mode", out var pm)) dict["permission_mode"] = pm.GetString();
                break;
            case "SubagentStart":
            case "SubagentStop":
                if (payload.TryGetProperty("agent_type", out var at)) dict["agent_type"] = at.GetString();
                if (payload.TryGetProperty("agent_id", out var aid)) dict["agent_id"] = aid.GetString();
                if (payload.TryGetProperty("last_assistant_message", out var lm)) dict["last_message"] = lm.GetString();
                break;
            case "StopFailure":
                if (payload.TryGetProperty("error_type", out var et)) dict["error_type"] = et.GetString();
                break;
            case "FileChanged":
                if (payload.TryGetProperty("file_path", out var fp)) dict["file_path"] = fp.GetString();
                if (payload.TryGetProperty("change_type", out var ct)) dict["change_type"] = ct.GetString();
                break;
            case "CwdChanged":
                if (payload.TryGetProperty("cwd", out var newCwd)) dict["new_cwd"] = newCwd.GetString();
                if (payload.TryGetProperty("previous_cwd", out var prevCwd)) dict["previous_cwd"] = prevCwd.GetString();
                break;
        }

        return JsonSerializer.Serialize(dict);
    }

    private static string GenerateRawContent(EventType eventType, string? toolName, string metadata)
    {
        return eventType switch
        {
            EventType.ToolCall => $"{toolName}: {GetBriefDescription(metadata)}",
            EventType.ToolFailure => $"{toolName} FAILED: {GetBriefDescription(metadata)}",
            EventType.UserPrompt => $"User: {Truncate(GetJsonStr(metadata, "prompt"), 200)}",
            EventType.SessionStart => $"Session started: {GetJsonStr(metadata, "model")} ({GetJsonStr(metadata, "source")})",
            EventType.SessionEnd => FormatSessionEnd(metadata),
            EventType.TurnComplete => FormatTurnComplete(metadata),
            EventType.TurnError => $"Turn error: {GetJsonStr(metadata, "error_type")}",
            EventType.SubagentStart => $"Subagent started: {GetJsonStr(metadata, "agent_type")} ({GetJsonStr(metadata, "agent_id")})",
            EventType.SubagentStop => $"Subagent finished: {GetJsonStr(metadata, "agent_type")} — {Truncate(GetJsonStr(metadata, "last_message"), 100)}",
            EventType.FileChange => $"File {GetJsonStr(metadata, "change_type")}: {GetJsonStr(metadata, "file_path")}",
            EventType.CwdChange => $"Directory changed: {GetJsonStr(metadata, "previous_cwd")} → {GetJsonStr(metadata, "new_cwd")}",
            EventType.ContextCompact => "Context compacted",
            _ => "Unknown event",
        };
    }

    private static string GetBriefDescription(string metadata)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            if (doc.RootElement.TryGetProperty("tool_input", out var ti))
            {
                if (ti.TryGetProperty("command", out var cmd)) return Truncate(cmd.GetString(), 100);
                if (ti.TryGetProperty("file_path", out var fp)) return fp.GetString() ?? "";
                if (ti.TryGetProperty("pattern", out var pat)) return pat.GetString() ?? "";
                if (ti.TryGetProperty("query", out var q)) return q.GetString() ?? "";
            }
        }
        catch { }
        return "";
    }

    private static string FormatSessionEnd(string metadata)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            var r = doc.RootElement;
            var turns = r.TryGetProperty("total_turns", out var t) ? t.GetInt32() : 0;
            var tokensIn = r.TryGetProperty("total_tokens_in", out var ti) ? ti.GetInt32() : 0;
            var tokensOut = r.TryGetProperty("total_tokens_out", out var to2) ? to2.GetInt32() : 0;
            var errors = r.TryGetProperty("error_count", out var e) ? e.GetInt32() : 0;
            return $"Session ended: {turns} turns, {(tokensIn + tokensOut) / 1000}K tokens, {errors} errors";
        }
        catch { return "Session ended"; }
    }

    private static string FormatTurnComplete(string metadata)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            var r = doc.RootElement;
            var ti = r.TryGetProperty("tokens_in", out var tip) ? tip.GetInt32() : 0;
            var to2 = r.TryGetProperty("tokens_out", out var top) ? top.GetInt32() : 0;
            var lat = r.TryGetProperty("latency_ms", out var l) ? l.GetInt32() : 0;
            return $"Turn complete: {ti / 1000.0:F1}K in / {to2 / 1000.0:F1}K out ({lat / 1000.0:F1}s)";
        }
        catch { return "Turn complete"; }
    }

    private static string GetJsonStr(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var val))
                return val.GetString() ?? "";
        }
        catch { }
        return "";
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (text is null) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    private static string EnrichWithTurnMetrics(string metadata, TurnMetrics metrics)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? [];
            dict["tokens_in"] = metrics.TokensIn;
            dict["tokens_out"] = metrics.TokensOut;
            dict["cache_read_tokens"] = metrics.CacheReadTokens;
            dict["cache_write_tokens"] = metrics.CacheWriteTokens;
            dict["latency_ms"] = metrics.LatencyMs;
            dict["model"] = metrics.Model;
            return JsonSerializer.Serialize(dict);
        }
        catch { return metadata; }
    }

    private static string EnrichWithSessionAggregates(string metadata, SessionAggregates agg)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? [];
            dict["total_turns"] = agg.TotalTurns;
            dict["total_tokens_in"] = agg.TotalTokensIn;
            dict["total_tokens_out"] = agg.TotalTokensOut;
            dict["tool_usage"] = agg.ToolUsage;
            dict["error_count"] = agg.ErrorCount;
            dict["models_used"] = agg.ModelsUsed;
            return JsonSerializer.Serialize(dict);
        }
        catch { return metadata; }
    }

    private static IReadOnlyList<string> ExtractFiles(JsonElement toolInput)
    {
        if (toolInput.ValueKind == JsonValueKind.Undefined) return [];
        var files = new List<string>();
        if (toolInput.TryGetProperty("file_path", out var fp) && fp.GetString() is { } fpath) files.Add(fpath);
        if (toolInput.TryGetProperty("path", out var p) && p.GetString() is { } ppath) files.Add(ppath);
        return files;
    }
}
