# Single-Click Packaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship DevBrain as cross-platform packages (winget, brew, apt) with an Electron tray app that manages daemon lifecycle, Ollama bootstrapping, and auto-launch at login.

**Architecture:** An Electron menubar app embeds the existing .NET daemon + CLI binaries, manages their lifecycle, and handles first-run setup (config creation, Ollama install). Package managers (winget, Homebrew, APT) distribute the Electron app as an NSIS installer, DMG, or .deb. CI/CD builds everything and publishes to package registries on tagged releases.

**Tech Stack:** Electron 36+, TypeScript, electron-builder, Jest, GitHub Actions, Homebrew formula, winget manifest, Debian packaging.

**Spec:** `docs/superpowers/specs/2026-04-07-single-click-packaging-design.md`

---

## File Structure

### New Files

```
package.json                                    # Root npm workspaces config
packages/
  tray/
    package.json                                # Electron + electron-builder deps
    tsconfig.json                               # TypeScript config (Node/ES2022)
    jest.config.js                              # Jest config for ts-jest
    electron-builder.yml                        # Platform build targets, autoLaunch, extraResources
    src/
      main.ts                                   # Electron entry — app lifecycle, tray creation, menu
      notifications.ts                          # Show/update OS-native notifications
      health.ts                                 # Poll daemon health, emit state changes
      daemon.ts                                 # Spawn/stop/restart devbrain-daemon, PID management
      bootstrap.ts                              # First-run: config, Ollama install, model pull
      paths.ts                                  # Resolve platform-specific paths (data dir, binaries, icons)
    assets/
      icon.png                                  # 256x256 tray icon (green state — base)
      icon-yellow.png                           # Yellow state (starting/bootstrapping)
      icon-red.png                              # Red state (stopped/error)
      icon.ico                                  # Windows .ico (multi-resolution)
      icon.icns                                 # macOS .icns
    build/
      installer.nsh                             # NSIS script for PATH manipulation (Windows)
      linux-after-install.sh                    # Post-install: symlinks + autostart (Linux .deb)
      linux-after-remove.sh                     # Pre-remove: cleanup (Linux .deb)
    __tests__/
      health.test.ts                            # Health state machine transitions
      daemon.test.ts                            # Spawn/restart/crash logic
      bootstrap.test.ts                         # Config creation, Ollama detection, idempotency
  homebrew/
    devbrain.rb                                 # Homebrew formula
  winget/
    DevBrain.DevBrain.yaml                      # winget manifest (version template)
  apt/
    debian/
      control                                   # Package metadata + dependencies
      postinst                                  # Symlinks + autostart registration
      prerm                                     # Stop daemon + tray, remove autostart
      rules                                     # dpkg-buildpackage rules
      devbrain.desktop                          # XDG autostart .desktop file
.github/
  workflows/
    package.yml                                 # Electron build + package manager publish
```

### Modified Files

```
.gitignore                                      # Add packages/tray/dist/, packages/tray/node_modules/
src/DevBrain.Cli/Commands/StartCommand.cs       # Add tray.lock check
src/DevBrain.Cli/Commands/StopCommand.cs        # Add tray.lock check + stopped sentinel
```

---

## Task 1: Project Scaffolding — npm Workspaces + Electron Skeleton

**Files:**
- Create: `package.json` (root)
- Create: `packages/tray/package.json`
- Create: `packages/tray/tsconfig.json`
- Modify: `.gitignore`

- [ ] **Step 1: Create root package.json for npm workspaces**

```json
{
  "name": "devbrain",
  "private": true,
  "workspaces": [
    "dashboard",
    "packages/tray"
  ]
}
```

Write to: `package.json` (project root)

- [ ] **Step 2: Create packages/tray/package.json**

```json
{
  "name": "devbrain-tray",
  "version": "1.0.0",
  "private": true,
  "main": "dist/main.js",
  "scripts": {
    "build": "tsc",
    "start": "npm run build && electron dist/main.js",
    "test": "jest --config jest.config.js",
    "pack": "npm run build && electron-builder --dir",
    "dist": "npm run build && electron-builder"
  },
  "dependencies": {
    "electron-log": "^5.3.0"
  },
  "devDependencies": {
    "@types/node": "^24.12.2",
    "electron": "^36.0.0",
    "electron-builder": "^26.0.0",
    "jest": "^30.0.0",
    "@types/jest": "^30.0.0",
    "ts-jest": "^29.3.0",
    "typescript": "~6.0.2"
  }
}
```

Write to: `packages/tray/package.json`

- [ ] **Step 3: Create packages/tray/tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "commonjs",
    "lib": ["ES2022"],
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true,
    "resolveJsonModule": true,
    "declaration": false,
    "sourceMap": true
  },
  "include": ["src/**/*"],
  "exclude": ["__tests__", "dist", "node_modules"]
}
```

Write to: `packages/tray/tsconfig.json`

- [ ] **Step 4: Update .gitignore**

Add these lines to `.gitignore`:

```
packages/tray/dist/
packages/tray/node_modules/
packages/tray/resources/bin/
packages/tray/resources/wwwroot/
```

- [ ] **Step 5: Install dependencies**

Run: `cd packages/tray && npm install`

Expected: `node_modules/` created, no errors.

- [ ] **Step 6: Verify workspace setup**

Run from project root: `npm ls --workspaces`

Expected: Lists `devbrain-dashboard` and `devbrain-tray` as workspaces.

- [ ] **Step 7: Commit**

```bash
git add package.json packages/tray/package.json packages/tray/tsconfig.json .gitignore
git commit -m "feat(packaging): scaffold Electron tray app with npm workspaces"
```

---

## Task 2: Platform Paths Module

**Files:**
- Create: `packages/tray/src/paths.ts`

This module resolves all platform-specific paths. Every other module imports from here — no hardcoded paths elsewhere.

- [ ] **Step 1: Write paths.ts**

```typescript
import * as path from "path";
import * as os from "os";
import { app } from "electron";

/** ~/.devbrain on all platforms */
export function dataDir(): string {
  return path.join(os.homedir(), ".devbrain");
}

/** ~/.devbrain/settings.toml */
export function settingsPath(): string {
  return path.join(dataDir(), "settings.toml");
}

/** ~/.devbrain/daemon.pid */
export function pidPath(): string {
  return path.join(dataDir(), "daemon.pid");
}

/** ~/.devbrain/tray.lock */
export function trayLockPath(): string {
  return path.join(dataDir(), "tray.lock");
}

/** ~/.devbrain/stopped — sentinel written by CLI to prevent tray auto-restart */
export function stoppedSentinelPath(): string {
  return path.join(dataDir(), "stopped");
}

/** ~/.devbrain/logs/ */
export function logsDir(): string {
  return path.join(dataDir(), "logs");
}

/**
 * Resolve path to an embedded binary (devbrain-daemon or devbrain).
 * In dev: looks in resources/bin/ relative to project.
 * In packaged app: looks in resources/bin/ inside the asar/resources.
 */
export function binaryPath(name: string): string {
  const ext = process.platform === "win32" ? ".exe" : "";
  const binaryName = `${name}${ext}`;

  if (app.isPackaged) {
    return path.join(process.resourcesPath, "bin", binaryName);
  }

  // Dev mode: expect binaries in resources/bin/ relative to project
  return path.join(__dirname, "..", "resources", "bin", binaryName);
}

