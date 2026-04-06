# DevBrain

**A developer's second brain that passively captures what you do, understands why you did it, and proactively tells you what you need to know.**

[![Build](https://img.shields.io/github/actions/workflow/status/devbrain/devbrain/ci.yml?branch=main&style=flat-square)](https://github.com/devbrain/devbrain/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Release](https://img.shields.io/github/v/release/devbrain/devbrain?style=flat-square)](https://github.com/devbrain/devbrain/releases)
[![.NET](https://img.shields.io/badge/.NET-9+-512BD4?style=flat-square)](https://dotnet.microsoft.com)

---

## What is DevBrain?

Developers lose massive amounts of context constantly — switching tasks, coming back after a weekend, debugging something they fixed months ago. DevBrain eliminates this by running as a background daemon on your machine, passively capturing your AI-assisted coding sessions, and building a knowledge graph of your decisions, dead ends, and patterns. It then proactively surfaces relevant context before you need to search for it — morning briefings, "you tried this before" warnings, and semantic search across your entire development history.

Everything stays on your machine. No telemetry. No cloud sync. Your data is yours.

## Key Features

- **Passive capture** — Automatically ingests AI coding sessions (Claude Code, Cursor) with zero friction. No manual logging.
- **Knowledge graph** — Builds a graph of decisions, dead ends, patterns, files, and threads. Understands how everything connects.
- **Morning briefings** — Start your day with a summary of where you left off, what changed overnight, and what to watch out for.
- **Dead end warnings** — "You tried this approach 3 weeks ago on the auth service. It failed because of X." Saves hours of repeated mistakes.
- **Semantic search** — Search your entire development history by meaning, not just keywords. Combines vector search, full-text search, and graph expansion.
- **Privacy-first** — Local-only by default. Automatic secret redaction (API keys, tokens, passwords). Configurable strict mode strips raw content entirely.
- **Web dashboard** — Timeline view, thread browser, dead end catalog, briefing viewer, and system health — all at `localhost:37800`.

## Quick Start

```bash
# Install
curl -fsSL https://raw.githubusercontent.com/devbrain/devbrain/main/scripts/install.sh | bash

# Start the daemon
devbrain start

# Check that everything is running
devbrain status

# Open the web dashboard
devbrain dashboard
```

## How It Works

```
AI Sessions ──> Capture Pipeline ──> SQLite + Knowledge Graph
                                          |
                  CLI / Dashboard <── Intelligence Agents ──> Briefings
```

DevBrain runs as two binaries:

- **Daemon** (`devbrain-api`) — background process hosting the capture pipeline, intelligence agents, storage, and web dashboard.
- **CLI** (`devbrain`) — thin HTTP client that talks to the daemon. Commands feel instant because the CLI doesn't initialize storage or agents.

The capture pipeline processes events through five stages: **Normalize** (schema), **Enrich** (project/branch/thread), **Tag** (classify via local LLM), **Privacy Filter** (redact secrets), and **Write** (persist to SQLite + index to LanceDB). Intelligence agents then run on schedules or in response to events, building the knowledge graph and generating briefings.

## CLI Commands

| Command | Description |
|---|---|
| `devbrain start` | Launch the daemon as a background process |
| `devbrain stop` | Graceful shutdown |
| `devbrain status` | Health check, storage stats, agent status, LLM connectivity |
| `devbrain briefing` | Show today's morning briefing |
| `devbrain briefing --generate` | Force-regenerate the briefing |
| `devbrain search <query>` | Semantic + full-text search across all observations |
| `devbrain search --exact <query>` | Full-text search only (FTS5) |
| `devbrain why <file>` | Show decisions, dead ends, and patterns for a specific file |
| `devbrain thread` | Show the current active thread |
| `devbrain thread list` | List all recent threads |
| `devbrain thread new <title>` | Manually start a new thread |
| `devbrain dead-ends` | List dead ends for the current project |
| `devbrain dead-ends --file <path>` | Dead ends for a specific file |
| `devbrain related <file>` | Graph traversal: everything connected to a file |
| `devbrain agents` | List agents, their status, and last run time |
| `devbrain agents run <name>` | Manually trigger an agent |
| `devbrain config` | Show current settings |
| `devbrain config set <key> <value>` | Update a setting |
| `devbrain export` | Full data export (JSON) |
| `devbrain purge` | Delete data by project or date |
| `devbrain rebuild vectors` | Re-embed all observations from SQLite |
| `devbrain rebuild graph` | Replay the Linker Agent over all observations |
| `devbrain dashboard` | Open the web dashboard in your browser |
| `devbrain service install` | Install as a system service for auto-start |
| `devbrain service uninstall` | Remove the system service |
| `devbrain update` | Update to the latest release |
| `devbrain update --check` | Check for available updates |

## Installation

### Linux / macOS (recommended)

```bash
curl -fsSL https://raw.githubusercontent.com/devbrain/devbrain/main/scripts/install.sh | bash
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/devbrain/devbrain/main/scripts/install.ps1 | iex
```

### Manual

Download the latest release for your platform from [GitHub Releases](https://github.com/devbrain/devbrain/releases). DevBrain ships as Native AOT binaries — no .NET runtime required.

| Platform | Binary |
|---|---|
| Linux x64 | `devbrain-linux-x64.tar.gz` |
| Linux ARM64 | `devbrain-linux-arm64.tar.gz` |
| macOS x64 | `devbrain-osx-x64.tar.gz` |
| macOS ARM64 | `devbrain-osx-arm64.tar.gz` |
| Windows x64 | `devbrain-win-x64.zip` |
| Windows ARM64 | `devbrain-win-arm64.zip` |

Extract and add the directory to your `PATH`.

### Build from Source

```bash
git clone https://github.com/devbrain/devbrain.git
cd devbrain
dotnet build
dotnet run --project src/DevBrain.Api    # Start the daemon
dotnet run --project src/DevBrain.Cli    # Run CLI commands
```

## Configuration

DevBrain stores its configuration at `~/.devbrain/settings.toml`. A minimal configuration looks like:

```toml
[capture]
sources = ["claude-code", "cursor"]

[llm.local]
provider = "ollama"
model = "llama3.2"
embedding_model = "nomic-embed-text"

[llm.cloud]
provider = "anthropic"
# Set ANTHROPIC_API_KEY environment variable
max_daily_requests = 50

[privacy]
mode = "redact"  # "redact" or "strict"

[agents]
briefing_time = "07:00"
compression_after_days = 30
```

Run `devbrain config` to view all current settings, or `devbrain config set <key> <value>` to change individual values. Most settings are hot-reloaded — no daemon restart required.

For the full configuration reference, see [docs/configuration.md](docs/configuration.md).

## Privacy & Security

DevBrain is designed with privacy as a core constraint, not an afterthought.

- **Local-only** — All data stays on your machine in `~/.devbrain/`. No telemetry, no phone-home, no cloud sync.
- **Automatic secret redaction** — API keys, tokens, passwords, PEM keys, and `.env` values are detected and replaced with `[REDACTED:type]` before storage. Original content is never persisted.
- **Strict mode** — For maximum privacy, `mode = "strict"` stores only summaries — no raw content, no file contents, no conversation text.
- **Cloud LLM boundary** — When using cloud LLMs (for briefings), only summaries and metadata are sent. Raw content, file diffs, and secrets never leave your machine.
- **File permissions** — `~/.devbrain/` is locked to your user account on first run (Unix `700`, Windows user-only ACL).
- **`.devbrainignore`** — Exclude specific files or projects from capture entirely.

## Roadmap

| Version | Focus | Highlights |
|---|---|---|
| **v1.0** | Core (current) | Capture pipeline, knowledge graph, briefings, dead end detection, semantic search, CLI, web dashboard |
| **v1.1** | IDE integration | VS Code extension, JetBrains plugin — inline dead end warnings, contextual search |
| **v2.0** | Deeper intelligence | Pattern detection across threads, git adapter (PR/merge context), recurring bug alerts |
| **v3.0** | Team sync | Optional encrypted team knowledge sharing, shared dead end catalog, cross-developer patterns |

## Contributing

We welcome contributions. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on:

- Setting up the development environment
- Running tests (`dotnet test`)
- Code style and conventions
- Submitting pull requests

If you're looking for a place to start, check issues labeled [`good first issue`](https://github.com/devbrain/devbrain/labels/good%20first%20issue).

## License

[MIT](LICENSE)

## Acknowledgments

DevBrain builds on excellent open source software:

- [SQLite](https://sqlite.org/) — storage engine
- [LanceDB](https://lancedb.com/) — vector search
- [Ollama](https://ollama.ai/) — local LLM inference
- [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet) — daemon host and API
- [System.CommandLine](https://github.com/dotnet/command-line-api) — CLI framework
- [React](https://react.dev/) + [Vite](https://vite.dev/) — web dashboard
