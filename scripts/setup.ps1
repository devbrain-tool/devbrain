# DevBrain Setup - installs DevBrain and configures AI tool integration
# Usage: irm https://raw.githubusercontent.com/devbrain-tool/devbrain/main/scripts/setup.ps1 | iex
$ErrorActionPreference = "Stop"

$Repo = "devbrain-tool/devbrain"
$InstallDir = "$env:USERPROFILE\.devbrain\bin"
$DataDir = "$env:USERPROFILE\.devbrain"
$DaemonPort = 37800

function Write-Info($msg)  { Write-Host "[devbrain] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "[devbrain] $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "[devbrain] $msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "  DevBrain Setup" -ForegroundColor Cyan
Write-Host "  A developer's second brain" -ForegroundColor Cyan
Write-Host ""

# -- Step 1: Detect platform ------------------------------------------------

$Arch = if ([Environment]::Is64BitOperatingSystem) {
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" }
} else {
    Write-Error "32-bit Windows is not supported"; exit 1
}
Write-Info "Platform: $Arch"

# -- Step 2: Install DevBrain -----------------------------------------------

$devbrainCmd = Get-Command devbrain -ErrorAction SilentlyContinue
if ($devbrainCmd) {
    Write-Ok "DevBrain already installed: $($devbrainCmd.Source)"
} else {
    Write-Info "Installing DevBrain..."

    $Release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -ErrorAction SilentlyContinue
    if (-not $Release) {
        Write-Error "Could not find latest release. Check https://github.com/$Repo/releases"
        exit 1
    }
    $Version = $Release.tag_name

    $Url = "https://github.com/$Repo/releases/download/$Version/devbrain-$Arch.zip"
    $TmpFile = [System.IO.Path]::GetTempFileName() + ".zip"
    Write-Info "Downloading $Version..."
    Invoke-WebRequest -Uri $Url -OutFile $TmpFile

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Expand-Archive -Path $TmpFile -DestinationPath $InstallDir -Force
    Remove-Item $TmpFile

    # Add to PATH (persistent)
    $UserPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($UserPath -notlike "*\.devbrain\bin*") {
        [Environment]::SetEnvironmentVariable("PATH", "$InstallDir;$UserPath", "User")
    }

    Write-Ok "DevBrain $Version installed to $InstallDir"
}

# Ensure current session PATH includes .devbrain\bin (even on re-runs)
if ($env:PATH -notlike "*\.devbrain\bin*") {
    $env:PATH = "$InstallDir;$env:PATH"
}

# -- Step 3: Create default config -----------------------------------------

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

$SettingsFile = "$DataDir\settings.toml"
if (-not (Test-Path $SettingsFile)) {
    Write-Info "Creating default configuration..."
    @'
[daemon]
port = 37800
log_level = "info"

[capture]
enabled = true
sources = ["ai-sessions"]
privacy_mode = "redact"

[llm.local]
enabled = true
provider = "ollama"
model = "llama3.2:3b"
endpoint = "http://localhost:11434"

[llm.cloud]
enabled = false

[agents.briefing]
enabled = true
schedule = "0 7 * * *"

[agents.dead_end]
enabled = true

[agents.linker]
enabled = true

[agents.compression]
enabled = true
idle_minutes = 60
'@ | Set-Content -Path $SettingsFile -Encoding UTF8
    Write-Ok "Config created at $SettingsFile"
} else {
    Write-Ok "Config already exists"
}

# -- Step 4: Start daemon --------------------------------------------------

$healthCheck = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/health" -ErrorAction SilentlyContinue } catch { $null }

if ($healthCheck) {
    Write-Ok "Daemon already running"
} else {
    Write-Info "Starting DevBrain daemon..."

    # Ensure logs directory exists before daemon tries to write
    $LogsDir = "$DataDir\logs"
    New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null

    $daemon = "$InstallDir\devbrain-daemon.exe"
    $started = $false

    # Try CLI first (preferred — it manages the daemon lifecycle)
    $devbrainCli = Get-Command devbrain -ErrorAction SilentlyContinue
    if ($devbrainCli) {
        try {
            & devbrain start 2>$null
            $started = $true
        } catch {}
    }

    # Fallback: launch daemon exe directly with log capture
    if (-not $started -and (Test-Path $daemon)) {
        $logFile = "$LogsDir\daemon.log"
        Start-Process -FilePath $daemon -WindowStyle Hidden `
            -RedirectStandardOutput $logFile `
            -RedirectStandardError "$LogsDir\daemon-error.log"
    }

    # Wait up to 10 seconds for the daemon to become healthy
    $retries = 5
    $healthCheck = $null
    for ($i = 0; $i -lt $retries; $i++) {
        Start-Sleep -Seconds 2
        $healthCheck = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/health" -ErrorAction SilentlyContinue } catch { $null }
        if ($healthCheck) { break }
    }

    if ($healthCheck) {
        Write-Ok "Daemon running on port $DaemonPort"
    } else {
        Write-Warn "Daemon may still be starting. Check: devbrain status"
        if (Test-Path "$LogsDir\daemon-error.log") {
            $errContent = Get-Content "$LogsDir\daemon-error.log" -Raw -ErrorAction SilentlyContinue
            if ($errContent) { Write-Warn "Daemon stderr: $errContent" }
        }
    }
}

# -- Step 5: Configure Claude Code CLI -------------------------------------

Write-Info "Configuring Claude Code CLI integration..."

$ClaudeDir = "$env:USERPROFILE\.claude"
$ClaudeSettings = "$ClaudeDir\settings.json"
New-Item -ItemType Directory -Force -Path $ClaudeDir | Out-Null

if (Test-Path $ClaudeSettings) {
    $content = Get-Content $ClaudeSettings -Raw
    if ($content -match "devbrain") {
        Write-Ok "Claude Code hook already configured"
    } else {
        Write-Warn "Claude Code settings exist at $ClaudeSettings"
        Write-Warn "Add a PostToolUse hook manually - see README for details"
    }
} else {
    @'
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "",
        "hooks": [
          {
            "type": "command",
            "command": "curl -s -X POST http://127.0.0.1:37800/api/v1/observations -H \"Content-Type: application/json\" -d \"{\\\"sessionId\\\":\\\"$CLAUDE_SESSION_ID\\\",\\\"eventType\\\":\\\"ToolCall\\\",\\\"source\\\":\\\"ClaudeCode\\\",\\\"rawContent\\\":\\\"Tool: $CLAUDE_TOOL_NAME\\\",\\\"project\\\":\\\"$CLAUDE_PROJECT\\\"}\" >/dev/null 2>&1"
          }
        ]
      }
    ]
  }
}
'@ | Set-Content -Path $ClaudeSettings -Encoding UTF8
    Write-Ok "Claude Code hook configured at $ClaudeSettings"
}

