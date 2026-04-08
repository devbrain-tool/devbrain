# Rich Observation Capture Design

**Date:** 2026-04-08
**Status:** Draft
**Goal:** Transform DevBrain from flat-string event logging into a full developer flight recorder — capturing structured data from all 12 Claude Code hook events, parsing transcripts for token/timing metrics, and archiving raw session data for deep analysis.

---

## Overview

DevBrain currently captures Claude Code tool calls as flat strings via a single `PostToolUse` hook. This design expands capture to 12 hook events, adds structured metadata with smart truncation, parses transcripts for per-turn token/timing data, and archives raw session transcripts. The result is a rich, queryable dataset that transforms every downstream feature (briefings, dead-end detection, growth tracking, deja vu alerts).

**Developer experience after this ships:** Every Claude Code session automatically captures user prompts, tool calls with full input/output, failures with error details, subagent delegation, turn-level token usage, and session-level aggregates. All queryable, all privacy-filtered, all time-retained.

---

## Architecture

### Data Flow

```
Claude Code Hooks                        DevBrain Daemon
┌───────────────────┐                   ┌──────────────────────────┐
│ PostToolUse       │──┐                │                          │
│ PostToolUseFailure│  │  POST          │  /api/v1/events          │
│ UserPromptSubmit  │  │  /events       │    ├─ Route by hookEvent │
│ SessionStart      │  ├───────────────>│    ├─ Smart Truncation   │
│ SessionEnd        │  │                │    ├─ Privacy Layer 1    │
│ SubagentStart     │  │                │    │  (blanket regex)    │
│ SubagentStop      │  │                │    ├─ Privacy Layer 2    │
│ Stop              │  │                │    │  (field-aware)      │
│ StopFailure       │  │                │    ├─ Write to DB        │
│ FileChanged       │  │                │    └─ EventBus.Publish() │
│ CwdChanged        │──┘                │                          │
│ PostCompact       │                   │  TranscriptParser        │
└───────────────────┘                   │    ├─ Per-turn (on Stop) │
        │                               │    ├─ Archive (on End)   │
        │ transcript_path               │    └─ Aggregates         │
        └──────────────────────────────>│                          │
                                        │  RetentionCleanup        │
                                        │    ├─ 7d: truncate meta  │
                                        │    └─ 30d: delete JSONL  │
                                        └──────────────────────────┘
```

### Components

| Component | Location | Responsibility |
|---|---|---|
| `devbrain-hook` | CLI project (new binary) | Read stdin, truncate, POST to daemon |
| `/api/v1/events` | Api endpoint (new) | Receive events, route by type |
| `EventIngestionService` | Api service (new) | Normalize, truncate, privacy filter, write |
| `TranscriptParser` | Capture project (new) | Parse JSONL for turn metrics + session aggregates |
| `RetentionCleanupJob` | Agents project (new) | Daily tiered retention cleanup |
| `FieldAwareRedactor` | Capture project (new) | Privacy Layer 2 — field-specific redaction |

---

## Event Types

### Expanded EventType Enum

```csharp
public enum EventType
{
    // Existing (enriched)
    ToolCall,         // PostToolUse — now with structured metadata
    FileChange,       // FileChanged — now with change_type
    Decision,         // Unchanged
    Error,            // Unchanged
    Conversation,     // Unchanged

    // New
    ToolFailure,      // PostToolUseFailure — errors, timeouts
    UserPrompt,       // UserPromptSubmit — exact prompt text
    SessionStart,     // Session began — model, source
    SessionEnd,       // Session ended — aggregates, exit reason
    TurnComplete,     // Stop — turn boundary + token metrics
    TurnError,        // StopFailure — API errors (rate limit, billing)
    SubagentStart,    // Subagent spawned — agent type
    SubagentStop,     // Subagent finished — final message
    CwdChange,        // Working directory changed
    ContextCompact,   // Context was compacted
}
```

### Hook-to-EventType Mapping

