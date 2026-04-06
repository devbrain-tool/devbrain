# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-04-05

### Added

- **Core Daemon** — background process with HTTP API on localhost:37800
- **Capture Pipeline** — 5-stage Channel\<T\> processing (Normalizer, Enricher, Tagger, PrivacyFilter, Writer)
- **SQLite Storage** — observations, threads, dead ends with FTS5 full-text search
- **Knowledge Graph** — graph-as-tables with bidirectional recursive CTE traversal
- **Intelligence Agents** — Linker (relationship extraction), Dead End (detection), Briefing (daily synthesis), Compression (thread archival)
- **Agent Scheduler** — event-driven, cron, and idle-based dispatch with concurrency control
- **LLM Integration** — Ollama (local) + Anthropic (cloud) with configurable routing and daily quota management
- **Privacy Pipeline** — automatic secret redaction (API keys, tokens, passwords, PEM keys), `<private>` tag support, `.devbrainignore` per-project exclusions
- **CLI** — 18 commands including start, stop, status, briefing, search, why, thread, dead-ends, agents, config, export, purge, rebuild, dashboard
- **Web Dashboard** — React + TypeScript SPA with 7 pages (Timeline, Briefings, Dead Ends, Threads, Search, Settings, Health)
- **TOML Configuration** — `~/.devbrain/settings.toml` with hot-reload for safe settings
- **Cross-Platform** — Windows (x64, ARM64), Linux (x64, ARM64), macOS (x64, ARM64)
- **CI/CD** — GitHub Actions with test, dashboard build, and 6-platform release pipeline
- **Install Scripts** — one-liner install for bash (Linux/macOS) and PowerShell (Windows)
