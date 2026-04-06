# DevBrain — C# Architecture Design Spec

**Version:** 1.0
**Date:** 2026-04-05
**Status:** Approved
**Based on:** docs/PRD.md (v1.0)

---

## 1. Key Departures from PRD

This spec adapts the PRD's Rust-based architecture to C#/.NET. The product vision, user journey, core concepts, and feature scope remain unchanged. The following architectural decisions differ:

| PRD Choice | This Spec | Rationale |
|---|---|---|
| Rust + Tokio | C# / .NET 9+ with Native AOT | Team has strong C# background. DevBrain is I/O-bound, not CPU-bound — C# performance is more than sufficient. Native AOT produces comparable single binaries. |
| Axum (Rust) | ASP.NET Core Minimal APIs | Idiomatic C# equivalent. Lightweight, async, excellent middleware story. |
| SQLite + LanceDB + CozoDB (3 stores) | SQLite + LanceDB (2 stores) | CozoDB eliminated. Graph modeled as relational tables in SQLite with recursive CTEs. Fewer moving parts, one fewer corruption/migration path. |
| CozoDB (Datalog) | Thin custom graph wrapper (~250 LOC) over SQLite | At DevBrain's scale (tens of thousands of nodes), recursive CTEs perform in single-digit milliseconds for 2-4 hop traversals. No need for a dedicated graph engine. |
| Cargo + cross | dotnet publish + Native AOT | Per-platform AOT binaries. Six targets: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64. |
| Single binary (daemon + CLI) | Two binaries (daemon + thin CLI) | CLI is a fast HTTP client (~5-10MB). Daemon is the full host (~25-40MB). CLI commands feel instant because they don't initialize storage/agents. |

Everything else — capture rules, agent behavior, privacy layers, API surface, CLI commands, configuration, phased rollout, success metrics — carries forward from the PRD as specified.

---

## 2. Solution Structure

```
DevBrain.sln
│
├── src/
│   ├── DevBrain.Core/           # Domain models, interfaces, shared types
│   ├── DevBrain.Storage/        # SQLite + Graph wrapper + LanceDB
│   ├── DevBrain.Capture/        # Pipeline stages, adapters
│   ├── DevBrain.Agents/         # Intelligence agents + scheduler
│   ├── DevBrain.Llm/            # LLM clients + task queue
│   ├── DevBrain.Api/            # Daemon host: Minimal API + embedded dashboard
│   └── DevBrain.Cli/            # Thin CLI binary (HTTP client)
│
├── tests/
│   ├── DevBrain.Core.Tests/
│   ├── DevBrain.Storage.Tests/
│   ├── DevBrain.Capture.Tests/
│   ├── DevBrain.Agents.Tests/
│   ├── DevBrain.Llm.Tests/
│   └── DevBrain.Integration.Tests/
│
├── dashboard/                   # React + TypeScript SPA (Vite)
│
└── build/
    └── ci/                      # GitHub Actions workflows
```

### Project Dependency Graph

```
DevBrain.Cli ──HTTP──> DevBrain.Api
                            │
              ┌─────────────┼─────────────┐
              v             v             v
        DevBrain.Capture  DevBrain.Agents  DevBrain.Llm
              │             │              │
              v             v              v
           DevBrain.Storage ◄──────────────┘
              │
              v
         DevBrain.Core  (no upstream dependencies)
```

### Project Responsibilities

