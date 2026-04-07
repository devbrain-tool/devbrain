# Setup Guide & Validation — Design Spec

**Date:** 2026-04-06
**Status:** Approved

## Overview

Add a "Setup" page to the DevBrain dashboard that shows CLI integration status, provides setup instructions, and can auto-fix configuration issues. Also add a summary badge to the Health page and strengthen the setup script validation checks.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Navigation | Top-level "Setup" page + summary badge on Health | Instructions deserve their own page; badge gives at-a-glance visibility |
| Auto-fix scope | Config only (DevBrain-owned files) | Safe and predictable; CLI installation is user's responsibility |
| Validation depth | Structural + live round-trip + CLI version check | Full end-to-end proof that integrations work |
| Architecture | Backend validation API + frontend display | Checks require server-side access (files, process spawning) |

## Backend API

### New file: `src/DevBrain.Api/Endpoints/SetupEndpoints.cs`

Two endpoints under `/api/v1/setup`:

#### `GET /api/v1/setup/status`

Runs all validation checks and returns structured results.

Response:
```json
{
  "checks": [
    {
      "id": "claude-cli",
      "category": "Claude Code",
      "name": "Claude CLI installed",
      "status": "pass",
      "detail": "claude v1.2.3 found at /usr/local/bin/claude",
      "fixable": false
    },
    {
      "id": "claude-hook",
      "category": "Claude Code",
      "name": "PostToolUse hook configured",
      "status": "fail",
      "detail": "Hook missing from ~/.claude/settings.json",
      "fixable": true
    }
  ],
  "summary": { "pass": 5, "fail": 2, "warn": 1, "skip": 1 }
}
```

**Status values:** `"pass"`, `"fail"`, `"warn"`, `"skip"`

#### Checks performed (9 total)

| ID | Category | What it validates | Fixable? |
|---|---|---|---|
| `claude-cli` | Claude Code | `claude` binary exists in PATH, capture `claude --version` output | No |
| `claude-settings` | Claude Code | `~/.claude/settings.json` exists and is valid JSON | Yes (create file with empty hooks) |
| `claude-hook` | Claude Code | `PostToolUse` hook array contains a command targeting daemon port | Yes (inject hook entry) |
| `claude-roundtrip` | Claude Code | POST test observation to daemon, verify it's readable | No |
| `gh-cli` | GitHub Copilot | `gh` binary exists in PATH, capture `gh --version` output | No |
| `gh-copilot` | GitHub Copilot | `gh copilot --help` succeeds (extension installed) | No |
| `copilot-wrappers` | GitHub Copilot | `ghcs`/`ghce` wrapper scripts exist (Unix: files in install dir; Windows: functions in $PROFILE) | Yes (create wrapper scripts/functions) |
| `copilot-roundtrip` | GitHub Copilot | POST test observation to daemon simulating wrapper capture, verify readable | No |
| `ollama` | LLM | Ollama running (HTTP check to localhost:11434) + model llama3.2:3b available | No |

**Check dependencies:**
- `claude-settings` skipped if `claude-cli` fails
- `claude-hook` skipped if `claude-settings` fails
- `claude-roundtrip` skipped if `claude-hook` fails
- `gh-copilot` skipped if `gh-cli` fails
- `copilot-wrappers` skipped if `gh-cli` fails
- `copilot-roundtrip` skipped if `copilot-wrappers` fails

**Implementation details:**
- CLI existence checks: `Process.Start` with `which`/`where` (platform-dependent), 5-second timeout
- Version capture: `Process.Start` with `claude --version` / `gh --version`, parse stdout
- File checks: `File.Exists`, `File.ReadAllText`, `System.Text.Json.JsonDocument.Parse` for JSON validation
- Hook structure check: Parse `settings.json`, navigate to `hooks.PostToolUse` array, verify at least one entry contains the daemon port
- Round-trip test: HTTP POST to own `/api/v1/observations` endpoint with test data (project: `"devbrain-setup-validation"`), wait 500ms, query back with project filter, verify match, then delete test observation
- Process timeout: 5 seconds per CLI command; on timeout, check returns `"warn"` with "Timed out after 5s"

