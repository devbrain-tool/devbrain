# Rich Observation Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform DevBrain from flat-string event logging into a full developer flight recorder — capturing structured data from 12 Claude Code hook events with smart truncation, privacy filtering, transcript parsing, and tiered retention.

**Architecture:** A thin CLI hook binary (`devbrain hook`) reads stdin from Claude Code hooks and forwards raw JSON to the daemon's new `/api/v1/events` endpoint. The daemon's `EventIngestionService` orchestrates: smart truncation → rawContent generation → privacy filtering (2 layers) → transcript parsing → DB write → EventBus publish. A daily `RetentionCleanupJob` trims old metadata and deletes aged transcripts.

**Tech Stack:** C# / .NET 9, ASP.NET Core Minimal APIs, SQLite, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-04-08-rich-observation-capture-design.md`

---

## File Structure

### New Files

```
src/DevBrain.Core/Enums/EventType.cs                      # Modified — add 10 new values
src/DevBrain.Core/Models/Observation.cs                    # Modified — add 5 new properties
src/DevBrain.Core/Interfaces/IObservationStore.cs          # Modified — add filter fields
src/DevBrain.Capture/Truncation/SmartTruncator.cs          # New — tool-type-aware truncation
src/DevBrain.Capture/Privacy/FieldAwareRedactor.cs         # New — privacy Layer 2
src/DevBrain.Capture/Transcript/TranscriptParser.cs        # New — JSONL tail + full parse
src/DevBrain.Capture/Transcript/TranscriptArchiver.cs      # New — copy JSONL to archive
src/DevBrain.Api/Endpoints/EventEndpoints.cs               # New — POST /api/v1/events
src/DevBrain.Api/Services/EventIngestionService.cs         # New — orchestrator
src/DevBrain.Agents/RetentionCleanupJob.cs                 # New — daily tiered cleanup
src/DevBrain.Cli/Commands/HookCommand.cs                   # New — thin stdin→HTTP forwarder
src/DevBrain.Storage/Schema/SchemaManager.cs               # Modified — add columns + migration
src/DevBrain.Storage/SqliteObservationStore.cs             # Modified — read/write new columns
src/DevBrain.Api/Program.cs                                # Modified — register new services
src/DevBrain.Cli/Program.cs                                # Modified — register HookCommand
src/DevBrain.Capture/Pipeline/PrivacyFilter.cs             # Modified — integrate Layer 2
tests/DevBrain.Capture.Tests/SmartTruncatorTests.cs        # New
tests/DevBrain.Capture.Tests/FieldAwareRedactorTests.cs    # New
tests/DevBrain.Capture.Tests/TranscriptParserTests.cs      # New
tests/DevBrain.Agents.Tests/RetentionCleanupTests.cs       # New
tests/DevBrain.Integration.Tests/EventIngestionTests.cs    # New
```

---

## Task 1: Expand EventType Enum + Observation Model

**Files:**
- Modify: `src/DevBrain.Core/Enums/EventType.cs`
- Modify: `src/DevBrain.Core/Models/Observation.cs`

- [ ] **Step 1: Add 10 new EventType values**

```csharp
namespace DevBrain.Core.Enums;

public enum EventType
{
    // Existing
    ToolCall,
    FileChange,
    Decision,
    Error,
    Conversation,

    // New
    ToolFailure,
    UserPrompt,
    SessionStart,
    SessionEnd,
    TurnComplete,
    TurnError,
    SubagentStart,
    SubagentStop,
    CwdChange,
    ContextCompact,
}
```

Write to: `src/DevBrain.Core/Enums/EventType.cs`

- [ ] **Step 2: Add new properties to Observation model**

```csharp
namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public record Observation
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public string? ThreadId { get; init; }
    public string? ParentId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Project { get; init; }
    public string? Branch { get; init; }
    public required EventType EventType { get; init; }
    public required CaptureSource Source { get; init; }
    public required string RawContent { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> FilesInvolved { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // New fields for rich capture
    public string Metadata { get; init; } = "{}";
    public string? ToolName { get; init; }
    public string? Outcome { get; init; }
    public int? DurationMs { get; init; }
    public int? TurnNumber { get; init; }
}
```

Write to: `src/DevBrain.Core/Models/Observation.cs`

- [ ] **Step 3: Add filter fields to ObservationFilter**

In `src/DevBrain.Core/Interfaces/IObservationStore.cs`, add `ToolName` and `Outcome` to the filter:

