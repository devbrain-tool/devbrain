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
