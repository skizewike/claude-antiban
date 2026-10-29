<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Аппаратный экран ядра Windows (WFP), Win32 Route Hook и Hybrid Sentinel Service для Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.4-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20(x64)-0078D4?style=for-the-badge&logo=windows" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## Что такое ClaudeBarrier?

**ClaudeBarrier** — флагманский клиент сетевой безопасности для Windows, разработанный для исключения утечки реального IP-адреса и пакетов **Anthropic Claude** (Claude Desktop, веб-интерфейс `claude.ai` и консольный клиент `Claude Code CLI`) мимо защищенного VPN-туннеля.

Объединяет аппаратную фильтрацию пакетов ядра Windows (**WFP — Windows Filtering Platform**) с гибридной архитектурой мониторинга **Sentinel Guard (NotifyAddrChange Hook 0–5 ms, Multi-Signal Triangulation, системная служба SYSTEM Daemon и Timezone Guard)**: 100% гарантия от бана по смене маршрута или потере туннеля.

---

## 🛡️ Архитектура безопасности (Next-Gen Hybrid Shield)

ClaudeBarrier защищает сессию по многоуровневой схеме:

1. **Win32 Kernel Hook `NotifyAddrChange` (0–5 мс):** Непосредственный перехват системных событий сетевого стека Windows (`iphlpapi.dll` / `tcpip.sys`). При любом аппаратном изменении таблицы маршрутизации реакция наступает мгновенно, без задержек таймеров.
2. **Многосигнальная триангуляция (Multi-Signal Triangulation):** Оценка 3 независимых сигналов в реальном времени:
   - *Сигнал 1 (VPN-процесс):* Активность клиентов WireGuard, OpenVPN, Amnezia, Tailscale, WARP, Sing-box, Clash, v2ray.
   - *Сигнал 2 (Интерфейс ядра):* Активность маршрута `0.0.0.0/0` через виртуальный адаптер (Wintun / WireGuard / TAP).
   - *Сигнал 3 (Pinned IP):* Соответствие публичного IP зафиксированному через параллельный опрос независимых гео-эндпоинтов (`api.ipify.org`, `ipinfo.io`, `ifconfig.me/ip`).
   - *Правило триангуляции:* 2 из 3 сигналов обязаны быть валидны. При сбросе маршрута ядра на физический адаптер — **мгновенный аппаратный сброс (Fail-Closed Drop)**.
3. **Двухэтапный Process Killer:**
   - *Этап 1:* Отправка `WM_CLOSE` всем окнам (включая фоновые) для корректного сохранения данных без повреждения кэша;
   - *Этап 2:* Пауза верификации 150 мс;
   - *Этап 3:* Принудительное снятие выживших проверенных PID (`Process.Kill(entireProcessTree: true)`).
4. **Системная служба Windows (`ClaudeBarrierService`):**
   - Работает под учетной записью `NT AUTHORITY\SYSTEM`;
   - Поддерживает связь с UI через Named Pipe (`\\.\pipe\ClaudeBarrierServicePipe`) с heartbeat 5 сек;
   - Обеспечивает fail-closed блокировку при сбоях интерфейса или до входа пользователя в систему при старте Windows.
5. **Timezone Guard (Anti-Fraud Guard):**
   - Контролирует совпадение системного часового пояса Windows с геолокацией VPN, предотвращая антифрод-блокировки аккаунтов Anthropic.
6. **Safe Launcher:**
   - Предотвращает запуск Claude Desktop, Web или CLI без предварительной валидации всех контрольных проверок безопасности (Pre-Flight Checklist).

```
[ Клиенты: Claude Desktop / claude.ai / Claude Code CLI ]
                         │
        ┌────────────────▼────────────────┐
        │       ClaudeBarrier Hybrid       │ (Win32 Hook 0-5 ms, Multi-Signal,
        │   Tray App + System WFP Daemon  │  Named Pipe IPC, Timezone Guard)
        └────────────────┬────────────────┘
                         │
          ┌──────────────┴──────────────┐
          ▼                             ▼
[ VPN / Безопасный туннель ]   [ Физический адаптер (Wi-Fi/LAN) ]
          │                             │
 ✅ Трафик разрешен            ❌ WFP ЯДРО: Аппаратный сброс (DROP)
    (Pinned IP подтвержден,       Подсети Anthropic: 160.79.104.0/23
     2 из 3 сигналов OK)          Маршрут ядра перехвачен (0-5 ms)
                                  Timezone Guard: синхронизировано
                                  🚨 Sentinel: Two-Stage Kill (<15 ms)
```

