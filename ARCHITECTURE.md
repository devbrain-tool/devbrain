# Architecture

This document describes DevBrain's internal architecture for contributors who want to understand the codebase, fix bugs, or add features.

## Overview

DevBrain is a .NET 9+ application compiled to Native AOT binaries. It ships as two executables:

- **Daemon** (`devbrain-api`) — ASP.NET Core Minimal API host that runs the capture pipeline, intelligence agents, storage layer, and serves the embedded web dashboard.
- **CLI** (`devbrain`) — thin `System.CommandLine` binary that sends HTTP requests to the daemon on `localhost:37800`. Starts instantly because it initializes nothing beyond an `HttpClient`.

All data lives in `~/.devbrain/`. SQLite is the authoritative store. The knowledge graph is modeled as relational tables in SQLite (no separate graph database). LanceDB provides vector search for semantic queries.

## Layered Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     DevBrain.Cli                        │
│              (thin HTTP client binary)                  │
└──────────────────────┬──────────────────────────────────┘
                       │ HTTP (localhost:37800)
┌──────────────────────v──────────────────────────────────┐
│                     DevBrain.Api                        │
│     (ASP.NET Core host, Minimal API, dashboard)         │
├─────────────┬───────────────┬───────────────────────────┤
│  DevBrain.  │  DevBrain.    │  DevBrain.                │
│  Capture    │  Agents       │  Llm                      │
├─────────────┴───────────────┴───────────────────────────┤
│                   DevBrain.Storage                      │
│          (SQLite, Graph wrapper, LanceDB)               │
├─────────────────────────────────────────────────────────┤
│                    DevBrain.Core                        │
│        (Domain models, interfaces, enums)               │
└─────────────────────────────────────────────────────────┘
```

## Project Dependency Graph

```
DevBrain.Cli ──HTTP──> DevBrain.Api
                            │
              ┌─────────────┼─────────────┐
              v             v             v
        DevBrain.Capture  DevBrain.Agents  DevBrain.Llm
              │             │              │
              v             v              v
           DevBrain.Storage <──────────────┘
              │
              v
         DevBrain.Core  (no upstream dependencies)
