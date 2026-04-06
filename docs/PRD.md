# DevBrain — Product Requirements Document

**Version:** 1.0
**Date:** 2026-04-05
**Status:** Draft

---

## 1. Product Vision

**DevBrain** is a developer's second brain that passively captures what you do, understands *why* you did it, and proactively tells you what you need to know.

It runs entirely on your machine as a background daemon, captures AI-assisted coding sessions, builds a knowledge graph of your decisions, dead ends, and patterns, and surfaces proactive insights — morning briefings, "you tried this before" warnings, and recurring bug pattern detection.

### Target User

- **v1:** Solo developers and freelancers working across multiple projects
- **Architecture designed for:** Small teams (v3), engineering orgs (v4)

### Core Value Proposition

Developers lose massive amounts of context constantly — switching tasks, coming back after a weekend, debugging something they fixed months ago. DevBrain eliminates this by:

1. Passively capturing developer activity (zero friction)
2. Building a knowledge graph of decisions, dead ends, and patterns
3. Proactively surfacing relevant context before you need to search for it

---

## 2. Core Concepts

| Concept | Definition |
|---|---|
| **Observation** | A raw captured event — AI tool call, file change, decision, error. The atomic unit of DevBrain. |
| **Decision** | An observation enriched with reasoning — "chose approach A because X, rejected B because Y" |
| **Thread** | A chain of related observations forming a narrative — debugging session, feature build, investigation |
| **Dead End** | An approach that was attempted and abandoned, with the reason why |
| **Pattern** | A recurring behavior detected across threads — same bug class, repeated refactoring, etc. |
| **Briefing** | A proactive summary generated for the developer — morning context, "you left off here," warnings |

---

## 3. User Journey

### Morning
Open terminal, `devbrain briefing` shows:
- What you were working on yesterday
- PRs merged overnight that touch your files (v2, requires Git Adapter)
- Dead end reminders ("don't retry the Redis caching approach on the auth service")
- Detected patterns ("3rd timeout bug in API handlers this month")

### During Work
DevBrain silently captures AI sessions. When you open a file in your IDE, the extension sidebar shows:
- Recent decisions about this file
- Related dead ends
- Who else touched it (future, team sync)

### Stuck Moment
`devbrain search "why does the webhook timeout"` returns your own past investigation + the approach that worked.

### End of Day
DevBrain's agents run: compress observations, detect patterns, update the knowledge graph, prepare tomorrow's briefing.

---

## 4. System Architecture

### High-Level Architecture

```
+--------------------------------------------------------------+
|                    Developer Touchpoints                      |
|  CLI (devbrain)  |  VS Code Extension  |  Web Dashboard      |
+--------+---------+-----------+----------+---------+-----------+
         |                     |                    |
         v                     v                    v
+--------------------------------------------------------------+
|                      HTTP API (REST)                         |
|  /observations  /search  /briefings  /graph  /settings       |
+--------------------------------------------------------------+
|                                                              |
|  +-------------+  +--------------+  +----------------+       |
|  |   Capture   |  |   Storage    |  |  Graph Engine  |       |
|  |   Engine    |  |   Layer      |  |  (CozoDB)      |       |
|  |             |  |              |  |                |       |
|  | - AI hooks  |  | - SQLite     |  | - Entities     |       |
|  | - Adapters  |  | - LanceDB   |  | - Relations    |       |
|  | - Pipeline  |  | - CozoDB    |  | - Traversal    |       |
|  +------+------+  +------+------+  +--------+-------+       |
|         |                |                   |               |
|         +----------------+-------------------+               |
|                                                              |
|              CORE DAEMON (single Rust process)               |
+--------------------------------------------------------------+
|                    Agent Scheduler                            |
|  +-----------+ +-----------+ +----------+ +--------------+   |
|  | Briefing  | | Dead End  | | Linker   | | Compression  |   |
|  | Agent     | | Agent     | | Agent    | | Agent        |   |
|  | (daily)   | | (on-event)| | (on-evt) | | (idle 60min) |   |
|  +-----+-----+ +-----+-----+ +----+-----+ +------+------+   |
|        +---------------+-----------+--------------+          |
|                         |                                    |
|                   LLM Task Queue                             |
|          +--------------+--------------+                     |
|          | Local (Ollama) | Cloud (API) |                    |
|          +----------------+-------------+                    |
+--------------------------------------------------------------+
         |
         v
+----------------------------+
|   ~/.devbrain/             |
|   +-- devbrain.db          |  <- SQLite (structured data)
|   +-- vectors/             |  <- LanceDB (embeddings)
|   +-- graph/               |  <- CozoDB (knowledge graph)
|   +-- briefings/           |  <- Generated briefings
|   +-- settings.toml        |  <- Configuration
|   +-- logs/                |  <- Daemon logs
+----------------------------+
```