---

## ⚠️ Ограничения и честное описание возможностей

Утилита решает конкретную инженерную задачу — **100% предотвращение утечки сетевых пакетов и реального IP при сбоях и разрывах VPN**.

| Что ClaudeBarrier делает | Чего ClaudeBarrier НЕ делает |
|---|---|
| Блокирует сетевой трафик Claude при отключении или сбое VPN | Не меняет цифровой отпечаток браузера / Canvas fingerprint |
| Мгновенно (0–5 мс) перехватывает смену маршрута через `NotifyAddrChange` | Не защищает от бана по поведению, тексту промптов или платёжным картам |
| Двухэтапно выгружает процессы Claude в диспетчере задач | Не скрывает факт использования VPN от антифрод-систем Anthropic |
| Контролирует часовой пояс системы относительно VPN (Timezone Guard) | Не делает датацентровый IP доверенным резидентским IP |
| Защищает ядро на уровне системной службы `NT AUTHORITY\SYSTEM` | Не шифрует трафик самостоятельно (это задача вашего VPN-клиента) |
| Измеряет 100% реальные метрики (TLS RTT к Anthropic, WFP, Gateway) | Не передает трафик третьим лицам |

> [!NOTE]
> Если ваш VPN использует публичные датацентровые подсети (DigitalOcean, Hetzner, AWS) или IP уже внесен в стоп-лист Anthropic, изоляция трафика не снимет блокировку на стороне сервиса. Для стабильной работы используйте качественные туннели с незаспамленными IP-адресами.

---

## 📥 Загрузка (Direct Download)

| Платформа (OS) | Пакеты загрузки (Download) | Описание |
| :--- | :--- | :--- |
| **Windows x64** <br> `Windows 10 / 11` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.4/ClaudeBarrier-Setup-x64.exe) | Полный инсталлятор с автоматической установкой в систему, службой ядра и треем |
| **Windows Portable** <br> `Windows 10 / 11` | [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.4/ClaudeBarrier-Windows-Portable-x64.zip) | Автономная версия без инсталляции (запуск из любой папки) |

---

## 🔑 Лицензирование и активация

Для включения защиты ядра требуется бесплатный персональный лицензионный ключ в формате `CB-XXXX-XXXX-XXXX-XXXX`:
* **Генерация ключей:** Ключи выдаются официальным Telegram-ботом [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **Криптографическая подпись:** Валидируется по алгоритму HMAC-SHA256 локально без передачи данных третьим лицам.
* **Шифрование Windows DPAPI:** Активированный ключ сохраняется в реестре в зашифрованном виде (`ProtectedData.Protect`) с привязкой к текущей учетной записи Windows.

---

## 🚀 Ключевые возможности версии 6.0.4

* **Win32 Hook NotifyAddrChange:** Аппаратная реакция за 0–5 мс на любые изменения таблицы маршрутизации Windows.
* **Многосигнальная триангуляция:** Мониторинг процесса VPN, интерфейса туннеля и Pinned IP по правилу 2 из 3 с мгновенным fail-closed при сбросе маршрута.
* **Двухэтапный Process Killer:** `WM_CLOSE` всем окнам -> 150 мс grace period -> принудительное завершение проверенных процессов `Claude.exe`, `claude-desktop.exe`, `claude.exe` (CLI) и `node.exe`.
* **Гибридная системная служба (`ClaudeBarrierService`):** Поддержка фонового демона Windows SYSTEM с Named Pipe IPC и 5-секундным heartbeat.
* **Timezone Guard:** Проверка согласованности системного часового пояса и геолокации выходного узла VPN.
* **Safe Launcher Pre-Flight:** Контрольная панель предстартовой проверки перед открытием Claude.
* **Управление WFP в Настройках:** Штатная кнопка безопасного сброса и удаления правил ядра.
* **Per-Monitor DPI v2 & WCAG 2.2 AA:** Идеальная четкость Segoe UI при масштабировании 125%, 150%, 200% и навигация с клавиатуры.

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