| Project | Contains | Depends on |
|---|---|---|
| **Core** | Domain models: `Observation`, `Thread`, `DeadEnd`, `Decision`, `Pattern`, `GraphNode`, `GraphEdge`. Interfaces: `IObservationStore`, `IGraphStore`, `IVectorStore`, `ILlmService`, `ICaptureAdapter`, `IIntelligenceAgent`, `IPipelineStage`. Configuration models. Enums: `EventType`, `CaptureSource`, `ThreadState`, `Priority`. | Nothing |
| **Storage** | `SqliteObservationStore`, `SqliteGraphStore` (~250 LOC graph wrapper), `LanceDbVectorStore`. `MigrationRunner`. Schema definitions. FTS5 trigger management. | Core |
| **Capture** | Pipeline stages: `Normalizer`, `Enricher`, `Tagger`, `PrivacyFilter`, `Writer`. `AiSessionAdapter`. Pipeline orchestrator using `Channel<T>`. Thread boundary resolution. | Core, Storage, Llm |
| **Agents** | `BriefingAgent`, `DeadEndAgent`, `LinkerAgent`, `CompressionAgent`. `AgentScheduler` (cron, on-event, idle triggers). | Core, Storage, Llm |
| **Llm** | `OllamaClient`, `AnthropicClient`, `EmbeddingService` (Ollama + ONNX fallback), `LlmTaskQueue` (routes tasks to local vs cloud, manages priority and persistence). | Core |
| **Api** | `Program.cs` (host builder), endpoint groups, `IHostedService` registrations for daemon lifecycle, capture pipeline, agent scheduler. Serves embedded React dashboard from `wwwroot/`. | Core, Storage, Capture, Agents, Llm |
| **Cli** | `System.CommandLine`-based argument parser, `HttpClient` wrapper, formatted console output. Talks to daemon over `http://localhost:37800`. | Core (shared models only) |

---

## 3. Storage Layer

### 3.1 SQLite Schema

Single database file (`~/.devbrain/devbrain.db`). WAL mode enabled for concurrent reads and atomic writes.

#### Observations

```sql
CREATE TABLE observations (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    thread_id TEXT,
    parent_id TEXT,
    timestamp TEXT NOT NULL,
    project TEXT NOT NULL,
    branch TEXT,
    event_type TEXT NOT NULL,
    source TEXT NOT NULL,
    raw_content TEXT NOT NULL,
    summary TEXT,
    tags TEXT,
    files_involved TEXT,
    created_at TEXT DEFAULT (datetime('now'))
);

CREATE INDEX idx_obs_thread ON observations(thread_id);
CREATE INDEX idx_obs_session ON observations(session_id);
CREATE INDEX idx_obs_project ON observations(project);
CREATE INDEX idx_obs_timestamp ON observations(timestamp);
CREATE INDEX idx_obs_event_type ON observations(event_type);
```

#### Threads

```sql
CREATE TABLE threads (
    id TEXT PRIMARY KEY,
    project TEXT NOT NULL,
    branch TEXT,
    title TEXT,
    state TEXT NOT NULL DEFAULT 'Active',
    started_at TEXT NOT NULL,
    last_activity TEXT NOT NULL,
    observation_count INTEGER DEFAULT 0,
    summary TEXT,
    created_at TEXT DEFAULT (datetime('now'))
);
```

#### Dead Ends

```sql
CREATE TABLE dead_ends (
    id TEXT PRIMARY KEY,
    thread_id TEXT REFERENCES threads(id),
    project TEXT NOT NULL,
    description TEXT NOT NULL,
    approach TEXT NOT NULL,
    reason TEXT NOT NULL,
    files_involved TEXT,
    detected_at TEXT NOT NULL,
    created_at TEXT DEFAULT (datetime('now'))
);

CREATE INDEX idx_de_project ON dead_ends(project);
```

#### Graph Tables

```sql
CREATE TABLE graph_nodes (
    id TEXT PRIMARY KEY,
    type TEXT NOT NULL,
    name TEXT NOT NULL,
    data TEXT,
    source_id TEXT,
    created_at TEXT DEFAULT (datetime('now'))
);

CREATE TABLE graph_edges (
    id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL REFERENCES graph_nodes(id),
    target_id TEXT NOT NULL REFERENCES graph_nodes(id),
    type TEXT NOT NULL,
    data TEXT,
    weight REAL DEFAULT 1.0,
    created_at TEXT DEFAULT (datetime('now'))
);

CREATE INDEX idx_ge_source ON graph_edges(source_id);
CREATE INDEX idx_ge_target ON graph_edges(target_id);
CREATE INDEX idx_ge_type ON graph_edges(type);
CREATE INDEX idx_gn_type ON graph_nodes(type);
CREATE INDEX idx_gn_source ON graph_nodes(source_id);
```