### Architecture: Core + Agent Model

The daemon is a **single Rust process** using async tasks (tokio). The core handles capture, storage, and API. Intelligence agents run as scheduled async tasks with read/write access to storage.

**Key design decisions:**

1. **Single process** — core + agents in one binary. No IPC complexity.
2. **Event-driven capture** — observations flow through: `Raw Event -> Normalize -> Enrich -> Store -> Notify Agents`
3. **LLM Task Queue** — agents submit inference tasks to a queue. Queue routes: lightweight tasks -> local Ollama, heavy tasks -> cloud API.
4. **CozoDB for graph** — embedded, file-backed, Rust-native. Datalog queries for traversal. No in-memory scaling concerns.

---

## 5. Tech Stack

| Layer | Technology | Rationale |
|---|---|---|
| **Core Daemon** | Rust + Tokio | Performance, single binary, low memory footprint |
| **HTTP API** | Axum (Rust) | Async, lightweight, idiomatic Rust |
| **Structured Storage** | SQLite | Zero-dependency, file-based, battle-tested |
| **Vector Search** | LanceDB | Rust-native, file-based, no external process |
| **Knowledge Graph** | CozoDB | Embedded, Datalog-based, purpose-built for knowledge graphs |
| **Web Dashboard** | React + TypeScript | Embedded as static assets in the binary |
| **IDE Extensions** | TypeScript | VS Code / Cursor extension API |
| **Local LLM** | Ollama / llama.cpp | Privacy-first, works offline |
| **Cloud LLM** | Anthropic API | Briefings and pattern analysis |
| **Build** | Cargo + cross | Cross-platform binary compilation |

---

## 6. Capture Engine

### Pipeline

```
Raw Event (AI session hook)
    |
    v
+---------------+
|  Normalizer   |  -> Uniform schema regardless of source
+---------------+
|  Enricher     |  -> Adds metadata: project, branch, file paths, timestamps
+---------------+
|  Tagger       |  -> Local LLM classifies: decision / exploration / dead-end / fix / refactor
+---------------+
|  Privacy      |  -> Strips <private> tags, redacts secrets (.env patterns, API keys)
|  Filter       |
+---------------+
|  Writer       |  -> Writes to SQLite + queues embedding for LanceDB
|               |  -> Emits event to Agent Scheduler
+---------------+
```

### Observation Schema

```rust
struct Observation {
    id: Uuid,
    session_id: Uuid,
    timestamp: DateTime<Utc>,
    project: String,          // git repo root or project name
    branch: String,

    // What happened
    event_type: EventType,    // ToolCall, FileChange, Decision, Error, Conversation
    source: CaptureSource,    // ClaudeCode, Cursor, VSCode, Git (future)
    raw_content: String,      // Original event data

    // Enriched by pipeline
    summary: String,          // LLM-compressed one-liner
    tags: Vec<String>,        // auto-classified: ["decision", "auth", "debugging"]
    files_involved: Vec<PathBuf>,

    // Relationships (fed to CozoDB)
    parent_id: Option<Uuid>,  // Previous observation in the thread
    thread_id: Option<Uuid>,  // Groups observations into a narrative
}
```

### Adapter Interface

