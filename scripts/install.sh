#!/bin/bash
# DevBrain installer — detects platform, downloads latest release, installs to ~/.devbrain/bin/
set -e

REPO="your-org/devbrain"  # TODO: update with your actual GitHub org/repo
INSTALL_DIR="$HOME/.devbrain/bin"

# Detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux)  PLATFORM="linux" ;;
  Darwin) PLATFORM="osx" ;;
  *)      echo "Unsupported OS: $OS"; exit 1 ;;
esac

case "$ARCH" in
  x86_64)  RID="${PLATFORM}-x64" ;;
  aarch64|arm64) RID="${PLATFORM}-arm64" ;;
  *)       echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

echo "Detected platform: $RID"

# Get latest release tag
if [ -z "$VERSION" ]; then
  VERSION=$(curl -sI "https://github.com/$REPO/releases/latest" | grep -i "location:" | sed 's/.*tag\///' | tr -d '\r\n')
  if [ -z "$VERSION" ]; then
    echo "Could not determine latest version. Set VERSION env var manually."
    exit 1
  fi
fi

echo "Installing DevBrain $VERSION..."

# Download
URL="https://github.com/$REPO/releases/download/$VERSION/devbrain-$RID.tar.gz"
TMP_DIR=$(mktemp -d)
echo "Downloading from $URL"
curl -fsSL "$URL" -o "$TMP_DIR/devbrain.tar.gz"

# Extract
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
    echo "Added ~/.devbrain/bin to PATH in $SHELL_RC"
  fi
fi

echo ""
echo "DevBrain $VERSION installed successfully!"
echo ""
echo "  Location: $INSTALL_DIR"
echo "  Binaries: devbrain (CLI), devbrain-daemon (daemon)"
echo ""
echo "Quick start:"
echo "  devbrain start       # Start the daemon"
echo "  devbrain status      # Check health"
echo "  devbrain briefing    # View morning briefing"
echo "  devbrain dashboard   # Open web UI"
echo ""
echo "Restart your shell or run: source $SHELL_RC"
