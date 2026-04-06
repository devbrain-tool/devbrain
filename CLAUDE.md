# CLAUDE.md

This file provides context for AI assistants working on the DevBrain codebase.

## Project Overview

DevBrain is a developer's second brain — a background daemon that passively captures AI coding sessions, builds a knowledge graph of decisions and dead ends, and surfaces proactive insights (morning briefings, "you tried this before" warnings, semantic search).

**Tech stack:** C# / .NET 9, ASP.NET Core Minimal APIs, SQLite, LanceDB, React + TypeScript (Vite), xUnit.

**License:** Apache 2.0

## Repository Structure

```
DevBrain.slnx                    # Solution file (use this, not .sln)
src/
  DevBrain.Core/                 # Domain models, interfaces, enums — no dependencies
  DevBrain.Storage/              # SQLite observation store, graph store (CTE traversal), LanceDB
  DevBrain.Llm/                  # Ollama + Anthropic clients, LlmTaskQueue (implements ILlmService)
  DevBrain.Capture/              # 5-stage Channel<T> pipeline, privacy filters, thread resolver
  DevBrain.Agents/               # 4 intelligence agents + AgentScheduler (BackgroundService)
  DevBrain.Api/                  # ASP.NET Core daemon — Program.cs, endpoint groups, DI wiring
  DevBrain.Cli/                  # System.CommandLine CLI — thin HTTP client, 18 commands
tests/
  DevBrain.{Project}.Tests/      # Unit tests per project
  DevBrain.Integration.Tests/    # End-to-end pipeline tests
dashboard/                       # React + TypeScript SPA (Vite), 7 pages
```

## Build & Test Commands

```bash
dotnet build DevBrain.slnx              # Build everything
dotnet test DevBrain.slnx               # Run all 54 tests
cd dashboard && npm ci && npm run build  # Build dashboard
dotnet run --project src/DevBrain.Api/   # Run daemon (localhost:37800)
dotnet run --project src/DevBrain.Cli/ -- status  # Run CLI
```

## Architecture Rules

