#!/data/data/com.termux/files/usr/bin/bash
# =========================================================================
#  PROJECT XO // BOGDAN ZHUKOV - Termux Quick Installer
# =========================================================================

echo -e "\033[0;36m[+] Установка зависимостей Project XO в Termux...\033[0m"
pkg update -y && pkg install -y curl netcat-openbsd termux-api procps

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cp "$DIR/xo_watchdog.sh" "$HOME/xo_watchdog.sh"
chmod +x "$HOME/xo_watchdog.sh"

echo -e "\033[0;32m[✓] Project XO успешно установлен в домашнюю папку Termux!\033[0m"
echo -e "\033[1;33mДля запуска сторожа выполните: ~/xo_watchdog.sh\033[0m"