```rust
trait CaptureAdapter {
    fn name(&self) -> &str;
    fn setup(&self, config: &Settings) -> Result<()>;
    fn capture(&self) -> mpsc::Receiver<RawEvent>;
    fn health(&self) -> AdapterHealth;
}
```

v1 ships with the **AI Session Adapter** (Claude Code + Cursor). Future adapters: Git, IDE, Slack.

### Capture Rules

| Event | Captured | Why |
|---|---|---|
| Tool calls (Read, Edit, Bash, etc.) | Yes | Core signal — what actions were taken |
| Tool results | Summarized only | Full results too large, summary preserves intent |
| User prompts | Yes | Captures intent and reasoning |
| AI responses | Key decisions only | Full responses are noise, decisions are signal |
| Errors/failures | Yes, with context | Dead end detection depends on this |
| File diffs | Summary + paths | Full diffs in git, we store "what changed and why" |

---

## 7. Intelligence Agents

### Agent Framework

```rust
trait IntelligenceAgent {
    fn name(&self) -> &str;
    fn schedule(&self) -> AgentSchedule;
    fn priority(&self) -> Priority;
    fn run(&self, ctx: &AgentContext) -> Result<Vec<AgentOutput>>;
}

enum AgentSchedule {
    Cron(String),                    // "0 7 * * *" (daily at 7am)
    OnEvent(Vec<EventType>),         // Triggers on specific captures
    OnDemand,                        // User-initiated only
    Idle(Duration),                  // Runs when machine idle for N minutes
}
```

### v1 Agents

#### Briefing Agent
- **Schedule:** Daily at configured time (default 7am) + on-demand
- **LLM:** Cloud (needs strong reasoning for synthesis)
- **Input:** Last session's observations, pending threads (+ overnight git changes when Git Adapter ships in v2)
- **Output:** Markdown briefing stored in `~/.devbrain/briefings/`

**Briefing structure (v1 — AI session data only):**
```markdown
## Morning Briefing — April 6, 2026

### Where You Left Off
You were debugging the webhook timeout in `src/api/webhooks.rs`.
You narrowed it to the retry logic — the backoff multiplier overflows
after 8 retries. You hadn't started the fix yet.

### Open Threads
- Auth token refresh (started April 3, paused) — 12 observations
- Webhook timeout investigation (started April 5, active) — 8 observations

### Watch Out
- You tried connection pooling for this service on March 20 and
  abandoned it — the pool manager deadlocked under concurrent requests.
  Don't retry without addressing the lock ordering issue.
```

**Briefing structure (v2 — with Git Adapter + Pattern Agent):**
```markdown
## Morning Briefing — April 6, 2026

### Where You Left Off
[same as v1]

### Overnight Changes
- PR #142 merged (Sarah) — refactored the HTTP client you depend on.
  `HttpClient::send()` now returns `Result<Response, ClientError>`
  instead of `Option<Response>`. Your webhook handler will need updating.

### Watch Out
[same as v1]

### Patterns Detected
- This is the 3rd timeout bug in API handlers this month. All were
  caused by missing backoff caps. Consider a shared retry utility.
```

#### Dead End Agent
- **Schedule:** `OnEvent(Error, Conversation)`
- **LLM:** Local (classification task)
- **Input:** Thread where an approach was tried then abandoned
- **Output:** Dead End record in CozoDB, linked to files/concepts

**Detection heuristics:**
- Sequence of edits -> error -> reverts
- User explicitly says "that didn't work" / "let's try something else"
- Branch abandoned without merge
- Same file edited 3+ times in a thread without resolution

#### Linker Agent
- **Schedule:** `OnEvent(*)` — runs on every new observation
- **LLM:** Local (lightweight relationship extraction), with rule-based fast path
- **Input:** New observation + nearby observations in time/file space
- **Output:** Edges in CozoDB

**Performance strategy:** Most relationships (file references, thread ordering, temporal proximity) are extractable via rules — no LLM needed. The Linker uses a two-tier approach:
1. **Rule-based fast path** (~90% of cases): regex/AST extraction for file paths, function names, explicit references. Runs in <1ms per observation.
2. **LLM slow path** (~10% of cases): only for ambiguous semantic relationships (e.g., "this decision relates to that earlier discussion"). Batched — accumulates up to 10 observations before submitting one LLM call.