| Claude Code Hook | EventType | Captured Data |
|---|---|---|
| PostToolUse | ToolCall | tool_name, tool_input, tool_output, outcome="success" |
| PostToolUseFailure | ToolFailure | tool_name, tool_input, error, outcome="failure" |
| UserPromptSubmit | UserPrompt | prompt text |
| SessionStart | SessionStart | model, source (startup/resume), permission_mode |
| SessionEnd | SessionEnd | exit_reason, aggregates from transcript |
| Stop | TurnComplete | turn-level tokens, latency, model (from transcript) |
| StopFailure | TurnError | error_type (rate_limit, billing, auth, etc.) |
| SubagentStart | SubagentStart | agent_type, agent_id |
| SubagentStop | SubagentStop | agent_type, agent_id, last_message |
| FileChanged | FileChange | file_path, change_type (created/modified/deleted) |
| CwdChanged | CwdChange | new cwd, previous cwd |
| PostCompact | ContextCompact | (marker event, minimal metadata) |

### Hooks NOT Captured (and why)

| Hook | Reason |
|---|---|
| PreToolUse | Redundant — PostToolUse has same input + adds output |
| Notification | Low signal — permission prompts are noise |
| ConfigChange | Low value for developer insights |
| InstructionsLoaded | CLAUDE.md loading not actionable |
| WorktreeCreate/Remove | Edge case, add later if needed |
| Elicitation/ElicitationResult | MCP-specific, rare |
| TaskCreated/TaskCompleted | Internal task tracking |
| PermissionRequest/Denied | Low priority, add later |
| PreCompact | PostCompact sufficient |
| TeammateIdle | Agent team specific |

---

## Schema Changes

### New Columns on `observations` Table

```sql
-- Flexible structured metadata (varies by event_type)
ALTER TABLE observations ADD COLUMN metadata TEXT NOT NULL DEFAULT '{}';

-- Indexed fields pulled out for fast queries
ALTER TABLE observations ADD COLUMN tool_name TEXT;
ALTER TABLE observations ADD COLUMN outcome TEXT;
ALTER TABLE observations ADD COLUMN duration_ms INTEGER;
ALTER TABLE observations ADD COLUMN turn_number INTEGER;

-- Indexes
CREATE INDEX IF NOT EXISTS idx_obs_tool_name ON observations(tool_name);
CREATE INDEX IF NOT EXISTS idx_obs_outcome ON observations(outcome);
```

### Metadata JSON Shapes by EventType

**ToolCall:**
```json
{
  "tool_input": { "command": "npm test", "timeout": 120000 },
  "tool_output": { "stdout": "...", "stderr": "...", "exit_code": 0 },
  "tool_use_id": "unique-id"
}
```

**ToolFailure:**
```json
{
  "tool_input": { "file_path": "/src/main.ts", "old_string": "..." },
  "error": "permission denied",
  "is_interrupt": false,
  "tool_use_id": "unique-id"
}
```

**UserPrompt:**
```json
{
  "prompt": "Fix the null pointer in auth middleware"
}
```

**SessionStart:**
```json
{
  "source": "startup",
  "model": "claude-sonnet-4-6",
  "permission_mode": "auto"
}
```

**SessionEnd:**
```json
{
  "exit_reason": "prompt_input_exit",
  "total_turns": 24,
  "total_tokens_in": 150000,
  "total_tokens_out": 45000,
  "total_duration_ms": 3600000,
  "tool_usage": { "Bash": 15, "Edit": 8, "Read": 22, "Grep": 5 },
  "error_count": 2,
  "models_used": ["claude-sonnet-4-6"]
}
```

**TurnComplete:**
```json
{
  "tokens_in": 8500,
  "tokens_out": 1200,
  "latency_ms": 3400,
  "model": "claude-sonnet-4-6",
  "cache_read_tokens": 6000,
  "cache_write_tokens": 2500
}
```

**TurnError:**
```json
{
  "error_type": "rate_limit"
}
```

**SubagentStart:**
```json
{
  "agent_type": "Explore",
  "agent_id": "subagent-xyz"
}
```

**SubagentStop:**
```json
{
  "agent_type": "Explore",
  "agent_id": "subagent-xyz",
  "last_message": "Found 3 matching files..."
}
```

**FileChange:**
```json
{
  "file_path": "/src/main.ts",
  "change_type": "modified"
}
```

**CwdChange:**
```json
{
  "new_cwd": "/project/backend",
  "previous_cwd": "/project"
}
```

