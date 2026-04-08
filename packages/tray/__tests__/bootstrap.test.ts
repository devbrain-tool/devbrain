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
      mockExistsSync.mockImplementation((p) => String(p).endsWith("settings.toml") || String(p) === "/mock/.devbrain");
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
      let callCount = 0;
      mockExistsSync.mockImplementation((p) => {
        const pathStr = String(p);
        if (pathStr.endsWith("settings.toml")) {
          callCount++;
          return callCount > 1;
        }
        return pathStr === "/mock/.devbrain";
      });

      await bootstrap.ensureConfig();
      await bootstrap.ensureConfig();

      expect(mockWriteFileSync).toHaveBeenCalledTimes(1);
    });
  });
});