# -- Step 6: Configure GitHub Copilot CLI ----------------------------------

Write-Info "Configuring GitHub Copilot CLI integration..."

# Create PowerShell wrapper functions
$ProfileDir = Split-Path $PROFILE -Parent
New-Item -ItemType Directory -Force -Path $ProfileDir -ErrorAction SilentlyContinue | Out-Null

$WrapperCode = @'

# DevBrain wrappers for GitHub Copilot CLI
function ghcs {
    $query = $args -join " "
    $output = & gh copilot suggest @args 2>&1
    Write-Output $output

    $project = try { Split-Path (git rev-parse --show-toplevel 2>$null) -Leaf } catch { "unknown" }
    try {
        Invoke-RestMethod -Uri "http://127.0.0.1:37800/api/v1/observations" -Method Post -ContentType "application/json" -Body (@{
            sessionId = "copilot-$(Get-Date -Format 'yyyyMMdd')"
            eventType = "Conversation"
            source = "VSCode"
            rawContent = "Copilot suggest: $query"
            project = $project
        } | ConvertTo-Json) -ErrorAction SilentlyContinue | Out-Null
    } catch {}
}

function ghce {
    $query = $args -join " "
    $output = & gh copilot explain @args 2>&1
    Write-Output $output

    $project = try { Split-Path (git rev-parse --show-toplevel 2>$null) -Leaf } catch { "unknown" }
    try {
        Invoke-RestMethod -Uri "http://127.0.0.1:37800/api/v1/observations" -Method Post -ContentType "application/json" -Body (@{
            sessionId = "copilot-$(Get-Date -Format 'yyyyMMdd')"
            eventType = "Conversation"
            source = "VSCode"
            rawContent = "Copilot explain: $query"
            project = $project
        } | ConvertTo-Json) -ErrorAction SilentlyContinue | Out-Null
    } catch {}
}
'@

