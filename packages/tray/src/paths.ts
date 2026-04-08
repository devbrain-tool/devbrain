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