```

### Project Responsibilities

| Project | What it contains | Depends on |
|---|---|---|
| **Core** | Domain models (`Observation`, `Thread`, `DeadEnd`, `Decision`, `Pattern`, `GraphNode`, `GraphEdge`). Interfaces (`IObservationStore`, `IGraphStore`, `IVectorStore`, `ILlmService`, `ICaptureAdapter`, `IIntelligenceAgent`, `IPipelineStage`). Enums (`EventType`, `CaptureSource`, `ThreadState`, `Priority`). Configuration models. | Nothing |
| **Storage** | `SqliteObservationStore`, `SqliteGraphStore` (~250 LOC graph wrapper using recursive CTEs), `LanceDbVectorStore`, `MigrationRunner`, schema definitions, FTS5 trigger management. | Core |
| **Capture** | Pipeline stages (`Normalizer`, `Enricher`, `Tagger`, `PrivacyFilter`, `Writer`). `AiSessionAdapter` (file watcher + hook-based capture). Pipeline orchestrator using `Channel<T>`. Thread boundary resolution. | Core, Storage, Llm |
| **Agents** | `BriefingAgent`, `DeadEndAgent`, `LinkerAgent`, `CompressionAgent`. `AgentScheduler` (cron, on-event, idle triggers). | Core, Storage, Llm |
| **Llm** | `OllamaClient`, `AnthropicClient`, `EmbeddingService` (Ollama + ONNX fallback), `LlmTaskQueue` (priority routing, persistence, cloud quota management). | Core |
| **Api** | `Program.cs` (host builder), endpoint groups, `IHostedService` registrations for daemon lifecycle, capture pipeline, and agent scheduler. Serves embedded React dashboard from `wwwroot/`. | Core, Storage, Capture, Agents, Llm |
| **Cli** | `System.CommandLine` argument parser, `HttpClient` wrapper, formatted console output. Uses shared models from Core for deserialization only. | Core (models only) |

## Data Flow

### How an observation flows from capture to storage to agents

```
1. AI Session Activity
   │
   ├─ File Watcher (monitors ~/.claude/projects/*/sessions/)
   │  └─ Parses new JSONL entries
   │
   └─ Hook POST (Claude Code PostToolUse → POST /api/v1/observations)

2. Capture Pipeline (Channel<T>-based, bounded capacity 100)
   │
   Normalizer ──> Enricher ──> Tagger ──> PrivacyFilter ──> Writer
   │               │            │          │                 │
   │               │            │          │                 ├─ SQLite INSERT
   │               │            │          │                 ├─ FTS5 index (auto via trigger)
   │               │            │          │                 ├─ LanceDB embedding queue
   │               │            │          │                 └─ Event Bus publish
   │               │            │          │
   │               │            │          └─ Redacts secrets, applies .devbrainignore
   │               │            └─ Local LLM classifies event type, generates summary
   │               └─ Adds project (git root), branch, file paths, thread assignment
   └─ Converts RawEvent to Observation schema, assigns ID and timestamp

3. Agent Scheduler (receives events from Event Bus)
   │
   ├─ LinkerAgent (OnEvent) ── extracts graph nodes/edges, links files/threads
   ├─ DeadEndAgent (OnEvent) ── detects abandoned approaches, writes DeadEnd records
   ├─ BriefingAgent (Cron 7am) ── generates morning briefing markdown
   └─ CompressionAgent (Idle 60min) ── summarizes old threads, archives them
```

The Writer deduplicates by content hash — both file watcher and hook-based capture can run simultaneously without producing duplicates.

## Storage

### SQLite Schema

Single database file at `~/.devbrain/devbrain.db`. WAL mode enabled for concurrent reads.

**Core tables:**

| Table | Purpose | Key columns |
|---|---|---|
| `observations` | Raw captured events (the atomic unit) | `id`, `session_id`, `thread_id`, `project`, `event_type`, `source`, `raw_content`, `summary`, `tags` |
| `threads` | Chains of related observations | `id`, `project`, `state` (Active/Paused/Archived), `title`, `summary` |
| `dead_ends` | Approaches that were tried and abandoned | `id`, `thread_id`, `project`, `approach`, `reason` |

**Graph tables (graph-as-relational-tables pattern):**

| Table | Purpose |
|---|---|
| `graph_nodes` | Entities: `File`, `Function`, `Decision`, `DeadEnd`, `Bug`, `Thread`, `Pattern`, `Person` |
| `graph_edges` | Relationships: `caused`, `fixed`, `relates_to`, `blocked_by`, `abandoned`, `references`, `preceded`, `succeeded`, `detected_pattern` |

Graph traversal uses recursive CTEs. At DevBrain's scale (tens of thousands of nodes), 2-4 hop traversals complete in single-digit milliseconds. No dedicated graph engine needed.

**Full-text search:**

`observations_fts` is an FTS5 virtual table over `summary`, `raw_content`, and `tags`. Sync triggers keep it in lockstep with the `observations` table on INSERT, UPDATE, and DELETE.

**Schema versioning:** The `_meta` table tracks the current schema version. `MigrationRunner` applies migrations on startup.

### Source of Truth and Recovery

```
SQLite (authoritative) ──rebuilds──> Graph tables (derived)
SQLite (authoritative) ──re-embeds──> LanceDB vectors (derived)
```

- LanceDB corrupt: `devbrain rebuild vectors`
- Graph tables corrupt: `devbrain rebuild graph`
- SQLite corrupt: WAL mode + `PRAGMA integrity_check` on startup + auto-backup before migrations

### File Layout on Disk

```
~/.devbrain/
├── devbrain.db              # SQLite (observations, threads, dead ends, graph, FTS)
├── vectors/                 # LanceDB embeddings
├── briefings/
│   ├── 2026-04-06.md
│   └── 2026-04-05.md
├── settings.toml            # User configuration
├── logs/
│   └── devbrain-2026-04-06.log
├── cache/
│   └── llm-tasks/
│       ├── pending/         # Persisted LLM task queue (survives restarts)
│       └── completed/       # Kept 24h for debugging
├── backups/                 # Auto-created before migrations
└── daemon.pid               # PID file for daemon state detection
```

## Key Design Decisions

### Why C# / .NET 9?

DevBrain is I/O-bound, not CPU-bound. The team has strong C# experience. .NET Native AOT produces single-binary distributions comparable to Rust/Go. ASP.NET Core Minimal APIs provide a lightweight, async HTTP host with excellent middleware support.

### Why SQLite (not Postgres)?

DevBrain is a local-first tool. SQLite requires zero setup, runs in-process, and handles DevBrain's scale (tens of thousands of observations) with ease. WAL mode gives concurrent read access. FTS5 provides full-text search without an external service.

### Why no CozoDB?

The PRD originally specified CozoDB for graph queries. Analysis showed that at DevBrain's scale, recursive CTEs over relational graph tables perform in single-digit milliseconds for 2-4 hop traversals. Eliminating CozoDB removes a dependency, a corruption surface, and a migration path — replaced by ~250 lines of C#.

### Why two binaries?

The CLI needs to feel instant. If it loaded storage, agents, and the full host pipeline, every command would have noticeable startup latency. Instead, the CLI is a thin HTTP client (~5-10 MB) and the daemon is the full host (~25-40 MB). CLI commands return in milliseconds.

### Why LanceDB for vectors?

Semantic search requires vector similarity. LanceDB is embedded (no server), stores on disk, and integrates with the local Ollama embeddings. If LanceDB data is lost, it can be rebuilt from SQLite — it's a derived index, not a source of truth.

## Directory Structure

```
DevBrain.slnx                    # Solution file
│
├── src/
│   ├── DevBrain.Core/           # Domain models, interfaces, enums
│   │   ├── Models/              #   Observation, Thread, DeadEnd, Decision, ...
│   │   ├── Enums/               #   EventType, CaptureSource, ThreadState, ...
│   │   └── Interfaces/          #   IObservationStore, IGraphStore, ...
│   │
│   ├── DevBrain.Storage/        # SQLite + Graph wrapper + LanceDB
│   │   ├── Sqlite/              #   SqliteObservationStore, SqliteGraphStore
│   │   ├── Vector/              #   LanceDbVectorStore
│   │   └── Migrations/          #   MigrationRunner, SQL migration scripts
│   │
│   ├── DevBrain.Capture/        # Capture pipeline
│   │   ├── Adapters/            #   AiSessionAdapter (file watcher + hook)
│   │   ├── Stages/              #   Normalizer, Enricher, Tagger, PrivacyFilter, Writer
│   │   └── Pipeline/            #   Orchestrator, Channel<T> wiring
│   │
│   ├── DevBrain.Agents/         # Intelligence agents
│   │   ├── Agents/              #   BriefingAgent, DeadEndAgent, LinkerAgent, CompressionAgent
│   │   └── Scheduling/          #   AgentScheduler, trigger types
│   │
│   ├── DevBrain.Llm/            # LLM integration
│   │   ├── Clients/             #   OllamaClient, AnthropicClient
│   │   ├── Embeddings/          #   EmbeddingService (Ollama + ONNX fallback)
│   │   └── Queue/               #   LlmTaskQueue (priority, routing, persistence)
│   │
│   ├── DevBrain.Api/            # Daemon host
│   │   ├── Program.cs           #   Host builder, service registration
│   │   ├── Endpoints/           #   Minimal API endpoint groups
│   │   └── wwwroot/             #   Embedded React dashboard (built from dashboard/)
│   │
│   └── DevBrain.Cli/            # CLI binary
│       ├── Program.cs           #   Root command, subcommand registration
│       └── Commands/            #   One file per command
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
│   ├── src/
│   ├── package.json
│   └── vite.config.ts
│
├── scripts/
│   ├── install.sh               # Linux/macOS installer
│   └── install.ps1              # Windows installer
│
└── docs/
    └── PRD.md                   # Product requirements document
```

## How to Add a New Agent

Intelligence agents run on schedules or in response to events. To add one:

1. **Create the agent class** in `src/DevBrain.Agents/Agents/`:

```csharp
public class MyAgent : IIntelligenceAgent
{
    public string Name => "my-agent";

    // Choose a schedule type:
    //   Cron("0 */6 * * *")   — every 6 hours
    //   OnEvent(EventType.Error) — when errors are captured
    //   Idle(TimeSpan.FromMinutes(30)) — after 30min of inactivity
    //   OnDemand — only via `devbrain agents run my-agent`
    public AgentSchedule Schedule => new AgentSchedule.OnEvent(EventType.Error);

    public Priority Priority => Priority.Normal;

    public async Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct)
    {
        // ctx gives you access to:
        //   ctx.Observations — query/write observations
        //   ctx.Graph — add/query graph nodes and edges
        //   ctx.Vectors — semantic search
        //   ctx.Llm — submit LLM tasks
        //   ctx.Settings — read configuration

        // Return a list of outputs (actions taken, things created)
        return [];
    }
}
```

2. **Register the agent** in `src/DevBrain.Api/Program.cs` by adding it to the DI container so the `AgentScheduler` discovers it.

3. **Add tests** in `tests/DevBrain.Agents.Tests/`. Mock `AgentContext` dependencies to test agent logic in isolation.

4. **Verify** by running `devbrain agents` — your agent should appear in the list. Trigger it manually with `devbrain agents run my-agent`.

## How to Add a New CLI Command

CLI commands are thin HTTP calls to the daemon API.

1. **Create the command class** in `src/DevBrain.Cli/Commands/`:

```csharp
public class MyCommand : Command
{
    public MyCommand() : base("my-command", "One-line description of what it does")
    {
        var someArg = new Argument<string>("name")
        {
            Description = "What this argument is"
        };
        Add(someArg);

        this.SetHandler(async (string name) =>
        {
            using var client = new HttpClient { BaseAddress = new Uri("http://localhost:37800") };
            var response = await client.GetAsync($"/api/v1/my-endpoint?name={name}");
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine(body);
        }, someArg);
    }
}
```

2. **Register it** in `src/DevBrain.Cli/Program.cs`:

```csharp
root.Add(new MyCommand());
```

3. **Ensure the API endpoint exists** — the CLI is just an HTTP client, so the corresponding endpoint in `DevBrain.Api` must be implemented first (or simultaneously).

## How to Add a New API Endpoint

API endpoints are defined as ASP.NET Core Minimal APIs in the `DevBrain.Api` project.

1. **Create or extend an endpoint group** in `src/DevBrain.Api/Endpoints/`:

```csharp
public static class MyEndpoints
{
    public static void MapMyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/my-resource");

