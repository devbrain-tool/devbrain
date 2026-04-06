#!/bin/bash
# DevBrain Setup — installs DevBrain and configures AI tool integration
# Usage: curl -fsSL https://raw.githubusercontent.com/devbrain-tool/devbrain/main/scripts/setup.sh | bash
set -e

REPO="devbrain-tool/devbrain"
INSTALL_DIR="$HOME/.devbrain/bin"
DATA_DIR="$HOME/.devbrain"
DAEMON_PORT=37800

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${CYAN}[devbrain]${NC} $1"; }
ok()    { echo -e "${GREEN}[devbrain]${NC} $1"; }
warn()  { echo -e "${YELLOW}[devbrain]${NC} $1"; }
err()   { echo -e "${RED}[devbrain]${NC} $1"; }

echo ""
echo -e "${CYAN}╭─────────────────────────────────────────╮${NC}"
echo -e "${CYAN}│         DevBrain Setup                   │${NC}"
echo -e "${CYAN}│   Your developer's second brain          │${NC}"
echo -e "${CYAN}╰─────────────────────────────────────────╯${NC}"
echo ""

# ── Step 1: Detect platform ────────────────────────────────────────────────

OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux)  PLATFORM="linux" ;;
  Darwin) PLATFORM="osx" ;;
  *)      err "Unsupported OS: $OS"; exit 1 ;;
esac

case "$ARCH" in
  x86_64)       RID="${PLATFORM}-x64" ;;
  aarch64|arm64) RID="${PLATFORM}-arm64" ;;
  *)            err "Unsupported architecture: $ARCH"; exit 1 ;;
esac

info "Platform: $RID"

# ── Step 2: Install DevBrain ───────────────────────────────────────────────

if command -v devbrain &>/dev/null; then
  ok "DevBrain already installed: $(which devbrain)"
else
  info "Installing DevBrain..."

  VERSION=$(curl -sI "https://github.com/$REPO/releases/latest" | grep -i "location:" | sed 's/.*tag\///' | tr -d '\r\n')
  if [ -z "$VERSION" ]; then
    err "Could not determine latest version. Check https://github.com/$REPO/releases"
    exit 1
  fi

  URL="https://github.com/$REPO/releases/download/$VERSION/devbrain-$RID.tar.gz"
  TMP_DIR=$(mktemp -d)
  info "Downloading $VERSION from GitHub Releases..."
  curl -fsSL "$URL" -o "$TMP_DIR/devbrain.tar.gz"

  mkdir -p "$INSTALL_DIR"
  tar -xzf "$TMP_DIR/devbrain.tar.gz" -C "$INSTALL_DIR"
  chmod +x "$INSTALL_DIR/devbrain" "$INSTALL_DIR/devbrain-daemon"
  rm -rf "$TMP_DIR"

  # Add to PATH
  SHELL_RC=""
  if [ -f "$HOME/.zshrc" ]; then
    SHELL_RC="$HOME/.zshrc"
  elif [ -f "$HOME/.bashrc" ]; then
    SHELL_RC="$HOME/.bashrc"
  fi

  if [ -n "$SHELL_RC" ]; then
    if ! grep -q ".devbrain/bin" "$SHELL_RC" 2>/dev/null; then
      echo 'export PATH="$HOME/.devbrain/bin:$PATH"' >> "$SHELL_RC"
    fi
  fi

  export PATH="$INSTALL_DIR:$PATH"
  ok "DevBrain $VERSION installed to $INSTALL_DIR"
fi

# ── Step 3: Create default config ─────────────────────────────────────────

mkdir -p "$DATA_DIR"

if [ ! -f "$DATA_DIR/settings.toml" ]; then
  info "Creating default configuration..."
  cat > "$DATA_DIR/settings.toml" << 'TOML'
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
TOML
  ok "Config created at $DATA_DIR/settings.toml"
else
  ok "Config already exists"
fi

# ── Step 4: Start daemon ──────────────────────────────────────────────────

if curl -s "http://127.0.0.1:$DAEMON_PORT/api/v1/health" >/dev/null 2>&1; then
  ok "Daemon already running"