**Relationship types:**
```
Entity Types: File, Function, Decision, DeadEnd, Bug, Thread, Pattern, Person
Edge Types:   caused, fixed, relates_to, blocked_by, abandoned,
              references, preceded, succeeded, detected_pattern
```

#### Compression Agent
- **Schedule:** `Idle(60min)`
- **LLM:** Local (summarization)
- **Input:** Observations older than 7 days not yet compressed
- **Output:** Compressed summaries augmenting raw content (originals preserved)

#### Pattern Agent (v2)
- **Schedule:** `Idle(30min)`
- **LLM:** Cloud (needs reasoning across many observations)
- **Input:** All observations from last 30 days
- **Output:** Pattern records in CozoDB + notifications

**Pattern types:** Bug clusters, hotspot files, recurring debugging, knowledge gaps

### Agent Coordination

```
+-------------------------------------+
|          Agent Scheduler            |
|                                     |
|  Event Bus <-- Capture Engine       |
|      |                              |
|      +-- OnEvent agents (immediate) |
|      |   +-- Linker Agent           |
|      |   +-- Dead End Agent         |
|      |                              |
|  Cron -- Briefing Agent (daily)     |
|                                     |
|  Idle Timer                         |
|      +-- Pattern Agent (30min idle) |
|      +-- Compression Agent (60min)  |
|                                     |
|  All agents --> LLM Task Queue      |
|                 +-- Local queue      |
|                 +-- Cloud queue      |
+-------------------------------------+
```

Agents never block each other. They submit LLM tasks to the queue and get results asynchronously. Priority: `OnEvent > Cron > Idle`.

---

## 8. API, CLI & Developer Touchpoints

### HTTP API

Local REST API on configurable port (default `37800`):

```
POST   /api/v1/observations          # Capture adapters push events
GET    /api/v1/observations           # Query with filters
GET    /api/v1/observations/:id       # Single observation with context

GET    /api/v1/search?q=              # Semantic search (LanceDB)
GET    /api/v1/search/exact?q=        # Exact text search (SQLite FTS)

GET    /api/v1/briefings              # List briefings
GET    /api/v1/briefings/latest       # Today's briefing
POST   /api/v1/briefings/generate     # Force-generate

GET    /api/v1/graph/node/:id         # Entity + all edges
GET    /api/v1/graph/traverse         # N-hop traversal
GET    /api/v1/graph/file/:path       # Everything related to a file

GET    /api/v1/context/file/:path     # Aggregated view: decisions + dead ends + patterns for a file

GET    /api/v1/patterns               # Detected patterns
GET    /api/v1/dead-ends              # All dead ends, filterable
GET    /api/v1/threads                # Observation threads
GET    /api/v1/threads/:id            # Full thread narrative

GET    /api/v1/health                 # Daemon status
GET    /api/v1/settings               # Current settings
PUT    /api/v1/settings               # Update settings

# Future (team sync)
POST   /api/v1/sync/push              # Push to team server
POST   /api/v1/sync/pull              # Pull team updates
```

### CLI Commands

```bash
# Core
devbrain start                         # Start daemon
devbrain stop                          # Stop daemon
devbrain status                        # Health, storage stats, agent status

# Daily workflow
devbrain briefing                      # Show morning briefing
devbrain briefing --generate           # Force regenerate
devbrain search "webhook timeout"      # Semantic search
devbrain search --exact "HttpClient::send"  # Exact text search

# Context on demand
devbrain why src/api/webhooks.rs       # Decisions, dead ends, patterns for a file
devbrain thread                        # Current active thread
devbrain thread list                   # All recent threads
devbrain dead-ends                     # List dead ends for current project
devbrain dead-ends --file src/auth.rs  # Dead ends for specific file

# Graph exploration
devbrain related src/api/webhooks.rs   # Everything connected to this file
devbrain trace <decision-id>           # Trace a decision to consequences

# Management
devbrain agents                        # List agents, status, last run
devbrain agents run briefing           # Manually trigger an agent
devbrain config                        # Show settings
devbrain config set llm.local.model "llama3.2"
devbrain export --format json          # Export all data
devbrain purge --project <name>        # Delete all data for a project
devbrain purge --before 2026-01-01     # Delete data older than date
devbrain dashboard                     # Open web dashboard in browser
```