**Dependency direction is strictly enforced by project references:**
- Core has ZERO dependencies
- Storage depends on Core only
- Llm depends on Core only
- Capture depends on Core, Storage, Llm
- Agents depends on Core, Storage, Llm
- Api depends on everything (it's the composition root)
- Cli depends on Core only (talks to daemon via HTTP)

**Never add upward dependencies.** Storage must not reference Api. Agents must not reference Capture. If you need shared types, put them in Core.

## Key Design Decisions

1. **Two binaries** — daemon (`devbrain-daemon`) and CLI (`devbrain`). CLI is a thin HTTP client. Never put business logic in the CLI.
2. **SQLite is the single source of truth** — graph tables and FTS5 live in the same database. LanceDB vectors are derived and rebuildable.
3. **Graph-as-tables** — no separate graph DB. `graph_nodes` + `graph_edges` tables with bidirectional recursive CTEs for traversal. The `SqliteGraphStore` is ~250 lines.
4. **Channel\<T\> pipeline** — capture pipeline uses bounded channels (capacity 100) with backpressure. Stage order: Normalizer -> Enricher -> Tagger -> PrivacyFilter -> Writer.
5. **LlmTaskQueue implements ILlmService** — single entry point for all LLM operations. Routes between local (Ollama) and cloud (Anthropic) based on LlmPreference.
6. **Agents are stateless** — all state is in the stores. Agents receive AgentContext and can be retried safely.
7. **Privacy filter runs AFTER tagger** — so the LLM sees original content for classification, but secrets are redacted before storage.

## Coding Conventions

- **Records** for immutable domain models (`record Observation { ... }`)
- **Classes** for mutable state (`class Settings { get; set; }`)
- **Async/await** everywhere — never block on async. Use `*Async()` methods for SQLite.
- **Nullable reference types** enabled — handle nulls explicitly
- **File-scoped namespaces** — `namespace X;` not `namespace X { }`
- **IReadOnlyList\<T\>** for return types, not `List<T>`
- Use `System.Text.Json` for JSON serialization
- DateTime stored as ISO 8601 strings in SQLite, parsed with `CultureInfo.InvariantCulture`
- Tags and FilesInvolved stored as JSON arrays in SQLite TEXT columns

## Commit Conventions

Follow [Conventional Commits](https://www.conventionalcommits.org/):
```
feat: add pattern detection agent
fix: prevent secret leakage in thread summaries
docs: update CLI command reference
test: add integration test for pipeline redaction
refactor: extract graph traversal into helper
```

## Testing Patterns

- Storage tests use in-memory SQLite: `new SqliteConnection("Data Source=:memory:")`
- Always call `SchemaManager.Initialize(connection)` before using stores
- LLM tests use function delegates (no mocks needed — `LlmTaskQueue` takes `Func<>` parameters)
- Pipeline tests create the full orchestrator and send `RawEvent`s through channels
- No test should require Ollama, Anthropic, or any external service

## Common Tasks

### Adding a new intelligence agent

1. Create `src/DevBrain.Agents/MyAgent.cs` implementing `IIntelligenceAgent`
2. Set `Name`, `Schedule` (Cron/OnEvent/Idle), `Priority`
3. Implement `Run(AgentContext ctx, CancellationToken ct)`
4. Register in `src/DevBrain.Api/Program.cs` DI: `builder.Services.AddSingleton<IIntelligenceAgent, MyAgent>()`
5. Add tests in `tests/DevBrain.Agents.Tests/`

### Adding a new API endpoint

1. Create `src/DevBrain.Api/Endpoints/MyEndpoints.cs` with a static `MapMyEndpoints` extension method
2. Register in `Program.cs`: `app.MapGroup("/api/v1/my-thing").MapMyEndpoints()`
3. Inject services via endpoint handler parameters (DI resolves them)

### Adding a new CLI command

1. Create `src/DevBrain.Cli/Commands/MyCommand.cs` extending `Command`
2. Make HTTP calls via `DevBrainHttpClient`
3. Format output via `ConsoleFormatter`
4. Register in `src/DevBrain.Cli/Program.cs`: `root.AddCommand(new MyCommand())`

## Important Files

| File | Why it matters |
|---|---|
| `src/DevBrain.Api/Program.cs` | Composition root — all DI wiring, pipeline startup, shutdown |
| `src/DevBrain.Storage/Schema/SchemaManager.cs` | All SQLite table definitions, FTS5 triggers, indexes |
| `src/DevBrain.Storage/SqliteGraphStore.cs` | Graph traversal CTEs — the core of the knowledge graph |
| `src/DevBrain.Llm/LlmTaskQueue.cs` | LLM routing logic — implements ILlmService |
| `src/DevBrain.Capture/Pipeline/PipelineOrchestrator.cs` | Pipeline wiring — stage order matters |
| `src/DevBrain.Core/Models/Settings.cs` | All configuration options |
| `src/DevBrain.Core/Interfaces/` | All contracts — read these first to understand the system |

## Known Limitations (v1)

- **NullVectorStore** — LanceDB not yet wired. Vector search degrades to FTS5.
- **CompressionAgent** does retroactive enrichment, not thread-level compression as spec describes.
- **Settings PUT endpoint** is a no-op — accepts but doesn't persist changes.
- **LLM daily counter** resets at midnight UTC but has no persistence across daemon restarts.
- **No ICaptureAdapter interface** in Core — adapter contract is defined inline in the Capture project.

## Security Notes

- Daemon binds to `127.0.0.1` ONLY — never `0.0.0.0`
- No authentication on the API — security is via localhost-only binding
- Secrets are redacted by `PrivacyFilter` BEFORE writing to SQLite — originals never stored
- API keys loaded from environment variables, never from config files
- Pre-commit hook in `scripts/pre-commit` scans for secrets locally
- GitHub Secret Scanning + Push Protection enabled on the repo