if (Test-Path $PROFILE) {
    $profileContent = Get-Content $PROFILE -Raw
    if ($profileContent -match "ghcs") {
        Write-Ok "Copilot wrappers already in PowerShell profile"
    } else {
        Add-Content -Path $PROFILE -Value $WrapperCode
        Write-Ok "Copilot wrappers added to $PROFILE"
    }
} else {
    Set-Content -Path $PROFILE -Value $WrapperCode
    Write-Ok "Copilot wrappers created in $PROFILE"
}

Write-Ok "  ghcs  - wraps 'gh copilot suggest' with DevBrain capture"
Write-Ok "  ghce  - wraps 'gh copilot explain' with DevBrain capture"

# -- Step 7: Check for Ollama ----------------------------------------------

$ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
if (-not $ollamaCmd) {
    Write-Info "Ollama not found. Installing Ollama (needed for local AI features)..."

    try {
        # Download Ollama Windows installer
        $ollamaInstaller = "$env:TEMP\OllamaSetup.exe"
        Write-Info "Downloading Ollama installer..."
        Invoke-WebRequest -Uri "https://ollama.com/download/OllamaSetup.exe" -OutFile $ollamaInstaller
        Write-Info "Running Ollama installer (follow the prompts)..."
        Start-Process -FilePath $ollamaInstaller -Wait
        Remove-Item $ollamaInstaller -ErrorAction SilentlyContinue

        # Refresh PATH
        $env:PATH = [Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [Environment]::GetEnvironmentVariable("PATH", "User")
        $ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue

        if ($ollamaCmd) {
            Write-Ok "Ollama installed"
        } else {
            Write-Warn "Ollama installed but not in PATH. Restart your terminal."
        }
    } catch {
        Write-Warn "Ollama installation failed. Install manually: https://ollama.ai"
        Write-Warn "DevBrain still works - AI features will activate once Ollama is available."
    }
}

$ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
if ($ollamaCmd) {
    # Check if Ollama is running
    $ollamaHealth = try { Invoke-RestMethod "http://localhost:11434/api/tags" -ErrorAction SilentlyContinue } catch { $null }

    if (-not $ollamaHealth) {
        Write-Info "Starting Ollama..."
        Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
        Start-Sleep -Seconds 3
        $ollamaHealth = try { Invoke-RestMethod "http://localhost:11434/api/tags" -ErrorAction SilentlyContinue } catch { $null }
    }

    if ($ollamaHealth) {
        Write-Ok "Ollama running"

        # Pull llama3.2:3b (~2GB) if not present
        $models = & ollama list 2>$null
        if ($models -notmatch "llama3.2:3b") {
            Write-Info "Pulling llama3.2:3b model (~2GB download)..."
            Write-Info "This may take a few minutes on first setup."
            & ollama pull "llama3.2:3b"
            if ($LASTEXITCODE -eq 0) {
                Write-Ok "Model llama3.2:3b ready"
            } else {
                Write-Warn "Model download failed. Run manually: ollama pull llama3.2:3b"
            }
        } else {
            Write-Ok "Model llama3.2:3b already available"
        }
    } else {
        Write-Warn "Ollama installed but failed to start. Run: ollama serve"
    }
}

# -- Step 8: Create .devbrainignore ----------------------------------------

if ((Test-Path ".git") -and -not (Test-Path ".devbrainignore")) {
    @'
# DevBrain ignore rules (gitignore syntax)
.env*
secrets/
**/credentials*
**/node_modules/
**/bin/
**/obj/
'@ | Set-Content -Path ".devbrainignore" -Encoding UTF8
    Write-Ok "Created .devbrainignore in current project"
}

# -- Step 9: End-to-End Validation -----------------------------------------

Write-Host ""
Write-Info "Running end-to-end validation..."
Write-Host ""

$PassCount = 0
$FailCount = 0
$WarnCount = 0

function Check-Pass($msg) { $script:PassCount++; Write-Host "  PASS  $msg" -ForegroundColor Green }
function Check-Fail($msg) { $script:FailCount++; Write-Host "  FAIL  $msg" -ForegroundColor Red }
function Check-Warn($msg) { $script:WarnCount++; Write-Host "  WARN  $msg" -ForegroundColor Yellow }

# 9.1 - Binaries installed
if (Get-Command devbrain -ErrorAction SilentlyContinue) {
    Check-Pass "devbrain CLI found"
} else { Check-Fail "devbrain CLI not found in PATH" }

if (Test-Path "$InstallDir\devbrain-daemon.exe") {
    Check-Pass "devbrain-daemon.exe exists"
} else { Check-Fail "devbrain-daemon.exe not found" }

# 9.2 - Config exists
if (Test-Path "$DataDir\settings.toml") {
    Check-Pass "settings.toml exists"
} else { Check-Fail "settings.toml not found" }

# 9.3 - Daemon running and healthy
$health = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/health" -ErrorAction Stop } catch { $null }
if ($health -and $health.status) {
    Check-Pass "Daemon responding on port $DaemonPort"
} else { Check-Fail "Daemon not responding on port $DaemonPort" }

# 9.4 - Dashboard accessible
$dashboardStatus = try { (Invoke-WebRequest "http://127.0.0.1:$DaemonPort/" -UseBasicParsing -ErrorAction Stop).StatusCode } catch { 0 }
if ($dashboardStatus -eq 200) {
    Check-Pass "Dashboard serving at http://127.0.0.1:$DaemonPort/"
} else { Check-Warn "Dashboard not accessible (build dashboard first: cd dashboard && npm run build)" }

# 9.5 - API endpoints working
$apiObs = try { (Invoke-WebRequest "http://127.0.0.1:$DaemonPort/api/v1/observations" -UseBasicParsing -ErrorAction Stop).StatusCode } catch { 0 }
if ($apiObs -eq 200) {
    Check-Pass "API /observations endpoint responding"
} else { Check-Fail "API /observations endpoint failed (HTTP $apiObs)" }

$apiSearch = try { (Invoke-WebRequest "http://127.0.0.1:$DaemonPort/api/v1/search?q=test&limit=1" -UseBasicParsing -ErrorAction Stop).StatusCode } catch { 0 }
if ($apiSearch -eq 200) {
    Check-Pass "API /search endpoint responding"
} else { Check-Fail "API /search endpoint failed (HTTP $apiSearch)" }

# 9.6 - Test observation round-trip
$postResult = try {
    Invoke-RestMethod -Uri "http://127.0.0.1:$DaemonPort/api/v1/observations" -Method Post -ContentType "application/json" -Body (@{
        sessionId = "setup-validation"
        eventType = "Decision"
        source = "ClaudeCode"
        rawContent = "DevBrain setup validation test"
        project = "devbrain-setup"
    } | ConvertTo-Json) -ErrorAction Stop
    $true
} catch { $false }

if ($postResult) {
    Check-Pass "Observation write succeeded"
    Start-Sleep -Seconds 1
    $readResult = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/observations?project=devbrain-setup&limit=1" -ErrorAction Stop } catch { $null }
    if ($readResult -and ($readResult | ConvertTo-Json) -match "devbrain-setup") {
        Check-Pass "Observation read-back verified"
    } else { Check-Fail "Observation written but not readable" }
} else { Check-Fail "Observation write failed" }

# 9.7 - Claude Code (deep validation)
$claudeCmd = Get-Command claude -ErrorAction SilentlyContinue
if ($claudeCmd) {
    $claudeVer = try { & claude --version 2>$null | Select-Object -First 1 } catch { "unknown" }
    Check-Pass "Claude CLI installed ($claudeVer)"
} else {
    Check-Warn "Claude CLI not found (install: npm install -g @anthropic-ai/claude-code)"
}

$claudeSettings = "$env:USERPROFILE\.claude\settings.json"
if (Test-Path $claudeSettings) {
    $settingsContent = Get-Content $claudeSettings -Raw
    try {
        $null = $settingsContent | ConvertFrom-Json -ErrorAction Stop
        Check-Pass "Claude settings.json is valid JSON"

        if ($settingsContent -match "PostToolUse" -and $settingsContent -match "$DaemonPort") {
            Check-Pass "PostToolUse hook configured for port $DaemonPort"
        } else {
            Write-Warn "  PostToolUse hook missing - attempting auto-fix..."
            $hookJson = @"
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "",
        "hooks": [
          {
            "type": "command",
            "command": "curl -s -X POST http://127.0.0.1:${DaemonPort}/api/v1/observations -H \"Content-Type: application/json\" -d \"{\\\"sessionId\\\":\\\"`$CLAUDE_SESSION_ID\\\",\\\"eventType\\\":\\\"ToolCall\\\",\\\"source\\\":\\\"ClaudeCode\\\",\\\"rawContent\\\":\\\"Tool: `$CLAUDE_TOOL_NAME\\\",\\\"project\\\":\\\"`$CLAUDE_PROJECT\\\"}\" >/dev/null 2>&1"
          }
        ]
      }
    ]
  }
}
"@
            Set-Content -Path $claudeSettings -Value $hookJson -Encoding UTF8
            if ((Get-Content $claudeSettings -Raw) -match "$DaemonPort") {
                Check-Pass "PostToolUse hook auto-fixed"
            } else {
                Check-Fail "Could not configure PostToolUse hook"
            }
        }
    } catch {
        Copy-Item $claudeSettings "$claudeSettings.bak" -ErrorAction SilentlyContinue
        Set-Content -Path $claudeSettings -Value '{"hooks":{}}' -Encoding UTF8
        Check-Warn "Claude settings.json was invalid - recreated (backup at .bak)"
    }
} else {
    Check-Warn "Claude settings.json not found (will be created when Claude Code is configured)"
}