/**
 * Resolve tray icon path by state.
 * @param state - "green" | "yellow" | "red"
 */
export function iconPath(state: "green" | "yellow" | "red"): string {
  const suffix = state === "green" ? "" : `-${state}`;
  const ext = process.platform === "win32" ? ".ico" : ".png";
  const filename = `icon${suffix}${ext}`;

  if (app.isPackaged) {
    return path.join(process.resourcesPath, "assets", filename);
  }

  return path.join(__dirname, "..", "assets", filename);
}
```

Write to: `packages/tray/src/paths.ts`

- [ ] **Step 2: Verify it compiles**

Run: `cd packages/tray && npx tsc --noEmit`

Expected: No errors (Electron types resolve `app` import).

- [ ] **Step 3: Commit**

```bash
git add packages/tray/src/paths.ts
git commit -m "feat(packaging): add platform path resolution module"
```

---

## Task 3: Notifications Module

**Files:**
- Create: `packages/tray/src/notifications.ts`

Thin wrapper over Electron's `Notification` API.

- [ ] **Step 1: Write notifications.ts**

```typescript
import { Notification } from "electron";

const APP_NAME = "DevBrain";

export function showInfo(title: string, body: string): void {
  new Notification({ title: `${APP_NAME}: ${title}`, body }).show();
}

export function showError(title: string, body: string): void {
  new Notification({
    title: `${APP_NAME}: ${title}`,
    body,
    urgency: "critical",
  }).show();
}

export function showProgress(title: string, body: string): Notification {
  const n = new Notification({ title: `${APP_NAME}: ${title}`, body });
  n.show();
  return n;
}
```

Write to: `packages/tray/src/notifications.ts`

- [ ] **Step 2: Verify it compiles**

Run: `cd packages/tray && npx tsc --noEmit`

Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add packages/tray/src/notifications.ts
git commit -m "feat(packaging): add OS notification helpers"
```

---

## Task 4: Health Monitor — TDD

**Files:**
- Create: `packages/tray/jest.config.js`
- Create: `packages/tray/__tests__/health.test.ts`
- Create: `packages/tray/src/health.ts`

The health monitor polls the daemon's `/api/v1/health` endpoint and emits state transitions.

- [ ] **Step 1: Create Jest config**

```javascript
module.exports = {
  preset: "ts-jest",
  testEnvironment: "node",
  roots: ["<rootDir>/__tests__"],
  testMatch: ["**/*.test.ts"],
};
```

Write to: `packages/tray/jest.config.js`

- [ ] **Step 2: Write the failing tests**

```typescript
import { HealthMonitor, HealthState } from "../src/health";

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

describe("HealthMonitor", () => {
  let monitor: HealthMonitor;
  let states: HealthState[];

  beforeEach(() => {
    jest.useFakeTimers();
    states = [];
    monitor = new HealthMonitor(1000);
    monitor.on("stateChange", (s: HealthState) => states.push(s));
    mockFetch.mockReset();
  });

  afterEach(() => {
    monitor.stop();
    jest.useRealTimers();
  });

  it("starts in 'starting' state", () => {
    expect(monitor.state).toBe("starting");
  });

  it("transitions to 'healthy' on successful health check", async () => {
    mockFetch.mockResolvedValueOnce({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    expect(monitor.state).toBe("healthy");
    expect(states).toEqual(["healthy"]);
  });

  it("transitions to 'unhealthy' on failed health check", async () => {
    mockFetch.mockRejectedValueOnce(new Error("ECONNREFUSED"));
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    expect(monitor.state).toBe("unhealthy");
    expect(states).toEqual(["unhealthy"]);
  });

  it("transitions healthy -> unhealthy -> healthy", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })
      .mockRejectedValueOnce(new Error("ECONNREFUSED"))
      .mockResolvedValueOnce({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    await jest.advanceTimersByTimeAsync(1000);
    await jest.advanceTimersByTimeAsync(1000);
    expect(states).toEqual(["healthy", "unhealthy", "healthy"]);
  });

  it("does not emit duplicate states", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    await jest.advanceTimersByTimeAsync(1000);
    expect(states).toEqual(["healthy"]);
  });

  it("stop() clears the polling interval", async () => {
    mockFetch.mockResolvedValue({ ok: true });
    monitor.start();
    await jest.advanceTimersByTimeAsync(1000);
    monitor.stop();
    mockFetch.mockReset();
    await jest.advanceTimersByTimeAsync(5000);
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
```

Write to: `packages/tray/__tests__/health.test.ts`

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd packages/tray && npx jest`

Expected: FAIL — `Cannot find module '../src/health'`

- [ ] **Step 4: Implement health.ts**

```typescript
import { EventEmitter } from "events";

export type HealthState = "starting" | "healthy" | "unhealthy";

const DAEMON_URL = "http://127.0.0.1:37800/api/v1/health";

export class HealthMonitor extends EventEmitter {
  private _state: HealthState = "starting";
  private timer: ReturnType<typeof setInterval> | null = null;
  private pollIntervalMs: number;

  constructor(pollIntervalMs = 5000) {
    super();
    this.pollIntervalMs = pollIntervalMs;
  }

  get state(): HealthState {
    return this._state;
  }

  start(): void {
    this.timer = setInterval(() => this.check(), this.pollIntervalMs);
  }

  stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  private async check(): Promise<void> {
    let newState: HealthState;

    try {
      const res = await fetch(DAEMON_URL);
      newState = res.ok ? "healthy" : "unhealthy";
    } catch {
      newState = "unhealthy";
    }

    if (newState !== this._state) {
      this._state = newState;
      this.emit("stateChange", newState);
    }
  }
}
```

Write to: `packages/tray/src/health.ts`

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd packages/tray && npx jest`

Expected: 5 passing tests.

- [ ] **Step 6: Commit**

```bash
git add packages/tray/jest.config.js packages/tray/__tests__/health.test.ts packages/tray/src/health.ts
git commit -m "feat(packaging): add daemon health monitor with TDD"
```

---

## Task 5: Daemon Manager — TDD

**Files:**
- Create: `packages/tray/__tests__/daemon.test.ts`
- Create: `packages/tray/src/daemon.ts`

Manages spawning, stopping, and auto-restarting the daemon process.

- [ ] **Step 1: Write the failing tests**

