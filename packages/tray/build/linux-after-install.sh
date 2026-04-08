#!/bin/bash
set -e

ln -sf /opt/DevBrain/resources/bin/devbrain /usr/local/bin/devbrain
ln -sf /opt/DevBrain/resources/bin/devbrain-daemon /usr/local/bin/devbrain-daemon

mkdir -p /etc/xdg/autostart
cat > /etc/xdg/autostart/devbrain.desktop << 'EOF'
[Desktop Entry]
Type=Application
Name=DevBrain
Exec=/opt/DevBrain/devbrain-tray
Icon=/opt/DevBrain/resources/assets/icon.png
Comment=Developer's second brain
Categories=Development;
X-GNOME-Autostart-enabled=true
StartupNotify=false
Terminal=false
EOF