### IDE Extension (VS Code / Cursor)

Thin client — all intelligence lives in the daemon. Extension talks to HTTP API.

| Feature | UX |
|---|---|
| **File context sidebar** | Open a file -> sidebar shows decisions, dead ends, patterns |
| **Inline annotations** | Hover over function -> tooltip with last decision/change reasoning |
| **Briefing notification** | On IDE open -> "Morning briefing ready" notification |
| **Dead end warning** | Editing a file with known dead ends -> subtle warning |
| **Search palette** | `Cmd+Shift+B` -> DevBrain search |
| **Status bar** | Daemon status, current thread, capture indicator |

### Web Dashboard

React SPA served at `http://localhost:37800`, embedded in binary.

**Pages:** Timeline, Graph Explorer (v2), Briefings, Patterns (v2), Dead Ends, Settings, Health

---

## 9. Configuration

### `~/.devbrain/settings.toml`

```toml
[daemon]
port = 37800
log_level = "info"
auto_start = true

[capture]
enabled = true
sources = ["ai-sessions"]
privacy_mode = "redact"             # "redact" or "strict"
ignored_projects = []
max_observation_size_kb = 512

[storage]
path = "~/.devbrain"
sqlite_max_size_mb = 2048
vector_dimensions = 384
compression_after_days = 7
retention_days = 365                # 0 = forever

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

[agents.pattern]
enabled = true
idle_minutes = 30
lookback_days = 30

[agents.linker]
enabled = true

[agents.compression]
enabled = true
idle_minutes = 60

[sync]
enabled = false
server = ""
share_scope = "decisions"
```

### Key Configuration Decisions

1. **API key from env var** — `api_key_env` points to env var name, never the key itself
2. **Cost control** — `max_daily_requests` caps cloud LLM usage
3. **Per-agent toggle** — every agent individually controllable
4. **Privacy first** — `redact` mode strips secrets automatically
5. **Works out of the box** — only needs Ollama installed, cloud is optional

---

## 10. Privacy & Security

### Threat Model

| Threat | Mitigation |
|---|---|
| **Local data theft** | `700` file permissions. Optional encryption at rest (AES-256). |
| **API key leakage** | Keys from env vars only. Capture pipeline auto-redacts key patterns. |
| **Cloud LLM data exposure** | Only summaries sent to cloud, never raw code. User controls via `[llm.cloud].tasks`. |
| **Accidental secret capture** | Regex-based secret detection in pipeline. Strips before storage. |
| **Team sync leaks** | `share_scope` controls what syncs. Default: `decisions` only. |

### Privacy Layers

```
Layer 1: User-controlled
  +-- <private>content</private> tags — stripped at capture, never stored

Layer 2: Automatic redaction
  +-- Secret patterns (API keys, tokens, passwords)
  +-- .env file contents
  +-- Paths matching .gitignore patterns

Layer 3: Capture exclusions
  +-- ignored_projects setting
  +-- .devbrainignore file (per-project, gitignore syntax)

Layer 4: Cloud boundary
  +-- Raw code NEVER sent to cloud LLM
  +-- Only compressed summaries go to cloud
  +-- Local LLM handles all code-touching tasks

Layer 5: Storage controls
  +-- retention_days auto-deletes old data
  +-- devbrain export/purge for manual control
  +-- Optional encryption at rest
```

### `.devbrainignore` (per-project)

```gitignore
.env*
secrets/
**/credentials*
internal-docs/
```

### Data Ownership