```typescript
import { DaemonManager } from "../src/daemon";
import * as child_process from "child_process";
import * as fs from "fs";

jest.mock("child_process");
jest.mock("fs");

const mockSpawn = child_process.spawn as jest.MockedFunction<typeof child_process.spawn>;
const mockExistsSync = fs.existsSync as jest.MockedFunction<typeof fs.existsSync>;
const mockReadFileSync = fs.readFileSync as jest.MockedFunction<typeof fs.readFileSync>;
const mockWriteFileSync = fs.writeFileSync as jest.MockedFunction<typeof fs.writeFileSync>;
const mockUnlinkSync = fs.unlinkSync as jest.MockedFunction<typeof fs.unlinkSync>;
const mockMkdirSync = fs.mkdirSync as jest.MockedFunction<typeof fs.mkdirSync>;

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

jest.mock("../src/paths", () => ({
  binaryPath: (name: string) => `/mock/bin/${name}`,
  pidPath: () => "/mock/.devbrain/daemon.pid",
  dataDir: () => "/mock/.devbrain",
  stoppedSentinelPath: () => "/mock/.devbrain/stopped",
  logsDir: () => "/mock/.devbrain/logs",
}));

jest.mock("../src/notifications", () => ({
  showInfo: jest.fn(),
  showError: jest.fn(),
  showProgress: jest.fn(() => ({ close: jest.fn() })),
}));

describe("DaemonManager", () => {
  let daemon: DaemonManager;
  let mockProcess: Partial<child_process.ChildProcess>;

  beforeEach(() => {
    jest.clearAllMocks();
    mockProcess = {
      pid: 12345,
      on: jest.fn(),
      unref: jest.fn(),
    };
    mockSpawn.mockReturnValue(mockProcess as child_process.ChildProcess);
    mockExistsSync.mockReturnValue(false);
    mockMkdirSync.mockReturnValue(undefined);
    daemon = new DaemonManager();
  });

  describe("start()", () => {
    it("spawns devbrain-daemon as a detached process", async () => {
      mockFetch.mockResolvedValue({ ok: true });
      await daemon.start();
      expect(mockSpawn).toHaveBeenCalledWith(
        "/mock/bin/devbrain-daemon",
        [],
        expect.objectContaining({ detached: true, stdio: "ignore" })
      );
    });

    it("writes PID file after spawning", async () => {
      mockFetch.mockResolvedValue({ ok: true });
      await daemon.start();
      expect(mockWriteFileSync).toHaveBeenCalledWith(
        "/mock/.devbrain/daemon.pid",
        "12345"
      );
    });

    it("clears stopped sentinel before starting", async () => {
      mockExistsSync.mockImplementation((p) =>
        String(p) === "/mock/.devbrain/stopped"
      );
      mockFetch.mockResolvedValue({ ok: true });
      await daemon.start();
      expect(mockUnlinkSync).toHaveBeenCalledWith("/mock/.devbrain/stopped");
    });
  });

  describe("stop()", () => {
    it("kills process by PID from file", async () => {
      mockExistsSync.mockImplementation((p) =>
        String(p) === "/mock/.devbrain/daemon.pid"
      );
      mockReadFileSync.mockReturnValue("12345");
      const killMock = jest.fn();
      jest.spyOn(process, "kill").mockImplementation(killMock);

      await daemon.stop();

      expect(killMock).toHaveBeenCalledWith(12345);
      expect(mockUnlinkSync).toHaveBeenCalledWith("/mock/.devbrain/daemon.pid");
      (process.kill as jest.Mock).mockRestore();
    });
  });

  describe("auto-restart", () => {
    it("starts with zero crash count", () => {
      expect(daemon.crashCount).toBe(0);
    });

    it("stops restarting after 3 crashes", () => {
      daemon.recordCrash();
      daemon.recordCrash();
      daemon.recordCrash();
      expect(daemon.shouldRestart()).toBe(false);
    });

    it("allows restart after manual reset", () => {
      daemon.recordCrash();
      daemon.recordCrash();
      daemon.recordCrash();
      daemon.resetCrashCount();
      expect(daemon.shouldRestart()).toBe(true);
    });
  });
});
```

Write to: `packages/tray/__tests__/daemon.test.ts`

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd packages/tray && npx jest daemon`

Expected: FAIL — `Cannot find module '../src/daemon'`

- [ ] **Step 3: Implement daemon.ts**

```typescript
import { spawn, ChildProcess } from "child_process";
import * as fs from "fs";
import {
  binaryPath,
  pidPath,
  dataDir,
  stoppedSentinelPath,
  logsDir,
} from "./paths";
import { showError } from "./notifications";

const MAX_CRASHES = 3;
const CRASH_WINDOW_MS = 5 * 60 * 1000;

export class DaemonManager {
  private process: ChildProcess | null = null;
  private crashes: number[] = [];
  private onCrashCallback: (() => void) | null = null;
  private onExhaustedCallback: (() => void) | null = null;

  get crashCount(): number {
    return this.crashes.length;
  }

  shouldRestart(): boolean {
    const now = Date.now();
    this.crashes = this.crashes.filter((t) => now - t < CRASH_WINDOW_MS);
    return this.crashes.length < MAX_CRASHES;
  }

  recordCrash(): void {
    this.crashes.push(Date.now());
  }

  resetCrashCount(): void {
    this.crashes = [];
  }

  onCrash(cb: () => void): void {
    this.onCrashCallback = cb;
  }

  onRestartsExhausted(cb: () => void): void {
    this.onExhaustedCallback = cb;
  }

  async start(): Promise<void> {
    const sentinel = stoppedSentinelPath();
    if (fs.existsSync(sentinel)) {
      fs.unlinkSync(sentinel);
    }

    const data = dataDir();
    if (!fs.existsSync(data)) {
      fs.mkdirSync(data, { recursive: true });
    }
    const logs = logsDir();
    if (!fs.existsSync(logs)) {
      fs.mkdirSync(logs, { recursive: true });
    }

    const daemonBin = binaryPath("devbrain-daemon");

    this.process = spawn(daemonBin, [], {
      detached: true,
      stdio: "ignore",
    });

    this.process.unref();

    if (this.process.pid) {
      fs.writeFileSync(pidPath(), String(this.process.pid));
    }

    this.process.on("exit", (code) => {
      if (fs.existsSync(stoppedSentinelPath())) {
        return;
      }

      if (code !== 0 && code !== null) {
        this.recordCrash();
        this.onCrashCallback?.();

        if (this.shouldRestart()) {
          this.start();
        } else {
          showError(
            "Daemon crashed",
            "DevBrain daemon crashed 3 times in 5 minutes. Use the tray menu to restart."
          );
          this.onExhaustedCallback?.();
        }
      }
    });
  }

  async stop(): Promise<void> {
    const pid = pidPath();

    if (fs.existsSync(pid)) {
      const pidValue = parseInt(fs.readFileSync(pid, "utf-8").trim(), 10);

      try {
        process.kill(pidValue);
      } catch {
        // Process already dead
      }

      fs.unlinkSync(pid);
    }

    this.process = null;
  }

  async restart(): Promise<void> {
    await this.stop();
    this.resetCrashCount();
    await this.start();
  }

  isRunning(): boolean {
    const pid = pidPath();
    if (!fs.existsSync(pid)) return false;

    const pidValue = parseInt(fs.readFileSync(pid, "utf-8").trim(), 10);

    try {
      process.kill(pidValue, 0);
      return true;
    } catch {
      return false;
    }
  }
}
```

Write to: `packages/tray/src/daemon.ts`

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd packages/tray && npx jest daemon`

Expected: All daemon tests pass.

- [ ] **Step 5: Commit**

```bash
git add packages/tray/__tests__/daemon.test.ts packages/tray/src/daemon.ts
git commit -m "feat(packaging): add daemon lifecycle manager with auto-restart"
```

---

## Task 6: Bootstrap Orchestrator — TDD