Node types: `File`, `Function`, `Decision`, `DeadEnd`, `Bug`, `Thread`, `Pattern`, `Person`.
Edge types: `caused`, `fixed`, `relates_to`, `blocked_by`, `abandoned`, `references`, `preceded`, `succeeded`, `detected_pattern`.

#### Full-Text Search

```sql
CREATE VIRTUAL TABLE observations_fts USING fts5(
    summary, raw_content, tags,
    content=observations,
    content_rowid=rowid
);

-- Sync triggers (required for external content FTS5 tables)
CREATE TRIGGER observations_ai AFTER INSERT ON observations BEGIN
    INSERT INTO observations_fts(rowid, summary, raw_content, tags)
    VALUES (new.rowid, new.summary, new.raw_content, new.tags);
END;

CREATE TRIGGER observations_ad AFTER DELETE ON observations BEGIN
    INSERT INTO observations_fts(observations_fts, rowid, summary, raw_content, tags)
    VALUES ('delete', old.rowid, old.summary, old.raw_content, old.tags);
END;

CREATE TRIGGER observations_au AFTER UPDATE ON observations BEGIN
    INSERT INTO observations_fts(observations_fts, rowid, summary, raw_content, tags)
    VALUES ('delete', old.rowid, old.summary, old.raw_content, old.tags);
    INSERT INTO observations_fts(rowid, summary, raw_content, tags)
    VALUES (new.rowid, new.summary, new.raw_content, new.tags);
END;
```

#### Schema Versioning

```sql
CREATE TABLE _meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
```

### 3.2 Graph Wrapper API

`SqliteGraphStore` (~250 lines) wraps the graph tables with a clean C# API:

```csharp
public class SqliteGraphStore : IGraphStore
{
    Task<GraphNode> AddNode(string type, string name, object? data = null, string? sourceId = null);
    Task<GraphNode?> GetNode(string id);
    Task<IReadOnlyList<GraphNode>> GetNodesByType(string type);
    Task RemoveNode(string id);

    Task<GraphEdge> AddEdge(string sourceId, string targetId, string type, object? data = null);
    Task RemoveEdge(string id);

    Task<IReadOnlyList<GraphNode>> GetNeighbors(string nodeId, int hops = 1, string? edgeType = null);
    Task<IReadOnlyList<GraphPath>> FindPaths(string fromId, string toId, int maxDepth = 4);
    Task<IReadOnlyList<GraphNode>> GetRelatedToFile(string filePath);
}
```

`GetNeighbors` and `FindPaths` generate recursive CTEs internally. Traversal is **bidirectional** — follows both outbound (`source_id → target_id`) and inbound (`target_id → source_id`) edges. Example N-hop traversal:

```sql
WITH RECURSIVE reachable(id, depth, path) AS (
    -- Outbound edges
    SELECT target_id, 1, @startId || '->' || target_id
    FROM graph_edges WHERE source_id = @startId
    UNION ALL
    -- Inbound edges
    SELECT source_id, 1, @startId || '->' || source_id
    FROM graph_edges WHERE target_id = @startId
    UNION ALL
    -- Recursive outbound
    SELECT e.target_id, r.depth + 1, r.path || '->' || e.target_id
    FROM graph_edges e
    JOIN reachable r ON e.source_id = r.id
    WHERE r.depth < @maxDepth
      AND instr(r.path, e.target_id) = 0
    UNION ALL
    -- Recursive inbound
    SELECT e.source_id, r.depth + 1, r.path || '->' || e.source_id
    FROM graph_edges e
    JOIN reachable r ON e.target_id = r.id
    WHERE r.depth < @maxDepth
      AND instr(r.path, e.source_id) = 0
)
SELECT DISTINCT n.* FROM reachable r JOIN graph_nodes n ON n.id = r.id;
```

### 3.3 LanceDB Vector Store

```csharp
public class LanceDbVectorStore : IVectorStore
{
    Task Index(string id, string text, VectorCategory category);
    Task<IReadOnlyList<VectorMatch>> Search(string query, int topK = 20, VectorCategory? filter = null);
    Task Remove(string id);
    Task Rebuild();
}

public enum VectorCategory { ObservationSummary, DecisionReasoning, DeadEndDescription, ThreadSummary }
```

