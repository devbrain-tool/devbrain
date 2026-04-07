namespace DevBrain.Api.Setup;

using System.Text.Json;
using DevBrain.Core.Models;

public class SetupValidator
{
    private readonly int _port;
    private readonly string _claudeSettingsPath;
    private readonly string _installDir;
    private static readonly HashSet<string> FixableChecks = ["claude-settings", "claude-hook", "copilot-wrappers"];

    public SetupValidator(Settings settings)
    {
        _port = settings.Daemon.Port;
        _claudeSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "settings.json");
        _installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".devbrain", "bin");
    }

    public async Task<SetupStatus> RunAllChecks()
    {
        var checks = new List<CheckResult>();

        // Claude Code checks
        var claudeCli = CheckClaudeCli();
        checks.Add(claudeCli);

        var claudeSettings = claudeCli.Status is "pass" or "warn"
            ? CheckClaudeSettings()
            : Skip("claude-settings", "Claude Code", "Settings file valid", "Skipped because Claude CLI not found");
        checks.Add(claudeSettings);

        var claudeHook = claudeSettings.Status == "pass"
            ? CheckClaudeHook()
            : Skip("claude-hook", "Claude Code", "PostToolUse hook configured", "Skipped because settings check failed");
        checks.Add(claudeHook);

        var claudeRoundtrip = claudeHook.Status == "pass"
            ? await CheckRoundtrip("claude-roundtrip", "Claude Code", "Capture round-trip")
            : Skip("claude-roundtrip", "Claude Code", "Capture round-trip", "Skipped because hook check failed");
        checks.Add(claudeRoundtrip);

        // GitHub Copilot checks
        var ghCli = CheckGhCli();
        checks.Add(ghCli);

        var ghCopilot = ghCli.Status is "pass" or "warn"
            ? CheckGhCopilot()
            : Skip("gh-copilot", "GitHub Copilot", "Copilot extension installed", "Skipped because gh CLI not found");
        checks.Add(ghCopilot);

        var copilotWrappers = ghCli.Status is "pass" or "warn"
            ? CheckCopilotWrappers()
            : Skip("copilot-wrappers", "GitHub Copilot", "Capture wrappers installed", "Skipped because gh CLI not found");
        checks.Add(copilotWrappers);

        var copilotRoundtrip = copilotWrappers.Status == "pass"
            ? await CheckRoundtrip("copilot-roundtrip", "GitHub Copilot", "Capture round-trip")
            : Skip("copilot-roundtrip", "GitHub Copilot", "Capture round-trip", "Skipped because wrappers check failed");
        checks.Add(copilotRoundtrip);

        // Ollama
        checks.Add(CheckOllama());

        var summary = new StatusSummary
        {
            Pass = checks.Count(c => c.Status == "pass"),
            Fail = checks.Count(c => c.Status == "fail"),
            Warn = checks.Count(c => c.Status == "warn"),
            Skip = checks.Count(c => c.Status == "skip")
        };

        return new SetupStatus { Checks = checks, Summary = summary };
    }

    public async Task<FixResult> Fix(string checkId)
    {
        if (!FixableChecks.Contains(checkId))
        {
            return checkId is "claude-cli" or "gh-cli" or "gh-copilot" or "claude-roundtrip" or "copilot-roundtrip" or "ollama"
                ? new FixResult { Success = false, Detail = $"Check '{checkId}' is not auto-fixable. See setup instructions." }
                : new FixResult { Success = false, Detail = $"Unknown check: {checkId}" };
        }

        try
        {
            return checkId switch
            {
                "claude-settings" => FixClaudeSettings(),
                "claude-hook" => FixClaudeHook(),
                "copilot-wrappers" => await Task.FromResult(FixCopilotWrappers()),
                _ => new FixResult { Success = false, Detail = $"Unknown check: {checkId}" }
            };
        }
        catch (Exception ex)
        {
            return new FixResult { Success = false, Detail = ex.Message };
        }
    }

    // ── Individual checks ────────────────────────────────────────────────

    private CheckResult CheckClaudeCli()
    {
        var path = ProcessRunner.GetCommandPath("claude");
        if (path is null)
            return Fail("claude-cli", "Claude Code", "Claude CLI installed",
                "claude not found in PATH. Install with: npm install -g @anthropic-ai/claude-code", fixable: false);

        var (exitCode, stdout, _) = ProcessRunner.Run("claude", "--version");
        if (exitCode == -1)
            return Warn("claude-cli", "Claude Code", "Claude CLI installed",
                $"claude found at {path} but --version timed out");

        var version = stdout.Length > 0 ? stdout.Split('\n')[0] : "unknown version";
        return Pass("claude-cli", "Claude Code", "Claude CLI installed", $"{version} at {path}");
    }

    private CheckResult CheckClaudeSettings()
    {
        if (!File.Exists(_claudeSettingsPath))
            return Fail("claude-settings", "Claude Code", "Settings file valid",
                $"{_claudeSettingsPath} does not exist", fixable: true);

        try
        {
            var json = File.ReadAllText(_claudeSettingsPath);
            JsonDocument.Parse(json);
            return Pass("claude-settings", "Claude Code", "Settings file valid",
                $"Valid JSON at {_claudeSettingsPath}");
        }
        catch (JsonException)
        {
            return Fail("claude-settings", "Claude Code", "Settings file valid",
                $"{_claudeSettingsPath} is not valid JSON", fixable: true);
        }
    }

    private CheckResult CheckClaudeHook()
    {
        try
        {
            var json = File.ReadAllText(_claudeSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("hooks", out var hooks))
                return Fail("claude-hook", "Claude Code", "PostToolUse hook configured",
                    "No 'hooks' property in settings.json", fixable: true);

            if (!hooks.TryGetProperty("PostToolUse", out var postToolUse))
                return Fail("claude-hook", "Claude Code", "PostToolUse hook configured",
                    "No 'PostToolUse' hook array in settings.json", fixable: true);

            if (postToolUse.ValueKind != JsonValueKind.Array || postToolUse.GetArrayLength() == 0)
                return Fail("claude-hook", "Claude Code", "PostToolUse hook configured",
                    "PostToolUse is empty", fixable: true);

            var portStr = _port.ToString();
            foreach (var matcher in postToolUse.EnumerateArray())
            {
                if (!matcher.TryGetProperty("hooks", out var innerHooks) ||
                    innerHooks.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var hook in innerHooks.EnumerateArray())
                {
                    if (hook.TryGetProperty("command", out var cmd) &&
                        cmd.GetString()?.Contains(portStr) == true)
                    {
                        return Pass("claude-hook", "Claude Code", "PostToolUse hook configured",
                            $"Hook found targeting port {_port}");
                    }
                }
            }

            return Fail("claude-hook", "Claude Code", "PostToolUse hook configured",
                $"No hook entry targeting port {_port}", fixable: true);
        }
        catch (Exception ex)
        {
            return Fail("claude-hook", "Claude Code", "PostToolUse hook configured",
                $"Error reading hook config: {ex.Message}", fixable: true);
        }
    }

    private async Task<CheckResult> CheckRoundtrip(string id, string category, string name)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var baseUrl = $"http://127.0.0.1:{_port}/api/v1";

            var body = new StringContent(
                JsonSerializer.Serialize(new
                {
                    sessionId = "setup-validation",
                    eventType = "Decision",
                    source = "ClaudeCode",
                    rawContent = $"Setup validation test {id}",
                    project = "devbrain-setup-validation"
                }),
                System.Text.Encoding.UTF8, "application/json");

            var postResp = await http.PostAsync($"{baseUrl}/observations", body);
            if (!postResp.IsSuccessStatusCode)
                return Fail(id, category, name,
                    $"POST observation failed: HTTP {(int)postResp.StatusCode}", fixable: false);

            await Task.Delay(500);
            var getResp = await http.GetStringAsync(
                $"{baseUrl}/observations?project=devbrain-setup-validation&limit=1");
            if (getResp.Contains("devbrain-setup-validation"))
                return Pass(id, category, name, "Observation round-trip succeeded");

            return Fail(id, category, name, "Observation written but not readable", fixable: false);
        }
        catch (Exception ex)
        {
            return Fail(id, category, name, $"Round-trip failed: {ex.Message}", fixable: false);
        }
    }

    private CheckResult CheckGhCli()
    {
        var path = ProcessRunner.GetCommandPath("gh");
        if (path is null)
            return Fail("gh-cli", "GitHub Copilot", "GitHub CLI installed",
                "gh not found in PATH. Install from https://cli.github.com/", fixable: false);

        var (exitCode, stdout, _) = ProcessRunner.Run("gh", "--version");
        if (exitCode == -1)
            return Warn("gh-cli", "GitHub Copilot", "GitHub CLI installed",
                $"gh found at {path} but --version timed out");

        var version = stdout.Length > 0 ? stdout.Split('\n')[0] : "unknown version";
        return Pass("gh-cli", "GitHub Copilot", "GitHub CLI installed", $"{version} at {path}");
    }

    private CheckResult CheckGhCopilot()
    {
        var (exitCode, _, _) = ProcessRunner.Run("gh", "copilot --help");
        if (exitCode == 0)
            return Pass("gh-copilot", "GitHub Copilot", "Copilot extension installed",
                "gh copilot extension is available");

        return Fail("gh-copilot", "GitHub Copilot", "Copilot extension installed",
            "gh copilot not installed. Run: gh extension install github/gh-copilot", fixable: false);
    }

    private CheckResult CheckCopilotWrappers()
    {
        if (OperatingSystem.IsWindows())
        {
            var profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1");
            var legacyProfile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");

            var found = (File.Exists(profilePath) && File.ReadAllText(profilePath).Contains("ghcs"))
                     || (File.Exists(legacyProfile) && File.ReadAllText(legacyProfile).Contains("ghcs"));

            return found
                ? Pass("copilot-wrappers", "GitHub Copilot", "Capture wrappers installed",
                    "ghcs/ghce functions found in PowerShell profile")
                : Fail("copilot-wrappers", "GitHub Copilot", "Capture wrappers installed",
                    "ghcs/ghce not found in PowerShell profile", fixable: true);
        }
        else
        {
            var ghcsPath = Path.Combine(_installDir, "ghcs");
            var ghcePath = Path.Combine(_installDir, "ghce");

            return File.Exists(ghcsPath) && File.Exists(ghcePath)
                ? Pass("copilot-wrappers", "GitHub Copilot", "Capture wrappers installed",
                    $"ghcs and ghce found in {_installDir}")
                : Fail("copilot-wrappers", "GitHub Copilot", "Capture wrappers installed",
                    $"ghcs/ghce not found in {_installDir}", fixable: true);
        }
    }

    private CheckResult CheckOllama()
    {
        if (!ProcessRunner.CommandExists("ollama"))
            return Warn("ollama", "LLM", "Ollama available",
                "Ollama not installed. Download from https://ollama.com/download");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = http.GetStringAsync("http://localhost:11434/api/tags").Result;

            if (resp.Contains("llama3.2:3b"))
                return Pass("ollama", "LLM", "Ollama available",
                    "Ollama running with llama3.2:3b model");

            return Warn("ollama", "LLM", "Ollama available",
                "Ollama running but llama3.2:3b model not found. Run: ollama pull llama3.2:3b");
        }
        catch
        {
            return Warn("ollama", "LLM", "Ollama available",
                "Ollama installed but not running. Start it first.");
        }
    }

    // ── Fix implementations ──────────────────────────────────────────────

    private FixResult FixClaudeSettings()
    {
        var dir = Path.GetDirectoryName(_claudeSettingsPath)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(_claudeSettingsPath))
            File.Copy(_claudeSettingsPath, _claudeSettingsPath + ".bak", overwrite: true);

        File.WriteAllText(_claudeSettingsPath, "{\n  \"hooks\": {}\n}\n");
        return new FixResult { Success = true, Detail = $"Created {_claudeSettingsPath}" };
    }

    private FixResult FixClaudeHook()
    {
        var json = File.Exists(_claudeSettingsPath)
            ? File.ReadAllText(_claudeSettingsPath)
            : "{}";

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hookCommand = $"curl -s -X POST http://127.0.0.1:{_port}/api/v1/observations "
            + "-H 'Content-Type: application/json' "
            + "-d '{\"sessionId\":\"'$CLAUDE_SESSION_ID'\",\"eventType\":\"ToolCall\","
            + "\"source\":\"ClaudeCode\",\"rawContent\":\"Tool: '$CLAUDE_TOOL_NAME'\","
            + "\"project\":\"'$CLAUDE_PROJECT'\"}' >/dev/null 2>&1";

        var hookEntry = new { type = "command", command = hookCommand };
        var matcherEntry = new { matcher = "", hooks = new[] { hookEntry } };

        // Preserve existing non-PostToolUse hooks
        var existingHooks = new Dictionary<string, object>();
        if (root.TryGetProperty("hooks", out var hooks))
        {
            foreach (var prop in hooks.EnumerateObject())
            {
                if (prop.Name != "PostToolUse")
                    existingHooks[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
            }
        }

        // Build PostToolUse array — keep existing non-devbrain matchers
        var postToolUseList = new List<object>();
        if (root.TryGetProperty("hooks", out var h2) &&
            h2.TryGetProperty("PostToolUse", out var ptu) &&
            ptu.ValueKind == JsonValueKind.Array)
        {
            foreach (var existing in ptu.EnumerateArray())
            {
                var raw = existing.GetRawText();
                if (!raw.Contains(_port.ToString()))
                    postToolUseList.Add(JsonSerializer.Deserialize<object>(raw)!);
            }
        }
        postToolUseList.Add(matcherEntry);
        existingHooks["PostToolUse"] = postToolUseList;

        // Rebuild full settings preserving non-hook properties
        var newSettings = new Dictionary<string, object>();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name != "hooks")
                newSettings[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
        }
        newSettings["hooks"] = existingHooks;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_claudeSettingsPath, JsonSerializer.Serialize(newSettings, options));

        return new FixResult { Success = true, Detail = $"Added PostToolUse hook targeting port {_port}" };
    }

    private FixResult FixCopilotWrappers()
    {
        if (OperatingSystem.IsWindows())
            return FixCopilotWrappersWindows();
        return FixCopilotWrappersUnix();
    }

    private FixResult FixCopilotWrappersUnix()
    {
        Directory.CreateDirectory(_installDir);

        var ghcsContent = $"#!/bin/bash\nQUERY=\"$*\"\nDAEMON=\"http://127.0.0.1:{_port}\"\n"
            + "OUTPUT=$(gh copilot suggest \"$@\" 2>&1)\nEXIT_CODE=$?\necho \"$OUTPUT\"\n"
            + "if curl -s \"$DAEMON/api/v1/health\" >/dev/null 2>&1; then\n"
            + "  PROJECT=$(basename \"$(git rev-parse --show-toplevel 2>/dev/null || pwd)\")\n"
            + "  curl -s -X POST \"$DAEMON/api/v1/observations\" \\\n"
            + "    -H \"Content-Type: application/json\" \\\n"
            + "    -d \"{\\\"sessionId\\\":\\\"copilot-$(date +%Y%m%d)\\\",\\\"eventType\\\":\\\"Conversation\\\","
            + "\\\"source\\\":\\\"VSCode\\\",\\\"rawContent\\\":\\\"Copilot suggest: $QUERY\\\","
            + "\\\"project\\\":\\\"$PROJECT\\\"}\" >/dev/null 2>&1 &\nfi\nexit $EXIT_CODE\n";

        var ghceContent = ghcsContent.Replace("suggest", "explain");

        File.WriteAllText(Path.Combine(_installDir, "ghcs"), ghcsContent);
        File.WriteAllText(Path.Combine(_installDir, "ghce"), ghceContent);

        ProcessRunner.Run("chmod", $"+x {Path.Combine(_installDir, "ghcs")}");
        ProcessRunner.Run("chmod", $"+x {Path.Combine(_installDir, "ghce")}");

        return new FixResult { Success = true, Detail = $"Created ghcs and ghce in {_installDir}" };
    }

    private FixResult FixCopilotWrappersWindows()
    {
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1");
        var profileDir = Path.GetDirectoryName(profilePath)!;
        Directory.CreateDirectory(profileDir);

        var wrapperCode = $@"

# DevBrain wrappers for GitHub Copilot CLI
function ghcs {{
    $query = $args -join "" ""
    $output = & gh copilot suggest @args 2>&1
    Write-Output $output
    $project = try {{ Split-Path (git rev-parse --show-toplevel 2>$null) -Leaf }} catch {{ ""unknown"" }}
    try {{
        Invoke-RestMethod -Uri ""http://127.0.0.1:{_port}/api/v1/observations"" -Method Post -ContentType ""application/json"" -Body (@{{
            sessionId = ""copilot-$(Get-Date -Format 'yyyyMMdd')""
            eventType = ""Conversation""
            source = ""VSCode""
            rawContent = ""Copilot suggest: $query""
            project = $project
        }} | ConvertTo-Json) -ErrorAction SilentlyContinue | Out-Null
    }} catch {{}}
}}

