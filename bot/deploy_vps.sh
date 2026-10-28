#!/usr/bin/env bash
# ClaudeBarrier Telegram Bot — 1-Line VPS Deployment Script (Ubuntu / Debian / CentOS)
set -e

echo "================================================================"
echo "    CLAUDEBARRIER TELEGRAM BOT AUTO-DEPLOY (24/7 SYSTEMD)"
echo "================================================================"

APP_DIR="/opt/claudebarrier-bot"
SERVICE_NAME="claudebarrier-bot"

echo "[*] Installing Python 3 & Git..."
if command -v apt-get &> /dev/null; then
    sudo apt-get update -y && sudo apt-get install -y python3 git
elif command -v yum &> /dev/null; then
    sudo yum install -y python3 git
fi

echo "[*] Setting up application directory at $APP_DIR..."
sudo mkdir -p "$APP_DIR"
if [ ! -d "$APP_DIR/.git" ]; then
    sudo git clone https://github.com/skizewike/claude-antiban.git "$APP_DIR"
else
    cd "$APP_DIR" && sudo git pull origin main
fi

echo "[*] Creating systemd service..."
cat <<EOF | sudo tee /etc/systemd/system/${SERVICE_NAME}.service > /dev/null
[Unit]
Description=ClaudeBarrier Official Telegram Bot
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=${APP_DIR}/bot
ExecStart=/usr/bin/python3 -u ${APP_DIR}/bot/bot.py
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

echo "[*] Reloading systemd & enabling service..."
sudo systemctl daemon-reload
sudo systemctl enable ${SERVICE_NAME}
sudo systemctl restart ${SERVICE_NAME}

echo ""
echo "================================================================"
echo "[+] SUCCESS! ClaudeBarrier Telegram Bot is now running 24/7!"
echo "    Status check: sudo systemctl status ${SERVICE_NAME}"
echo "    Live logs:    sudo journalctl -u ${SERVICE_NAME} -f"
echo "================================================================"