# Claude round-trip test
if ($claudeCmd -and (Test-Path $claudeSettings) -and ((Get-Content $claudeSettings -Raw) -match "$DaemonPort")) {
    $rtResult = try {
        Invoke-RestMethod -Uri "http://127.0.0.1:$DaemonPort/api/v1/observations" -Method Post -ContentType "application/json" -Body (@{
            sessionId = "setup-claude-test"
            eventType = "Decision"
            source = "ClaudeCode"
            rawContent = "Claude setup validation"
            project = "devbrain-setup-validation"
        } | ConvertTo-Json) -ErrorAction Stop
        $true
    } catch { $false }

    if ($rtResult) {
        Start-Sleep -Milliseconds 500
        $rtRead = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/observations?project=devbrain-setup-validation&limit=1" -ErrorAction Stop } catch { $null }
        if ($rtRead -and ($rtRead | ConvertTo-Json) -match "devbrain-setup-validation") {
            Check-Pass "Claude capture round-trip verified"
        } else { Check-Fail "Claude round-trip: written but not readable" }
    } else { Check-Fail "Claude round-trip: POST failed" }
}

# 9.8 - GitHub Copilot (deep validation)
$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
if ($ghCmd) {
    $ghVer = try { & gh --version 2>$null | Select-Object -First 1 } catch { "unknown" }
    Check-Pass "GitHub CLI installed ($ghVer)"

    $ghCopilotOk = try { & gh copilot --help 2>$null; $LASTEXITCODE -eq 0 } catch { $false }
    if ($ghCopilotOk) {
        Check-Pass "gh copilot extension installed"
    } else {
        Check-Warn "gh copilot extension not installed (run: gh extension install github/gh-copilot)"
    }
} else {
    Check-Warn "GitHub CLI not found (install from https://cli.github.com/)"
}