**Files:**
- Create: `packages/tray/__tests__/bootstrap.test.ts`
- Create: `packages/tray/src/bootstrap.ts`

First-run flow: config creation, Ollama detection, model pull.

- [ ] **Step 1: Write the failing tests**

```typescript
import { Bootstrap } from "../src/bootstrap";
import * as fs from "fs";
import * as child_process from "child_process";

jest.mock("fs");
jest.mock("child_process");

const mockExistsSync = fs.existsSync as jest.MockedFunction<typeof fs.existsSync>;
const mockWriteFileSync = fs.writeFileSync as jest.MockedFunction<typeof fs.writeFileSync>;
const mockMkdirSync = fs.mkdirSync as jest.MockedFunction<typeof fs.mkdirSync>;
const mockExecFileSync = child_process.execFileSync as jest.MockedFunction<typeof child_process.execFileSync>;

const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

jest.mock("../src/paths", () => ({
  dataDir: () => "/mock/.devbrain",
  settingsPath: () => "/mock/.devbrain/settings.toml",
}));

jest.mock("../src/notifications", () => ({
  showInfo: jest.fn(),
  showError: jest.fn(),
  showProgress: jest.fn(() => ({ close: jest.fn() })),
}));

describe("Bootstrap", () => {
  let bootstrap: Bootstrap;

  beforeEach(() => {
    jest.clearAllMocks();
    mockExistsSync.mockReturnValue(false);
    mockMkdirSync.mockReturnValue(undefined);
    bootstrap = new Bootstrap();
  });

  describe("ensureConfig()", () => {
    it("creates settings.toml when missing", async () => {
      mockExistsSync.mockReturnValue(false);
      await bootstrap.ensureConfig();
      expect(mockWriteFileSync).toHaveBeenCalledWith(
        "/mock/.devbrain/settings.toml",
        expect.stringContaining("[daemon]")
      );
    });

    it("skips config creation when file exists", async () => {
      mockExistsSync.mockImplementation((p) => String(p).endsWith("settings.toml"));
      await bootstrap.ensureConfig();
      expect(mockWriteFileSync).not.toHaveBeenCalled();
    });
  });

  describe("isOllamaInstalled()", () => {
    it("returns true when Ollama API responds", async () => {
      mockFetch.mockResolvedValueOnce({ ok: true });
      const result = await bootstrap.isOllamaInstalled();
      expect(result).toBe(true);
    });

    it("returns false when Ollama API is unreachable", async () => {
      mockFetch.mockRejectedValueOnce(new Error("ECONNREFUSED"));
      const result = await bootstrap.isOllamaInstalled();
      expect(result).toBe(false);
    });
  });

  describe("isModelPulled()", () => {
    it("returns true when model is in ollama list output", async () => {
      mockExecFileSync.mockReturnValue(Buffer.from("llama3.2:3b\t3.2GB\n"));
      const result = await bootstrap.isModelPulled("llama3.2:3b");
      expect(result).toBe(true);
    });

    it("returns false when model is not found", async () => {
      mockExecFileSync.mockReturnValue(Buffer.from(""));
      const result = await bootstrap.isModelPulled("llama3.2:3b");
      expect(result).toBe(false);
    });

    it("returns false when ollama command fails", async () => {
      mockExecFileSync.mockImplementation(() => {
        throw new Error("command not found");
      });
      const result = await bootstrap.isModelPulled("llama3.2:3b");
      expect(result).toBe(false);
    });
  });

  describe("idempotency", () => {
    it("running ensureConfig twice does not overwrite existing config", async () => {
      mockExistsSync
        .mockReturnValueOnce(false)  // dataDir check
        .mockReturnValueOnce(false)  // settings check (first call)
        .mockReturnValueOnce(true)   // dataDir check
        .mockReturnValueOnce(true);  // settings check (second call)

      await bootstrap.ensureConfig();
      await bootstrap.ensureConfig();

      expect(mockWriteFileSync).toHaveBeenCalledTimes(1);
    });
  });
});
```

Write to: `packages/tray/__tests__/bootstrap.test.ts`

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd packages/tray && npx jest bootstrap`

Expected: FAIL — `Cannot find module '../src/bootstrap'`

- [ ] **Step 3: Implement bootstrap.ts**

```typescript
import * as fs from "fs";
import { execFileSync } from "child_process";
import { dataDir, settingsPath } from "./paths";
import { showInfo, showError, showProgress } from "./notifications";

const OLLAMA_API = "http://localhost:11434/api/version";
const DEFAULT_MODEL = "llama3.2:3b";

const DEFAULT_SETTINGS = `[daemon]
port = 37800
log_level = "info"

[capture]
enabled = true
sources = ["ai-sessions"]
privacy_mode = "redact"
max_observation_size_kb = 512
thread_gap_hours = 2

[storage]
sqlite_max_size_mb = 2048
retention_days = 365

[llm.local]
enabled = true
provider = "ollama"
model = "llama3.2:3b"
endpoint = "http://localhost:11434"
max_concurrent = 2

[llm.cloud]
enabled = true
provider = "anthropic"
api_key_env = "DEVBRAIN_CLOUD_API_KEY"
max_daily_requests = 50

[agents.briefing]
enabled = true
schedule = "0 7 * * *"

[agents.dead_end]
enabled = true
sensitivity = "medium"

[agents.compression]
enabled = true
idle_minutes = 60
`;

export class Bootstrap {
  async ensureConfig(): Promise<void> {
    const dir = dataDir();
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true });
    }

    const settings = settingsPath();
    if (!fs.existsSync(settings)) {
      fs.writeFileSync(settings, DEFAULT_SETTINGS);
    }
  }

  async isOllamaInstalled(): Promise<boolean> {
    try {
      const res = await fetch(OLLAMA_API);
      return res.ok;
    } catch {
      return false;
    }
  }

  async isModelPulled(model: string): Promise<boolean> {
    try {
      const output = execFileSync("ollama", ["list"], {
        encoding: "utf-8",
        timeout: 10000,
      });
      return output.includes(model);
    } catch {
      return false;
    }
  }

  async installOllama(): Promise<boolean> {
    showProgress("Setup", "Installing local AI runtime (first time only)...");

    try {
      if (process.platform === "win32") {
        await this.installOllamaWindows();
      } else if (process.platform === "darwin") {
        await this.installOllamaMac();
      } else {
        await this.installOllamaLinux();
      }
      return true;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      showError(
        "Ollama install failed",
        `Could not install Ollama: ${msg}. DevBrain works without it — add a cloud API key in Settings.`
      );
      return false;
    }
  }

  async pullModel(model: string): Promise<boolean> {
    showProgress("Setup", "Downloading AI model (~2GB)...");

    try {
      execFileSync("ollama", ["pull", model], {
        timeout: 600000,
        stdio: "ignore",
      });
      return true;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      showError("Model download failed", `Could not download ${model}: ${msg}`);
      return false;
    }
  }

  /**
   * Run the full bootstrap flow. Non-blocking — daemon starts first,
   * Ollama install happens in background.
   */
  async run(startDaemon: () => Promise<void>): Promise<void> {
    await this.ensureConfig();
    await startDaemon();

    // Ollama setup in background — non-blocking
    this.bootstrapOllama().catch(() => {
      // Errors already shown via notifications
    });
  }

  private async bootstrapOllama(): Promise<void> {
    const installed = await this.isOllamaInstalled();
    if (!installed) {
      const success = await this.installOllama();
      if (!success) return;
    }

    const pulled = await this.isModelPulled(DEFAULT_MODEL);
    if (!pulled) {
      await this.pullModel(DEFAULT_MODEL);
    }

    showInfo("Ready", "DevBrain is ready with local AI.");
  }

  private async installOllamaWindows(): Promise<void> {
    const tmpPath = `${process.env.TEMP || "C:\\Temp"}\\OllamaSetup.exe`;
    execFileSync("powershell", [
      "-Command",
      `Invoke-WebRequest -Uri 'https://ollama.com/download/OllamaSetup.exe' -OutFile '${tmpPath}'`,
    ], { timeout: 300000 });
    execFileSync(tmpPath, ["/S"], { timeout: 300000 });
  }

  private async installOllamaMac(): Promise<void> {
    try {
      execFileSync("brew", ["install", "ollama"], { timeout: 300000 });
    } catch {
      execFileSync("bash", ["-c", "curl -fsSL https://ollama.com/install.sh | sh"], { timeout: 300000 });
    }
  }

  private async installOllamaLinux(): Promise<void> {
    execFileSync("bash", ["-c", "curl -fsSL https://ollama.com/install.sh | sh"], { timeout: 300000 });
  }
}
```

Write to: `packages/tray/src/bootstrap.ts`

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd packages/tray && npx jest bootstrap`

