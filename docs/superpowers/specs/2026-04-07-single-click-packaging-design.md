# DevBrain Single-Click Packaging Design

**Date:** 2026-04-07
**Status:** Draft
**Goal:** Ship DevBrain as a cross-platform package that developers install via package managers with zero manual configuration.

---

## Overview

DevBrain currently distributes as raw archives (`.tar.gz`/`.zip`) via GitHub Releases, with shell scripts handling installation. This design replaces that with native package manager distribution (`winget`, `brew`, `apt`) backed by an Electron tray app that manages the daemon lifecycle, Ollama bootstrapping, and system integration.

**Developer experience after this ships:**

```bash
# macOS
brew tap devbrain/tap && brew install devbrain

# Windows
winget install DevBrain

# Linux (Debian/Ubuntu)
curl -s https://devbrain.dev/install.gpg | sudo gpg --dearmor -o /usr/share/keyrings/devbrain.gpg
echo "deb [signed-by=/usr/share/keyrings/devbrain.gpg] https://devbrain.dev/apt stable main" | sudo tee /etc/apt/sources.list.d/devbrain.list
sudo apt update && sudo apt install devbrain
```

After install: tray icon appears, daemon starts, Ollama auto-installs in background, everything works.

---

## Architecture

### Components

```
Developer's Machine
┌─────────────────────────────────────────────┐
│  Electron Tray App (DevBrain.exe / .app)    │
│  ├── System tray icon (green/yellow/red)    │
│  ├── Context menu (start/stop/dashboard)    │
│  ├── Bootstrap orchestrator                 │
│  │   ├── Config creation                    │
│  │   ├── Ollama auto-install                │
│  │   └── Model pull (llama3.2:3b)           │
│  └── Daemon lifecycle manager               │
│      ├── Spawn devbrain-daemon              │
│      ├── Health poll (/api/v1/health, 5s)   │
│      └── Auto-restart on crash (max 3)      │
│                                             │
│  devbrain-daemon (embedded .NET binary)     │
│  ├── HTTP API on 127.0.0.1:37800            │
│  ├── Capture pipeline (5 stages)            │
│  ├── Agent scheduler (8 agents)             │
│  ├── SQLite database                        │
│  └── Dashboard (static files in wwwroot/)   │
│                                             │
│  devbrain CLI (embedded .NET binary)        │
│  └── Thin HTTP client to daemon             │
│                                             │
│  Ollama (auto-installed, external process)  │
│  └── localhost:11434                        │
└─────────────────────────────────────────────┘
```

### Repository Layout Changes

New directories added to the monorepo:

```
packages/
  tray/                              # Electron tray app
    package.json                     # electron, electron-builder deps
    electron-builder.yml             # Platform-specific build config
    src/
      main.ts                        # Electron entry — tray icon, menu, autostart
      bootstrap.ts                   # First-run: config, Ollama install, model pull
      daemon.ts                      # Spawn/monitor/restart devbrain-daemon
      health.ts                      # Poll health endpoint, update tray icon state
      notifications.ts               # OS-native notification helpers
    assets/
      icon.png                       # Tray icon (all platforms)
      icon.ico                       # Windows tray icon
      icon.icns                      # macOS tray icon
    __tests__/
      bootstrap.test.ts
      daemon.test.ts
      health.test.ts

  homebrew/
    devbrain.rb                      # Homebrew formula

  winget/
    DevBrain.DevBrain.yaml           # winget manifest

  apt/
    debian/
      control                        # Package metadata + dependencies
      postinst                       # Post-install: config, Ollama, autostart
      prerm                          # Pre-remove: stop daemon, cleanup
      rules                          # Build rules

package.json                         # Root — npm workspaces config
```

The `dashboard/` and `packages/tray/` share code via npm workspaces:

```json
{
  "workspaces": ["dashboard", "packages/tray"]
}
```

---

## Electron Tray App

### Tray Icon & Menu

The tray app is a **menubar utility**, not a windowed application. It manages the daemon and provides quick access.