- All data lives on your machine
- `devbrain export --format json` — full data portability
- `devbrain purge --project <name>` — delete all project data
- `devbrain purge --before 2026-01-01` — delete by date
- Team sync (future) is opt-in and scoped

---

## 11. Thread Lifecycle

A **Thread** groups related observations into a narrative. Thread boundaries are critical for agents (Dead End detection, Briefing context).

### Thread Creation

A new thread starts when:
- A new AI coding session begins (new `session_id` from the capture adapter)
- The user explicitly switches context: different project, different branch, or >2 hour gap between observations
- The user manually starts one: `devbrain thread new "investigating webhook timeout"`

### Thread Boundaries

| Signal | Behavior |
|---|---|
| New AI session | New thread (sessions are natural boundaries) |
| Same session, same project/branch | Same thread |
| Same session, different project | New thread |
| Same session, branch switch | New thread |
| Gap > 2 hours within session | New thread (configurable via `settings.toml`) |
| User runs `devbrain thread new` | New thread |

### Thread States

- **Active** — currently receiving observations
- **Paused** — no observations for >2 hours, can resume if same context returns
- **Closed** — session ended or user manually closed it
- **Archived** — compressed by Compression Agent after `compression_after_days`

### Thread Metadata

```rust
struct Thread {
    id: Uuid,
    project: String,
    branch: String,
    title: Option<String>,       // Auto-generated or user-provided
    state: ThreadState,
    started_at: DateTime<Utc>,
    last_activity: DateTime<Utc>,
    observation_count: u32,
    summary: Option<String>,     // Generated by Compression Agent
}
```

---

## 12. Failure Modes & Graceful Degradation

| Failure | Behavior |
|---|---|
| **Ollama not installed / not running** | Capture still works — observations stored raw without tags/summaries. Tagger and Compression agents queue tasks and retry when Ollama becomes available. CLI shows warning: "Local LLM unavailable — observations captured but not enriched." |
| **Cloud LLM unavailable** | Briefing Agent skips generation, logs warning. Next successful run covers the gap. Dead End and Linker agents unaffected (local LLM only). |
| **Neither LLM available** | Pure capture mode — raw observations stored in SQLite, no enrichment, no agents run. All processing happens retroactively when an LLM becomes available. |
| **Disk full** | Daemon detects <100MB free, pauses capture, emits CLI warning. Existing data remains accessible. Resumes when space freed. |
| **SQLite corruption** | On startup, run `PRAGMA integrity_check`. If corrupt, attempt `PRAGMA recover` into a new file. If unrecoverable, start fresh DB, preserve LanceDB/CozoDB data. Log incident. |
| **CozoDB corruption** | Graph is rebuildable from SQLite observation data. On corruption detection, delete graph files and trigger a full rebuild from SQLite records. |
| **LanceDB corruption** | Re-embed all observations from SQLite. Slower but fully recoverable since SQLite is the source of truth. |
| **Daemon crash mid-write** | SQLite WAL mode ensures atomic writes. Incomplete observations are rolled back on restart. LLM task queue persists to disk — incomplete tasks re-queued on startup. |
| **Capture adapter disconnects** | Adapter health check runs every 30 seconds. On failure, attempt reconnect with exponential backoff. Observations during disconnect are lost (acceptable — AI sessions are ephemeral). |

**Source of truth hierarchy:** SQLite > CozoDB (rebuildable) > LanceDB (rebuildable) > Briefings (regenerable)

---

## 13. Embedding Model & Vector Search

### Embedding Strategy

Semantic search requires converting text to vectors. Two options for generating embeddings:

**v1 approach: Ollama embedding model**
- Use Ollama's `nomic-embed-text` model (384 dimensions, fast, good quality)
- Same dependency as the Tagger — no additional install
- Runs locally, privacy-preserving

**Fallback: Built-in ONNX model**
- Bundle a small ONNX embedding model (e.g., `all-MiniLM-L6-v2`) in the binary via `ort` (ONNX Runtime for Rust)
- ~30MB addition to binary size but eliminates Ollama dependency for search
- Used automatically when Ollama is unavailable

### What Gets Embedded

