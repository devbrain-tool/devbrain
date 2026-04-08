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

  async run(startDaemon: () => Promise<void>): Promise<void> {
    await this.ensureConfig();
    await startDaemon();

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