Expected: All bootstrap tests pass.

- [ ] **Step 5: Commit**

```bash
git add packages/tray/__tests__/bootstrap.test.ts packages/tray/src/bootstrap.ts
git commit -m "feat(packaging): add first-run bootstrap orchestrator"
```

---

## Task 7: Electron Main Entry — Tray Icon + Context Menu

**Files:**
- Create: `packages/tray/src/main.ts`
- Create: `packages/tray/assets/` (placeholder icons)

This is the Electron entry point — creates the tray icon, wires up the context menu, and orchestrates health + daemon + bootstrap.

- [ ] **Step 1: Create placeholder tray icon assets**

Run:
```bash
mkdir -p packages/tray/assets
```

Create minimal placeholder files for each icon. These will be replaced with real designed icons before release. For now, copy the dashboard favicon or create empty files:

```bash
echo "placeholder" > packages/tray/assets/icon.png
echo "placeholder" > packages/tray/assets/icon-yellow.png
echo "placeholder" > packages/tray/assets/icon-red.png
echo "placeholder" > packages/tray/assets/icon.ico
echo "placeholder" > packages/tray/assets/icon.icns
```

Note: Real icon assets (proper .ico, .icns, multi-resolution PNGs) must be designed and added before first release. electron-builder can generate .ico and .icns from a 1024x1024 PNG source.

- [ ] **Step 2: Write main.ts**

```typescript
import { app, Tray, Menu, shell, nativeImage } from "electron";
import * as fs from "fs";
import { HealthMonitor, HealthState } from "./health";
import { DaemonManager } from "./daemon";
import { Bootstrap } from "./bootstrap";
import { iconPath, trayLockPath, dataDir, logsDir } from "./paths";

let tray: Tray | null = null;
let healthMonitor: HealthMonitor;
let daemonManager: DaemonManager;
let bootstrap: Bootstrap;
let currentState: HealthState = "starting";

function createTray(): void {
  const icon = nativeImage.createFromPath(iconPath("green"));
  tray = new Tray(icon);
  tray.setToolTip("DevBrain (Starting...)");
  updateMenu();
}

function updateMenu(): void {
  if (!tray) return;

  const statusLabel =
    currentState === "healthy"
      ? "DevBrain (Running)"
      : currentState === "unhealthy"
        ? "DevBrain (Stopped)"
        : "DevBrain (Starting...)";

  const template = Menu.buildFromTemplate([
    { label: statusLabel, enabled: false },
    { type: "separator" },
    {
      label: "Open Dashboard",
      click: () => shell.openExternal("http://localhost:37800"),
      enabled: currentState === "healthy",
    },
    { type: "separator" },
    {
      label: "Start Daemon",
      click: async () => {
        await daemonManager.start();
        healthMonitor.start();
      },
      enabled: currentState !== "healthy",
    },
    {
      label: "Stop Daemon",
      click: async () => {
        await daemonManager.stop();
      },
      enabled: currentState === "healthy",
    },
    {
      label: "Restart Daemon",
      click: async () => {
        await daemonManager.restart();
      },
    },
    { type: "separator" },
    {
      label: "View Logs",
      click: () => {
        const logs = logsDir();
        if (!fs.existsSync(logs)) {
          fs.mkdirSync(logs, { recursive: true });
        }
        shell.openPath(logs);
      },
    },
    { type: "separator" },
    {
      label: "Quit DevBrain",
      click: async () => {
        await daemonManager.stop();
        removeTrayLock();
        app.quit();
      },
    },
  ]);

  tray.setContextMenu(template);
}

function updateTrayIcon(state: HealthState): void {
  if (!tray) return;

  currentState = state;

  const iconState =
    state === "healthy" ? "green" : state === "unhealthy" ? "red" : "yellow";

  const icon = nativeImage.createFromPath(iconPath(iconState));
  tray.setImage(icon);

  const tooltip =
    state === "healthy"
      ? "DevBrain (Running)"
      : state === "unhealthy"
        ? "DevBrain (Stopped)"
        : "DevBrain (Starting...)";
  tray.setToolTip(tooltip);

  updateMenu();
}

function writeTrayLock(): void {
  const lockPath = trayLockPath();
  const dir = dataDir();
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  fs.writeFileSync(lockPath, String(process.pid));
}

function removeTrayLock(): void {
  try {
    fs.unlinkSync(trayLockPath());
  } catch {
    // Best-effort
  }
}

app.whenReady().then(async () => {
  const gotLock = app.requestSingleInstanceLock();
  if (!gotLock) {
    app.quit();
    return;
  }

  if (process.platform === "darwin") {
    app.dock.hide();
  }

  writeTrayLock();

  daemonManager = new DaemonManager();
  healthMonitor = new HealthMonitor();
  bootstrap = new Bootstrap();

  createTray();

  healthMonitor.on("stateChange", (state: HealthState) => {
    updateTrayIcon(state);
  });

  daemonManager.onRestartsExhausted(() => {
    updateTrayIcon("unhealthy");
  });

  await bootstrap.run(() => daemonManager.start());

  healthMonitor.start();
});

app.on("window-all-closed", (e: Event) => {
  e.preventDefault();
});

app.on("before-quit", () => {
  healthMonitor.stop();
  removeTrayLock();
});
```

Write to: `packages/tray/src/main.ts`

- [ ] **Step 3: Verify it compiles**