```csharp
public record ObservationFilter
{
    public string? Project { get; init; }
    public EventType? EventType { get; init; }
    public string? ThreadId { get; init; }
    public string? ToolName { get; init; }
    public string? Outcome { get; init; }
    public DateTime? After { get; init; }
    public DateTime? Before { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded (new enum values and properties don't break existing code — all new fields have defaults).

- [ ] **Step 5: Commit**

```bash
git add src/DevBrain.Core/
git commit -m "feat(capture): expand EventType enum and Observation model for rich capture"
```

---

## Task 2: Schema Migration + Storage Updates

**Files:**
- Modify: `src/DevBrain.Storage/Schema/SchemaManager.cs`
- Modify: `src/DevBrain.Storage/SqliteObservationStore.cs`

- [ ] **Step 1: Add migration to SchemaManager**

In `SchemaManager.cs`, after the existing schema creation block (after line ~120, after all CREATE TABLE statements), add migration logic:

```csharp
// Migration: add rich capture columns (v2)
MigrateToV2(connection);
```

Add the migration method:

```csharp
private static void MigrateToV2(SqliteConnection connection)
{
    // Check if metadata column already exists
    using var checkCmd = connection.CreateCommand();
    checkCmd.CommandText = "PRAGMA table_info(observations)";
    using var reader = checkCmd.ExecuteReader();
    var columns = new HashSet<string>();
    while (reader.Read())
        columns.Add(reader.GetString(1));

    if (columns.Contains("metadata"))
        return; // Already migrated

    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        ALTER TABLE observations ADD COLUMN metadata TEXT NOT NULL DEFAULT '{}';
        ALTER TABLE observations ADD COLUMN tool_name TEXT;
        ALTER TABLE observations ADD COLUMN outcome TEXT;
        ALTER TABLE observations ADD COLUMN duration_ms INTEGER;
        ALTER TABLE observations ADD COLUMN turn_number INTEGER;

        CREATE INDEX IF NOT EXISTS idx_obs_tool_name ON observations(tool_name);
        CREATE INDEX IF NOT EXISTS idx_obs_outcome ON observations(outcome);
        """;
    cmd.ExecuteNonQuery();

    // Update schema version
    using var versionCmd = connection.CreateCommand();
    versionCmd.CommandText = "UPDATE _meta SET value = '2' WHERE key = 'schema_version'";
    versionCmd.ExecuteNonQuery();
}
```

- [ ] **Step 2: Update SqliteObservationStore.Add() to write new columns**

Replace the `Add` method's INSERT statement to include the new columns:

```csharp
public async Task<Observation> Add(Observation observation)
{
    using var cmd = _connection.CreateCommand();
    cmd.CommandText = """
        INSERT INTO observations (id, session_id, thread_id, parent_id, timestamp, project, branch,
            event_type, source, raw_content, summary, tags, files_involved, created_at,
            metadata, tool_name, outcome, duration_ms, turn_number)
        VALUES (@id, @sessionId, @threadId, @parentId, @timestamp, @project, @branch,
            @eventType, @source, @rawContent, @summary, @tags, @filesInvolved, @createdAt,
            @metadata, @toolName, @outcome, @durationMs, @turnNumber)
        """;

    cmd.Parameters.AddWithValue("@id", observation.Id);
    cmd.Parameters.AddWithValue("@sessionId", observation.SessionId);
    cmd.Parameters.AddWithValue("@threadId", (object?)observation.ThreadId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@parentId", (object?)observation.ParentId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@timestamp", observation.Timestamp.ToString("o"));
    cmd.Parameters.AddWithValue("@project", observation.Project);
    cmd.Parameters.AddWithValue("@branch", (object?)observation.Branch ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@eventType", observation.EventType.ToString());
    cmd.Parameters.AddWithValue("@source", observation.Source.ToString());
    cmd.Parameters.AddWithValue("@rawContent", observation.RawContent);
    cmd.Parameters.AddWithValue("@summary", (object?)observation.Summary ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(observation.Tags));
    cmd.Parameters.AddWithValue("@filesInvolved", JsonSerializer.Serialize(observation.FilesInvolved));
    cmd.Parameters.AddWithValue("@createdAt", observation.CreatedAt.ToString("o"));
    cmd.Parameters.AddWithValue("@metadata", observation.Metadata);
    cmd.Parameters.AddWithValue("@toolName", (object?)observation.ToolName ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@outcome", (object?)observation.Outcome ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@durationMs", (object?)observation.DurationMs ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@turnNumber", (object?)observation.TurnNumber ?? DBNull.Value);

    await cmd.ExecuteNonQueryAsync();
    return observation;
}
```

- [ ] **Step 3: Update MapObservation to read new columns**

Replace the `MapObservation` method:

```csharp
private static Observation MapObservation(SqliteDataReader reader)
{
    var obs = new Observation
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        SessionId = reader.GetString(reader.GetOrdinal("session_id")),
        ThreadId = reader.IsDBNull(reader.GetOrdinal("thread_id")) ? null : reader.GetString(reader.GetOrdinal("thread_id")),
        ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id")),
        Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Project = reader.GetString(reader.GetOrdinal("project")),
        Branch = reader.IsDBNull(reader.GetOrdinal("branch")) ? null : reader.GetString(reader.GetOrdinal("branch")),
        EventType = Enum.Parse<EventType>(reader.GetString(reader.GetOrdinal("event_type"))),
        Source = Enum.Parse<CaptureSource>(reader.GetString(reader.GetOrdinal("source"))),
        RawContent = reader.GetString(reader.GetOrdinal("raw_content")),
        Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
        Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("tags"))) ?? [],
        FilesInvolved = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("files_involved"))) ?? [],
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };

    // New columns — may not exist in older DBs before migration runs
    try
    {
        var metaOrd = reader.GetOrdinal("metadata");
        obs = obs with
        {
            Metadata = reader.IsDBNull(metaOrd) ? "{}" : reader.GetString(metaOrd),
            ToolName = reader.IsDBNull(reader.GetOrdinal("tool_name")) ? null : reader.GetString(reader.GetOrdinal("tool_name")),
            Outcome = reader.IsDBNull(reader.GetOrdinal("outcome")) ? null : reader.GetString(reader.GetOrdinal("outcome")),
            DurationMs = reader.IsDBNull(reader.GetOrdinal("duration_ms")) ? null : reader.GetInt32(reader.GetOrdinal("duration_ms")),
            TurnNumber = reader.IsDBNull(reader.GetOrdinal("turn_number")) ? null : reader.GetInt32(reader.GetOrdinal("turn_number")),
        };
    }
    catch (IndexOutOfRangeException)
    {
        // Pre-migration DB — new columns don't exist yet
    }

    return obs;
}
```

- [ ] **Step 4: Update Query() to support new filter fields**

In the `Query` method, add clauses for `ToolName` and `Outcome` after the existing filter clauses:

```csharp
if (filter.ToolName is not null)
{
    clauses.Add("tool_name = @toolName");
    cmd.Parameters.AddWithValue("@toolName", filter.ToolName);
}
if (filter.Outcome is not null)
{
    clauses.Add("outcome = @outcome");
    cmd.Parameters.AddWithValue("@outcome", filter.Outcome);
}
```

- [ ] **Step 5: Update the Update() method to include new columns**

```csharp
public async Task Update(Observation observation)
{
    using var cmd = _connection.CreateCommand();
    cmd.CommandText = """
        UPDATE observations SET
            thread_id = @threadId, summary = @summary, tags = @tags,
            files_involved = @filesInvolved, metadata = @metadata,
            tool_name = @toolName, outcome = @outcome,
            duration_ms = @durationMs, turn_number = @turnNumber
        WHERE id = @id
        """;

    cmd.Parameters.AddWithValue("@id", observation.Id);
    cmd.Parameters.AddWithValue("@threadId", (object?)observation.ThreadId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@summary", (object?)observation.Summary ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(observation.Tags));
    cmd.Parameters.AddWithValue("@filesInvolved", JsonSerializer.Serialize(observation.FilesInvolved));
    cmd.Parameters.AddWithValue("@metadata", observation.Metadata);
    cmd.Parameters.AddWithValue("@toolName", (object?)observation.ToolName ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@outcome", (object?)observation.Outcome ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@durationMs", (object?)observation.DurationMs ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@turnNumber", (object?)observation.TurnNumber ?? DBNull.Value);

    await cmd.ExecuteNonQueryAsync();
}
```

- [ ] **Step 6: Verify build + run existing tests**

Run: `dotnet build DevBrain.slnx && dotnet test DevBrain.slnx`

Expected: All existing tests pass (new columns have defaults, migration is backward compatible).

- [ ] **Step 7: Commit**

```bash
git add src/DevBrain.Storage/
git commit -m "feat(capture): add metadata columns + schema migration to observations table"
```

---

## Task 3: Smart Truncator — TDD

**Files:**
- Create: `src/DevBrain.Capture/Truncation/SmartTruncator.cs`
- Create: `tests/DevBrain.Capture.Tests/SmartTruncatorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
namespace DevBrain.Capture.Tests;

using System.Text.Json;
using DevBrain.Capture.Truncation;

public class SmartTruncatorTests
{
    [Fact]
    public void Bash_TruncatesOutput_To8KB()
    {
        var largeOutput = new string('x', 16_000);
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command = "npm test" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { stdout = largeOutput, exit_code = 0 })).RootElement;

        var result = SmartTruncator.Truncate("Bash", input, output);

        var stdout = result.Output.GetProperty("stdout").GetString()!;
        Assert.True(stdout.Length <= 8192 + 30); // 8KB + truncation marker
        Assert.Contains("[truncated at 8KB]", stdout);
    }

    [Fact]
    public void Bash_PreservesInput_Under4KB()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command = "ls -la" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { stdout = "file.txt", exit_code = 0 })).RootElement;

        var result = SmartTruncator.Truncate("Bash", input, output);

        Assert.Equal("ls -la", result.Input.GetProperty("command").GetString());
    }

    [Fact]
    public void Write_SkipsContent_KeepsFilePath()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file_path = "/src/main.ts",
            content = new string('x', 50_000)
        })).RootElement;

        var result = SmartTruncator.Truncate("Write", input, default);

        Assert.Equal("/src/main.ts", result.Input.GetProperty("file_path").GetString());
        Assert.False(result.Input.TryGetProperty("content", out _));
    }

    [Fact]
    public void Edit_TruncatesStrings_To4KB()
    {
        var largeString = new string('y', 10_000);
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file_path = "/src/main.ts",
            old_string = largeString,
            new_string = largeString
        })).RootElement;

        var result = SmartTruncator.Truncate("Edit", input, default);

        var oldStr = result.Input.GetProperty("old_string").GetString()!;
        Assert.True(oldStr.Length <= 4096 + 30);
        Assert.Contains("[truncated at 4KB]", oldStr);
    }

    [Fact]
    public void Read_KeepsOnlyFilePath()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file_path = "/src/main.ts",
            offset = 10,
            limit = 50
        })).RootElement;

        var result = SmartTruncator.Truncate("Read", input, default);

        Assert.Equal("/src/main.ts", result.Input.GetProperty("file_path").GetString());
    }

    [Fact]
    public void SmallPayload_NotTruncated()
    {
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { command = "echo hi" })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { stdout = "hi", exit_code = 0 })).RootElement;

        var result = SmartTruncator.Truncate("Bash", input, output);

        Assert.Equal("echo hi", result.Input.GetProperty("command").GetString());
        Assert.Equal("hi", result.Output.GetProperty("stdout").GetString());
    }

    [Fact]
    public void UnknownTool_Uses2KB_4KB_Defaults()
    {
        var largeInput = new string('a', 5000);
        var largeOutput = new string('b', 8000);
        var input = JsonDocument.Parse(JsonSerializer.Serialize(new { data = largeInput })).RootElement;
        var output = JsonDocument.Parse(JsonSerializer.Serialize(new { result = largeOutput })).RootElement;

        var result = SmartTruncator.Truncate("mcp__custom__tool", input, output);

        var serializedInput = result.Input.GetRawText();
        Assert.True(serializedInput.Length <= 2048 + 100);
    }
}
```

Write to: `tests/DevBrain.Capture.Tests/SmartTruncatorTests.cs`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DevBrain.Capture.Tests/ --filter SmartTruncator`

Expected: FAIL — `SmartTruncator` type not found.

- [ ] **Step 3: Implement SmartTruncator**

```csharp
namespace DevBrain.Capture.Truncation;

using System.Text.Json;

public record TruncationResult(JsonElement Input, JsonElement Output);

public static class SmartTruncator
{
    private static readonly Dictionary<string, (int InputLimit, int OutputLimit)> Limits = new()
    {
        ["Bash"] = (4096, 8192),
        ["Read"] = (0, 2048),       // 0 = keep only file_path
        ["Grep"] = (1024, 2048),
        ["Glob"] = (1024, 2048),
        ["Edit"] = (4096, 0),       // 0 = skip output
        ["Write"] = (0, 0),         // 0 = skip both (keep file_path only)
        ["WebFetch"] = (1024, 4096),
        ["WebSearch"] = (1024, 4096),
        ["Agent"] = (2048, 4096),
    };

    private const int DefaultInputLimit = 2048;
    private const int DefaultOutputLimit = 4096;

    public static TruncationResult Truncate(string toolName, JsonElement input, JsonElement output)
    {
        var (inputLimit, outputLimit) = Limits.GetValueOrDefault(toolName, (DefaultInputLimit, DefaultOutputLimit));

        var truncatedInput = TruncateElement(toolName, input, inputLimit, isInput: true);
        var truncatedOutput = TruncateElement(toolName, output, outputLimit, isInput: false);

        return new TruncationResult(truncatedInput, truncatedOutput);
    }

    private static JsonElement TruncateElement(string toolName, JsonElement element, int limit, bool isInput)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return element;

        // For Write/Read input: keep only file_path
        if (isInput && limit == 0 && (toolName == "Write" || toolName == "Read"))
        {
            return KeepOnlyFilePath(element);
        }

        // Skip output entirely
        if (!isInput && limit == 0)
            return default;

        // Truncate individual string properties
        return TruncateStringProperties(element, limit);
    }

    private static JsonElement KeepOnlyFilePath(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element;

        var dict = new Dictionary<string, object?>();
        if (element.TryGetProperty("file_path", out var fp))
            dict["file_path"] = fp.GetString();
        if (element.TryGetProperty("path", out var p))
            dict["path"] = p.GetString();

        return JsonDocument.Parse(JsonSerializer.Serialize(dict)).RootElement;
    }

    private static JsonElement TruncateStringProperties(JsonElement element, int limit)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element;

        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var str = prop.Value.GetString() ?? "";
                if (str.Length > limit)
                    dict[prop.Name] = str[..limit] + $" [truncated at {limit / 1024}KB]";
                else
                    dict[prop.Name] = str;
            }
            else
            {
                dict[prop.Name] = prop.Value;
            }
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(dict)).RootElement;
    }
}
```

Write to: `src/DevBrain.Capture/Truncation/SmartTruncator.cs`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DevBrain.Capture.Tests/ --filter SmartTruncator`

Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/DevBrain.Capture/Truncation/ tests/DevBrain.Capture.Tests/SmartTruncatorTests.cs
git commit -m "feat(capture): add SmartTruncator with tool-type-aware truncation"
```

---

## Task 4: Field-Aware Redactor (Privacy Layer 2) — TDD

**Files:**
- Create: `src/DevBrain.Capture/Privacy/FieldAwareRedactor.cs`
- Create: `tests/DevBrain.Capture.Tests/FieldAwareRedactorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
namespace DevBrain.Capture.Tests;

using System.Text.Json;
using DevBrain.Capture.Privacy;

public class FieldAwareRedactorTests
{
    private readonly FieldAwareRedactor _redactor = new();

    [Fact]
    public void Write_EnvFile_RedactsContent()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new { file_path = "/project/.env", content = "API_KEY=sk-secret123" }
        });

        var result = _redactor.Redact("Write", metadata);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("[REDACTED:sensitive-file]",
            doc.RootElement.GetProperty("tool_input").GetProperty("content").GetString());
    }

    [Fact]
    public void Write_NormalFile_PreservesContent()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new { file_path = "/project/src/main.ts", content = "console.log('hello')" }
        });

        var result = _redactor.Redact("Write", metadata);
        var doc = JsonDocument.Parse(result);

        Assert.Equal("console.log('hello')",
            doc.RootElement.GetProperty("tool_input").GetProperty("content").GetString());
    }

    [Fact]
    public void Bash_ExportCommand_RedactsValues()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new { command = "export API_KEY=FAKE_SECRET_FOR_TEST" }
        });

        var result = _redactor.Redact("Bash", metadata);

        Assert.Contains("[REDACTED", result);
        Assert.DoesNotContain("FAKE_SECRET_FOR_TEST", result);
    }

    [Fact]
    public void Edit_CredentialFile_RedactsStrings()
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tool_input = new
            {
                file_path = "/project/credentials.json",
                old_string = "old-secret",
                new_string = "new-secret"
            }
        });

        var result = _redactor.Redact("Edit", metadata);
        var doc = JsonDocument.Parse(result);
        var input = doc.RootElement.GetProperty("tool_input");

        Assert.Equal("[REDACTED:sensitive-file]", input.GetProperty("old_string").GetString());
        Assert.Equal("[REDACTED:sensitive-file]", input.GetProperty("new_string").GetString());
    }

    [Fact]
    public void NonToolEvent_PassesThrough()
    {
        var metadata = JsonSerializer.Serialize(new { prompt = "Fix the auth bug" });

        var result = _redactor.Redact(null, metadata);

        Assert.Equal(metadata, result);
    }
}
```

Write to: `tests/DevBrain.Capture.Tests/FieldAwareRedactorTests.cs`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DevBrain.Capture.Tests/ --filter FieldAwareRedactor`

Expected: FAIL — `FieldAwareRedactor` type not found.

- [ ] **Step 3: Implement FieldAwareRedactor**

```csharp
namespace DevBrain.Capture.Privacy;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public class FieldAwareRedactor
{
    private static readonly string[] SensitiveFilePatterns =
        ["*.env", "*.env.*", "*secret*", "*credential*", "*password*", "*.pem", "*.key"];

    private static readonly Regex ExportPattern =
        new(@"(?:export|set)\s+\w+=\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Redact(string? toolName, string metadataJson)
    {
        if (toolName is null || metadataJson is "{}" or "")
            return metadataJson;

        try
        {
            var node = JsonNode.Parse(metadataJson);
            if (node is null) return metadataJson;

            var toolInput = node["tool_input"];
            if (toolInput is null) return metadataJson;

            var filePath = toolInput["file_path"]?.GetValue<string>();

            switch (toolName)
            {
                case "Write" when filePath is not null && IsSensitiveFile(filePath):
                    toolInput["content"] = "[REDACTED:sensitive-file]";
                    break;

                case "Edit" when filePath is not null && IsSensitiveFile(filePath):
                    if (toolInput["old_string"] is not null)
                        toolInput["old_string"] = "[REDACTED:sensitive-file]";
                    if (toolInput["new_string"] is not null)
                        toolInput["new_string"] = "[REDACTED:sensitive-file]";
                    break;

                case "Bash":
                    var command = toolInput["command"]?.GetValue<string>();
                    if (command is not null && ExportPattern.IsMatch(command))
                    {
                        // Redact the value part of export KEY=VALUE
                        var redacted = Regex.Replace(command,
                            @"((?:export|set)\s+\w+=)\S+",
                            "$1[REDACTED:secret]",
                            RegexOptions.IgnoreCase);
                        toolInput["command"] = redacted;
                    }
                    break;
            }

            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return metadataJson;
        }
    }

    private static bool IsSensitiveFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        foreach (var pattern in SensitiveFilePatterns)
        {
            var p = pattern.TrimStart('*');
            if (fileName.Contains(p.TrimStart('.')))
                return true;
        }
        return false;
    }
}
```

Write to: `src/DevBrain.Capture/Privacy/FieldAwareRedactor.cs`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DevBrain.Capture.Tests/ --filter FieldAwareRedactor`

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/DevBrain.Capture/Privacy/FieldAwareRedactor.cs tests/DevBrain.Capture.Tests/FieldAwareRedactorTests.cs
git commit -m "feat(capture): add FieldAwareRedactor for privacy Layer 2"
```

---

## Task 5: Transcript Parser — TDD

**Files:**
- Create: `src/DevBrain.Capture/Transcript/TranscriptParser.cs`
- Create: `src/DevBrain.Capture/Transcript/TranscriptArchiver.cs`
- Create: `tests/DevBrain.Capture.Tests/TranscriptParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
namespace DevBrain.Capture.Tests;

using DevBrain.Capture.Transcript;

public class TranscriptParserTests
{
    [Fact]
    public void ParseLastTurn_ExtractsTokenMetrics()
    {
        var jsonl = """
        {"type":"user","content":"hello"}
        {"type":"assistant","content":"hi","usage":{"input_tokens":100,"output_tokens":50,"cache_read_input_tokens":80,"cache_creation_input_tokens":20},"model":"claude-sonnet-4-6","latency_ms":1200}
        """;

        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, jsonl);

        try
        {
            var result = TranscriptParser.ParseLastTurn(tmpFile);

            Assert.NotNull(result);
            Assert.Equal(100, result!.TokensIn);
            Assert.Equal(50, result.TokensOut);
            Assert.Equal(80, result.CacheReadTokens);
            Assert.Equal(20, result.CacheWriteTokens);
            Assert.Equal(1200, result.LatencyMs);
            Assert.Equal("claude-sonnet-4-6", result.Model);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseLastTurn_ReturnsNull_ForEmptyFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var result = TranscriptParser.ParseLastTurn(tmpFile);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseSessionAggregates_ComputesTotals()
    {
        var jsonl = """
        {"type":"assistant","usage":{"input_tokens":100,"output_tokens":50},"model":"claude-sonnet-4-6","tool_use":{"name":"Bash"}}
        {"type":"assistant","usage":{"input_tokens":200,"output_tokens":100},"model":"claude-sonnet-4-6","tool_use":{"name":"Edit"}}
        {"type":"assistant","usage":{"input_tokens":150,"output_tokens":75},"model":"claude-sonnet-4-6","tool_use":{"name":"Bash"}}
        """;

        var tmpFile = Path.GetTempFileName();
        File.WriteAllText(tmpFile, jsonl);

        try
        {
            var result = TranscriptParser.ParseSessionAggregates(tmpFile);

            Assert.Equal(450, result.TotalTokensIn);
            Assert.Equal(225, result.TotalTokensOut);
            Assert.Equal(3, result.TotalTurns);
            Assert.Contains("claude-sonnet-4-6", result.ModelsUsed);
            Assert.Equal(2, result.ToolUsage["Bash"]);
            Assert.Equal(1, result.ToolUsage["Edit"]);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseLastTurn_ReturnsNull_ForNonexistentFile()
    {
        var result = TranscriptParser.ParseLastTurn("/nonexistent/file.jsonl");
        Assert.Null(result);
    }
}
```

Write to: `tests/DevBrain.Capture.Tests/TranscriptParserTests.cs`

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DevBrain.Capture.Tests/ --filter TranscriptParser`

Expected: FAIL — `TranscriptParser` type not found.

- [ ] **Step 3: Implement TranscriptParser**

```csharp
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

        // Read last non-empty line
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
```

Write to: `src/DevBrain.Capture/Transcript/TranscriptParser.cs`

- [ ] **Step 4: Implement TranscriptArchiver**

```csharp
namespace DevBrain.Capture.Transcript;

public static class TranscriptArchiver
{
    public static string Archive(string transcriptPath, string sessionId, string archiveDir)
    {
        if (!File.Exists(transcriptPath))
            throw new FileNotFoundException($"Transcript not found: {transcriptPath}");

        Directory.CreateDirectory(archiveDir);

        var destPath = Path.Combine(archiveDir, $"{sessionId}.jsonl");
        File.Copy(transcriptPath, destPath, overwrite: true);

        return destPath;
    }
}
```

Write to: `src/DevBrain.Capture/Transcript/TranscriptArchiver.cs`

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/DevBrain.Capture.Tests/ --filter TranscriptParser`

Expected: All 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/DevBrain.Capture/Transcript/ tests/DevBrain.Capture.Tests/TranscriptParserTests.cs
git commit -m "feat(capture): add TranscriptParser and TranscriptArchiver"
```

---

## Task 6: EventIngestionService

**Files:**
- Create: `src/DevBrain.Api/Services/EventIngestionService.cs`

This is the orchestrator — receives raw events, truncates, generates rawContent, applies privacy, parses transcripts, writes to DB.

- [ ] **Step 1: Implement EventIngestionService**

```csharp
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
    private readonly IAlertSink? _alertSink;
    private readonly ConcurrentDictionary<string, int> _turnCounters = new();
    private readonly string _transcriptArchiveDir;

    public EventIngestionService(
        IObservationStore store,
        SecretPatternRedactor secretRedactor,
        FieldAwareRedactor fieldRedactor,
        IAlertSink? alertSink = null)
    {
        _store = store;
        _secretRedactor = secretRedactor;
        _fieldRedactor = fieldRedactor;
        _alertSink = alertSink;
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

        // Extract tool info
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

        // Build metadata
        var metadata = BuildMetadata(hookEvent, payload, toolInput, toolOutput);

        // Turn tracking
        int? turnNumber = null;
        int? durationMs = null;

        if (hookEvent == "SessionStart")
            _turnCounters[sessionId] = 0;

        if (hookEvent == "Stop" || hookEvent == "StopFailure")
        {
            var count = _turnCounters.AddOrUpdate(sessionId, 1, (_, v) => v + 1);
            turnNumber = count;

            // Parse transcript for turn metrics
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

        // Session end: archive transcript + compute aggregates
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
                catch
                {
                    // Non-fatal — transcript may not exist
                }
            }
            _turnCounters.TryRemove(sessionId, out _);
        }

        // Generate rawContent summary
        var rawContent = GenerateRawContent(eventType.Value, toolName, metadata, payload);

        // Extract project from cwd
        var project = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(project)) project = "unknown";

        // Extract files_involved from tool input
        var filesInvolved = ExtractFiles(toolInput);

        // Privacy Layer 1: blanket regex on all text fields
        rawContent = _secretRedactor.Redact(rawContent);
        metadata = _secretRedactor.Redact(metadata);

        // Privacy Layer 2: field-aware redaction
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
                if (toolInput.ValueKind != JsonValueKind.Undefined)
                    dict["tool_input"] = toolInput;
                if (toolOutput.ValueKind != JsonValueKind.Undefined)
                    dict["tool_output"] = toolOutput;
                if (payload.TryGetProperty("tool_use_id", out var tuId))
                    dict["tool_use_id"] = tuId.GetString();
                break;

            case "PostToolUseFailure":
                if (toolInput.ValueKind != JsonValueKind.Undefined)
                    dict["tool_input"] = toolInput;
                if (payload.TryGetProperty("error", out var err))
                    dict["error"] = err.GetString();
                if (payload.TryGetProperty("is_interrupt", out var intr))
                    dict["is_interrupt"] = intr.GetBoolean();
                if (payload.TryGetProperty("tool_use_id", out var tuId2))
                    dict["tool_use_id"] = tuId2.GetString();
                break;

            case "UserPromptSubmit":
                if (payload.TryGetProperty("prompt", out var prompt))
                    dict["prompt"] = prompt.GetString();
                break;

            case "SessionStart":
                if (payload.TryGetProperty("source", out var src))
                    dict["source"] = src.GetString();
                if (payload.TryGetProperty("model", out var model))
                    dict["model"] = model.GetString();
                if (payload.TryGetProperty("permission_mode", out var pm))
                    dict["permission_mode"] = pm.GetString();
                break;

            case "SubagentStart":
            case "SubagentStop":
                if (payload.TryGetProperty("agent_type", out var at))
                    dict["agent_type"] = at.GetString();
                if (payload.TryGetProperty("agent_id", out var aid))
                    dict["agent_id"] = aid.GetString();
                if (payload.TryGetProperty("last_assistant_message", out var lm))
                    dict["last_message"] = lm.GetString();
                break;

            case "StopFailure":
                if (payload.TryGetProperty("error_type", out var et))
                    dict["error_type"] = et.GetString();
                break;

            case "FileChanged":
                if (payload.TryGetProperty("file_path", out var fp))
                    dict["file_path"] = fp.GetString();
                if (payload.TryGetProperty("change_type", out var ct))
                    dict["change_type"] = ct.GetString();
                break;

            case "CwdChanged":
                if (payload.TryGetProperty("cwd", out var newCwd))
                    dict["new_cwd"] = newCwd.GetString();
                if (payload.TryGetProperty("previous_cwd", out var prevCwd))
                    dict["previous_cwd"] = prevCwd.GetString();
                break;
        }

        return JsonSerializer.Serialize(dict);
    }

    private static string GenerateRawContent(EventType eventType, string? toolName,
        string metadata, JsonElement payload)
    {
        return eventType switch
        {
            EventType.ToolCall => $"{toolName}: {GetBriefToolDescription(toolName, metadata)}",
            EventType.ToolFailure => $"{toolName} FAILED: {GetBriefToolDescription(toolName, metadata)}",
            EventType.UserPrompt => $"User: {TruncateForSummary(GetJsonString(metadata, "prompt"), 200)}",
            EventType.SessionStart => $"Session started: {GetJsonString(metadata, "model")} ({GetJsonString(metadata, "source")})",
            EventType.SessionEnd => FormatSessionEnd(metadata),
            EventType.TurnComplete => FormatTurnComplete(metadata),
            EventType.TurnError => $"Turn error: {GetJsonString(metadata, "error_type")}",
            EventType.SubagentStart => $"Subagent started: {GetJsonString(metadata, "agent_type")} ({GetJsonString(metadata, "agent_id")})",
            EventType.SubagentStop => $"Subagent finished: {GetJsonString(metadata, "agent_type")} — {TruncateForSummary(GetJsonString(metadata, "last_message"), 100)}",
            EventType.FileChange => $"File {GetJsonString(metadata, "change_type")}: {GetJsonString(metadata, "file_path")}",
            EventType.CwdChange => $"Directory changed: {GetJsonString(metadata, "previous_cwd")} → {GetJsonString(metadata, "new_cwd")}",
            EventType.ContextCompact => "Context compacted",
            _ => "Unknown event",
        };
    }

    private static string GetBriefToolDescription(string? toolName, string metadata)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            var root = doc.RootElement;
            if (root.TryGetProperty("tool_input", out var ti))
            {
                if (ti.TryGetProperty("command", out var cmd))
                    return TruncateForSummary(cmd.GetString(), 100);
                if (ti.TryGetProperty("file_path", out var fp))
                    return fp.GetString() ?? "";
                if (ti.TryGetProperty("pattern", out var pat))
                    return pat.GetString() ?? "";
                if (ti.TryGetProperty("query", out var q))
                    return q.GetString() ?? "";
            }
        }
        catch { }
        return toolName ?? "unknown";
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
            var totalK = (tokensIn + tokensOut) / 1000;
            return $"Session ended: {turns} turns, {totalK}K tokens, {errors} errors";
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

    private static string GetJsonString(string json, string property)
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

    private static string TruncateForSummary(string? text, int maxLen)
    {
        if (text is null) return "";
        return text.Length <= maxLen ? text : text[..maxLen] + "...";
    }

    private static string EnrichWithTurnMetrics(string metadata, TurnMetrics metrics)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadata);
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
        if (toolInput.ValueKind == JsonValueKind.Undefined)
            return [];

        var files = new List<string>();
        if (toolInput.TryGetProperty("file_path", out var fp) && fp.GetString() is { } fpath)
            files.Add(fpath);
        if (toolInput.TryGetProperty("path", out var p) && p.GetString() is { } ppath)
            files.Add(ppath);
        return files;
    }
}
```

Write to: `src/DevBrain.Api/Services/EventIngestionService.cs`

- [ ] **Step 2: Verify build**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/DevBrain.Api/Services/EventIngestionService.cs
git commit -m "feat(capture): add EventIngestionService — event orchestrator"
```

---

## Task 7: API Endpoint + Hook Command

**Files:**
- Create: `src/DevBrain.Api/Endpoints/EventEndpoints.cs`
- Create: `src/DevBrain.Cli/Commands/HookCommand.cs`
- Modify: `src/DevBrain.Api/Program.cs`
- Modify: `src/DevBrain.Cli/Program.cs`

- [ ] **Step 1: Create EventEndpoints**

```csharp
namespace DevBrain.Api.Endpoints;

using System.Text.Json;
using DevBrain.Api.Services;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/events", async (JsonElement payload, EventIngestionService ingestion) =>
        {
            if (!payload.TryGetProperty("hookEvent", out var he) || !payload.TryGetProperty("session_id", out _))
                return Results.BadRequest(new { error = "hookEvent and session_id are required" });

            var observation = await ingestion.IngestEvent(payload);
            return observation is not null
                ? Results.Created($"/api/v1/observations/{observation.Id}", new { id = observation.Id })
                : Results.BadRequest(new { error = $"Unknown hook event: {he.GetString()}" });
        });
    }
}
```

Write to: `src/DevBrain.Api/Endpoints/EventEndpoints.cs`

- [ ] **Step 2: Create HookCommand**

```csharp
using System.CommandLine;
using System.Text.Json;

namespace DevBrain.Cli.Commands;

public class HookCommand : Command
{
    public HookCommand() : base("hook", "Forward Claude Code hook events to the DevBrain daemon")
    {
        var eventArg = new Argument<string>("event", "The hook event name (e.g. PostToolUse)");
        AddArgument(eventArg);
        SetAction((string @event) => Execute(@event));
    }

    private static async Task Execute(string @event)
    {
        try
        {
            // Read JSON from stdin
            using var reader = new StreamReader(Console.OpenStandardInput());
            var stdin = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(stdin))
                return;

            // Parse and add hookEvent field
            using var doc = JsonDocument.Parse(stdin);
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(stdin) ?? [];
            dict["hookEvent"] = @event;

            // Forward to daemon
            using var client = new DevBrainHttpClient();
            await client.Post("/api/v1/events", dict);
        }
        catch
        {
            // Never fail — hooks must not block Claude Code
        }
    }
}
```

Write to: `src/DevBrain.Cli/Commands/HookCommand.cs`

- [ ] **Step 3: Register in Program.cs files**

In `src/DevBrain.Api/Program.cs`, after the existing endpoint registrations (around line 169), add:

```csharp
app.MapEventEndpoints();
```

Also register `EventIngestionService` in the DI section (before `var app = builder.Build();`):

```csharp
builder.Services.AddSingleton<FieldAwareRedactor>();
builder.Services.AddSingleton<EventIngestionService>();
```

Add the required using:

```csharp
using DevBrain.Capture.Privacy;
using DevBrain.Api.Services;
```

In `src/DevBrain.Cli/Program.cs`, add:

```csharp
root.Add(new HookCommand());
```

- [ ] **Step 4: Verify build**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/DevBrain.Api/Endpoints/EventEndpoints.cs src/DevBrain.Cli/Commands/HookCommand.cs src/DevBrain.Api/Program.cs src/DevBrain.Cli/Program.cs
git commit -m "feat(capture): add /api/v1/events endpoint and devbrain hook CLI command"
```

---

## Task 8: Retention Cleanup Job — TDD

**Files:**
- Create: `src/DevBrain.Agents/RetentionCleanupJob.cs`
- Create: `tests/DevBrain.Agents.Tests/RetentionCleanupTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
namespace DevBrain.Agents.Tests;

using DevBrain.Agents;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class RetentionCleanupTests
{
    [Fact]
    public async Task Schedule_IsIdleWithDailyInterval()
    {
        var agent = new RetentionCleanupJob();
        Assert.Equal("retention-cleanup", agent.Name);
        Assert.IsType<AgentSchedule.Idle>(agent.Schedule);
        var idle = (AgentSchedule.Idle)agent.Schedule;
        Assert.Equal(TimeSpan.FromHours(24), idle.IdleThreshold);
    }

    [Fact]
    public async Task Run_ReturnsCompletedOutput()
    {
        var store = new FakeObservationStore();
        var ctx = TestHelpers.CreateContext(store);
        var agent = new RetentionCleanupJob();

        var outputs = await agent.Run(ctx, CancellationToken.None);

        Assert.Single(outputs);
        Assert.Equal("RetentionCleanup", outputs[0].Type);
    }
}
```

Write to: `tests/DevBrain.Agents.Tests/RetentionCleanupTests.cs`

Note: `TestHelpers.CreateContext` and `FakeObservationStore` already exist in `tests/DevBrain.Agents.Tests/TestHelpers.cs` — reuse them.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DevBrain.Agents.Tests/ --filter RetentionCleanup`

Expected: FAIL — `RetentionCleanupJob` type not found.

- [ ] **Step 3: Implement RetentionCleanupJob**

```csharp
namespace DevBrain.Agents;

using DevBrain.Core;
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
            new AgentOutput
            {
                Type = "RetentionCleanup",
                Content = $"Trimmed metadata on {trimmed} observations. Deleted {deleted} old transcripts.",
            }
        ];
    }

    private async Task<int> TrimOldMetadata(AgentContext ctx)
    {
        // Find observations older than 7 days with non-empty metadata
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var old = await ctx.Observations.Query(new ObservationFilter
        {
            Before = cutoff,
            Limit = 500,
        });

        var count = 0;
        foreach (var obs in old)
        {
            if (obs.Metadata is "{}" or "") continue;

            var trimmed = TrimMetadataFields(obs.Metadata);
            if (trimmed != obs.Metadata)
            {
                await ctx.Observations.Update(obs with { Metadata = trimmed });
                count++;
            }
        }

        return count;
    }

    private static string TrimMetadataFields(string metadata)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metadata);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? [];

            // Truncate tool_output to 1KB
            if (dict.ContainsKey("tool_output"))
            {
                var outputStr = System.Text.Json.JsonSerializer.Serialize(dict["tool_output"]);
                if (outputStr.Length > 1024)
                    dict["tool_output"] = outputStr[..1024] + " [trimmed by retention]";
            }

            // Remove Write file content
            if (dict.ContainsKey("tool_input") && dict["tool_input"] is System.Text.Json.JsonElement ti
                && ti.ValueKind == System.Text.Json.JsonValueKind.Object
                && ti.TryGetProperty("content", out _))
            {
                var inputDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(ti.GetRawText()) ?? [];
                inputDict.Remove("content");
                dict["tool_input"] = inputDict;
            }

            // Truncate prompt to 2000 chars
            if (dict.ContainsKey("prompt") && dict["prompt"] is System.Text.Json.JsonElement pe
                && pe.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var prompt = pe.GetString() ?? "";
                if (prompt.Length > 2000)
                    dict["prompt"] = prompt[..2000] + " [trimmed by retention]";
            }

            // Truncate last_message to 1KB
            if (dict.ContainsKey("last_message") && dict["last_message"] is System.Text.Json.JsonElement lm
                && lm.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var msg = lm.GetString() ?? "";
                if (msg.Length > 1024)
                    dict["last_message"] = msg[..1024] + " [trimmed by retention]";
            }

            return System.Text.Json.JsonSerializer.Serialize(dict);
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
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                count++;
            }
        }
        return count;
    }
}
```

Write to: `src/DevBrain.Agents/RetentionCleanupJob.cs`

- [ ] **Step 4: Register in Program.cs**

In `src/DevBrain.Api/Program.cs`, add with the other agent registrations:

```csharp
builder.Services.AddSingleton<IIntelligenceAgent, RetentionCleanupJob>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/DevBrain.Agents.Tests/ --filter RetentionCleanup`

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/DevBrain.Agents/RetentionCleanupJob.cs tests/DevBrain.Agents.Tests/RetentionCleanupTests.cs src/DevBrain.Api/Program.cs
git commit -m "feat(capture): add RetentionCleanupJob for tiered data retention"
```

---

## Task 9: Integration Test + Full Verification

**Files:**
- Create: `tests/DevBrain.Integration.Tests/EventIngestionTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
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

        // Verify metadata has structured tool data
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
            tool_input = new { command = "export API_KEY=FAKE_KEY_FOR_TEST" },
            tool_response = new { stdout = "done", exit_code = 0 },
        })).RootElement;

        var obs = await service.IngestEvent(payload);

        Assert.DoesNotContain("FAKE_KEY_FOR_TEST", obs!.Metadata);
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
```

Write to: `tests/DevBrain.Integration.Tests/EventIngestionTests.cs`

- [ ] **Step 2: Run all tests**

Run: `dotnet test DevBrain.slnx`

Expected: All tests pass (existing + new).

- [ ] **Step 3: Verify full build**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add tests/DevBrain.Integration.Tests/EventIngestionTests.cs
git commit -m "test(capture): add integration tests for event ingestion pipeline"
```

---

## Task 10: Integrate PrivacyFilter with Layer 2

**Files:**
- Modify: `src/DevBrain.Capture/Pipeline/PrivacyFilter.cs`

- [ ] **Step 1: Add FieldAwareRedactor to PrivacyFilter**

Update the PrivacyFilter constructor and Run method to also redact the `Metadata` field:

```csharp
namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Capture.Privacy;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class PrivacyFilter : IPipelineStage
{
    private readonly PrivateTagRedactor _privateTagRedactor;
    private readonly SecretPatternRedactor _secretPatternRedactor;
    private readonly FieldAwareRedactor _fieldAwareRedactor;
    private readonly IgnoreFileRedactor? _ignoreFileRedactor;

    public PrivacyFilter(
        PrivateTagRedactor? privateTagRedactor = null,
        SecretPatternRedactor? secretPatternRedactor = null,
        FieldAwareRedactor? fieldAwareRedactor = null,
        IgnoreFileRedactor? ignoreFileRedactor = null)
    {
        _privateTagRedactor = privateTagRedactor ?? new PrivateTagRedactor();
        _secretPatternRedactor = secretPatternRedactor ?? new SecretPatternRedactor();
        _fieldAwareRedactor = fieldAwareRedactor ?? new FieldAwareRedactor();
        _ignoreFileRedactor = ignoreFileRedactor;
    }

    public async Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct)
    {
        try
        {
            await foreach (var obs in input.ReadAllAsync(ct))
            {
                if (_ignoreFileRedactor is not null && obs.FilesInvolved.Count > 0
                    && _ignoreFileRedactor.ShouldIgnore(obs.FilesInvolved))
                {
                    continue;
                }

                var rawContent = _privateTagRedactor.Redact(obs.RawContent);
                rawContent = _secretPatternRedactor.Redact(rawContent);

                string? summary = obs.Summary;
                if (summary is not null)
                {
                    summary = _privateTagRedactor.Redact(summary);
                    summary = _secretPatternRedactor.Redact(summary);
                }

                // Layer 1: blanket regex on metadata
                var metadata = _secretPatternRedactor.Redact(obs.Metadata);
                // Layer 2: field-aware redaction on metadata
                metadata = _fieldAwareRedactor.Redact(obs.ToolName, metadata);

                await output.WriteAsync(obs with
                {
                    RawContent = rawContent,
                    Summary = summary,
                    Metadata = metadata,
                }, ct);
            }
        }
        finally
        {
            output.Complete();
        }
    }
}
```

Write to: `src/DevBrain.Capture/Pipeline/PrivacyFilter.cs`

- [ ] **Step 2: Run all tests**

Run: `dotnet test DevBrain.slnx`

Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/DevBrain.Capture/Pipeline/PrivacyFilter.cs
git commit -m "feat(capture): integrate FieldAwareRedactor into PrivacyFilter pipeline"
```

---

## Task 11: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test DevBrain.slnx`

Expected: All tests pass.

- [ ] **Step 3: Verify schema migration on existing DB**

Run: `dotnet run --project src/DevBrain.Api/ &` then `curl http://127.0.0.1:37800/api/v1/health`

Expected: Daemon starts, migration runs, health check returns OK.

- [ ] **Step 4: Test event ingestion**

```bash
echo '{"hookEvent":"PostToolUse","session_id":"test","cwd":"/tmp","tool_name":"Bash","tool_input":{"command":"echo hello"},"tool_response":{"stdout":"hello","exit_code":0}}' | curl -X POST -H "Content-Type: application/json" -d @- http://127.0.0.1:37800/api/v1/events
```

Expected: `201 Created` with observation ID.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat(capture): complete rich observation capture implementation"
```

---

## Summary

| Task | What it builds | New files | Tests |
|------|---------------|-----------|-------|
| 1 | EventType enum + Observation model | 0 (modify 3) | build check |
| 2 | Schema migration + storage updates | 0 (modify 2) | existing tests |
| 3 | SmartTruncator (TDD) | 2 | 7 tests |
| 4 | FieldAwareRedactor (TDD) | 2 | 5 tests |
| 5 | TranscriptParser + Archiver (TDD) | 3 | 4 tests |
| 6 | EventIngestionService | 1 | build check |
| 7 | API endpoint + Hook command | 2 new + modify 2 | build check |
| 8 | RetentionCleanupJob (TDD) | 2 | 2 tests |
| 9 | Integration tests | 1 | 4 tests |
| 10 | PrivacyFilter Layer 2 integration | 0 (modify 1) | existing tests |
| 11 | Final verification | 0 | all tests |

**Total:** ~13 new files, ~8 modified files, ~22 new tests, 11 commits.
