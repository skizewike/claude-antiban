<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Windows Kernel Filtering Platform (WFP) & Fail-Closed Sentinel Kill-Switch for Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.3-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20(x64)-0078D4?style=for-the-badge&logo=windows" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## What is ClaudeBarrier?

**ClaudeBarrier** is a high-security desktop client for Windows engineered to prevent accidental leaks of your real ISP IP address and network packets when using **Anthropic Claude** (Claude Desktop, `claude.ai` web app, and `Claude Code CLI`) over VPN connections.

It bridges low-level hardware packet filtering (**Windows Filtering Platform / WFP**) with an aggressive proactive **Sentinel Guard (Fail-Closed Sentinel Architecture)**: instantly terminating Claude processes in memory (<15 ms) upon any IP drift from the pinned VPN endpoint or routing failure.

---

## 🛡️ Security Architecture (Hybrid WFP + Sentinel Guard)

ClaudeBarrier safeguards your connections across two independent defensive echelons:
1. **Tier 1 (Windows Kernel NDIS/WFP, 0 ms):** Hardware-level filtering drops any packet bound for Anthropic CIDRs (`160.79.104.0/23`) over physical network adapters (LAN/Wi-Fi). Even if an application attempts to transmit data, the Windows kernel silently drops packets before they touch the wire.
2. **Tier 2 (Sentinel KillSwitch Service, <15 ms):** A dedicated background watchdog performing concurrent asynchronous resolution against distributed public IP endpoints (`api.ipify.org`, `ipinfo.io`, `ifconfig.me/ip`). If the public IP deviates from the verified VPN IP (`PinnedVpnIp`) or if the default route changes, it immediately kills `Claude.exe`, `claude-desktop.exe`, `claude.exe` (CLI), and `node.exe` from Task Manager and locks launch vectors.

```
[ Clients: Claude Desktop / claude.ai / Claude Code CLI ]
                         │
               ┌─────────▼─────────┐
               │   ClaudeBarrier   │ (Pinned IP Sentinel & Watchdog)
               └─────────┬─────────┘
                         │
          ┌──────────────┴──────────────┐
          ▼                             ▼
[ VPN / Secure Tunnel ]        [ Physical NIC (Wi-Fi/LAN) ]
          │                             │
 ✅ Traffic Permitted          ❌ WFP KERNEL: Hardware Drop (DROP)
    (Pinned IP verified)          Anthropic CIDRs: 160.79.104.0/23
                                  IPv6: ms_tcpip6 deactivated
                                  DNS: Smart Multi-Homed disabled
                                  🚨 Sentinel: Kill Claude.exe (<15 ms)
```

---

## ⚠️ Realistic Expectations & Technical Boundaries

ClaudeBarrier accomplishes a specific engineering goal: **100% fail-closed prevention of packet and IP leaks during VPN disconnects and routing anomalies**.

| What ClaudeBarrier Does | What ClaudeBarrier Does NOT Do |
|---|---|
| Blocks Claude network traffic when VPN drops or misroutes | Does not modify browser or device fingerprints |
| Instantly kills Claude processes in Task Manager on IP mismatch | Does not protect against account bans based on behavioral patterns or billing |
| Prevents exposure of your real ISP IPv4 and IPv6 addresses | Does not hide the fact that a VPN IP is used from Anthropic anti-fraud |
| Verifies default route 0.0.0.0/0 and Anthropic destination routing | Does not make a datacenter IP look like a residential clean IP |
| Measures 100% genuine metrics (live TLS RTT to Anthropic, WFP, Gateway) | Does not encrypt traffic itself (that is your VPN's responsibility) |

> [!NOTE]
> If your VPN uses datacenter IP ranges (DigitalOcean, AWS, Hetzner, etc.) or an IP already blacklisted by Anthropic, isolating traffic will not bypass account restrictions. Use high-quality residential/clean VPN tunnels.

---

## 📥 Direct Download

| Platform (OS) | Download Packages | Description |
| :--- | :--- | :--- |
| **Windows x64** <br> `Windows 10 / 11` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Setup-x64.exe) | Complete installer with system registration and system tray |
| **Windows Portable** <br> `Windows 10 / 11` | [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Windows-Portable-x64.zip) | Portable standalone zip (runs from any folder, zero installation) |

---

## 🔑 Licensing & Activation

Activation requires a free personal license key in the format `CB-XXXX-XXXX-XXXX-XXXX`:
* **Key Generation:** Issued via the official Telegram bot [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **HMAC-SHA256 Cryptography:** Validated offline by the core engine.
* **Windows DPAPI Security:** Stored securely in the Windows Registry using `ProtectedData.Protect` tied to the active Windows user account.

---

## 🚀 Key Features in Version 6.0.3

* **Sentinel Guard (Fail-Closed Watchdog):** Rapid millisecond termination of `Claude.exe`, `claude-desktop.exe`, `claude.exe` (CLI), and `node.exe` upon any IP deviation or interface shift.
* **Pinned IP Lock:** Fixates the verified VPN IP upon connection. Any outbound attempt through unverified physical routes is suppressed before leaks occur.
* **Concurrent Multi-Endpoint IP Resolution:** Asynchronous non-blocking IP queries across distributed providers (`api.ipify.org`, `ipinfo.io`, `ifconfig.me/ip`).
* **100% Genuine Metrics (Zero Mock/AI Data):** Real TLS handshake RTT directly to `api.anthropic.com:443`, real WFP filter counts from the Windows filtering engine, and live gateway route audits.
* **WFP Rule Management in Settings:** Safe, dedicated one-click removal and reset of active kernel WFP filtering rules without requiring app uninstallation.
* **Sleep / Hibernate Leak Protection:** Intercepts `PowerModeChanged`. Kills Claude processes prior to sleep and retains network blockage until VPN reconnect is confirmed on wake.
* **Per-Monitor DPI v2 & WCAG 2.2 AA:** Crisp Segoe UI rendering across 125%, 150%, and 200% scaling, complete with full keyboard navigation and focus rings.

---

## 🔨 Building from Source

Prerequisites: .NET Framework 4.0 / 4.5+ and Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

The pipeline outputs:
1. `dist\Uninstall_ClaudeBarrier.exe` — Clean system uninstaller;
2. `dist\ClaudeBarrier.exe` — Main application with Sentinel Guard;
3. `dist\ClaudeBarrier-Setup-x64.exe` — Standalone installer;
4. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — Portable zip package.

---

## 🤖 Support & Key Bot

* Official Telegram Bot: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)
* Serverless Webhook Worker: `bot/cloudflare_worker.js`
* GitHub Issues: [skizewike/claude-antiban/issues](https://github.com/skizewike/claude-antiban/issues)

License: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
