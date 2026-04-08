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
    app.dock?.hide();
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

app.on("window-all-closed", () => {
  // Prevent app from quitting when no windows — we're a tray app
});

app.on("before-quit", () => {
  healthMonitor.stop();
  removeTrayLock();
});
