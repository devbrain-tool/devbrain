using DevBrain.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace DevBrain.Core;

public static class SettingsLoader
{
    public static Settings LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new Settings();

        var toml = File.ReadAllText(path);
        return LoadFromString(toml);
    }

    public static Settings LoadFromString(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
            return new Settings();

        var table = TomlSerializer.Deserialize<TomlTable>(toml)
            ?? throw new InvalidOperationException("Failed to parse TOML");
        var settings = new Settings();

        if (table.TryGetValue("daemon", out var daemonObj) && daemonObj is TomlTable daemonTbl)
            MapDaemon(daemonTbl, settings.Daemon);

        if (table.TryGetValue("capture", out var captureObj) && captureObj is TomlTable captureTbl)
            MapCapture(captureTbl, settings.Capture);

        if (table.TryGetValue("storage", out var storageObj) && storageObj is TomlTable storageTbl)
            MapStorage(storageTbl, settings.Storage);

        if (table.TryGetValue("llm", out var llmObj) && llmObj is TomlTable llmTbl)
            MapLlm(llmTbl, settings.Llm);

        if (table.TryGetValue("agents", out var agentsObj) && agentsObj is TomlTable agentsTbl)
            MapAgents(agentsTbl, settings.Agents);

        return settings;
    }

    public static string ResolveDataPath(string path)
    {
        if (path.StartsWith("~/") || path == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Length > 2 ? path[2..] : "");
        }

        return path;
    }

    private static void MapDaemon(TomlTable t, DaemonSettings s)
    {
        if (t.TryGetValue("port", out var v)) s.Port = Convert.ToInt32(v);
        if (t.TryGetValue("log_level", out v)) s.LogLevel = (string)v;
        if (t.TryGetValue("auto_start", out v)) s.AutoStart = (bool)v;
        if (t.TryGetValue("data_path", out v)) s.DataPath = (string)v;
    }

    private static void MapCapture(TomlTable t, CaptureSettings s)
    {
        if (t.TryGetValue("enabled", out var v)) s.Enabled = (bool)v;
        if (t.TryGetValue("sources", out v)) s.Sources = ToStringList(v);
        if (t.TryGetValue("privacy_mode", out v)) s.PrivacyMode = (string)v;
        if (t.TryGetValue("ignored_projects", out v)) s.IgnoredProjects = ToStringList(v);
        if (t.TryGetValue("max_observation_size_kb", out v)) s.MaxObservationSizeKb = Convert.ToInt32(v);
        if (t.TryGetValue("thread_gap_hours", out v)) s.ThreadGapHours = Convert.ToInt32(v);
    }

    private static void MapStorage(TomlTable t, StorageSettings s)
    {
        if (t.TryGetValue("sqlite_max_size_mb", out var v)) s.SqliteMaxSizeMb = Convert.ToInt32(v);
        if (t.TryGetValue("vector_dimensions", out v)) s.VectorDimensions = Convert.ToInt32(v);
        if (t.TryGetValue("compression_after_days", out v)) s.CompressionAfterDays = Convert.ToInt32(v);
        if (t.TryGetValue("retention_days", out v)) s.RetentionDays = Convert.ToInt32(v);
    }

    private static void MapLlm(TomlTable t, LlmSettings s)
    {
        if (t.TryGetValue("local", out var localObj) && localObj is TomlTable localTbl)
        {
            if (localTbl.TryGetValue("enabled", out var v)) s.Local.Enabled = (bool)v;
            if (localTbl.TryGetValue("provider", out v)) s.Local.Provider = (string)v;
            if (localTbl.TryGetValue("model", out v)) s.Local.Model = (string)v;
            if (localTbl.TryGetValue("endpoint", out v)) s.Local.Endpoint = (string)v;
            if (localTbl.TryGetValue("max_concurrent", out v)) s.Local.MaxConcurrent = Convert.ToInt32(v);
        }

        if (t.TryGetValue("cloud", out var cloudObj) && cloudObj is TomlTable cloudTbl)
        {
            if (cloudTbl.TryGetValue("enabled", out var v)) s.Cloud.Enabled = (bool)v;
            if (cloudTbl.TryGetValue("provider", out v)) s.Cloud.Provider = (string)v;
            if (cloudTbl.TryGetValue("model", out v)) s.Cloud.Model = (string)v;
            if (cloudTbl.TryGetValue("api_key_env", out v)) s.Cloud.ApiKeyEnv = (string)v;
            if (cloudTbl.TryGetValue("max_daily_requests", out v)) s.Cloud.MaxDailyRequests = Convert.ToInt32(v);
            if (cloudTbl.TryGetValue("tasks", out v)) s.Cloud.Tasks = ToStringList(v);
        }
    }

    private static void MapAgents(TomlTable t, AgentSettings s)
    {
        if (t.TryGetValue("briefing", out var obj) && obj is TomlTable briefTbl)
        {
            if (briefTbl.TryGetValue("enabled", out var v)) s.Briefing.Enabled = (bool)v;
            if (briefTbl.TryGetValue("schedule", out v)) s.Briefing.Schedule = (string)v;
            if (briefTbl.TryGetValue("timezone", out v)) s.Briefing.Timezone = (string)v;
        }

        if (t.TryGetValue("dead_end", out obj) && obj is TomlTable deTbl)
        {
            if (deTbl.TryGetValue("enabled", out var v)) s.DeadEnd.Enabled = (bool)v;
            if (deTbl.TryGetValue("sensitivity", out v)) s.DeadEnd.Sensitivity = (string)v;
        }

        if (t.TryGetValue("linker", out obj) && obj is TomlTable linkerTbl)
        {
            if (linkerTbl.TryGetValue("enabled", out var v)) s.Linker.Enabled = (bool)v;
            if (linkerTbl.TryGetValue("debounce_seconds", out v)) s.Linker.DebounceSeconds = Convert.ToInt32(v);
        }

        if (t.TryGetValue("compression", out obj) && obj is TomlTable compTbl)
        {
            if (compTbl.TryGetValue("enabled", out var v)) s.Compression.Enabled = (bool)v;
            if (compTbl.TryGetValue("idle_minutes", out v)) s.Compression.IdleMinutes = Convert.ToInt32(v);
        }

        if (t.TryGetValue("pattern", out obj) && obj is TomlTable patTbl)
        {
            if (patTbl.TryGetValue("enabled", out var v)) s.Pattern.Enabled = (bool)v;
            if (patTbl.TryGetValue("idle_minutes", out v)) s.Pattern.IdleMinutes = Convert.ToInt32(v);
            if (patTbl.TryGetValue("lookback_days", out v)) s.Pattern.LookbackDays = Convert.ToInt32(v);
        }
    }

    private static List<string> ToStringList(object value)
    {
        if (value is TomlArray array)
            return array.Select(item => (string)item!).ToList();

        return [];
    }
}
