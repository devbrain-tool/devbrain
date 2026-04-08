import { spawn, ChildProcess, execFileSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
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
    // Check if daemon is already running (e.g. started by CLI or previous session)
    try {
      const res = await fetch("http://127.0.0.1:37800/api/v1/health");
      if (res.ok) {
        // Daemon is already running — adopt it instead of spawning a new one
        return;
      }
    } catch {
      // Not running — proceed to spawn
    }

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

    // Redirect stderr to a log file for debugging startup failures
    const logFile = path.join(logs, "daemon-stderr.log");
    const stderrStream = fs.openSync(logFile, "a");

    this.process = spawn(daemonBin, [], {
      detached: true,
      stdio: ["ignore", "ignore", stderrStream],
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
    // Write sentinel BEFORE killing so the exit handler doesn't auto-restart
    const sentinel = stoppedSentinelPath();
    fs.writeFileSync(sentinel, "stopped");

    const pidFile = pidPath();

    if (fs.existsSync(pidFile)) {
      const pidValue = parseInt(fs.readFileSync(pidFile, "utf-8").trim(), 10);

      if (this.isDaemonProcess(pidValue)) {
        try {
          process.kill(pidValue);
        } catch {
          // Process already dead
        }
      }

      fs.unlinkSync(pidFile);
    }

    this.process = null;
  }

  async restart(): Promise<void> {
    await this.stop();
    this.resetCrashCount();
    await this.start();
  }

  isRunning(): boolean {
    const pidFile = pidPath();
    if (!fs.existsSync(pidFile)) return false;

    const pidValue = parseInt(fs.readFileSync(pidFile, "utf-8").trim(), 10);

    if (!this.isDaemonProcess(pidValue)) {
      // PID was recycled to a different process — stale PID file
      try { fs.unlinkSync(pidFile); } catch { /* best-effort */ }
      return false;
    }

    return true;
  }

  /**
   * Verify that the process at the given PID is actually devbrain-daemon.
   * Prevents killing an unrelated process after PID reuse.
   */
  private isDaemonProcess(pid: number): boolean {
    try {
      if (process.platform === "win32") {
        const output = execFileSync("tasklist", ["/FI", `PID eq ${pid}`, "/FO", "CSV", "/NH"], {
          encoding: "utf-8",
          timeout: 5000,
        });
        return output.toLowerCase().includes("devbrain-daemon");
      } else {
        // Unix: read /proc/<pid>/comm or use ps
        const output = execFileSync("ps", ["-p", String(pid), "-o", "comm="], {
          encoding: "utf-8",
          timeout: 5000,
        });
        return output.trim().includes("devbrain-daemon");
      }
    } catch {
      return false; // Process doesn't exist
    }
  }
}