**Tray icon states:**
- Green: daemon running, healthy
- Yellow: starting up, bootstrapping, or Ollama installing
- Red: daemon stopped or unhealthy

**Context menu:**

```
DevBrain (Running)              ← status text
─────────────────
Open Dashboard                  → opens http://localhost:37800 in default browser
─────────────────
Start Daemon
Stop Daemon
Restart Daemon
─────────────────
View Logs                       → opens ~/.devbrain/logs/ in file manager
─────────────────
Quit DevBrain                   → stops daemon, exits tray app
```

### Daemon Lifecycle Management

The tray app replaces the CLI's `devbrain start`/`devbrain stop` as the primary daemon manager.

```typescript
// daemon.ts — simplified contract
export interface DaemonManager {
  start(): Promise<void>;       // Spawn devbrain-daemon, write PID
  stop(): Promise<void>;        // Kill process, cleanup PID
  restart(): Promise<void>;     // Stop + start
  isRunning(): Promise<boolean>; // Check PID + health endpoint
}
```

**Auto-restart policy:**
- On daemon crash: restart immediately, up to 3 times in 5 minutes
- After 3 failures: stop retrying, show error notification, tray icon turns red
- User can manually restart from tray menu to reset the counter

**Auto-launch at login:**
- electron-builder's `autoLaunch: true` option handles per-platform registration
  - Windows: registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - macOS: Login Items
  - Linux: `~/.config/autostart/devbrain.desktop`

### CLI / Tray Coordination

Both the CLI (`devbrain start`/`stop`) and the tray app can manage the daemon. Without coordination, conflicts arise — e.g., `devbrain stop` kills the daemon, tray thinks it crashed, auto-restarts it.

**Resolution:** The tray app writes a management lock file at `~/.devbrain/tray.lock` while running. The CLI checks for this file:
- `devbrain start` — if `tray.lock` exists and the tray process is alive, print "Daemon is managed by the tray app. Use the tray menu or run `devbrain-tray` to manage it." and exit.
- `devbrain stop` — if `tray.lock` exists and the tray process is alive, send a stop request to the tray app via a local IPC socket (or simply delete the PID file and set a `~/.devbrain/stopped` sentinel). The tray app checks for the sentinel before auto-restarting — if present, it shows "Stopped" state instead of restarting.
- If the tray app is not running (no lock or stale lock), CLI behaves as it does today.

This keeps the CLI fully functional for headless/server use while preventing fights with the tray app.

### Dashboard Access

Tray menu "Open Dashboard" opens `http://localhost:37800` in the system default browser. No embedded Electron BrowserWindow — keeps the tray app lightweight and avoids duplicating browser memory.

---

## First-Run Bootstrap

Orchestrated by `bootstrap.ts` in the Electron main process. The daemon is NOT responsible for installing external software.

### Flow

```
1. Config check
   ~/.devbrain/settings.toml exists?
   ├─ YES → skip
   └─ NO  → create default config (embedded template)

2. Daemon start
   Spawn devbrain-daemon process
   Poll /api/v1/health every 500ms, timeout 10s
   ├─ SUCCESS → continue
   └─ FAIL    → show error notification, abort bootstrap

3. Ollama detection
   Probe http://localhost:11434/api/version
   ├─ REACHABLE → skip install
   └─ UNREACHABLE → install Ollama
       ├─ Windows: download OllamaSetup.exe, run /S (silent)
       ├─ macOS: brew install ollama || download from ollama.com
       └─ Linux: curl -fsSL https://ollama.com/install.sh | sh
       Show notification: "Installing local AI model (first time only)..."

4. Model check
   ollama list | grep llama3.2:3b
   ├─ FOUND → skip
   └─ NOT FOUND → ollama pull llama3.2:3b
       Show notification: "Downloading AI model (~2GB)..."

5. Ready
   Tray icon → green
   Notification: "DevBrain is ready"
```

### Design Principles

