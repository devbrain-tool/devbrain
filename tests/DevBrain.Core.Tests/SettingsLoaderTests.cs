using DevBrain.Core;

namespace DevBrain.Core.Tests;

public class SettingsLoaderTests
{
    [Fact]
    public void LoadFromString_EmptyString_ReturnsDefaults()
    {
        var settings = SettingsLoader.LoadFromString("");

        Assert.Equal(37800, settings.Daemon.Port);
        Assert.Equal("info", settings.Daemon.LogLevel);
        Assert.True(settings.Daemon.AutoStart);
        Assert.Equal("~/.devbrain", settings.Daemon.DataPath);
        Assert.True(settings.Capture.Enabled);
        Assert.Equal(["ai-sessions"], settings.Capture.Sources);
        Assert.Equal("redact", settings.Capture.PrivacyMode);
        Assert.Equal(512, settings.Capture.MaxObservationSizeKb);
        Assert.Equal(2048, settings.Storage.SqliteMaxSizeMb);
        Assert.Equal(384, settings.Storage.VectorDimensions);
        Assert.True(settings.Llm.Local.Enabled);
        Assert.Equal("ollama", settings.Llm.Local.Provider);
        Assert.Equal(2, settings.Llm.Local.MaxConcurrent);
        Assert.True(settings.Agents.Briefing.Enabled);
    }

    [Fact]
    public void LoadFromString_OverriddenValues_AppliesOverrides()
    {
        var toml = """
            [daemon]
            port = 9999
            log_level = "debug"
            auto_start = false
            data_path = "/custom/path"

            [capture]
            enabled = false
            sources = ["ai-sessions", "git-commits"]
            privacy_mode = "strict"
            max_observation_size_kb = 1024
            thread_gap_hours = 4

            [storage]
            sqlite_max_size_mb = 4096
            vector_dimensions = 768
            compression_after_days = 14
            retention_days = 730

            [llm.local]
            enabled = false
            provider = "llamacpp"
            model = "mistral"
            endpoint = "http://localhost:8080"
            max_concurrent = 4

            [llm.cloud]
            enabled = false
            provider = "openai"
            model = "gpt-4"
            api_key_env = "OPENAI_KEY"
            max_daily_requests = 100
            tasks = ["briefing"]

            [agents.briefing]
            enabled = false
            schedule = "0 9 * * *"
            timezone = "UTC"

            [agents.dead_end]
            enabled = false
            sensitivity = "high"

            [agents.linker]
            debounce_seconds = 10

            [agents.compression]
            idle_minutes = 120

            [agents.pattern]
            idle_minutes = 60
            lookback_days = 60
            """;

        var settings = SettingsLoader.LoadFromString(toml);

        Assert.Equal(9999, settings.Daemon.Port);
        Assert.Equal("debug", settings.Daemon.LogLevel);
        Assert.False(settings.Daemon.AutoStart);
        Assert.Equal("/custom/path", settings.Daemon.DataPath);

        Assert.False(settings.Capture.Enabled);
        Assert.Equal(["ai-sessions", "git-commits"], settings.Capture.Sources);
        Assert.Equal("strict", settings.Capture.PrivacyMode);
        Assert.Equal(1024, settings.Capture.MaxObservationSizeKb);
        Assert.Equal(4, settings.Capture.ThreadGapHours);

        Assert.Equal(4096, settings.Storage.SqliteMaxSizeMb);
        Assert.Equal(768, settings.Storage.VectorDimensions);
        Assert.Equal(14, settings.Storage.CompressionAfterDays);
        Assert.Equal(730, settings.Storage.RetentionDays);

        Assert.False(settings.Llm.Local.Enabled);
        Assert.Equal("llamacpp", settings.Llm.Local.Provider);
        Assert.Equal("mistral", settings.Llm.Local.Model);
        Assert.Equal("http://localhost:8080", settings.Llm.Local.Endpoint);
        Assert.Equal(4, settings.Llm.Local.MaxConcurrent);

        Assert.False(settings.Llm.Cloud.Enabled);
        Assert.Equal("openai", settings.Llm.Cloud.Provider);
        Assert.Equal("gpt-4", settings.Llm.Cloud.Model);
        Assert.Equal("OPENAI_KEY", settings.Llm.Cloud.ApiKeyEnv);
        Assert.Equal(100, settings.Llm.Cloud.MaxDailyRequests);
        Assert.Equal(["briefing"], settings.Llm.Cloud.Tasks);

        Assert.False(settings.Agents.Briefing.Enabled);
        Assert.Equal("0 9 * * *", settings.Agents.Briefing.Schedule);
        Assert.Equal("UTC", settings.Agents.Briefing.Timezone);

        Assert.False(settings.Agents.DeadEnd.Enabled);
        Assert.Equal("high", settings.Agents.DeadEnd.Sensitivity);

        Assert.Equal(10, settings.Agents.Linker.DebounceSeconds);
        Assert.Equal(120, settings.Agents.Compression.IdleMinutes);
        Assert.Equal(60, settings.Agents.Pattern.IdleMinutes);
        Assert.Equal(60, settings.Agents.Pattern.LookbackDays);
    }

    [Fact]
    public void ResolveDataPath_TildePrefix_ExpandsToHome()
    {
        var resolved = SettingsLoader.ResolveDataPath("~/.devbrain");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.StartsWith(home, resolved);
        Assert.EndsWith(".devbrain", resolved);
        Assert.DoesNotContain("~", resolved);
    }

    [Fact]
    public void ResolveDataPath_AbsolutePath_ReturnsUnchanged()
    {
        var path = "/opt/devbrain/data";
        var resolved = SettingsLoader.ResolveDataPath(path);

        Assert.Equal(path, resolved);
    }

    [Fact]
    public void LoadFromFile_NonExistentFile_ReturnsDefaults()
    {
        var settings = SettingsLoader.LoadFromFile("/nonexistent/path/config.toml");

        Assert.Equal(37800, settings.Daemon.Port);
        Assert.Equal("info", settings.Daemon.LogLevel);
    }

    [Fact]
    public void LoadFromString_PartialOverride_PreservesDefaults()
    {
        var toml = """
            [daemon]
            port = 5000
            """;

        var settings = SettingsLoader.LoadFromString(toml);

        // Overridden
        Assert.Equal(5000, settings.Daemon.Port);

        // Defaults preserved
        Assert.Equal("info", settings.Daemon.LogLevel);
        Assert.True(settings.Daemon.AutoStart);
        Assert.Equal("~/.devbrain", settings.Daemon.DataPath);
        Assert.True(settings.Capture.Enabled);
        Assert.Equal(2048, settings.Storage.SqliteMaxSizeMb);
    }
}