**ContextCompact:**
```json
{}
```

---

## Smart Truncation

Applied by the `devbrain-hook` binary before POSTing to the daemon. Different limits per tool type to optimize for what's actually useful:

| Tool | `tool_input` limit | `tool_output` limit | Rationale |
|---|---|---|---|
| Bash | 4KB | 8KB | stderr/stdout valuable for debugging |
| Read | skip (just file_path) | 2KB | Content is in the file, path is enough |
| Grep | 1KB | 2KB | Pattern + sample matches |
| Edit | 4KB (old_string + new_string) | skip | The edit IS the data |
| Write | skip (just file_path) | skip | Content too large, path is enough |
| Glob | 1KB | 2KB | Pattern + matching files |
| WebFetch | 1KB | 4KB | URL + extracted content |
| WebSearch | 1KB | 4KB | Query + results |
| Agent | 2KB (prompt) | 4KB (final message) | Delegation intent + outcome |
| MCP tools | 2KB | 4KB | Varies, reasonable default |

Truncated fields get `"[truncated at NKB]"` appended.

For `UserPrompt` events: prompt text stored in full (no truncation — user prompts are rarely large and are the most valuable signal).

---

## Transcript Parsing

### Per-Turn Extraction (on `Stop` hook)

When the `Stop` hook fires:

1. Hook script reads `transcript_path` from stdin
2. Reads the **last few lines** of the JSONL file (seek to end, read backwards to find last complete JSON object)
3. Extracts from the last turn entry:
   - `tokens_in`, `tokens_out`
   - `cache_read_tokens`, `cache_write_tokens`
   - `latency_ms`
   - `model`
4. POSTs a `TurnComplete` event with these metrics

This is lightweight — only reads the tail of the file, not the whole thing.

### Session Archive (on `SessionEnd` hook)

When the `SessionEnd` hook fires:

1. **Archive:** Copy transcript JSONL to `~/.devbrain/transcripts/{session_id}.jsonl`
2. **Parse full file** to compute aggregates:
   - Total tokens (in/out/cache)
   - Total turns
   - Total duration (first timestamp to last)
   - Models used (set)
   - Error count
   - Tool usage breakdown (count per tool_name)
3. **POST** a `SessionEnd` event with aggregates in metadata

### Transcript Storage

- Location: `~/.devbrain/transcripts/{session_id}.jsonl`
- Retention: 30 days, then deleted by cleanup job
- Not stored in SQLite — disk-only, referenced by session_id if deep analysis needed

---

## Tiered Retention

A `RetentionCleanupJob` runs daily (registered as an idle-schedule agent, every 24 hours):

### 7-Day Threshold (metadata trimming)

For observations older than 7 days:
- `tool_output` in metadata → truncated to 1KB
- `tool_input.content` (Write file body) → removed
- `prompt` in metadata → truncated to 2000 chars
- `last_message` (SubagentStop) → truncated to 1KB

Implementation: SQL UPDATE with json_replace() on the metadata column, filtered by `created_at < datetime('now', '-7 days')`.

### 30-Day Threshold (transcript deletion)

For transcript files older than 30 days:
- Delete `~/.devbrain/transcripts/{session_id}.jsonl`
- The `SessionEnd` observation (with aggregates) is permanent — only the raw JSONL is deleted

---

## Privacy — Defense in Depth

### Layer 1: Blanket Regex Pass

The existing `SecretPatternRedactor` runs on:
- The serialized `metadata` JSON string
- The `rawContent` field
- Applied to ALL event types, ALL fields

Patterns:
- GitHub PATs (`ghp_`, `gho_`, `ghu_`, `ghs_`, `ghr_`) → `[REDACTED:github-pat]`
- API keys (`sk-`, `AKIA`) → `[REDACTED:api-key]`
- Bearer tokens → `[REDACTED:bearer-token]`
- PEM private keys → `[REDACTED:private-key]`
- Generic secrets (`password=`, `secret=`, `token=`, `api_key=`) → `[REDACTED:secret]`

### Layer 2: Field-Aware Redaction

After Layer 1, apply event-specific rules:

| Condition | Action |
|---|---|
| `tool_name == "Write"` and `file_path` matches `*.env*`, `*secret*`, `*credential*`, `*password*` | Remove `tool_input.content` entirely → `[REDACTED:sensitive-file]` |
| `tool_name == "Bash"` and `command` contains `export`/`set`/`env` + key-like patterns | Redact values in command string |
| `tool_name == "Edit"` and `file_path` matches sensitive patterns | Redact `old_string` and `new_string` |
| Any `tool_output.stderr` with embedded secrets | Already caught by Layer 1, but explicitly scanned |

### .devbrainignore Integration

If `tool_input.file_path` matches a `.devbrainignore` pattern → the entire observation is **dropped** (not stored). User controls what never enters the database.

---

## Hook Registration

### devbrain-hook Binary

A compiled .NET single-file binary, part of the CLI project. Invoked by Claude Code hooks with the event name as argument.

**Behavior:**
1. Read JSON from stdin (Claude Code hook payload)
2. Extract `session_id`, `cwd`, `transcript_path`, and event-specific fields
3. Apply smart truncation based on event type + tool name
4. POST to `http://127.0.0.1:37800/api/v1/events`
5. For `Stop`: also read transcript tail for turn metrics
6. For `SessionEnd`: also archive transcript + compute aggregates
7. Exit 0 always (never blocks Claude Code)

**Performance target:** < 100ms per invocation. Critical path is the HTTP POST to localhost.

### settings.json Configuration

```json
{
  "hooks": {
    "PostToolUse": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook PostToolUse"
    }],
    "PostToolUseFailure": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook PostToolUseFailure"
    }],
    "UserPromptSubmit": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook UserPromptSubmit"
    }],
    "SessionStart": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook SessionStart"
    }],
    "SessionEnd": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook SessionEnd"
    }],
    "SubagentStart": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook SubagentStart"
    }],
    "SubagentStop": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook SubagentStop"
    }],
    "Stop": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook Stop"
    }],
    "StopFailure": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook StopFailure"
    }],
    "FileChanged": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook FileChanged"
    }],
    "CwdChanged": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook CwdChanged"
    }],
    "PostCompact": [{
      "type": "command",
      "command": "~/.devbrain/bin/devbrain-hook PostCompact"
    }]
  }
}
```

### Auto-Registration

The `devbrain setup` command (existing) and the tray app bootstrap both register these hooks automatically. The setup flow:
1. Check if `~/.claude/settings.json` exists
2. Merge DevBrain hooks into the `hooks` section (preserving any existing user hooks)
3. Verify hooks point to the correct binary path

---

## New API Endpoint

### POST /api/v1/events

```
POST /api/v1/events
Content-Type: application/json

{
  "hookEvent": "PostToolUse",
  "sessionId": "abc123",
  "cwd": "/project/path",
  "timestamp": "2026-04-08T12:00:00Z",
  "toolName": "Bash",
  "toolUseId": "tu-xyz",
  "toolInput": { "command": "npm test", "timeout": 120000 },
  "toolOutput": { "stdout": "...", "stderr": "", "exit_code": 0 },
  "outcome": "success",
  "transcriptPath": "/path/to/transcript.jsonl",
  "metadata": {}
}
```

**Response:** `201 Created` with observation ID, or `400` for invalid event.

**Internal processing:**
1. Map `hookEvent` → `EventType`
2. Extract `project` from `cwd` (last path segment or git root)
3. Extract `files_involved` from `toolInput` (any `file_path`, `path` fields)
4. Build `metadata` JSON from event-specific fields
5. Set `tool_name`, `outcome`, `duration_ms`, `turn_number` columns
6. Run Privacy Layer 1 (blanket regex on all string fields)
7. Run Privacy Layer 2 (field-aware redaction)
8. Check `.devbrainignore` — drop if file path matches
9. Insert into `observations` table
10. Publish to EventBus (triggers agents)

---

## What This Unlocks

### Existing Features Enhanced

