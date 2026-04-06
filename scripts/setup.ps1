# DevBrain Setup — installs DevBrain and configures AI tool integration
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
Write-Host "  Your developer's second brain" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Detect platform ────────────────────────────────────────────────

$Arch = if ([Environment]::Is64BitOperatingSystem) {
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } else { "win-x64" }
} else {
    Write-Error "32-bit Windows is not supported"; exit 1
}
Write-Info "Platform: $Arch"

# ── Step 2: Install DevBrain ───────────────────────────────────────────────

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

    # Add to PATH
    $UserPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($UserPath -notlike "*\.devbrain\bin*") {
        [Environment]::SetEnvironmentVariable("PATH", "$InstallDir;$UserPath", "User")
        $env:PATH = "$InstallDir;$env:PATH"
    }

    Write-Ok "DevBrain $Version installed to $InstallDir"
}

# ── Step 3: Create default config ─────────────────────────────────────────

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

# ── Step 4: Start daemon ──────────────────────────────────────────────────

$healthCheck = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/health" -ErrorAction SilentlyContinue } catch { $null }

if ($healthCheck) {
    Write-Ok "Daemon already running"
} else {
    Write-Info "Starting DevBrain daemon..."
    try {
        & devbrain start 2>$null
    } catch {
        $daemon = "$InstallDir\devbrain-daemon.exe"
        if (Test-Path $daemon) {
            Start-Process -FilePath $daemon -WindowStyle Hidden
            Start-Sleep -Seconds 3
        }
    }

    $healthCheck = try { Invoke-RestMethod "http://127.0.0.1:$DaemonPort/api/v1/health" -ErrorAction SilentlyContinue } catch { $null }
    if ($healthCheck) {
        Write-Ok "Daemon running on port $DaemonPort"
    } else {
        Write-Warn "Daemon may still be starting. Check: devbrain status"
    }
}

# ── Step 5: Configure Claude Code CLI ─────────────────────────────────────

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
        Write-Warn "Add a PostToolUse hook manually — see README for details"
    }
} else {
    @'
{
  "hooks": {
    "PostToolUse": [
      {
        "type": "command",
        "command": "curl -s -X POST http://127.0.0.1:37800/api/v1/observations -H \"Content-Type: application/json\" -d \"{\\\"sessionId\\\":\\\"%CLAUDE_SESSION_ID%\\\",\\\"eventType\\\":\\\"ToolCall\\\",\\\"source\\\":\\\"ClaudeCode\\\",\\\"rawContent\\\":\\\"Tool: %CLAUDE_TOOL_NAME%\\\",\\\"project\\\":\\\"%CLAUDE_PROJECT%\\\"}\" >nul 2>&1"
      }
    ]
  }
}
'@ | Set-Content -Path $ClaudeSettings -Encoding UTF8
    Write-Ok "Claude Code hook configured at $ClaudeSettings"
}

# ── Step 6: Configure GitHub Copilot CLI ──────────────────────────────────

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

# ── Step 7: Check for Ollama ──────────────────────────────────────────────

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
        Write-Warn "DevBrain still works — AI features will activate once Ollama is available."
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

# ── Step 8: Create .devbrainignore ────────────────────────────────────────

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

# ── Done ──────────────────────────────────────────────────────────────────

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