else
  info "Starting DevBrain daemon..."
  devbrain start 2>/dev/null || {
    # Fallback: start directly
    nohup devbrain-daemon > "$DATA_DIR/logs/daemon.log" 2>&1 &
    sleep 2
  }

  if curl -s "http://127.0.0.1:$DAEMON_PORT/api/v1/health" >/dev/null 2>&1; then
    ok "Daemon running on port $DAEMON_PORT"
  else
    warn "Daemon may still be starting. Check: devbrain status"
  fi
fi

# ── Step 5: Configure Claude Code CLI ─────────────────────────────────────

info "Configuring Claude Code CLI integration..."

CLAUDE_SETTINGS="$HOME/.claude/settings.json"
mkdir -p "$HOME/.claude"

# Claude Code hook: sends observation to DevBrain after every tool use
HOOK_CMD="curl -s -X POST http://127.0.0.1:$DAEMON_PORT/api/v1/observations -H 'Content-Type: application/json' -d '{\"sessionId\":\"'\$CLAUDE_SESSION_ID'\",\"eventType\":\"ToolCall\",\"source\":\"ClaudeCode\",\"rawContent\":\"'\$CLAUDE_TOOL_NAME: \$CLAUDE_TOOL_INPUT'\",\"project\":\"'\$CLAUDE_PROJECT'\"}' >/dev/null 2>&1"

if [ -f "$CLAUDE_SETTINGS" ]; then
  # Check if DevBrain hook already exists
  if grep -q "devbrain" "$CLAUDE_SETTINGS" 2>/dev/null; then
    ok "Claude Code hook already configured"
  else
    warn "Claude Code settings exist. Add this hook manually:"
    echo ""
    echo "  In $CLAUDE_SETTINGS, add to hooks.PostToolUse:"
    echo ""
    echo '  {'
    echo '    "type": "command",'
    echo '    "command": "curl -s -X POST http://127.0.0.1:37800/api/v1/observations -H \"Content-Type: application/json\" -d \"{\\\"sessionId\\\":\\\"$CLAUDE_SESSION_ID\\\",\\\"eventType\\\":\\\"ToolCall\\\",\\\"source\\\":\\\"ClaudeCode\\\",\\\"rawContent\\\":\\\"Tool: $CLAUDE_TOOL_NAME\\\",\\\"project\\\":\\\"$CLAUDE_PROJECT\\\"}\" >/dev/null 2>&1"'
    echo '  }'
    echo ""
  fi
else
  # Create fresh settings with DevBrain hook
  cat > "$CLAUDE_SETTINGS" << 'JSON'
{
  "hooks": {
    "PostToolUse": [
      {
        "type": "command",
        "command": "curl -s -X POST http://127.0.0.1:37800/api/v1/observations -H 'Content-Type: application/json' -d '{\"sessionId\":\"'$CLAUDE_SESSION_ID'\",\"eventType\":\"ToolCall\",\"source\":\"ClaudeCode\",\"rawContent\":\"Tool: '$CLAUDE_TOOL_NAME'\",\"project\":\"'$CLAUDE_PROJECT'\"}' >/dev/null 2>&1"
      }
    ]
  }
}
JSON
  ok "Claude Code hook configured at $CLAUDE_SETTINGS"
fi

# ── Step 6: Configure GitHub Copilot CLI ──────────────────────────────────

info "Configuring GitHub Copilot CLI integration..."

# Copilot CLI doesn't have native hooks, so we create a wrapper
COPILOT_WRAPPER="$INSTALL_DIR/ghcs"

cat > "$COPILOT_WRAPPER" << 'WRAPPER'
#!/bin/bash
# DevBrain wrapper for GitHub Copilot CLI (gh copilot suggest)
# Captures the query and result for DevBrain knowledge graph

QUERY="$*"
DAEMON="http://127.0.0.1:37800"

# Run the actual copilot command and capture output
OUTPUT=$(gh copilot suggest "$@" 2>&1)
EXIT_CODE=$?

echo "$OUTPUT"

# Send to DevBrain (non-blocking)
if curl -s "$DAEMON/api/v1/health" >/dev/null 2>&1; then
  PROJECT=$(basename "$(git rev-parse --show-toplevel 2>/dev/null || pwd)")
  BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")

  curl -s -X POST "$DAEMON/api/v1/observations" \
    -H "Content-Type: application/json" \
    -d "{
      \"sessionId\": \"copilot-$(date +%Y%m%d)\",
      \"eventType\": \"Conversation\",
      \"source\": \"VSCode\",
      \"rawContent\": \"Copilot suggest: $QUERY\",
      \"project\": \"$PROJECT\",
      \"branch\": \"$BRANCH\"
    }" >/dev/null 2>&1 &
