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
    // Write sentinel BEFORE killing so the exit handler doesn't auto-restart
    const sentinel = stoppedSentinelPath();
    fs.writeFileSync(sentinel, "stopped");

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