#### `POST /api/v1/setup/fix/{checkId}`

Runs auto-fix for a fixable check.

Success response:
```json
{ "success": true, "detail": "Created PostToolUse hook in ~/.claude/settings.json" }
```

Failure response:
```json
{ "success": false, "detail": "Permission denied writing ~/.claude/settings.json" }
```

Returns 400 for non-fixable check IDs.

**Fix implementations:**

**`claude-settings`** — Create `~/.claude/` directory if missing, write minimal valid `settings.json`:
```json
{
  "hooks": {}
}
```
If file exists but isn't valid JSON, back up as `settings.json.bak` and recreate.

**`claude-hook`** — Read existing `settings.json`, parse JSON, ensure `hooks.PostToolUse` array exists, append DevBrain hook entry:
```json
{
  "type": "command",
  "command": "curl -s -X POST http://127.0.0.1:{port}/api/v1/observations -H 'Content-Type: application/json' -d '{\"sessionId\":\"$CLAUDE_SESSION_ID\",\"eventType\":\"ToolCall\",\"source\":\"ClaudeCode\",\"rawContent\":\"Tool: $CLAUDE_TOOL_NAME\",\"project\":\"$CLAUDE_PROJECT\"}' >/dev/null 2>&1"
}
```
Port comes from the running daemon's settings. If hook already exists but points to wrong port, update it.

**`copilot-wrappers`** — Platform-dependent:
- **Unix:** Write `ghcs` and `ghce` scripts to the DevBrain install directory (`~/.devbrain/bin/`), make executable
- **Windows:** Append `ghcs`/`ghce` PowerShell functions to `$PROFILE`, creating profile file if it doesn't exist

Wrapper scripts follow the same template as `setup.sh`/`setup.ps1` (run copilot command, capture output, POST to DevBrain).

### Helper class: `src/DevBrain.Api/Setup/SetupValidator.cs`

Encapsulates all check logic and fix logic. Injected into endpoints via DI. Takes `Settings` (for daemon port) and `IObservationStore` (for round-trip tests).

Methods:
- `Task<SetupStatus> RunAllChecks()` — returns full check results
- `Task<FixResult> Fix(string checkId)` — runs fix for one check

### DI registration in Program.cs

```csharp
builder.Services.AddSingleton<SetupValidator>();
```

Plus `app.MapSetupEndpoints();`

## Frontend

### New files

| File | Purpose |
|---|---|
| `dashboard/src/pages/Setup.tsx` | Full setup page: status banner, check results, instructions |
| `dashboard/src/api/client.ts` | Add `setup.status()`, `setup.fix(checkId)` methods |
| `dashboard/src/App.tsx` | Add route `/setup` |
| `dashboard/src/components/Navigation.tsx` | Add "Setup" link |

### Modified files

| File | Change |
|---|---|
| `dashboard/src/pages/Health.tsx` | Add Integrations summary badge card |

### Setup page layout

Three stacked sections:

**1. Status banner**
- Green: "All integrations configured" — all checks pass
- Yellow: "Some integrations need attention" — warnings or fixable failures
- Red: "Setup incomplete" — non-fixable failures

**2. Check results — grouped by category**

Three category cards: "Claude Code", "GitHub Copilot", "LLM"

Each card lists its checks as rows:
- Green dot + "PASS" + name + detail for passing checks
- Red X + "FAIL" + name + detail + **[Fix] button** (if fixable) for failures
- Yellow triangle + "WARN" + name + detail for warnings
- Gray circle + "SKIP" + name + detail for skipped checks

**[Fix] button** calls `POST /api/v1/setup/fix/{id}`, shows spinner while running, then auto-triggers re-validation.

**[Re-validate] button** at the top of the check results section. Calls `GET /api/v1/setup/status` and re-renders.

**3. Setup instructions — collapsible panels**