| Data | Embedded | Stored In |
|---|---|---|
| Observation summaries | Yes | LanceDB |
| Decision reasoning | Yes | LanceDB |
| Dead end descriptions | Yes | LanceDB |
| Thread titles + summaries | Yes | LanceDB |
| Raw observation content | No (too noisy) | SQLite only |
| Briefings | No (derived content) | Filesystem only |

### Search Pipeline

```
User query ("webhook timeout")
    |
    v
Embed query -> LanceDB vector search (top 20 candidates)
    |
    v
SQLite FTS rerank (boost exact matches)
    |
    v
CozoDB expansion (add related entities within 1 hop)
    |
    v
Return ranked results with context
```

---

## 14. Migration & Upgrade Strategy

### Schema Versioning

Each storage backend maintains a version number:
- **SQLite:** `schema_version` in a `_meta` table
- **CozoDB:** `graph_version` in a metadata relation
- **LanceDB:** version file in `~/.devbrain/vectors/.version`

### Migration Process

On daemon startup:
1. Read current schema versions from all three stores
2. Compare against expected versions for this binary
3. If mismatch, run migrations sequentially (never skip versions)
4. **Before any migration:** auto-backup the affected files to `~/.devbrain/backups/<timestamp>/`
5. If migration fails, restore from backup and refuse to start (log error with instructions)

### Migration Rules

- Migrations are forward-only (no downgrade support)
- Each migration is a standalone Rust function: `fn migrate_sqlite_v3_to_v4(db: &Connection) -> Result<()>`
- SQLite migrations run in a transaction — atomic success or full rollback
- CozoDB migrations may require a full graph rebuild from SQLite (acceptable — graph is derived data)
- LanceDB migrations may require re-embedding (slow but automated)

### Breaking Changes

For major version bumps that fundamentally change data format:
- `devbrain export --format json` before upgrade (documented in release notes)
- `devbrain import --format json` after upgrade
- Export/import as the universal migration escape hatch

---

## 15. Logging & Observability

### Log Strategy

| Component | Log Level | Destination |
|---|---|---|
| Daemon lifecycle (start/stop) | INFO | Log file + stderr |
| API requests | DEBUG | Log file only |
| Capture pipeline events | DEBUG | Log file only |
| Agent runs (start/complete/error) | INFO | Log file + stderr |
| LLM task queue (submit/complete) | DEBUG | Log file only |
| Storage operations | DEBUG | Log file only |
| Errors (any component) | ERROR | Log file + stderr |
| Privacy redactions | WARN | Log file only (log that redaction happened, never the content) |

### Log Files

- Location: `~/.devbrain/logs/devbrain-YYYY-MM-DD.log`
- Format: structured JSON lines (machine-parseable)
- Rotation: daily, retain 14 days, configurable via `settings.toml`
- Max size: 50MB per file, rotate early if exceeded

### Health Endpoint (`GET /api/v1/health`)

```json
{
  "status": "healthy",
  "uptime_seconds": 84200,
  "storage": {
    "sqlite_size_mb": 142,
    "lancedb_size_mb": 58,
    "cozodb_size_mb": 23,
    "total_observations": 12847
  },
  "agents": {
    "briefing": { "last_run": "2026-04-06T07:00:00Z", "status": "ok" },
    "dead_end": { "last_run": "2026-04-05T18:32:00Z", "status": "ok" },
    "linker": { "last_run": "2026-04-05T18:45:00Z", "status": "ok" },
    "compression": { "last_run": "2026-04-05T20:00:00Z", "status": "ok" }
  },
  "llm": {
    "local": { "status": "connected", "model": "llama3.2", "queue_depth": 3 },
    "cloud": { "status": "connected", "requests_today": 12, "limit": 50 }
  }
}
```

### Telemetry

- **No telemetry in v1.** Privacy-first tool should not phone home.
- v3+ (team sync): optional anonymous usage stats, strictly opt-in, documented exactly what's sent.

---

## 16. Phased Rollout

### v1.0 — Solo Developer (MVP)