Embeddings via Ollama `nomic-embed-text` (384 dimensions), with ONNX fallback (`all-MiniLM-L6-v2` via `Microsoft.ML.OnnxRuntime`).

### 3.4 Source of Truth & Recovery

```
SQLite (authoritative) ──rebuilds──> Graph tables (derived from observations)
SQLite (authoritative) ──re-embeds──> LanceDB (derived from summaries)
```

- LanceDB corrupt → `devbrain rebuild vectors` re-embeds from SQLite.
- Graph tables corrupt → `devbrain rebuild graph` replays Linker Agent over all observations.
- SQLite corrupt → WAL mode + `PRAGMA integrity_check` on startup + auto-backup before migrations.

### 3.5 File Layout

```
~/.devbrain/
├── devbrain.db              # SQLite — observations, threads, dead ends, graph, FTS, settings cache
├── vectors/                 # LanceDB — embeddings for semantic search
├── briefings/
│   ├── 2026-04-06.md
│   └── 2026-04-05.md
├── settings.toml
├── logs/
│   └── devbrain-2026-04-06.log
├── cache/
│   └── llm-tasks/
│       ├── pending/         # Persisted LLM task queue
│       └── completed/       # Kept 24h for debugging
├── backups/                 # Auto-created before migrations
└── daemon.pid               # PID file for daemon state detection
```

---

## 4. Capture Engine

### 4.1 Pipeline Architecture

`Channel<T>`-based producer/consumer chain. Each stage runs as an independent `Task` with built-in backpressure (bounded channels, capacity 100).

```
AiSessionAdapter ──> Channel<RawEvent>
                         │
                    Normalizer ──> Channel<Observation>
                         │
                      Enricher ──> Channel<Observation>
                         │
                       Tagger ──> Channel<Observation>
                         │
                   PrivacyFilter ──> Channel<Observation>
                         │
                       Writer ──> SQLite + LanceDB + Event Bus
```

### 4.2 Stage Contract

```csharp
public interface IPipelineStage
{
    Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct);
}
```

### 4.3 Stage Responsibilities

| Stage | What it does | LLM needed? |
|---|---|---|
| **Normalizer** | Converts `RawEvent` to uniform `Observation` schema. Assigns IDs, timestamps. | No |
| **Enricher** | Adds project name (git root), branch, file paths. Resolves thread assignment using boundary rules. | No |
| **Tagger** | Classifies: decision / exploration / dead-end / fix / refactor. Generates one-line summary. | Yes (local) |
| **PrivacyFilter** | Strips `<private>` tags, redacts secrets, filters `.devbrainignore` matches. | No |
| **Writer** | Writes to SQLite + FTS index, queues embedding to LanceDB, emits event to Agent Scheduler. | No |

### 4.4 AI Session Adapter

Dual capture mechanism:

**File watcher:** Monitors Claude Code (`~/.claude/projects/*/sessions/`) and Cursor session paths using `FileSystemWatcher`. Parses new JSONL entries.

**Hook-based:** Claude Code `PostToolUse` hook POSTs observations directly to `POST /api/v1/observations`. More reliable than file watching.

Both run simultaneously. Writer deduplicates by event content hash.

```csharp
public class AiSessionAdapter : ICaptureAdapter
{
    public string Name => "ai-sessions";
    public async Task Start(ChannelWriter<RawEvent> output, CancellationToken ct);
    public AdapterHealth Health { get; }
}
```

### 4.5 Thread Boundary Rules

| Signal | Behavior |
|---|---|
| New AI session (different session_id) | New thread |
| Same session, same project/branch | Continue thread |
| Same session, different project | New thread |
| Same session, branch switch | New thread |
| Gap > 2 hours (configurable) | New thread |
| User runs `devbrain thread new` | New thread |

### 4.6 Retroactive Enrichment

When Ollama becomes available after being down, a background task picks up unenriched observations:

```sql
SELECT * FROM observations WHERE summary IS NULL ORDER BY timestamp ASC LIMIT 50;
```

Runs through Tagger in batches until all observations are enriched.

---

## 5. Intelligence Agents

### 5.1 Agent Contract