Three expandable sections:

**"Install Claude Code"**
- Auto-expanded if `claude-cli` check fails
- Command: `npm install -g @anthropic-ai/claude-code`
- "After installing, click Re-validate above"
- Copy button on the command

**"Install GitHub Copilot CLI"**
- Auto-expanded if `gh-cli` or `gh-copilot` fails
- Step 1: Install `gh` — link to https://cli.github.com/
- Step 2: `gh auth login`
- Step 3: `gh extension install github/gh-copilot`
- "After installing, click Re-validate above"
- Copy buttons on each command

**"Install Ollama"**
- Auto-expanded if `ollama` check fails
- Link to https://ollama.com/download
- After install: `ollama pull llama3.2:3b`
- Copy button on the command

### Health page badge

New card in the existing grid (alongside Status, Uptime, etc.):

```
┌─ Integrations ──────┐
│  2/3 configured      │
│  ● Claude  ● Copilot │
│  ⚠ Ollama           │
└──────────────────────┘
```

- Each tool gets a colored dot: green (all category checks pass), red (any fail), yellow (any warn)
- Card is clickable — navigates to `/setup` via `react-router-dom` `useNavigate`
- Data from `GET /api/v1/setup/status` called on Health page mount

### Data flow

1. Setup page mounts → `GET /api/v1/setup/status` → render results, auto-expand failed instruction sections
2. User clicks [Fix] → `POST /api/v1/setup/fix/{id}` → on success, auto re-validate → re-render
3. User clicks [Re-validate] → fresh `GET /api/v1/setup/status` → re-render
4. Health page mounts → `GET /api/v1/setup/status` → render badge only

### Styling

Same dark theme as existing pages. Status indicator colors:
- Pass: `#22c55e` (green)
- Fail: `#ef4444` (red)
- Warn: `#eab308` (yellow)
- Skip: `#6b7280` (gray)

Banner backgrounds: green `#14532d`, yellow `#422006`, red `#450a0a` (with matching border colors).

Copy button: small monospace button next to commands, uses `navigator.clipboard.writeText`.

### Error handling

- Fix endpoint fails → show red inline error on that check row with `detail` message
- Validation endpoint fails → show "Could not run validation" banner
- Individual check timeouts → that check returns `"warn"` with "Timed out after 5s"
- Network errors → same pattern as other pages (`catch → setError`)

## Setup script improvements

### Strengthen checks #9.7 and #9.8

Replace the shallow checks in both `setup.sh` and `setup.ps1` with deep validation matching the dashboard's checks:

**Check #9.7 — Claude Code (replace single check with 4 sub-checks):**
1. `claude` CLI exists in PATH → PASS/FAIL
2. `~/.claude/settings.json` is valid JSON → PASS/FAIL (auto-fix: create)
3. `PostToolUse` hook has correct structure → PASS/FAIL (auto-fix: inject)
4. Round-trip test: POST observation, read back → PASS/FAIL

**Check #9.8 — GitHub Copilot (replace single check with 4 sub-checks):**
1. `gh` CLI exists in PATH → PASS/FAIL
2. `gh copilot` extension installed → PASS/FAIL
3. Wrappers exist (`ghcs`/`ghce`) → PASS/FAIL (auto-fix: create)
4. Round-trip test: simulate wrapper POST, read back → PASS/FAIL

**Auto-fix behavior in setup scripts:**
When a fixable check fails, the setup script automatically runs the fix (same logic as the API's fix endpoint), then re-checks. This means the setup script's validation is self-healing — it fixes what it can and only reports failures for things it can't fix.

**Validation count update:** Total checks increases from 10 to ~16 (existing 10 minus the 2 shallow ones, plus 8 new deep sub-checks).

## Scope exclusions (not in v1)

- No WebSocket live-updating of check status (page refresh / re-validate button is sufficient)
- No auto-detection of CLI updates or upgrade prompts
- No support for custom Claude Code hook configurations beyond DevBrain's
- No new npm or NuGet dependencies