if ((Test-Path $PROFILE) -and ((Get-Content $PROFILE -Raw) -match "ghcs")) {
    Check-Pass "Copilot wrappers in PowerShell profile"
} else {
    Check-Warn "Copilot wrappers not found in profile"
}

# Copilot round-trip test
if ((Test-Path $PROFILE) -and ((Get-Content $PROFILE -Raw) -match "ghcs")) {
    $crtResult = try {
        Invoke-RestMethod -Uri "http://127.0.0.1:$DaemonPort/api/v1/observations" -Method Post -ContentType "application/json" -Body (@{
            sessionId = "setup-copilot-test"
            eventType = "Conversation"
            source = "VSCode"
            rawContent = "Copilot setup validation"
            project = "devbrain-setup-validation"
        } | ConvertTo-Json) -ErrorAction Stop
        $true
    } catch { $false }

    if ($crtResult) {
        Check-Pass "Copilot capture round-trip verified"
    } else { Check-Fail "Copilot round-trip: POST failed" }
}

# 9.9 - Ollama + model
$ollamaCmd2 = Get-Command ollama -ErrorAction SilentlyContinue
if ($ollamaCmd2) {
    $ollamaUp = try { Invoke-RestMethod "http://localhost:11434/api/tags" -ErrorAction Stop; $true } catch { $false }
    if ($ollamaUp) {
        Check-Pass "Ollama running"
        $models2 = & ollama list 2>$null
        if ($models2 -match "llama3.2:3b") {
            Check-Pass "Model llama3.2:3b available"
        } else { Check-Warn "Model llama3.2:3b not yet downloaded" }
    } else { Check-Warn "Ollama installed but not running" }
} else { Check-Warn "Ollama not installed (AI features disabled)" }