```csharp
public interface IIntelligenceAgent
{
    string Name { get; }
    AgentSchedule Schedule { get; }
    Priority Priority { get; }
    Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct);
}

public abstract record AgentSchedule
{
    public record Cron(string Expression) : AgentSchedule;
    public record OnEvent(params EventType[] Types) : AgentSchedule;
    public record OnDemand : AgentSchedule;
    public record Idle(TimeSpan After) : AgentSchedule;
}

public record AgentContext(
    IObservationStore Observations,
    IGraphStore Graph,
    IVectorStore Vectors,
    ILlmService Llm,
    Settings Settings
);
```

### 5.2 Agent Scheduler

`IHostedService` managing three trigger sources:

- **Event Bus** (from Writer) → OnEvent agents. Debounced: batches events within a 5-second window before triggering.
- **Cron Timer** → Cron agents. Checks every 60 seconds if any agent is due.
- **Idle Detector** → Idle agents. Tracks last observation timestamp. "Idle" = no new observations for N minutes (not OS-level idle detection).

Priority order: `OnEvent > Cron > Idle > OnDemand`.
Concurrency: `SemaphoreSlim` caps concurrent agent runs at configurable max (default 3).
Failure: Agent errors are logged, no retry loops — scheduler retries on the next trigger.

### 5.3 v1 Agents

#### Linker Agent
- **Schedule:** `OnEvent(*)` — debounced
- **LLM:** Rule-based fast path (~90%, no LLM). Local LLM slow path (~10%, batched).
- **Logic:** Extract file paths → File nodes + "references" edges. Thread ordering → "preceded"/"succeeded" edges. Error after edit → "caused" edge. Same function → "relates_to" edge. Ambiguous relationships batched (up to 10) for local LLM classification.

#### Dead End Agent
- **Schedule:** `OnEvent(Error, Conversation)`
- **LLM:** Local (classification)
- **Heuristics:** Edits → error → reverts. User says "that didn't work." Same file edited 3+ times without resolution. Branch abandoned. On heuristic match → local LLM confirms and extracts: what was tried, why it failed, what to avoid. Writes DeadEnd record + graph node + vector index.

#### Briefing Agent
- **Schedule:** `Cron("0 7 * * *")` + `OnDemand`
- **LLM:** Cloud (Anthropic). Fallback: local LLM (lower quality). Fallback: skip.
- **Input:** Last session's observations by thread, active/paused threads, recent dead ends.
- **Output:** Markdown briefing → `~/.devbrain/briefings/YYYY-MM-DD.md`

#### Compression Agent
- **Schedule:** `Idle(60 minutes)`
- **LLM:** Local (summarization)
- **Logic:** Find threads older than `compression_after_days` with no summary. Summarize observation sequence into narrative. Update thread state to Archived. Index thread summary in LanceDB.

### 5.4 LLM Task Queue

Lives in `DevBrain.Llm`. Used by both agents (via `AgentContext`) and the capture pipeline's Tagger stage (via `ILlmService`). Both consumers submit tasks through the same queue, ensuring unified priority ordering and cloud quota management.

```csharp
public record LlmTask(
    string AgentName,
    Priority Priority,
    LlmTaskType Type,
    string Prompt,
    LlmPreference Preference
);
```

Routing: explicit preference honored → `PreferLocal` tries Ollama then cloud → `PreferCloud` tries Anthropic then local. Respects `max_daily_requests`. Queue persists pending tasks to `~/.devbrain/cache/llm-tasks/pending/` — survives daemon restarts.

---

## 6. HTTP API

Local REST API on `127.0.0.1:{port}` (default `37800`). No authentication — secured by localhost-only binding.

### Endpoints

