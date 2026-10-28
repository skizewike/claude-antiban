<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Professional Network Protection & WFP Hardware Isolation Suite for Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.0-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-6366f1?style=for-the-badge" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## What is ClaudeBarrier?

**ClaudeBarrier** is an open-source network security client built to deliver leak-proof protection for **Anthropic Claude** (Claude Desktop, `claude.ai` web app, and `Claude Code CLI`) against geo-restrictions, account terminations, and ISP route leaks.

The client features a sleek dark UI with zero scrollbars, real-time live event streaming with chip filters, and hardware-grade isolation.

---

## 📥 Direct Download

| Operating System | Download Links |
| :--- | :--- |
| **Windows** <br> `Windows 10 / 11 (x64)` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.0/ClaudeBarrier-Setup-x64.exe) <br><br> [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.0/ClaudeBarrier-Windows-Portable-x64.zip) |
| **Android** <br> `Android 8.0+` | [![APK Universal](https://img.shields.io/badge/APK-Universal-3DDC84?style=for-the-badge&logo=android&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.0/ClaudeBarrier-Android.apk) |

---

## 🔑 Licensing & Activation

An activation key formatted as `CB-XXXX-XXXX-XXXX-XXXX` is required:
* **Key Issuance:** Free activation keys are distributed via the official Telegram bot [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **HMAC-SHA256 Signature:** Every key carries a cryptographic signature verified offline by the core.
* **Windows DPAPI Protection:** Active keys are encrypted on disk using `System.Security.Cryptography.ProtectedData` tied to the user profile, preventing unauthorized key extraction.

---

## 🚀 Key Features

* **Zero-Leak Hardware Isolation (WFP Kernel Firewall):** Kernel-level packet filtering via the Windows Filtering Platform blocks Anthropic Claude CIDR blocks (`160.79.104.0/23` and `2607:6bc0::/32`) on all non-VPN network interfaces.
* **3-Tier Failsafe Network KillSwitch:**
  1. *Kernel (WFP):* Hardware-level packet drop on physical adapters.
  2. *User-space Process Watchdog:* Gateway route monitor terminates Claude processes in memory within 15 ms of tunnel drops, even without admin rights.
  3. *DNS Leak Prevention:* Disables Smart Multi-Homed Name Resolution and shuts down IPv6 on active adapters.
* **Interactive Event Journal with Chip Filters:**
  - Category filters: `[ All ]`, `[ Errors ]`, `[ Network ]`, `[ WFP ]`.
  - Actions: `[ Clear ]`, `[ Export ]`, `[ Copy ]`.
  - Full-height real-time terminal viewport.
* **Embedded Zapret DPI Bypass (winws):** Bundled anti-censorship toolkit for bypassing DPI restrictions on Claude endpoints.
* **Anti-Tamper & Security Hardening:**
  - DLL Hijacking & Binary Planting immunity via `SetDllDirectory("")` and `SetDefaultDllDirectories`.
  - Child process spawning strictly locked to `%SystemRoot%\System32`.
  - Path sanitization and injection guards.

---

## 🛡️ Security Architecture

```
[ User Clients: Claude Desktop / Web / Claude Code CLI ]
                              │
                    ┌─────────▼─────────┐
                    │   ClaudeBarrier   │ (Watchdog & KillSwitch)
                    └─────────┬─────────┘
                              │
               ┌──────────────┴──────────────┐
               ▼                             ▼
    [ VPN / Secure Tunnel ]       [ Physical NIC (Wi-Fi / LAN) ]
               │                             │
    ✅ Traffic Permitted          ❌ WFP KERNEL: Packets Dropped (DROP)
                                     CIDR: 160.79.104.0/23
                                     IPv6: Disabled (ms_tcpip6)
                                     DNS: Leak Protected
```

---

## 🔨 Building from Source

Prerequisites: .NET Framework 4.5+ and Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

The build pipeline will output:
1. `dist\ClaudeBarrier-Setup-x64.exe` — Standalone installer;
2. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — Portable package;
3. `dist\ClaudeBarrier-Android.apk` — Standalone Android APK.

---

## 🤖 Support & Key Bot

* Official Telegram Bot: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)
* Bot Source: `bot/` directory in repo.
* GitHub Issues: [skizewike/claude-antiban/issues](https://github.com/skizewike/claude-antiban/issues)

License: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