| Feature | Before | After |
|---|---|---|
| Dead End Detection | Heuristic: "file edited 3+ times with errors" | Precise: count ToolFailure events per file, track retry sequences |
| Deja Vu Alerts | File overlap matching only | Match on tool_name + error patterns across sessions |
| Blast Radius | Graph traversal on decision nodes | Weight by actual edit count per file, factor in test failures |
| Growth Tracking | Generic observation counts | Real metrics: failure rate, debugging time, tokens/turn, session efficiency |
| Briefings | LLM summarizes flat text | Structured: "12 turns, 45K tokens, 8 files edited, 2 dead ends" |
| Session Stories | LLM guesses phases | Precise from tool sequence: Read/Grep=Exploration, Edit=Implementation, Bash(test) failures=Debugging |

### New Features Unlocked (future scope)

| Feature | Data Source |
|---|---|
| Token usage dashboard | TurnComplete metadata |
| Tool usage analytics | Aggregate by tool_name |
| Subagent tracking | SubagentStart/Stop events |
| Prompt history search | UserPrompt events |
| Error pattern detection | ToolFailure + TurnError |
| Session cost estimation | Token counts × pricing |
| Context efficiency | cache_read_tokens / total_tokens ratio |

---

## Out of Scope

| Item | Rationale |
|---|---|
| Token usage dashboard UI | Data captured, dashboard page is separate effort |
| Cost estimation calculations | Data captured, pricing logic is separate |
| Modifying existing agents | Agents query observations — richer data flows through automatically |
| PreToolUse capture | Redundant with PostToolUse |
| Notification/Config/Instructions hooks | Low value signals |
| Worktree/Permission/Task hooks | Edge cases, add later |

---

## File Inventory

### New Files

| File | Purpose |
|---|---|
| `src/DevBrain.Cli/Commands/HookCommand.cs` | `devbrain-hook` entry point — reads stdin, truncates, POSTs |
| `src/DevBrain.Cli/Hooks/HookPayload.cs` | Deserialization models for Claude Code hook stdin |
| `src/DevBrain.Cli/Hooks/SmartTruncator.cs` | Tool-type-aware truncation logic |
| `src/DevBrain.Cli/Hooks/TranscriptTailReader.cs` | Read last entry from JSONL for turn metrics |
| `src/DevBrain.Api/Endpoints/EventEndpoints.cs` | POST /api/v1/events endpoint |
| `src/DevBrain.Api/Services/EventIngestionService.cs` | Normalize, privacy filter, write events |
| `src/DevBrain.Capture/Privacy/FieldAwareRedactor.cs` | Privacy Layer 2 — field-specific redaction |
| `src/DevBrain.Capture/Transcript/TranscriptParser.cs` | Full transcript parsing for session aggregates |
| `src/DevBrain.Capture/Transcript/TranscriptArchiver.cs` | Copy JSONL to ~/.devbrain/transcripts/ |
| `src/DevBrain.Agents/RetentionCleanupJob.cs` | Daily tiered retention (7d trim, 30d transcript delete) |
| `tests/DevBrain.Cli.Tests/SmartTruncatorTests.cs` | Truncation logic tests |
| `tests/DevBrain.Capture.Tests/FieldAwareRedactorTests.cs` | Privacy Layer 2 tests |
| `tests/DevBrain.Capture.Tests/TranscriptParserTests.cs` | Transcript parsing tests |
| `tests/DevBrain.Storage.Tests/RetentionCleanupTests.cs` | Retention cleanup tests |
| `tests/DevBrain.Integration.Tests/EventIngestionTests.cs` | End-to-end event capture tests |

### Modified Files

| File | Change |
|---|---|
| `src/DevBrain.Core/Enums/EventType.cs` | Add 10 new enum values |
| `src/DevBrain.Core/Models/Observation.cs` | Add `Metadata`, `ToolName`, `Outcome`, `DurationMs`, `TurnNumber` properties |
| `src/DevBrain.Core/Interfaces/IObservationStore.cs` | Add filter support for new fields |
| `src/DevBrain.Storage/Schema/SchemaManager.cs` | Add columns, indexes, migration |
| `src/DevBrain.Storage/SqliteObservationStore.cs` | Read/write new columns |
| `src/DevBrain.Api/Program.cs` | Register new endpoint + EventIngestionService |
| `src/DevBrain.Cli/Program.cs` | Register HookCommand |
| `src/DevBrain.Capture/Pipeline/PrivacyFilter.cs` | Integrate FieldAwareRedactor |