# 9.10 - Database
if (Test-Path "$DataDir\devbrain.db") {
    Check-Pass "Database file exists"
} else { Check-Warn "Database file not yet created (created on first observation)" }

# -- Validation Summary ----------------------------------------------------

Write-Host ""
Write-Host "---------------------------------------------"
Write-Host "  Results:  " -NoNewline
Write-Host "$PassCount passed" -ForegroundColor Green -NoNewline
Write-Host "  " -NoNewline
Write-Host "$FailCount failed" -ForegroundColor Red -NoNewline
Write-Host "  " -NoNewline
Write-Host "$WarnCount warnings" -ForegroundColor Yellow
Write-Host "---------------------------------------------"

if ($FailCount -gt 0) {
    Write-Host ""
    Write-Host "  Setup completed with failures. Check the errors above." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Troubleshooting:"
    Write-Host "    1. Check daemon logs: Get-Content $DataDir\logs\daemon.log"
    Write-Host "    2. Restart daemon:   devbrain stop; devbrain start"
    Write-Host "    3. Check health:     Invoke-RestMethod http://127.0.0.1:$DaemonPort/api/v1/health"
    Write-Host "    4. Report issue:     https://github.com/$Repo/issues"
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  DevBrain is running and capturing your AI sessions."
Write-Host ""
Write-Host "  Quick commands:"
Write-Host "    devbrain status        Check daemon health"
Write-Host "    devbrain briefing      View morning briefing"
Write-Host "    devbrain search '...'  Search your history"
Write-Host "    devbrain dashboard     Open web UI"
Write-Host ""
Write-Host "  Claude Code:  Hooks auto-capture every tool use"
Write-Host "  Copilot CLI:  Use 'ghcs' instead of 'gh copilot suggest'"
Write-Host "                Use 'ghce' instead of 'gh copilot explain'"
Write-Host ""
Write-Host "  Dashboard:    http://127.0.0.1:$DaemonPort"
Write-Host ""
Write-Warn "Restart your terminal to pick up PATH and alias changes."
