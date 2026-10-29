<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Аппаратный сетевой экран ядра Windows (WFP) и Fail-Closed Sentinel Kill-Switch для Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.3-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20(x64)-0078D4?style=for-the-badge&logo=windows" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## Что такое ClaudeBarrier?

**ClaudeBarrier** — автономный клиент сетевой безопасности для Windows, разработанный для исключения случайной утечки реального IP-адреса и сетевых пакетов **Anthropic Claude** (Claude Desktop, веб-интерфейс `claude.ai` и консольный клиент `Claude Code CLI`) мимо защищенного VPN-туннеля.

Объединяет в себе аппаратную фильтрацию сетевых пакетов ядра Windows (**WFP — Windows Filtering Platform**) с превентивной службой мониторинга **Sentinel Guard (архитектура Sentinel Guard 10x)**: мгновенное снятие процессов Claude из памяти при малейшем отклонении публичного IP от закрепленного (Pinned IP) или разрыве VPN-сессии.

---

## 🛡️ Архитектура безопасности (Гибридный WFP + Sentinel Guard)

ClaudeBarrier защищает соединение на двух независимых эшелонах:
1. **Эшелон 1 (Уровень ядра Windows - NDIS/WFP, 0 мс):** Фильтры ядра Windows блокируют любые пакеты к подсетям Anthropic (`160.79.104.0/23`) на всех физических интерфейсах (LAN/Wi-Fi). Даже если приложение пытается передать данные, ядро отбрасывает их на аппаратном уровне.
2. **Эшелон 2 (Служба Sentinel KillSwitch, <15 мс):** Фоновый сторожевой поток с параллельным конкурентным опросом независимых резолверов (`api.ipify.org`, `ipinfo.io`, `ifconfig.me/ip`). При малейшем несовпадении текущего IP с закрепленным VPN-адресом (`PinnedVpnIp`) или падении туннеля, служба принудительно выгружает процессы `Claude.exe`, `claude-desktop.exe`, `claude.exe` (CLI) и `node.exe` из памяти и запрещает их повторный запуск.

```
[ Клиенты: Claude Desktop / claude.ai / Claude Code CLI ]
                         │
               ┌─────────▼─────────┐
               │   ClaudeBarrier   │ (Pinned IP Sentinel & Watchdog)
               └─────────┬─────────┘
                         │
          ┌──────────────┴──────────────┐
          ▼                             ▼
[ VPN / Безопасный туннель ]   [ Физический адаптер (Wi-Fi/LAN) ]
          │                             │
 ✅ Трафик разрешен            ❌ WFP ЯДРО: Аппаратный сброс (DROP)
    (Pinned IP подтвержден)       Подсети Anthropic: 160.79.104.0/23
                                  IPv6: ms_tcpip6 деактивирован
                                  DNS: Smart Multi-Homed заблокирован
                                  🚨 Sentinel: Kill Claude.exe (<15 ms)
```

---

## ⚠️ Ограничения и честное описание возможностей

Утилита решает конкретную инженерную задачу — **100% предотвращение утечки сетевых пакетов и реального IP при сбоях и разрывах VPN**.

| Что ClaudeBarrier делает | Чего ClaudeBarrier НЕ делает |
|---|---|
| Блокирует сетевой трафик Claude при отключении или сбое VPN | Не меняет цифровой отпечаток браузера / Canvas fingerprint |
| Мгновенно (<15 мс) завершает процессы Claude в диспетчере задач при смене IP | Не защищает от бана по поведению, тексту промптов или платёжным картам |
| Предотвращает утечку реального IPv4 и IPv6 провайдера | Не скрывает факт использования VPN от антифрод-систем Anthropic |
| Контролирует системный маршрут ядра `0.0.0.0/0` и `160.79.104.10` | Не делает грязный датацентровый IP доверенным резидентским IP |
| Измеряет 100% реальные метрики (TLS RTT к Anthropic, WFP, Gateway) | Не шифрует трафик самостоятельно (это задача вашего VPN-клиента) |

> [!NOTE]
> Если ваш VPN использует публичные датацентровые подсети (DigitalOcean, Hetzner, AWS) или IP уже внесен в стоп-лист Anthropic, изоляция трафика не снимет блокировку на стороне сервиса. Для стабильной работы используйте качественные туннели с незаспамленными IP-адресами.

---

## 📥 Загрузка (Direct Download)

| Платформа (OS) | Пакеты загрузки (Download) | Описание |
| :--- | :--- | :--- |
| **Windows x64** <br> `Windows 10 / 11` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Setup-x64.exe) | Полный инсталлятор с автоматической установкой в систему и треем |
| **Windows Portable** <br> `Windows 10 / 11` | [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Windows-Portable-x64.zip) | Автономная версия без инсталляции (запуск из любой папки) |

---

## 🔑 Лицензирование и активация

Для включения защиты ядра требуется бесплатный персональный лицензионный ключ в формате `CB-XXXX-XXXX-XXXX-XXXX`:
* **Генерация ключей:** Ключи выдаются официальным Telegram-ботом [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **Криптографическая подпись:** Валидируется по алгоритму HMAC-SHA256 локально без передачи данных третьим лицам.
* **Шифрование Windows DPAPI:** Активированный ключ сохраняется в реестре в зашифрованном виде (`ProtectedData.Protect`) с привязкой к текущей учетной записи Windows.

---

## 🚀 Ключевые возможности версии 6.0.3

* **Sentinel Guard (Fail-Closed Watchdog):** Служба мгновенного выгружения процессов `Claude.exe`, `claude-desktop.exe`, `claude.exe` (CLI) и `node.exe` при отклонении IP или смене сетевого интерфейса.
* **Закрепление доверенного IP (Pinned IP Lock):** При установлении безопасного соединения текущий VPN IP фиксируется. При любой попытке выхода пакета через физический адаптер процесс пресекается на корню.
* **Параллельный конкурентный резолвинг IP:** Опрос независимых гео-эндпоинтов без блокировки UI (`api.ipify.org`, `ipinfo.io`, `ifconfig.me/ip`).
* **100% Реальные метрики (Zero AI/Mock):** Честное измерение сетевой задержки через прямое TLS-рукопожатие с `api.anthropic.com:443`, подсчет реальных активных правил фильтрации WFP и маршрута ядра Windows.
* **Управление правилами в Настройках:** Возможность безопасного удаления и сброса правил изоляции ядра Windows WFP в один клик.
* **Защита спящего режима (Sleep Guard):** Перехват системного события `PowerModeChanged`. Принудительное завершение Claude перед сном и блокировка до восстановления туннеля после пробуждения.
* **Per-Monitor DPI v2 & WCAG 2.2 AA:** Безупречная четкость шрифтов и векторных элементов при масштабировании 125%, 150%, 200%, а также полная поддержка навигации с клавиатуры.

---

## 🔨 Сборка из исходного кода

Для сборки требуются .NET Framework 4.0 / 4.5+ и Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

Сборочный конвейер скомпилирует:
1. `dist\Uninstall_ClaudeBarrier.exe` — системный инструмент чистого удаления;
2. `dist\ClaudeBarrier.exe` — основное приложение со службой Sentinel;
3. `dist\ClaudeBarrier-Setup-x64.exe` — автономный инсталлятор;
4. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — портативный пакет.

---

## 🤖 Бот поддержки и ключей

* Официальный Telegram-бот: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)
* Исходный код бота и Serverless Webhook: папка `bot/` в репозитории.
* GitHub Issues: [skizewike/claude-antiban/issues](https://github.com/skizewike/claude-antiban/issues)

Лицензия: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
