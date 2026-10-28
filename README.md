<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Профессиональный комплекс превентивной сетевой защиты и WFP-изоляции для Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.0-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-6366f1?style=for-the-badge" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## Что такое ClaudeBarrier?

**ClaudeBarrier** — автономный клиент сетевой безопасности, разработанный для гарантированной защиты сессий **Anthropic Claude** (Claude Desktop, веб-интерфейс `claude.ai` и консольный клиент `Claude Code CLI`) от геоблокировок, банов учетных записей и утечек сетевого трафика.

Интерфейс выполнен в минималистичном стиле: темная тема, отсутствие белых скроллбаров, аккуратные скругления, терминальная консоль ядра в реальном времени с чип-фильтрами и защищенная система активации.

---

## 📥 Direct Download

| Платформа (OS) | Пакеты загрузки (Download) |
| :--- | :--- |
| **Windows** <br> `Windows 10 / 11 (x64)` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.0/ClaudeBarrier-Setup-x64.exe) <br><br> [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.0/ClaudeBarrier-Windows-Portable-x64.zip) |
| **Android** <br> `Android 8.0+` | [![APK Universal](https://img.shields.io/badge/APK-Universal-3DDC84?style=for-the-badge&logo=android&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.0/ClaudeBarrier-Android.apk) |

---

## 🔑 Лицензирование и активация

Для работы модулей изоляции требуется лицензионный ключ в формате `CB-XXXX-XXXX-XXXX-XXXX`:
* **Генерация ключей:** бесплатные ключи выдаются официальным Telegram-ботом [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **Криптографическая подпись:** каждый ключ содержит встроенную подпись HMAC-SHA256 и валидируется ядром автономно.
* **Шифрование Windows DPAPI:** активированный ключ защищается криптографией `System.Security.Cryptography.ProtectedData` с привязкой к профилю текущего пользователя. Ключ невозможно извлечь или скопировать в открытом виде.

---

## 🚀 Ключевые возможности

* **Аппаратная изоляция WFP (Zero-Leak Kernel Firewall):** Пакетная фильтрация на уровне драйверов Windows Filtering Platform блокирует сетевые подсети Claude (`160.79.104.0/23` и `2607:6bc0::/32`) на всех физических адаптерах (Wi-Fi, Ethernet). Прямой трафик вне туннеля физически невозможен.
* **3-уровневый KillSwitch (Защита от разрыва VPN):**
  1. *Ядро (WFP):* Аппаратный сброс пакетов (DROP) на сетевых платах.
  2. *User-space Watchdog:* Фоновый процесс-сторож отслеживает состояние маршрутов. При обрыве VPN процессы Claude мгновенно выгружаются из памяти (< 15 мс) даже без прав администратора.
  3. *DNS-изоляция:* Блокировка утечек через Windows Smart Multi-Homed Name Resolution и отключение IPv6 на физических интерфейсах.
* **Интерактивный журнал с чип-фильтрами:**
  - Верхний тулбар фильтрации по категориям: `[ Все ]`, `[ Ошибки ]`, `[ Сеть ]`, `[ WFP ]`.
  - Быстрые действия: `[ 🗑 Очистить ]`, `[ 💾 Экспорт ]`, `[ 📋 Копировать ]`.
  - Полноразмерный терминал консоли событий реального времени.
* **Встроенный обход DPI (Zapret winws):** Встроенный комплект `winws.exe` с Desync-модификацией TCP-пакетов для свободного доступа к API Anthropic без снижения скорости.
* **Anti-Tamper & Security Hardening:**
  - Полная защита от подмены библиотек (DLL Hijacking / Binary Planting) через `SetDllDirectory("")` и `SetDefaultDllDirectories`.
  - Изолированный вызов системных утилит строго из каталога `%SystemRoot%\System32`.
  - Санитизация входящих пользовательских путей и защита от командных инъекций.

---

## 🛡️ Архитектура безопасности

```
[ Клиенты пользователя: Claude Desktop / Web / Claude Code CLI ]
                              │
                    ┌─────────▼─────────┐
                    │   ClaudeBarrier   │ (Watchdog & KillSwitch)
                    └─────────┬─────────┘
                              │
               ┌──────────────┴──────────────┐
               ▼                             ▼
    [ VPN / Secure Tunnel ]       [ Физический адаптер (Wi-Fi/LAN) ]
               │                             │
    ✅ Трафик разрешен             ❌ WFP ЯДРО: Пакеты сброшены (DROP)
                                     Подсети: 160.79.104.0/23
                                     IPv6: Отключен ms_tcpip6
                                     DNS: Защищен от утечек
```

---

## 🔨 Сборка из исходного кода

Для компиляции проекта требуется установленный .NET Framework 4.5+ и Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

Сборочный конвейер `build.bat` автоматически соберет официальные пакеты:
1. `dist\ClaudeBarrier-Setup-x64.exe` — Инсталлятор со встроенным ядром;
2. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — Портативный архив;
3. `dist\ClaudeBarrier-Android.apk` — Автономный Android APK.

---

## 🤖 Бот поддержки и ключей

* Официальный Telegram-бот: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)
* Исходный код бота: каталог `bot/` в репозитории.
* GitHub Issues: [skizewike/claude-antiban/issues](https://github.com/skizewike/claude-antiban/issues)

Лицензия: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
