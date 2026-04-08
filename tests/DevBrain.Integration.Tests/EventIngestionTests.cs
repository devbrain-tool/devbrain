namespace DevBrain.Integration.Tests;

using System.Text.Json;
using DevBrain.Api.Services;
using DevBrain.Capture.Privacy;
using DevBrain.Core.Enums;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

public class EventIngestionTests
{
    [Fact]
    public async Task PostToolUse_CreatesObservation_WithStructuredMetadata()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        SchemaManager.Initialize(conn);

        var store = new SqliteObservationStore(conn);
        var service = new EventIngestionService(store, new SecretPatternRedactor(), new FieldAwareRedactor());

        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            hookEvent = "PostToolUse",
            session_id = "test-session",
            cwd = "/project/devbrain",
            tool_name = "Bash",
            tool_input = new { command = "npm test", timeout = 120000 },
            tool_response = new { stdout = "All tests passed", stderr = "", exit_code = 0 },
            tool_use_id = "tu-123",
        })).RootElement;

        var obs = await service.IngestEvent(payload);

        Assert.NotNull(obs);
        Assert.Equal(EventType.ToolCall, obs!.EventType);
        Assert.Equal("Bash", obs.ToolName);
        Assert.Equal("success", obs.Outcome);
        Assert.Contains("npm test", obs.RawContent);

        var meta = JsonDocument.Parse(obs.Metadata);
        Assert.True(meta.RootElement.TryGetProperty("tool_input", out _));
        Assert.True(meta.RootElement.TryGetProperty("tool_output", out _));
    }

    [Fact]
    public async Task UserPromptSubmit_CreatesObservation()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        SchemaManager.Initialize(conn);

        var store = new SqliteObservationStore(conn);
        var service = new EventIngestionService(store, new SecretPatternRedactor(), new FieldAwareRedactor());

        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            hookEvent = "UserPromptSubmit",
            session_id = "test-session",
            cwd = "/project/devbrain",
            prompt = "Fix the null pointer in auth middleware",
        })).RootElement;

        var obs = await service.IngestEvent(payload);

        Assert.NotNull(obs);
        Assert.Equal(EventType.UserPrompt, obs!.EventType);
        Assert.Contains("Fix the null pointer", obs.RawContent);
    }

    [Fact]
    public async Task PrivacyLayer_RedactsSecrets_InMetadata()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        SchemaManager.Initialize(conn);

        var store = new SqliteObservationStore(conn);
        var service = new EventIngestionService(store, new SecretPatternRedactor(), new FieldAwareRedactor());

        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            hookEvent = "PostToolUse",
            session_id = "test-session",
            cwd = "/project",
            tool_name = "Bash",
            tool_input = new { command = "curl -H 'Authorization: Bearer eyJhbGciOiJ' https://api.example.com" },
            tool_response = new { stdout = "done", exit_code = 0 },
        })).RootElement;

        var obs = await service.IngestEvent(payload);

        Assert.DoesNotContain("eyJhbGciOiJ", obs!.Metadata);
        Assert.Contains("REDACTED", obs.Metadata);
    }

    [Fact]
    public async Task UnknownHookEvent_ReturnsNull()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        SchemaManager.Initialize(conn);

        var store = new SqliteObservationStore(conn);
        var service = new EventIngestionService(store, new SecretPatternRedactor(), new FieldAwareRedactor());

        var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            hookEvent = "UnknownEvent",
            session_id = "test-session",
        })).RootElement;

        var obs = await service.IngestEvent(payload);

        Assert.Null(obs);
    }
}
