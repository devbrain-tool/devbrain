#!/bin/bash
set -e

rm -f /usr/local/bin/devbrain
rm -f /usr/local/bin/devbrain-daemon
rm -f /etc/xdg/autostart/devbrain.desktop

pkill -f devbrain-daemon 2>/dev/null || true
pkill -f devbrain-tray 2>/dev/null || true

# NOTE: ~/.devbrain/ is intentionally preserved (user data)