- **Non-blocking:** Daemon starts at step 2. Ollama install (steps 3-4) happens in background. DevBrain is usable immediately — LLM features queue until Ollama is ready.
- **Idempotent:** Every step checks before acting. Running bootstrap 10 times produces the same result.
- **Graceful degradation:** If Ollama install fails, daemon keeps running. Notification suggests cloud LLM fallback via API key in Settings.
- **No internet required:** If offline, daemon runs fine. LLM features deferred until connectivity returns.

---

## Package Manager Distribution

### Homebrew (macOS + Linux)

**Tap repository:** `devbrain/homebrew-tap` (separate GitHub repo)

```ruby
class Devbrain < Formula
  desc "Developer's second brain — captures coding sessions, builds knowledge graph"
  homepage "https://github.com/devbrain/devbrain"
  version "1.0.0"

  if OS.mac? && Hardware::CPU.arm?
    url "https://github.com/.../devbrain-osx-arm64.tar.gz"
    sha256 "..."
  elsif OS.mac? && Hardware::CPU.intel?
    url "https://github.com/.../devbrain-osx-x64.tar.gz"
    sha256 "..."
  elsif OS.linux? && Hardware::CPU.intel?
    url "https://github.com/.../devbrain-linux-x64.tar.gz"
    sha256 "..."
  end

  def install
    bin.install "devbrain"
    bin.install "devbrain-daemon"
    prefix.install "DevBrain.app" if OS.mac?  # Electron tray app
  end

  # No Homebrew service block — the Electron tray app owns daemon lifecycle.
  # Adding a launchd service here would conflict with the tray app on port 37800.

  def post_install
    # Tray app handles all user-space bootstrap (config, Ollama, daemon) on first launch.
    # Nothing to do here beyond what `install` already did.
  end
end
```

**Install experience:**
```bash
brew tap devbrain/tap
brew install devbrain
```

**Updates:** `brew upgrade devbrain`

### winget (Windows)

electron-builder produces an NSIS installer (`.exe`). winget manifest points to it.

```yaml
PackageIdentifier: DevBrain.DevBrain
PackageVersion: 1.0.0
PackageName: DevBrain
Publisher: DevBrain
License: Apache-2.0
InstallerType: exe
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/.../DevBrain-Setup-1.0.0-x64.exe
    InstallerSha256: "..."
    InstallerSwitches:
      Silent: /S
      SilentWithProgress: /S
```

Submitted via PR to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

**Install experience:**
```powershell
winget install DevBrain
```

**Updates:** `winget upgrade DevBrain`

### APT (Debian/Ubuntu)

`.deb` package built from `packages/apt/debian/` control files. Hosted on GitHub Pages as an APT repository.

```
Package: devbrain
Version: 1.0.0
Architecture: amd64
Depends: libgtk-3-0, libnotify4, libnss3
Description: Developer's second brain
```

Post-install (`postinst`) script:
- Creates symlinks in `/usr/local/bin/` for `devbrain` and `devbrain-daemon`
- Registers desktop autostart entry for the tray app (`/etc/xdg/autostart/devbrain.desktop`)
- Does NOT create user config or install Ollama — runs as root, cannot write to `~/.devbrain/`. The tray app handles all user-space bootstrap on first launch.

**Install experience:**
```bash
# Add repo (one-time)
curl -s https://devbrain.dev/install.gpg | sudo gpg --dearmor -o /usr/share/keyrings/devbrain.gpg
echo "deb [signed-by=/usr/share/keyrings/devbrain.gpg] https://devbrain.dev/apt stable main" | sudo tee /etc/apt/sources.list.d/devbrain.list

# Install
sudo apt update && sudo apt install devbrain
```

**Updates:** `sudo apt upgrade devbrain`

**One-liner alternative** (wraps repo setup + install for a simpler experience):
```bash
curl -fsSL https://devbrain.dev/install.sh | sh
```

This script adds the GPG key, configures the repo, and runs `apt install devbrain` — reducing the 3-command flow to one.

---

## Uninstall Behavior

Uninstall (via `winget uninstall`, `brew uninstall`, `apt remove`) should:

1. **Stop the daemon** — kill the running `devbrain-daemon` process
2. **Stop the tray app** — kill the Electron process
3. **Remove binaries** — CLI, daemon, tray app executables
4. **Remove autostart registration** — registry entry (Windows), Login Items (macOS), `.desktop` file (Linux)
5. **Remove PATH entries** — undo any PATH modifications from install

Uninstall should **NOT**:
- Delete `~/.devbrain/` — contains the user's knowledge graph, settings, and SQLite database. This is user data.
- Uninstall Ollama — the user may use it for other purposes. DevBrain installed it but doesn't own it.

If the user wants a full purge, they manually delete `~/.devbrain/`. This follows the convention of tools like Docker, VS Code, and Homebrew itself.

---

## CI/CD Pipeline

### Build Stages

```
Stage 1 (parallel):
  ├── Test (.NET)           — dotnet test DevBrain.slnx
  ├── Dashboard Build       — npm ci && npm run build
  └── Security Scan         — TruffleHog + dependency review

Stage 2:
  └── Build .NET Binaries   — 6 platforms (win/mac/linux × x64/arm64)
      Self-contained, single-file, PublishSingleFile=true

Stage 3:
  └── Build Electron App    — 3 platforms (win-x64, mac-x64, linux-x64)
      electron-builder produces:
        Windows: NSIS installer (.exe)
        macOS: DMG
        Linux: .deb + AppImage
      Embeds: daemon binary + CLI binary + dashboard static files

Stage 4 (on v* tag only):
  ├── GitHub Release        — upload all archives + installers
  ├── Homebrew Tap Update   — auto-PR to devbrain/homebrew-tap
  ├── winget Submission     — auto-PR to microsoft/winget-pkgs
  └── APT Repo Update       — push .deb to GitHub Pages
```

### Electron Build Job

```yaml
electron-build:
  needs: [build-dotnet, build-dashboard]
  strategy:
    matrix:
      include:
        - os: windows-latest
          rid: win-x64
          electron-target: nsis
        - os: macos-latest
          rid: osx-x64
          electron-target: dmg
        - os: ubuntu-latest
          rid: linux-x64
          electron-target: deb
  steps:
    - Download .NET binary artifacts (daemon + CLI for this platform)
    - Download dashboard dist/ artifact
    - Copy binaries to packages/tray/resources/bin/
    - Copy dashboard to packages/tray/resources/wwwroot/
    - npm ci in packages/tray/
    - npx electron-builder --${{ matrix.electron-target }}
    - Upload installer artifact
```

### Package Manager Publish Jobs