Run: `cd packages/tray && npx tsc --noEmit`

Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add packages/tray/src/main.ts packages/tray/assets/
git commit -m "feat(packaging): add Electron main entry with tray icon and context menu"
```

---

## Task 8: CLI / Tray Coordination

**Files:**
- Modify: `src/DevBrain.Cli/Commands/StartCommand.cs`
- Modify: `src/DevBrain.Cli/Commands/StopCommand.cs`

Add `tray.lock` checks so CLI and tray app don't fight over daemon management.

- [ ] **Step 1: Update StartCommand to check for tray.lock**

In `src/DevBrain.Cli/Commands/StartCommand.cs`, replace the `Execute` method body. After the existing health check on line 18, add tray lock detection before spawning the daemon:

```csharp
private static async Task Execute(ParseResult pr)
{
    var client = new DevBrainHttpClient();

    if (await client.IsHealthy())
    {
        ConsoleFormatter.PrintWarning("Daemon is already running.");
        return;
    }

    // Check if tray app is managing the daemon
    var dataPath = SettingsLoader.ResolveDataPath("~/.devbrain");
    var trayLockPath = Path.Combine(dataPath, "tray.lock");

    if (File.Exists(trayLockPath))
    {
        var lockPidText = (await File.ReadAllTextAsync(trayLockPath)).Trim();
        if (int.TryParse(lockPidText, out var trayPid))
        {
            try
            {
                Process.GetProcessById(trayPid);
                ConsoleFormatter.PrintWarning(
                    "Daemon is managed by the tray app. Use the tray menu to start it.");
                return;
            }
            catch (ArgumentException)
            {
                // Tray process is dead — stale lock, continue normally
            }
        }
    }

    var cliDir = AppContext.BaseDirectory;
    var daemonName = OperatingSystem.IsWindows() ? "devbrain-daemon.exe" : "devbrain-daemon";
    var daemonPath = Path.Combine(cliDir, daemonName);

    if (!File.Exists(daemonPath))
    {
        ConsoleFormatter.PrintError($"Daemon binary not found at: {daemonPath}");
        return;
    }

    var psi = new ProcessStartInfo
    {
        FileName = daemonPath,
        UseShellExecute = true,
        WindowStyle = ProcessWindowStyle.Hidden
    };

    try
    {
        Process.Start(psi);
    }
    catch (Exception ex)
    {
        ConsoleFormatter.PrintError($"Failed to start daemon: {ex.Message}");
        return;
    }

    Console.Write("Starting daemon");

    for (var i = 0; i < 20; i++)
    {
        await Task.Delay(500);
        Console.Write(".");

        if (await client.IsHealthy())
        {
            Console.WriteLine();
            ConsoleFormatter.PrintSuccess("Daemon started successfully.");
            return;
        }
    }

    Console.WriteLine();
    ConsoleFormatter.PrintError("Daemon did not become healthy within 10 seconds.");
}
```

Note: Ensure `using DevBrain.Core;` is present at the top for `SettingsLoader`.

- [ ] **Step 2: Update StopCommand to write stopped sentinel when tray is active**

In `src/DevBrain.Cli/Commands/StopCommand.cs`, replace the `Execute` method body. Before killing the daemon process, check for the tray lock and write a sentinel:

```csharp
private static async Task Execute(ParseResult pr)
{
    var dataPath = SettingsLoader.ResolveDataPath("~/.devbrain");
    var pidPath = Path.Combine(dataPath, "daemon.pid");

    if (!File.Exists(pidPath))
    {
        ConsoleFormatter.PrintWarning("No PID file found. Daemon may not be running.");
        return;
    }

    // If tray app is running, write stopped sentinel so it doesn't auto-restart
    var trayLockPath = Path.Combine(dataPath, "tray.lock");
    if (File.Exists(trayLockPath))
    {
        var trayPidText = (await File.ReadAllTextAsync(trayLockPath)).Trim();
        if (int.TryParse(trayPidText, out var trayPid))
        {
            try
            {
                Process.GetProcessById(trayPid);
                // Tray is alive — write sentinel to prevent auto-restart
                var sentinelPath = Path.Combine(dataPath, "stopped");
                await File.WriteAllTextAsync(sentinelPath, "stopped by cli");
            }
            catch (ArgumentException)
            {
                // Tray is dead — no sentinel needed
            }
        }
    }

    var pidText = (await File.ReadAllTextAsync(pidPath)).Trim();

    if (!int.TryParse(pidText, out var pid))
    {
        ConsoleFormatter.PrintError($"Invalid PID file content: {pidText}");
        return;
    }

    try
    {
        var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
        ConsoleFormatter.PrintSuccess($"Daemon (PID {pid}) stopped.");
    }
    catch (ArgumentException)
    {
        ConsoleFormatter.PrintWarning($"No process found with PID {pid}. Daemon may have already stopped.");
    }
    catch (Exception ex)
    {
        ConsoleFormatter.PrintError($"Failed to stop daemon: {ex.Message}");
    }

    try
    {
        File.Delete(pidPath);
    }
    catch
    {
        // Best-effort cleanup
    }
}
```

- [ ] **Step 3: Verify .NET build**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run existing tests**

Run: `dotnet test DevBrain.slnx`

Expected: All 54+ tests pass (no behavioral changes to existing functionality).

- [ ] **Step 5: Commit**

```bash
git add src/DevBrain.Cli/Commands/StartCommand.cs src/DevBrain.Cli/Commands/StopCommand.cs
git commit -m "feat(packaging): add CLI/tray coordination via tray.lock + stopped sentinel"
```

---

## Task 9: electron-builder Configuration

**Files:**
- Create: `packages/tray/electron-builder.yml`
- Create: `packages/tray/build/installer.nsh`
- Create: `packages/tray/build/linux-after-install.sh`
- Create: `packages/tray/build/linux-after-remove.sh`

Configures electron-builder to produce NSIS (Windows), DMG (macOS), and .deb (Linux).

- [ ] **Step 1: Write electron-builder.yml**

```yaml
appId: com.devbrain.tray
productName: DevBrain
copyright: Copyright 2026 DevBrain

extraResources:
  - from: resources/bin/
    to: bin/
    filter:
      - "**/*"
  - from: resources/wwwroot/
    to: wwwroot/
    filter:
      - "**/*"
  - from: assets/
    to: assets/
    filter:
      - "*.png"
      - "*.ico"
      - "*.icns"

win:
  target:
    - target: nsis
      arch: [x64]
  icon: assets/icon.ico

nsis:
  oneClick: true
  allowToChangeInstallationDirectory: false
  perMachine: false
  installerIcon: assets/icon.ico
  include: build/installer.nsh

mac:
  target:
    - target: dmg
      arch: [x64]
  icon: assets/icon.icns
  category: public.app-category.developer-tools

dmg:
  contents:
    - x: 130
      y: 220
    - x: 410
      y: 220
      type: link
      path: /Applications

linux:
  target:
    - target: deb
      arch: [x64]
    - target: AppImage
      arch: [x64]
  icon: assets/icon.png
  category: Development
  desktop:
    StartupWMClass: DevBrain

deb:
  depends:
    - libgtk-3-0
    - libnotify4
    - libnss3
  afterInstall: build/linux-after-install.sh
  afterRemove: build/linux-after-remove.sh
```

Write to: `packages/tray/electron-builder.yml`

- [ ] **Step 2: Create NSIS installer script for PATH**

```nsis
!macro customInstall
  nsExec::ExecToLog 'setx PATH "%PATH%;$INSTDIR\resources\bin"'
