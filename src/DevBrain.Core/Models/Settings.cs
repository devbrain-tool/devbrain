namespace DevBrain.Core.Models;

public class Settings
{
    public DaemonSettings Daemon { get; set; } = new();
    public CaptureSettings Capture { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public LlmSettings Llm { get; set; } = new();
    public AgentSettings Agents { get; set; } = new();
}

public class DaemonSettings
{
    public int Port { get; set; } = 37800;
    public string LogLevel { get; set; } = "info";
    public bool AutoStart { get; set; } = true;
    public string DataPath { get; set; } = "~/.devbrain";
}

public class CaptureSettings
{
    public bool Enabled { get; set; } = true;
    public List<string> Sources { get; set; } = ["ai-sessions"];
    public string PrivacyMode { get; set; } = "redact";
    public List<string> IgnoredProjects { get; set; } = [];
    public int MaxObservationSizeKb { get; set; } = 512;
    public int ThreadGapHours { get; set; } = 2;
}

public class StorageSettings
{
    public int SqliteMaxSizeMb { get; set; } = 2048;
    public int VectorDimensions { get; set; } = 384;
    public int CompressionAfterDays { get; set; } = 7;
    public int RetentionDays { get; set; } = 365;
}

public class LlmSettings
{
    public LocalLlmSettings Local { get; set; } = new();
    public CloudLlmSettings Cloud { get; set; } = new();
}

public class LocalLlmSettings
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2:3b";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public int MaxConcurrent { get; set; } = 2;
}

public class CloudLlmSettings
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "anthropic";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string ApiKeyEnv { get; set; } = "DEVBRAIN_CLOUD_API_KEY";
    public int MaxDailyRequests { get; set; } = 50;
    public List<string> Tasks { get; set; } = ["briefing", "pattern"];
}

public class AgentSettings
{
    public BriefingAgentSettings Briefing { get; set; } = new();
    public DeadEndAgentSettings DeadEnd { get; set; } = new();
    public LinkerAgentSettings Linker { get; set; } = new();
    public CompressionAgentSettings Compression { get; set; } = new();
    public PatternAgentSettings Pattern { get; set; } = new();
}

public class BriefingAgentSettings
{
    public bool Enabled { get; set; } = true;
    public string Schedule { get; set; } = "0 7 * * *";
    public string Timezone { get; set; } = "America/New_York";
}

public class DeadEndAgentSettings
{
    public bool Enabled { get; set; } = true;
    public string Sensitivity { get; set; } = "medium";
}

public class LinkerAgentSettings
{
    public bool Enabled { get; set; } = true;
    public int DebounceSeconds { get; set; } = 5;
}

public class CompressionAgentSettings
{
    public bool Enabled { get; set; } = true;
    public int IdleMinutes { get; set; } = 60;
}

public class PatternAgentSettings
{
    public bool Enabled { get; set; } = true;
    public int IdleMinutes { get; set; } = 30;
    public int LookbackDays { get; set; } = 30;
}