- **Homebrew:** [homebrew-releaser](https://github.com/Justintime50/homebrew-releaser) GitHub Action auto-updates formula with new version + SHA256
- **winget:** [winget-create](https://github.com/microsoft/winget-create) GitHub Action submits PR to microsoft/winget-pkgs
- **APT:** Build `.deb`, sign with GPG key (stored in GitHub Secrets), push to `gh-pages` branch

### Code Signing

**Deferred.** Ship unsigned for initial release. Both macOS and Windows will show security warnings — acceptable for early adopters. Add signing later ($220/year, half-day setup) as a single PR without architectural changes.

---

## Embedded Binary Bundling

The Electron tray app embeds the .NET daemon and CLI binaries inside its package:

### Windows (NSIS installer)

```
C:\Program Files\DevBrain\
  DevBrain.exe                    # Electron tray app
  resources/
    bin/
      devbrain-daemon.exe         # .NET daemon
      devbrain.exe                # .NET CLI
    wwwroot/                      # Dashboard static files
```

NSIS post-install adds `C:\Program Files\DevBrain\resources\bin\` to user PATH.

### macOS (DMG)

```
/Applications/DevBrain.app/
  Contents/
    MacOS/
      DevBrain                    # Electron binary
    Resources/
      bin/
        devbrain-daemon           # .NET daemon
        devbrain                  # .NET CLI
      wwwroot/                    # Dashboard static files
```

Homebrew formula symlinks CLI binaries to `/usr/local/bin/`.

### Linux (.deb)

```
/opt/devbrain/
  devbrain-tray                   # Electron tray app
  resources/
    bin/
      devbrain-daemon             # .NET daemon
      devbrain                    # .NET CLI
    wwwroot/                      # Dashboard static files
/usr/local/bin/
  devbrain -> /opt/devbrain/resources/bin/devbrain
  devbrain-daemon -> /opt/devbrain/resources/bin/devbrain-daemon
```

---

## Testing

### Unit Tests (Jest)

```
packages/tray/__tests__/
  bootstrap.test.ts    # Mock Ollama probe, mock filesystem, verify decision tree
  daemon.test.ts       # Mock child_process.spawn, simulate health responses, verify restart logic
  health.test.ts       # Feed health states, assert correct icon/tooltip transitions
```

Key scenarios:
- Bootstrap skips Ollama install when already present
- Bootstrap creates config only when missing (idempotent)
- Daemon restarts on crash, stops after 3 failures in 5 minutes
- Health transitions: starting→healthy, healthy→unhealthy, unhealthy→healthy
- Ollama install failure falls back gracefully (notification, no crash)

### CI Smoke Tests

New job that runs on each platform VM after package build:

1. Install the built package (NSIS/DMG/.deb)
2. Verify `devbrain --version` outputs correctly
3. Verify `devbrain-daemon` starts and responds on `/api/v1/health`
4. Verify tray app process launches without crash (exit code 0 after 10s)
5. Verify uninstall cleans up (PATH, autostart, but preserves `~/.devbrain/` data)

### Existing Tests

No changes to existing 54 xUnit tests. Daemon and CLI behavior unchanged.

---

## Out of Scope (v1)

| Item | Rationale |
|------|-----------|
| Code signing | Ship unsigned, add as a follow-up PR ($220/year, half-day setup) |
| Electron autoUpdater | Package managers handle updates |
| In-app settings UI in tray | Dashboard Settings page already exists |
| ARM64 Electron builds | Start x64 only. Add ARM64 macOS when signing is in place |
| Flatpak / Snap | APT + AppImage covers Linux |
| CLI shell completions | Nice-to-have, separate effort |
| Multi-user / team features | DevBrain is a single-dev tool |

---

## File Inventory

New files to create:

| File | Purpose |
|------|---------|
| `package.json` (root) | npm workspaces config |
| `packages/tray/package.json` | Electron + electron-builder deps |
| `packages/tray/electron-builder.yml` | Platform build config, autoLaunch, binary paths |
| `packages/tray/tsconfig.json` | TypeScript config for tray app |
| `packages/tray/src/main.ts` | Electron entry — tray icon, context menu |
| `packages/tray/src/bootstrap.ts` | First-run orchestrator |
| `packages/tray/src/daemon.ts` | Daemon spawn/monitor/restart |
| `packages/tray/src/health.ts` | Health polling, tray icon state machine |
| `packages/tray/src/notifications.ts` | OS notification helpers |
| `packages/tray/assets/icon.png` | Tray icon (base) |
| `packages/tray/assets/icon.ico` | Windows tray icon |
| `packages/tray/assets/icon.icns` | macOS tray icon |
| `packages/tray/__tests__/bootstrap.test.ts` | Bootstrap unit tests |
| `packages/tray/__tests__/daemon.test.ts` | Daemon lifecycle tests |
| `packages/tray/__tests__/health.test.ts` | Health state tests |
| `packages/homebrew/devbrain.rb` | Homebrew formula |
| `packages/winget/DevBrain.DevBrain.yaml` | winget manifest |
| `packages/apt/debian/control` | Debian package metadata |
| `packages/apt/debian/postinst` | Post-install script |
| `packages/apt/debian/prerm` | Pre-remove script |
| `packages/apt/debian/rules` | Build rules |
| `.github/workflows/package.yml` | Electron build + package manager publish |

Files to modify:

| File | Change |
|------|--------|
| `.github/workflows/build.yml` | Add electron build stage dependency |
| `.gitignore` | Add `packages/tray/dist/`, `packages/tray/node_modules/` |