!macroend

!macro customUnInstall
  ; PATH cleanup is complex in NSIS — users can manually clean up
!macroend
```

Write to: `packages/tray/build/installer.nsh`

- [ ] **Step 3: Create Linux post-install script**

```bash
#!/bin/bash
set -e

ln -sf /opt/DevBrain/resources/bin/devbrain /usr/local/bin/devbrain
ln -sf /opt/DevBrain/resources/bin/devbrain-daemon /usr/local/bin/devbrain-daemon

mkdir -p /etc/xdg/autostart
cat > /etc/xdg/autostart/devbrain.desktop << 'EOF'
[Desktop Entry]
Type=Application
Name=DevBrain
Exec=/opt/DevBrain/devbrain-tray
Icon=/opt/DevBrain/resources/assets/icon.png
Comment=Developer's second brain
Categories=Development;
X-GNOME-Autostart-enabled=true
StartupNotify=false
Terminal=false
EOF
```

Write to: `packages/tray/build/linux-after-install.sh`

- [ ] **Step 4: Create Linux post-remove script**

```bash
#!/bin/bash
set -e

rm -f /usr/local/bin/devbrain
rm -f /usr/local/bin/devbrain-daemon
rm -f /etc/xdg/autostart/devbrain.desktop

pkill -f devbrain-daemon 2>/dev/null || true
pkill -f devbrain-tray 2>/dev/null || true

# NOTE: ~/.devbrain/ is intentionally preserved (user data)
```

Write to: `packages/tray/build/linux-after-remove.sh`

- [ ] **Step 5: Make Linux scripts executable**

Run: `chmod +x packages/tray/build/linux-after-install.sh packages/tray/build/linux-after-remove.sh`

- [ ] **Step 6: Commit**

```bash
git add packages/tray/electron-builder.yml packages/tray/build/
git commit -m "feat(packaging): add electron-builder config for Windows/macOS/Linux"
```

---

## Task 10: Package Manager Manifests

**Files:**
- Create: `packages/homebrew/devbrain.rb`
- Create: `packages/winget/DevBrain.DevBrain.yaml`
- Create: `packages/apt/debian/control`
- Create: `packages/apt/debian/postinst`
- Create: `packages/apt/debian/prerm`
- Create: `packages/apt/debian/rules`
- Create: `packages/apt/debian/devbrain.desktop`

- [ ] **Step 1: Create Homebrew formula**

```ruby
class Devbrain < Formula
  desc "Developer's second brain - captures coding sessions, builds knowledge graph"
  homepage "https://github.com/devbrain/devbrain"
  version "1.0.0"

  if OS.mac? && Hardware::CPU.arm?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-osx-arm64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  elsif OS.mac? && Hardware::CPU.intel?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-osx-x64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  elsif OS.linux? && Hardware::CPU.intel?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-linux-x64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  elsif OS.linux? && Hardware::CPU.arm?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-linux-arm64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  end

  def install
    bin.install "devbrain"
    bin.install "devbrain-daemon"
    prefix.install "DevBrain.app" if OS.mac?
  end

  # No Homebrew service block - the Electron tray app owns daemon lifecycle.

  def post_install
    # Tray app handles all user-space bootstrap on first launch.
  end

  test do
    assert_match "devbrain", shell_output("#{bin}/devbrain --version")
  end
end
```

Write to: `packages/homebrew/devbrain.rb`

- [ ] **Step 2: Create winget manifest**

```yaml
PackageIdentifier: DevBrain.DevBrain
PackageVersion: 1.0.0
DefaultLocale: en-US
PackageName: DevBrain
Publisher: DevBrain
PublisherUrl: https://github.com/devbrain/devbrain
License: Apache-2.0
ShortDescription: Developer's second brain - captures coding sessions, builds knowledge graph
Tags:
  - developer-tools
  - productivity
  - knowledge-graph
  - ai
InstallerType: exe
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/devbrain/devbrain/releases/download/v1.0.0/DevBrain-Setup-1.0.0-x64.exe
    InstallerSha256: PLACEHOLDER_SHA256
    InstallerSwitches:
      Silent: /S
      SilentWithProgress: /S
ManifestType: singleton
ManifestVersion: 1.6.0
```

Write to: `packages/winget/DevBrain.DevBrain.yaml`

- [ ] **Step 3: Create APT debian/control**

```
Package: devbrain
Version: 1.0.0
Section: devel
Priority: optional
Architecture: amd64
Depends: libgtk-3-0, libnotify4, libnss3, libxss1, libxtst6, libatspi2.0-0
Maintainer: DevBrain <devbrain@users.noreply.github.com>
Homepage: https://github.com/devbrain/devbrain
Description: Developer's second brain
 DevBrain is a background daemon that passively captures AI coding sessions,
 builds a knowledge graph of decisions and dead ends, and surfaces proactive
 insights including morning briefings, pattern detection, and semantic search.
```

Write to: `packages/apt/debian/control`

- [ ] **Step 4: Create APT postinst**

```bash
#!/bin/bash
set -e

ln -sf /opt/devbrain/resources/bin/devbrain /usr/local/bin/devbrain
ln -sf /opt/devbrain/resources/bin/devbrain-daemon /usr/local/bin/devbrain-daemon

mkdir -p /etc/xdg/autostart
cp /opt/devbrain/devbrain.desktop /etc/xdg/autostart/devbrain.desktop 2>/dev/null || true
```

Write to: `packages/apt/debian/postinst`

- [ ] **Step 5: Create APT prerm**

```bash
#!/bin/bash
set -e

pkill -f devbrain-daemon 2>/dev/null || true
pkill -f devbrain-tray 2>/dev/null || true

rm -f /usr/local/bin/devbrain
rm -f /usr/local/bin/devbrain-daemon
rm -f /etc/xdg/autostart/devbrain.desktop

# ~/.devbrain/ is intentionally preserved (user data)
```

Write to: `packages/apt/debian/prerm`

- [ ] **Step 6: Create APT rules**

```makefile
#!/usr/bin/make -f

%:
	dh $@

override_dh_auto_build:
	true

override_dh_auto_install:
	mkdir -p debian/devbrain/opt/devbrain
	cp -r . debian/devbrain/opt/devbrain/
```

Write to: `packages/apt/debian/rules`

- [ ] **Step 7: Create desktop file**

```desktop
[Desktop Entry]
Type=Application
Name=DevBrain
Exec=/opt/devbrain/devbrain-tray
Icon=/opt/devbrain/resources/assets/icon.png
Comment=Developer's second brain
Categories=Development;
X-GNOME-Autostart-enabled=true
StartupNotify=false
Terminal=false
```

Write to: `packages/apt/debian/devbrain.desktop`

- [ ] **Step 8: Make scripts executable**

Run: `chmod +x packages/apt/debian/postinst packages/apt/debian/prerm packages/apt/debian/rules`

- [ ] **Step 9: Commit**

```bash
git add packages/homebrew/ packages/winget/ packages/apt/
git commit -m "feat(packaging): add Homebrew, winget, and APT package manifests"
```

---

## Task 11: CI/CD — Electron Build + Package Publish Workflow

**Files:**
- Create: `.github/workflows/package.yml`

- [ ] **Step 1: Write package.yml**

```yaml
name: Package & Publish