```
POST   /api/v1/observations              # Adapters push events (feeds into capture pipeline)
GET    /api/v1/observations               # Query with filters (project, type, date range)
GET    /api/v1/observations/:id           # Single observation with thread context

GET    /api/v1/search?q=                  # Semantic (LanceDB) + exact (FTS5) + graph expansion
GET    /api/v1/search/exact?q=            # FTS5 only

GET    /api/v1/briefings                  # List briefings
GET    /api/v1/briefings/latest           # Today's briefing
POST   /api/v1/briefings/generate         # Force-generate

GET    /api/v1/graph/node/:id             # Node + all edges
GET    /api/v1/graph/neighbors            # N-hop traversal (nodeId, hops, edgeType params)
GET    /api/v1/graph/paths                # Path finding (from, to, maxDepth params)

GET    /api/v1/context/file/:path         # Aggregated: decisions + dead ends + patterns for a file

GET    /api/v1/threads                    # List threads (filterable by state, project)
GET    /api/v1/threads/:id                # Full thread with observation narrative

GET    /api/v1/dead-ends                  # All dead ends (filterable by project, file)
GET    /api/v1/patterns                   # Detected patterns (v2)

GET    /api/v1/agents                     # Agent status, last run, next run
POST   /api/v1/agents/:name/run           # Manual trigger (returns 202 Accepted)

GET    /api/v1/health                     # Daemon status, storage stats, LLM status
GET    /api/v1/settings                   # Current settings
PUT    /api/v1/settings                   # Update settings (hot-reload safe ones)

POST   /api/v1/rebuild/vectors            # Re-embed from SQLite
POST   /api/v1/rebuild/graph              # Replay Linker Agent
POST   /api/v1/export                     # Full JSON export
DELETE /api/v1/data                       # Purge (with project/date filters)
```

### Search Pipeline

```
User query
    → Embed query → LanceDB vector search (top 20 candidates)
    → SQLite FTS5 rerank (boost exact matches)
    → Graph expansion (add related entities within 1 hop)
    → Return ranked results with context
```

---

## 7. CLI

Thin binary using `System.CommandLine`. All commands are HTTP calls to the running daemon.

### Commands

```bash
devbrain start                              # Launch daemon as background process
devbrain stop                               # Graceful shutdown
devbrain status                             # Health, storage stats, agent status

devbrain briefing                           # Show morning briefing
devbrain briefing --generate                # Force regenerate
devbrain search "webhook timeout"           # Semantic search
devbrain search --exact "HttpClient::Send"  # FTS5 search

devbrain why src/api/webhooks.cs            # Decisions + dead ends + patterns for a file
devbrain thread                             # Current active thread
devbrain thread list                        # All recent threads
devbrain thread new "investigating X"       # Start new thread manually
devbrain dead-ends                          # List dead ends for current project
devbrain dead-ends --file src/auth.cs       # Dead ends for specific file

devbrain related src/api/webhooks.cs        # Graph: everything connected to this file
devbrain trace <decision-id>                # Graph: trace to consequences

devbrain agents                             # List agents, status, last run
devbrain agents run briefing                # Manually trigger an agent
devbrain config                             # Show settings
devbrain config set llm.local.model "llama3.2"

devbrain export --format json               # Full data export
devbrain purge --project <name>             # Delete project data
devbrain purge --before 2026-01-01          # Delete by date
devbrain rebuild vectors                    # Re-embed from SQLite
devbrain rebuild graph                      # Replay Linker Agent
devbrain dashboard                          # Open browser to localhost:37800

devbrain service install                    # Create system service for auto-start
devbrain service uninstall                  # Remove system service
devbrain update --check                     # Check GitHub Releases for newer version
devbrain update                             # Re-runs install script for current platform
```

### Daemon State Detection

| PID file exists | Health endpoint responds | State |
|---|---|---|
| Yes | Yes | Running |
| Yes | No | Crashed (stale PID, clean up + offer restart) |
| No | No | Stopped |

---

## 8. Web Dashboard

React 18+ with TypeScript, built with Vite. Output embedded in `DevBrain.Api/wwwroot/`. Served by ASP.NET Core static file middleware with SPA fallback routing.

### Pages

| Page | Purpose |
|---|---|
| **Timeline** (default) | Chronological observation feed with project/type/date filters |
| **Briefings** | Date-navigable briefing viewer with "Generate Now" action |
| **Dead Ends** | Filterable catalog of dead ends by project and file |
| **Threads** | Thread browser with state filters, click to expand full narrative |
| **Search** | Semantic + exact search with relevance scores |
| **Settings** | Form-based `settings.toml` editor, grouped by section, indicates which changes require restart |
| **Health** | Live system status: daemon uptime, storage sizes, LLM connection status, agent run history, capture rate |

