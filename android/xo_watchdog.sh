#!/data/data/com.termux/files/usr/bin/bash
# =========================================================================
#  PROJECT XO // BOGDAN ZHUKOV - ANDROID ZERO-LEAK WATCHDOG
#  Protects Claude Code CLI on Android (Termux) against VPN Drops
# =========================================================================

TARGET_VPN_IP="193.27.19.100"
MAX_STRIKES=3

RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

clear
echo -e "${CYAN}"
echo "  //   :::::  ::::   ::::  :::::   :::  ::  ::    :::::: ::  :: ::  :: ::  ::  ::::  ::   ::"
echo " //    ::  :: :: :: ::  :: ::  :: :: :: ::: ::      ::   ::  :: ::  :: :: ::  ::  ::  :: :: "
echo "//     :::::  :: :: :: ::: ::  :: ::::: :: ::::     ::   :::::: ::  :: ::::   ::  ::   :::  "
echo " //    ::  :: :: :: ::  :: ::  :: :: :: ::  :::    ::    ::  :: ::  :: :: ::  ::  ::    ::  "
echo "  //   :::::  ::::   ::::  :::::  :: :: ::   ::   :::::: ::  ::  ::::  ::  ::  ::::     ::  "
echo -e "${NC}"
echo -e "${CYAN}================================================================${NC}"
echo -e "${CYAN}          PROJECT XO // ANDROID ZERO-LEAK GUARDIAN              ${NC}"
echo -e "${CYAN}================================================================${NC}"
echo -e " [OK] Защитный сторож запущен в Termux на Android."
echo -e "      Сервер привязки:   ${YELLOW}${TARGET_VPN_IP}${NC}"
echo -e "      Буфер упреждения:  ${YELLOW}3 секунды${NC}"
echo -e "      При обрыве:        ${RED}pkill -9 claude моментально!${NC}"
echo -e "${CYAN}================================================================${NC}\n"

kill_claude() {
    local reason="$1"
    echo -e "\n${RED}############################################################${NC}"
    echo -e "${RED}[!!!] АВАРИЙНЫЙ KILLSWITCH СРАБОТАЛ! CLAUDE УНИЧТОЖЕН [!!!] ${NC}"
    echo -e "${RED}############################################################${NC}"
    echo -e "${YELLOW}Причина: ${reason}${NC}"
    
    # Жесткое уничтожение процессов
    pkill -9 -f "claude" 2>/dev/null
    pkill -9 -f "node.*claude" 2>/dev/null
    
    # Звуковой сигнал в Termux
    termux-vibrate -d 500 2>/dev/null
    termux-toast "ВНИМАНИЕ! VPN оборвался! Claude был закрыт для защиты от бана." 2>/dev/null
    
    echo -e "${GREEN}[+] Все процессы Claude на Android успешно ликвидированы.${NC}"
    echo -e "${GREEN}[+] Утечка реального IP предотвращена!${NC}\n"
    exit 1
}

consecutive_fails=0

while true; do
    # 1. Проверка доступности через сокет
    if nc -z -w 1 api.anthropic.com 443 2>/dev/null || curl -s --max-time 1 https://api.anthropic.com >/dev/null 2>&1; then
        consecutive_fails=0
    else
        ((consecutive_fails++))
        echo -e "${YELLOW}[!] Потерян пакет через туннель ($consecutive_fails / $MAX_STRIKES)...${NC}"
        if [ $consecutive_fails -ge $MAX_STRIKES ]; then
            kill_claude "3 секунды подряд нет ответа через VPN туннель!"
        fi
    fi

    # 2. Периодический опрос внешнего IP
    CURRENT_IP=$(curl -s --max-time 2 http://ip-api.com/json | grep -o '"query":"[^"]*' | cut -d'"' -f4)
    if [ -n "$CURRENT_IP" ]; then
        if [ -n "$TARGET_VPN_IP" ] && [ "$CURRENT_IP" != "$TARGET_VPN_IP" ]; then
            kill_claude "Внешний IP ($CURRENT_IP) больше не соответствует серверу VPN ($TARGET_VPN_IP)!"
        fi
    fi

    sleep 1
done