        group.MapGet("/", async (IObservationStore store) =>
        {
            var items = await store.GetAll();
            return Results.Ok(items);
        });

        group.MapGet("/{id}", async (string id, IObservationStore store) =>
        {
            var item = await store.GetById(id);
            return item is not null ? Results.Ok(item) : Results.NotFound();
        });

        group.MapPost("/", async (MyRequest request, IObservationStore store) =>
        {
            // Process and store
            return Results.Accepted();
        });
    }
}
```

2. **Register the endpoint group** in `src/DevBrain.Api/Program.cs`:

```csharp
app.MapMyEndpoints();
```

3. **Add integration tests** in `tests/DevBrain.Integration.Tests/` using `WebApplicationFactory<Program>` to test the full HTTP pipeline.

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests for a specific project
dotnet test tests/DevBrain.Core.Tests
dotnet test tests/DevBrain.Storage.Tests

# Run with verbose output
dotnet test --verbosity normal

# Run a specific test by name
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTestMethod"
```

### Where Tests Live

| Test project | What it covers |
|---|---|
| `DevBrain.Core.Tests` | Domain model validation, enum behavior |
| `DevBrain.Storage.Tests` | SQLite store operations, graph wrapper CTEs, FTS5 queries, migrations |
| `DevBrain.Capture.Tests` | Pipeline stage logic (normalizer, enricher, tagger, privacy filter) |
| `DevBrain.Agents.Tests` | Agent logic with mocked dependencies |
| `DevBrain.Llm.Tests` | LLM client behavior, task queue routing, embedding service |
| `DevBrain.Integration.Tests` | Full HTTP API tests using `WebApplicationFactory`, end-to-end data flow |

### Conventions

- **Unit tests** use in-memory SQLite databases (`:memory:`) to avoid filesystem side effects.
- **Integration tests** use `WebApplicationFactory<Program>` with a temporary data directory.
- **Agent tests** mock `AgentContext` — inject fake stores and a fake `ILlmService` to test logic without real LLM calls.
- **Privacy filter tests** include known secret patterns (test-only) to verify redaction works correctly.
- Test files follow the pattern `{ClassName}Tests.cs` in the corresponding test project.