fi

exit $EXIT_CODE
WRAPPER
chmod +x "$COPILOT_WRAPPER"

# Also create wrapper for gh copilot explain
COPILOT_EXPLAIN="$INSTALL_DIR/ghce"

cat > "$COPILOT_EXPLAIN" << 'WRAPPER'
#!/bin/bash
# DevBrain wrapper for GitHub Copilot CLI (gh copilot explain)

QUERY="$*"
DAEMON="http://127.0.0.1:37800"

OUTPUT=$(gh copilot explain "$@" 2>&1)
EXIT_CODE=$?

echo "$OUTPUT"

if curl -s "$DAEMON/api/v1/health" >/dev/null 2>&1; then
  PROJECT=$(basename "$(git rev-parse --show-toplevel 2>/dev/null || pwd)")

  curl -s -X POST "$DAEMON/api/v1/observations" \
    -H "Content-Type: application/json" \
    -d "{
      \"sessionId\": \"copilot-$(date +%Y%m%d)\",
      \"eventType\": \"Conversation\",
      \"source\": \"VSCode\",
      \"rawContent\": \"Copilot explain: $QUERY\",
      \"project\": \"$PROJECT\"
    }" >/dev/null 2>&1 &
fi

exit $EXIT_CODE
WRAPPER
chmod +x "$COPILOT_EXPLAIN"

ok "Copilot wrappers created:"
ok "  ghcs  — wraps 'gh copilot suggest' with DevBrain capture"
ok "  ghce  — wraps 'gh copilot explain' with DevBrain capture"

# ── Step 7: Check for Ollama ──────────────────────────────────────────────

if command -v ollama &>/dev/null; then
  if curl -s "http://localhost:11434/api/tags" >/dev/null 2>&1; then
    ok "Ollama detected and running"
    # Pull required model if not present
    if ! ollama list 2>/dev/null | grep -q "llama3.2:3b"; then
      info "Pulling llama3.2:3b model for local AI features..."
      ollama pull llama3.2:3b &
      warn "Model downloading in background. Briefings and tagging will work once complete."
    fi
  else
    warn "Ollama installed but not running. Start it: ollama serve"
  fi
else
  warn "Ollama not installed. DevBrain works without it but AI features (briefings, tagging) need it."
  warn "Install: https://ollama.ai"
fi

# ── Step 8: Create .devbrainignore template ───────────────────────────────

if [ -d ".git" ] && [ ! -f ".devbrainignore" ]; then
  cat > ".devbrainignore" << 'IGNORE'
# DevBrain ignore rules (gitignore syntax)
# Files matching these patterns won't be captured

.env*
secrets/
**/credentials*
**/node_modules/
**/bin/
**/obj/
IGNORE
  ok "Created .devbrainignore in current project"
fi

# ── Done ──────────────────────────────────────────────────────────────────

echo ""
echo -e "${GREEN}╭─────────────────────────────────────────╮${NC}"
echo -e "${GREEN}│         Setup Complete!                  │${NC}"
echo -e "${GREEN}╰─────────────────────────────────────────╯${NC}"
echo ""
echo "  DevBrain is running and capturing your AI sessions."
echo ""
echo "  Quick commands:"
echo "    devbrain status        Check daemon health"
echo "    devbrain briefing      View morning briefing"
echo "    devbrain search \"...\"  Search your history"
echo "    devbrain dashboard     Open web UI"
echo ""
echo "  Claude Code:  Hooks auto-capture every tool use"
echo "  Copilot CLI:  Use 'ghcs' instead of 'gh copilot suggest'"
echo "                Use 'ghce' instead of 'gh copilot explain'"
echo ""
echo "  Dashboard:    http://127.0.0.1:$DAEMON_PORT"
echo ""

if [ -n "$SHELL_RC" ] && grep -q ".devbrain/bin" "$SHELL_RC" 2>/dev/null; then
  warn "Restart your shell or run: source $SHELL_RC"
fi