| Component | Scope |
|---|---|
| Core Daemon | Rust binary, HTTP API, embedded web dashboard |
| Storage | SQLite + LanceDB + CozoDB, all file-based |
| Capture | AI session adapter (Claude Code + Cursor) |
| Agents | Briefing, Dead End, Linker, Compression |
| CLI | Full command set |
| LLM | Ollama (local) + Anthropic (cloud) + bundled ONNX fallback for embeddings |
| Privacy | Redaction pipeline, `.devbrainignore`, env-var keys |
| Dashboard | Timeline, Briefings, Dead Ends, Settings, Health |
| Distribution | Single binary (macOS, Linux, Windows) via curl/brew |

### v1.1 — IDE Integration

| Component | Scope |
|---|---|
| VS Code extension | File sidebar, dead end warnings, search, briefing notifications |
| Cursor extension | Same feature set |

### v2.0 — Deeper Intelligence

| Component | Scope |
|---|---|
| Pattern Agent | Recurring bug detection, hotspot files |
| Git Adapter | Capture from commits, PRs, branches |
| Graph Explorer | Visual knowledge graph in dashboard |
| Multi-provider LLM | OpenAI, Gemini alongside Anthropic |

### v3.0 — Team Sync

| Component | Scope |
|---|---|
| Sync server | Self-hosted, scoped data sharing |
| Team briefings | Cross-team knowledge surfacing |
| Shared dead ends | Team-wide "don't try this" catalog |
| Access controls | Project-level scoping |

### v4.0 — Platform

| Component | Scope |
|---|---|
| Hosted sync | Managed SaaS |
| Slack/Jira adapters | Team tool integration |
| IDE adapter | File/edit/debug event capture |
| Org-level patterns | Cross-team detection |
| Public API | Third-party integrations |

---

## 17. Success Metrics (v1)

| Metric | Target |
|---|---|
| Daemon memory usage | < 50MB idle, < 200MB during agent runs |
| Capture latency | < 10ms from event to stored observation |
| Search latency | < 200ms for semantic search |
| Briefing generation | < 30 seconds |
| Binary size | < 30MB |
| Cold start | < 2 seconds to daemon ready |
| Daily cloud LLM cost | < $0.10 for typical solo dev usage |

---

## 18. Open Questions

1. **Ollama dependency:** v1 bundles a small ONNX model for embeddings as fallback, but Ollama is still needed for tagging/compression. Should we bundle a small GGUF model too, or keep Ollama as a required dependency?
2. **Graph query API:** How much of CozoDB's Datalog should be exposed to end users vs abstracted behind simpler endpoints?
3. **Briefing delivery:** Beyond CLI and dashboard, should briefings integrate with system notifications (macOS/Windows/Linux)?
4. **Update mechanism:** Auto-update the binary, or rely on package managers (brew, curl)?

---

## Appendix A: Competitive Landscape

| Tool | What it does | DevBrain differentiator |
|---|---|---|
| Git blame | Who changed a line | DevBrain knows *why* |
| Jira/Linear | Task tracking | DevBrain connects tasks to code decisions |
| Confluence | Documentation | DevBrain's knowledge is always current (auto-captured) |
| Slack search | Past conversations | DevBrain links conversations to code |
| Claude-mem | AI session memory | DevBrain adds proactive intelligence + knowledge graph |
| Pieces.app | Code snippet saving | DevBrain captures context, not just code |
| Cody (Sourcegraph) | AI code search | DevBrain knows your personal history, not just the codebase |

## Appendix B: File Structure

```
~/.devbrain/
+-- devbrain.db              # SQLite — observations, sessions, settings cache
+-- vectors/                 # LanceDB — embeddings for semantic search
+-- graph/                   # CozoDB — knowledge graph
+-- briefings/
|   +-- 2026-04-06.md        # Daily briefings
|   +-- 2026-04-05.md
+-- settings.toml            # User configuration
+-- logs/
|   +-- devbrain-2026-04-06.log
+-- cache/
    +-- llm-tasks/           # Pending/completed LLM task queue
```