### Build Integration

Dashboard is built as a CI pre-step. The `DevBrain.Api.csproj` includes a build target that copies `dashboard/dist/` into `wwwroot/` if not already present. In CI, the dashboard job runs first and its output is downloaded by the .NET build jobs.

---

## 9. Privacy & Security

### Privacy Pipeline

The `PrivacyFilter` capture stage applies four redactors in order:

1. **PrivateTagRedactor** — strips `<private>...</private>` blocks
2. **SecretPatternRedactor** — regex patterns for API keys, tokens, passwords, PEM keys, GitHub PATs
3. **EnvFileRedactor** — redacts content matching `.env` file patterns
4. **IgnoreFileRedactor** — drops observations matching `.devbrainignore` rules

Redacted content replaced with `[REDACTED:type]`. Original content never stored.

### Privacy Modes

| Mode | Behavior |
|---|---|
| **redact** (default) | Secret patterns replaced. Everything else captured normally. |
| **strict** | Only summaries stored (no raw_content). File paths stored but not contents. Conversation text stripped to intent only. |

### Cloud LLM Boundary

Never sent to cloud: `raw_content`, file contents/diffs, `.env` values, raw conversation text.
Sent to cloud: observation summaries, thread titles/summaries, dead end descriptions, project/branch names.

### File Permissions

On startup: `chmod 700` equivalent on `~/.devbrain/`. Windows: restrict ACL to current user. Linux/macOS: `UnixFileMode.UserRead | UserWrite | UserExecute`.

### .devbrainignore

Per-project, gitignore syntax. Parsed with `Microsoft.Extensions.FileSystemGlobbing`.

---

## 10. Configuration

### settings.toml

```toml
[daemon]
port = 37800
log_level = "info"
auto_start = true
data_path = "~/.devbrain"

[capture]
enabled = true
sources = ["ai-sessions"]
privacy_mode = "redact"
ignored_projects = []
max_observation_size_kb = 512
thread_gap_hours = 2

[storage]
sqlite_max_size_mb = 2048
vector_dimensions = 384
compression_after_days = 7
retention_days = 365

[llm.local]
enabled = true
provider = "ollama"
model = "llama3.2"
endpoint = "http://localhost:11434"
max_concurrent = 2

[llm.cloud]
enabled = true
provider = "anthropic"
model = "claude-sonnet-4-6"
api_key_env = "DEVBRAIN_CLOUD_API_KEY"
max_daily_requests = 50
tasks = ["briefing", "pattern"]

[agents.briefing]
enabled = true
schedule = "0 7 * * *"
timezone = "America/New_York"

[agents.dead_end]
enabled = true
sensitivity = "medium"

[agents.linker]
enabled = true
debounce_seconds = 5

[agents.compression]
enabled = true
idle_minutes = 60

[agents.pattern]
enabled = true
idle_minutes = 30
lookback_days = 30
```

Parsed with `Tomlyn`. Hot-reload via `FileSystemWatcher` for safe settings (agent schedules, LLM config). Unsafe settings (port, data_path) require restart — API indicates which.

---

## 11. Distribution & Build

### Native AOT Build Matrix

| Target | Runtime Identifier |
|---|---|
| Windows x64 | `win-x64` |
| Windows ARM64 | `win-arm64` |
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS x64 | `osx-x64` |
| macOS ARM64 | `osx-arm64` |

### CI Pipeline (GitHub Actions)

1. **Dashboard job:** `npm ci && npm run build` → artifact
2. **Build matrix:** Download dashboard artifact → `dotnet publish` with Native AOT for each RID → produces `devbrain-daemon` + `devbrain` per platform
3. **Release job:** Package as tar.gz (linux/mac) / zip (windows), create GitHub release, update Homebrew tap

### Installation

```bash
brew install devbrain/tap/devbrain              # macOS
curl -sSL https://install.devbrain.dev | bash   # Linux / macOS
irm https://install.devbrain.dev/win | iex      # Windows
```

Install script detects platform/arch, downloads correct archive, extracts to `~/.devbrain/bin/`, adds to PATH.

### Release Archive Contents