on:
  workflow_run:
    workflows: ["Build & Test"]
    types: [completed]
    branches: [main, master]

jobs:
  check:
    runs-on: ubuntu-latest
    if: github.event.workflow_run.conclusion == 'success'
    outputs:
      is_tag: ${{ steps.check_tag.outputs.is_tag }}
    steps:
      - id: check_tag
        run: |
          if [[ "${{ github.event.workflow_run.head_branch }}" == v* ]]; then
            echo "is_tag=true" >> $GITHUB_OUTPUT
          else
            echo "is_tag=false" >> $GITHUB_OUTPUT
          fi

  electron-build:
    needs: check
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
            electron-args: --win --x64
            artifact-pattern: "*.exe"
          - os: macos-latest
            rid: osx-x64
            electron-args: --mac --x64
            artifact-pattern: "*.dmg"
          - os: ubuntu-latest
            rid: linux-x64
            electron-args: --linux --x64
            artifact-pattern: "*.deb"
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: "20"

      - uses: actions/download-artifact@v4
        with:
          name: devbrain-${{ matrix.rid }}
          path: packages/tray/resources/bin/
          run-id: ${{ github.event.workflow_run.id }}
          github-token: ${{ secrets.GITHUB_TOKEN }}

      - uses: actions/download-artifact@v4
        with:
          name: dashboard-dist
          path: packages/tray/resources/wwwroot/
          run-id: ${{ github.event.workflow_run.id }}
          github-token: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract .NET binaries (Unix)
        if: runner.os != 'Windows'
        run: |
          cd packages/tray/resources/bin
          for f in *.tar.gz; do [ -f "$f" ] && tar xzf "$f" && rm "$f"; done
          chmod +x devbrain devbrain-daemon 2>/dev/null || true

      - name: Extract .NET binaries (Windows)
        if: runner.os == 'Windows'
        shell: bash
        run: |
          cd packages/tray/resources/bin
          for f in *.zip; do [ -f "$f" ] && 7z x "$f" -y && rm "$f"; done

      - name: Install dependencies
        run: cd packages/tray && npm ci

      - name: Build TypeScript
        run: cd packages/tray && npm run build

      - name: Build Electron package
        run: cd packages/tray && npx electron-builder ${{ matrix.electron-args }} --publish never

      - uses: actions/upload-artifact@v4
        with:
          name: electron-${{ matrix.rid }}
          path: packages/tray/dist/${{ matrix.artifact-pattern }}

  publish-homebrew:
    needs: [check, electron-build]
    if: needs.check.outputs.is_tag == 'true'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Update Homebrew formula
        uses: Justintime50/homebrew-releaser@v1
        with:
          homebrew_owner: devbrain
          homebrew_tap: homebrew-tap
          formula_folder: Formula
          github_token: ${{ secrets.HOMEBREW_TAP_TOKEN }}
          commit_owner: devbrain-bot
          commit_email: bot@devbrain.dev
          install: |
            bin.install "devbrain"
            bin.install "devbrain-daemon"
          test: |
            assert_match "devbrain", shell_output("#{bin}/devbrain --version")

  publish-winget:
    needs: [check, electron-build]
    if: needs.check.outputs.is_tag == 'true'
    runs-on: windows-latest
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: electron-win-x64
          path: installer/

      - name: Submit to winget
        uses: vedantmgoyal9/winget-releaser@v2
        with:
          identifier: DevBrain.DevBrain
          installers-regex: '\.exe$'
          token: ${{ secrets.WINGET_TOKEN }}

  publish-apt:
    needs: [check, electron-build]
    if: needs.check.outputs.is_tag == 'true'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/download-artifact@v4
        with:
          name: electron-linux-x64
          path: deb-package/

      - name: Setup GPG
        run: echo "${{ secrets.APT_GPG_PRIVATE_KEY }}" | gpg --batch --import

      - name: Update APT repository
        run: |
          mkdir -p apt-repo/pool/main
          cp deb-package/*.deb apt-repo/pool/main/
          cd apt-repo
          dpkg-scanpackages pool/main /dev/null | gzip -9c > Packages.gz
          apt-ftparchive release . > Release
          gpg --batch --yes --armor --detach-sign -o Release.gpg Release
          gpg --batch --yes --clearsign -o InRelease Release

      - name: Publish to GitHub Pages
        uses: peaceiris/actions-gh-pages@v4
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: ./apt-repo
          destination_dir: apt
```

Write to: `.github/workflows/package.yml`

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/package.yml
git commit -m "feat(packaging): add CI/CD for Electron build + package manager publishing"
```

---

## Task 12: Local Build Verification

Verify everything builds and tests pass locally.

- [ ] **Step 1: Run all tray app tests**

Run: `cd packages/tray && npx jest --verbose`

Expected: All tests pass (~15 tests across health, daemon, bootstrap).

- [ ] **Step 2: Build TypeScript**

Run: `cd packages/tray && npm run build`

Expected: `dist/` directory created with compiled `.js` files, no errors.

- [ ] **Step 3: Verify .NET build still passes**

Run: `dotnet build DevBrain.slnx`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run all .NET tests**

Run: `dotnet test DevBrain.slnx`

Expected: All tests pass.

- [ ] **Step 5: Test local Electron build (optional — requires pre-built .NET binaries)**

To test the full Electron build locally, first populate the resources:

```bash
mkdir -p packages/tray/resources/bin packages/tray/resources/wwwroot
dotnet publish src/DevBrain.Api -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o packages/tray/resources/bin/
dotnet publish src/DevBrain.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o packages/tray/resources/bin/
# Rename to clean names
mv packages/tray/resources/bin/DevBrain.Api.exe packages/tray/resources/bin/devbrain-daemon.exe 2>/dev/null || true
mv packages/tray/resources/bin/DevBrain.Cli.exe packages/tray/resources/bin/devbrain.exe 2>/dev/null || true
# Copy dashboard
cp -r dashboard/dist/* packages/tray/resources/wwwroot/ 2>/dev/null || true
```

Then build unpacked:
```bash
cd packages/tray && npx electron-builder --dir
```

Expected: `packages/tray/dist/` contains an unpacked Electron app.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "feat(packaging): complete single-click packaging implementation"
```

---

## Summary

| Task | What it builds | New files | Tests |
|------|---------------|-----------|-------|
| 1 | npm workspaces + Electron scaffold | 3 + modify 1 | — |
| 2 | Platform paths module | 1 | — |
| 3 | Notifications module | 1 | — |
| 4 | Health monitor (TDD) | 2 + jest config | 5 tests |
| 5 | Daemon manager (TDD) | 2 | 5 tests |
| 6 | Bootstrap orchestrator (TDD) | 2 | 5 tests |
| 7 | Electron main entry + tray | 1 + 5 assets | — |
| 8 | CLI/tray coordination | modify 2 | existing .NET tests |
| 9 | electron-builder config | 4 | — |
| 10 | Package manager manifests | 7 | — |
| 11 | CI/CD workflow | 1 | — |
| 12 | Local build verification | — | all tests |

**Total:** ~30 new files, 2 modified files, ~15 new tests, 12 commits.
