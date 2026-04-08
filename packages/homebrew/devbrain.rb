class Devbrain < Formula
  desc "Developer's second brain - captures coding sessions, builds knowledge graph"
  homepage "https://github.com/devbrain/devbrain"
  version "1.0.0"

  if OS.mac? && Hardware::CPU.arm?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-osx-arm64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  elsif OS.mac? && Hardware::CPU.intel?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-osx-x64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  elsif OS.linux? && Hardware::CPU.intel?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-linux-x64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  elsif OS.linux? && Hardware::CPU.arm?
    url "https://github.com/devbrain/devbrain/releases/download/v1.0.0/devbrain-linux-arm64.tar.gz"
    sha256 "PLACEHOLDER_SHA256"
  end

  def install
    bin.install "devbrain"
    bin.install "devbrain-daemon"
    prefix.install "DevBrain.app" if OS.mac?
  end

  # No Homebrew service block - the Electron tray app owns daemon lifecycle.

  def post_install
    # Tray app handles all user-space bootstrap on first launch.
  end

  test do
    assert_match "devbrain", shell_output("#{bin}/devbrain --version")
  end
end