```
devbrain-v1.0.0-{platform}.{ext}
├── devbrain            # CLI (~5-10MB)
├── devbrain-daemon     # Daemon (~25-40MB)
└── LICENSE
```

---

## 12. Failure Handling

| Failure | Behavior |
|---|---|
| **Ollama not running** | Tagger passes observations through unenriched. Agents needing local LLM skip and log. Retroactive enrichment when Ollama returns. CLI warning shown. |
| **Cloud LLM unavailable** | Briefing Agent falls back to local. If both unavailable, skips. Next run covers the gap. |
| **Disk full** | Check every 60s. Below 100MB: pause capture, emit warning. Existing data queryable. |
| **SQLite corruption** | `PRAGMA integrity_check` on startup. Attempt `PRAGMA recover`. If unrecoverable: fresh DB, preserve LanceDB. |
| **LanceDB corruption** | `devbrain rebuild vectors` re-embeds from SQLite. Auto-detected via search errors flagged in health endpoint. |
| **Daemon crash mid-write** | SQLite WAL ensures atomicity. Channel contents lost (acceptable). LLM task queue persisted to disk, re-queued on restart. |
| **Adapter disconnect** | Health check every 30s, exponential backoff reconnect. Events during disconnect lost (acceptable). |

### Daemon Lifecycle

Graceful shutdown sequence:
1. Stop accepting new API requests
2. Signal capture pipeline to flush (drain channels)
3. Wait for in-flight agent runs (timeout 30s)
4. Flush pending LLM tasks to disk
5. SQLite WAL checkpoint
6. Close LanceDB
7. Write clean shutdown marker to log

---

## 13. Migration Strategy

On daemon startup, `MigrationRunner` compares schema version in `_meta` table against version compiled into the binary. If mismatch:

1. Auto-backup affected files to `~/.devbrain/backups/{timestamp}/`
2. Run migrations sequentially (never skip versions)
3. Each migration runs in a SQLite transaction — atomic success or full rollback
4. LanceDB migrations may require re-embedding (automated)

Migrations are forward-only. Breaking changes use `devbrain export` / `devbrain import` as escape hatch.

---

## 14. Key Dependencies

| Package | Purpose | AOT Compatible |
|---|---|---|
| `Microsoft.Data.Sqlite` | SQLite access | Yes |
| `LanceDB .NET bindings` | Vector storage | Validate early — fallback: sqlite-vec or HnswLib.NET if AOT incompatible |
| `Microsoft.ML.OnnxRuntime` | Fallback embeddings | Yes |
| `System.CommandLine` | CLI argument parsing | Yes |
| `Tomlyn` | TOML config parsing | Yes |
| `System.Threading.Channels` | Pipeline backpressure | Yes (built-in) |
| `Microsoft.Extensions.FileSystemGlobbing` | .devbrainignore parsing | Yes |

**AOT validation is a CI gate** — `dotnet publish` with AOT on all targets must succeed before merge.

---

## 15. Success Metrics (adapted from PRD)

| Metric | Target |
|---|---|
| Daemon memory usage | < 80MB idle, < 250MB during agent runs |
| Capture latency | < 10ms from event to stored observation |
| Search latency | < 200ms for semantic search |
| Briefing generation | < 30 seconds |
| CLI binary size | < 15MB (Native AOT) |
| Daemon binary size | < 50MB (Native AOT) |
| CLI cold start | < 200ms |
| Daemon cold start | < 3 seconds |
| Daily cloud LLM cost | < $0.10 for typical solo dev usage |

Memory targets relaxed from PRD (50MB → 80MB idle) to account for .NET GC overhead. Binary size targets adjusted for two binaries. All other metrics unchanged.

---

## 16. Out of Scope for v1

Carried forward from PRD — not changed by the C# migration:

- Pattern Agent (v2)
- Git Adapter (v2)
- Graph Explorer dashboard page (v2)
- Multi-provider LLM — OpenAI, Gemini (v2)
- Team sync (v3)
- IDE extension (v1.1)
- Encryption at rest (future — design accommodates SQLCipher drop-in)
- Auto-update (v1 uses manual update via package manager)
- Telemetry (none in v1, opt-in in v3+)
