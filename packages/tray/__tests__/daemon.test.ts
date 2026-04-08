import { DaemonManager } from "../src/daemon";
import * as child_process from "child_process";
import * as fs from "fs";

jest.mock("child_process");
jest.mock("fs");

const mockSpawn = child_process.spawn as jest.MockedFunction<typeof child_process.spawn>;
const mockExecFileSync = child_process.execFileSync as jest.MockedFunction<typeof child_process.execFileSync>;
const mockExistsSync = fs.existsSync as jest.MockedFunction<typeof fs.existsSync>;
const mockReadFileSync = fs.readFileSync as jest.MockedFunction<typeof fs.readFileSync>;
const mockWriteFileSync = fs.writeFileSync as jest.MockedFunction<typeof fs.writeFileSync>;
const mockUnlinkSync = fs.unlinkSync as jest.MockedFunction<typeof fs.unlinkSync>;
const mockMkdirSync = fs.mkdirSync as jest.MockedFunction<typeof fs.mkdirSync>;
const mockOpenSync = fs.openSync as jest.MockedFunction<typeof fs.openSync>;

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
    mockOpenSync.mockReturnValue(3);
    daemon = new DaemonManager();
  });

  describe("start()", () => {
    it("spawns devbrain-daemon as a detached process", async () => {
      mockFetch.mockResolvedValue({ ok: true });
      await daemon.start();
      expect(mockSpawn).toHaveBeenCalledWith(
        "/mock/bin/devbrain-daemon",
        [],
        expect.objectContaining({ detached: true })
      );
    });

    it("redirects stderr to a log file", async () => {
      mockFetch.mockResolvedValue({ ok: true });
      await daemon.start();
      const spawnCall = mockSpawn.mock.calls[0];
      const options = spawnCall[2] as child_process.SpawnOptions;
      expect(Array.isArray(options.stdio)).toBe(true);
      const stdio = options.stdio as Array<unknown>;
      expect(stdio[0]).toBe("ignore");
      expect(stdio[1]).toBe("ignore");
      expect(typeof stdio[2]).toBe("number");
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
    it("writes sentinel and kills verified daemon process", async () => {
      mockExistsSync.mockImplementation((p) =>
        String(p) === "/mock/.devbrain/daemon.pid"
      );
      mockReadFileSync.mockReturnValue("12345");
      // isDaemonProcess calls execFileSync with encoding: "utf-8", returns string
      (mockExecFileSync as jest.Mock).mockReturnValue('"devbrain-daemon.exe","12345"');
      const killMock = jest.fn();
      jest.spyOn(process, "kill").mockImplementation(killMock);

      await daemon.stop();

      expect(mockWriteFileSync).toHaveBeenCalledWith(
        "/mock/.devbrain/stopped",
        "stopped"
      );
      expect(killMock).toHaveBeenCalledWith(12345);
      expect(mockUnlinkSync).toHaveBeenCalledWith("/mock/.devbrain/daemon.pid");
      (process.kill as jest.Mock).mockRestore();
    });

    it("does not kill process if PID belongs to a different process", async () => {
      mockExistsSync.mockImplementation((p) =>
        String(p) === "/mock/.devbrain/daemon.pid"
      );
      mockReadFileSync.mockReturnValue("12345");
      (mockExecFileSync as jest.Mock).mockReturnValue('"chrome.exe","12345"');
      const killMock = jest.fn();
      jest.spyOn(process, "kill").mockImplementation(killMock);

      await daemon.stop();

      expect(killMock).not.toHaveBeenCalled();
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