function ghce {{
    $query = $args -join "" ""
    $output = & gh copilot explain @args 2>&1
    Write-Output $output
    $project = try {{ Split-Path (git rev-parse --show-toplevel 2>$null) -Leaf }} catch {{ ""unknown"" }}
    try {{
        Invoke-RestMethod -Uri ""http://127.0.0.1:{_port}/api/v1/observations"" -Method Post -ContentType ""application/json"" -Body (@{{
            sessionId = ""copilot-$(Get-Date -Format 'yyyyMMdd')""
            eventType = ""Conversation""
            source = ""VSCode""
            rawContent = ""Copilot explain: $query""
            project = $project
        }} | ConvertTo-Json) -ErrorAction SilentlyContinue | Out-Null
    }} catch {{}}
}}
";

        if (File.Exists(profilePath))
        {
            var content = File.ReadAllText(profilePath);
            if (!content.Contains("ghcs"))
                File.AppendAllText(profilePath, wrapperCode);
        }
        else
        {
            File.WriteAllText(profilePath, wrapperCode);
        }

        return new FixResult { Success = true, Detail = $"Added ghcs/ghce functions to {profilePath}" };
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static CheckResult Pass(string id, string category, string name, string detail) =>
        new() { Id = id, Category = category, Name = name, Status = "pass", Detail = detail, Fixable = false };

    private static CheckResult Fail(string id, string category, string name, string detail, bool fixable) =>
        new() { Id = id, Category = category, Name = name, Status = "fail", Detail = detail, Fixable = fixable };

    private static CheckResult Warn(string id, string category, string name, string detail) =>
        new() { Id = id, Category = category, Name = name, Status = "warn", Detail = detail, Fixable = false };

    private static CheckResult Skip(string id, string category, string name, string detail) =>
        new() { Id = id, Category = category, Name = name, Status = "skip", Detail = detail, Fixable = false };
}
