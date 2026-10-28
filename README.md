<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Сетевой экран изоляции трафика и локальный Kill-Switch для Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.2-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-6366f1?style=for-the-badge" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## Что такое ClaudeBarrier?

**ClaudeBarrier** — автономный клиент сетевой безопасности, разработанный для исключения случайной утечки реального IP-адреса и пакетов **Anthropic Claude** (Claude Desktop, веб-интерфейс `claude.ai` и консольный клиент `Claude Code CLI`) мимо защищенного VPN-туннеля.

Интерфейс утилиты выполнен в строгой темной теме: сегментированный тулбар фильтрации журнала, встроенная онлайн-проверка обновлений через GitHub, низкоуровневая диагностика сетевых интерфейсов и шифрование конфигурации с использованием Windows DPAPI.

---

## ⚠️ Ограничения и честное описание возможностей

Утилита решает конкретную техническую задачу — **предотвращение утечки сетевых пакетов на физических интерфейсах при сбоях VPN**. Она не является «магической кнопкой» от любых банов.

| Что ClaudeBarrier делает | Чего ClaudeBarrier НЕ делает |
|---|---|
| Блокирует сетевой трафик Claude при отключении или сбое VPN | Не меняет цифровой отпечаток браузера / устройства (fingerprint) |
| Предотвращает утечку реального IPv4 и IPv6 провайдера | Не защищает от бана по аккаунту, поведению или платёжным данным |
| Закрывает IPv6-утечки на физических адаптерах в реальном времени | Не скрывает факт использования VPN от антифрод-систем Anthropic |
| Завершает процессы Claude при разрыве туннеля или переходе ПК в сон | Не делает «грязный» датацентровый IP доверенным домашним IP |
| Контролирует маршрут 0.0.0.0/0 и целостность правил фильтрации WFP | Не шифрует трафик самостоятельно (это задача вашего VPN) |

> [!NOTE]
> Если ваш VPN использует датацентровые подсети (DigitalOcean, AWS, Hetzner и т.д.) или IP уже внесен в стоп-лист Anthropic, изоляция трафика не решит проблему блокировки на стороне сервиса. Для стабильной работы рекомендуется использовать качественные туннели с незаспамленными IP-адресами.

---

## 📥 Загрузка (Direct Download)

| Платформа (OS) | Пакеты загрузки (Download) |
| :--- | :--- |
| **Windows** <br> `Windows 10 / 11 (x64)` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.2/ClaudeBarrier-Setup-x64.exe) <br><br> [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.2/ClaudeBarrier-Windows-Portable-x64.zip) |
| **Android** <br> `Android 8.0+` | [![APK Universal](https://img.shields.io/badge/APK-Universal-3DDC84?style=for-the-badge&logo=android&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.2/ClaudeBarrier-Android.apk) |

---

## 🔑 Лицензирование и активация

Для включения модулей ядра требуется бесплатный ключ в формате `CB-XXXX-XXXX-XXXX-XXXX`:
* **Генерация ключей:** ключи выдаются официальным Telegram-ботом [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **Криптографическая подпись:** каждый ключ подписан HMAC-SHA256 и валидируется ядром автономно.
* **Шифрование Windows DPAPI:** активированный ключ сохраняется в реестре в зашифрованном виде (`System.Security.Cryptography.ProtectedData`) с привязкой к учетной записи Windows.

---

## 🚀 Ключевые возможности версии 6.0.2

* **Универсальное обнаружение VPN:** автоопределение любых современных VPN-клиентов по типу интерфейса и драйверу (WireGuard, AmneziaVPN, OpenVPN, TAP/TUN, WARP, Mullvad, ProtonVPN, Tailscale, Shadowsocks, Sing-box, Clash, Hiddify).
* **Проверка маршрутизации ядра 0.0.0.0/0:** Win32-вызов `GetBestInterface` для адреса `160.79.104.10` в реальном времени проверяет, что исходящие пакеты Claude действительно направляются в VPN-интерфейс, а не в обход через физический шлюз.
* **Защита при переходе в спящий режим (Sleep/Resume):** перехват системного события `PowerModeChanged`. При уходе ПК в сон процессы Claude немедленно завершаются; при пробуждении блокировка удерживается до подтверждения полного восстановления туннеля VPN.
* **Динамическое пересканирование адаптеров:** при подключении нового сетевого адаптера (USB Wi-Fi, Ethernet-док) правила изоляции WFP создаются автоматически без перезапуска.
* **Контроль целостности WFP:** сторожевой таймер раз в минуту проверяет наличие фильтров ядра. При сбросе сторонним антивирусом или политикой Windows правила восстанавливаются автоматически.
* **Мониторинг утечек IPv6 в реальном времени:** отслеживание глобальных IPv6-адресов на физических интерфейсах с авто-отключением `ms_tcpip6`.
* **Аудит DNS-утечек:** предупреждение пользователя при обнаружении DNS-серверов провайдера в обход шифрованного канала.
* **Строгая блокировка запуска без VPN:** информативный диалог при попытке открыть Claude без туннеля с кнопкой быстрого перехода в сетевые подключения Windows (`ncpa.cpl`).
* **Встроенный обход DPI (Zapret winws):** Desync-модификация пакетов для доступа к доменам Anthropic.

---

## 🛡️ Архитектура безопасности

```
[ Клиенты пользователя: Claude Desktop / Web / Claude Code CLI ]
                              │
                    ┌─────────▼─────────┐
                    │   ClaudeBarrier   │ (Watchdog, Sleep Guard & KillSwitch)
                    └─────────┬─────────┘
                              │
               ┌──────────────┴──────────────┐
               ▼                             ▼
    [ VPN / Защищенный туннель ]  [ Физический адаптер (Wi-Fi/LAN) ]
               │                             │
    ✅ Трафик разрешен             ❌ WFP ЯДРО: Пакеты сброшены (DROP)
       (Маршрут 0.0.0.0/0 подтвержден)    Подсети: 160.79.104.0/23
                                          IPv6: ms_tcpip6 деактивирован
                                          DNS: Smart Multi-Homed заблокирован
```

---

## 🔨 Сборка из исходного кода

Для сборки проекта требуется .NET Framework 4.5+ и Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

Сборочный конвейер скомпилирует:
1. `dist\ClaudeBarrier.exe` — основное приложение;
2. `dist\ClaudeBarrier-Setup-x64.exe` — автономный установщик;
3. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — портативная версия;
4. `dist\ClaudeBarrier-Android.apk` — Android-клиент Sentinel.

---

## 🤖 Бот поддержки и ключей

* Официальный Telegram-бот: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)


Лицензия: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
