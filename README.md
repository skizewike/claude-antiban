# Claude Anti-Ban Barrier (Project XO)

**Автор:** Bogdan Zhukov ([@bzhukov](https://t.me/bzhukov))  
**Версия:** 5.3.0  
**Платформы:** Windows 10/11 (WFP Ядро / .NET Framework / Cloudflare Zero-Trust Dark UI) + Android (Standalone APK)  
**Репозиторий:** [skizewike/claude-antiban](https://github.com/skizewike/claude-antiban)

Комплексный комплекс превентивной сетевой защиты и изоляции для безопасной работы с сервисами Anthropic Claude (Claude Desktop, веб-интерфейс claude.ai и терминальный клиент Claude Code CLI) из регионов с ограниченным доступом.

---

## Архитектура и функционал

### 1. Превентивная блокировка прямых утечек (WFP Shield)
Anthropic применяет строгую политику постоянной блокировки аккаунтов при фиксации хотя бы одного прямого сетевого пакета с IP-адресов РФ/РБ. Project XO решает эту проблему на уровне ядра ОС:
- Блокировка всех исходящих соединений к подсетям Claude (`160.79.104.0/23`) через физические сетевые адаптеры (Ethernet, Wi-Fi) с помощью правил Windows Filtering Platform (WFP).
- Доступ к серверам Anthropic разрешается исключительно через верифицированные виртуальные туннели (WireGuard, OpenVPN, Wintun, Amnezia).

### 2. Встроенный модуль Zapret DPI Bypass (winws.exe)
- Полностью встроен в сборку `ProjectXO.exe` в виде сжатого ресурса: `winws.exe`, `WinDivert.dll`, `WinDivert64.sys`, `cygwin1.dll` и бинарные шаблоны QUIC/TLS.
- **По умолчанию выключен.** Активируется пользователем по требованию в один клик из раздела «Обход блокировок» либо опционально при включении общего барьера защиты.
- Поддерживает ручную настройку аргументов командной строки и путей во вкладке «Настройки».

### 3. Честная сетевая телеметрия (Без имитации)
- Многопоточный опрос независимых геораспределенных шлюзов (`ip-api.com`, `api.ipify.org`, `ipinfo.io`) для вычисления реального публичного IP и юрисдикции.
- Реальный замер задержки отклика (RTT/Ping) к боевому API Anthropic (`api.anthropic.com`).
- Проверка сетевых интерфейсов на утечки IPv6 и валидация системного DNS-кэша.

### 4. Интеграция с клиентами Claude
- **Claude Desktop:** автообнаружение инсталляций (включая Store-пакет `Microsoft\\WindowsApps\\claude-desktop.exe` и стандартные пути `AppData`).
- **Claude Web:** защищенный запуск в браузере по умолчанию.
- **Claude Code CLI:** запуск терминального агента с поддержкой кастомного пути из настроек (`claude.cmd` / `claude.exe`).

### 5. Контакт с разработчиком
- Карточка разработчика **Bogdan Zhukov** в нижней части бокового меню и системном трее.
- Прямой переход в Telegram: [@bzhukov](https://t.me/bzhukov).

---

## Установка и запуск

### Вариант 1: Установщик (Рекомендуется)
1. Скачайте `Setup_Project_XO.exe` из раздела [Releases](https://github.com/skizewike/claude-antiban/releases).
2. Запустите файл от имени администратора. Мастер установит барьер в `C:\Program Files\Project XO`, настроит правила брандмауэра и создаст ярлыки.

### Вариант 2: Портативный запуск
1. Скачайте `ProjectXO.exe`.
2. Запустите от имени администратора. Программа готова к работе без предварительной настройки.

---

## Сборка из исходного кода

Для компиляции проекта требуется .NET Framework 4.5+ (стандартно присутствует во всех версиях Windows 10 и 11):

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

Скрипт `build.bat` автоматически соберет:
- `dist\ProjectXO.exe` — основное приложение со встроенным Zapret;
- `dist\Setup_Project_XO.exe` — автономный установщик;
- `dist\Uninstall_Project_XO.exe` — утилиту полного удаления;
- `dist\ProjectXO.apk` — нативный Android-компаньон.

---

## Связь и поддержка

Разработчик: **Bogdan Zhukov**  
Telegram: [@bzhukov](https://t.me/bzhukov)
