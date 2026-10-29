<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Windows Kernel Filtering Platform (WFP), Win32 Route Hook & Hybrid Sentinel Service for Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.5-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20(x64)-0078D4?style=for-the-badge&logo=windows" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## What is ClaudeBarrier?

**ClaudeBarrier** is an advanced desktop security client for Windows engineered to prevent accidental leaks of your real ISP IP address and network packets when using **Anthropic Claude** (Claude Desktop, `claude.ai` web app, and `Claude Code CLI`) over VPN connections.

It bridges hardware-level packet filtering (**Windows Filtering Platform / WFP**) with a next-generation hybrid monitoring engine **Sentinel Guard (NotifyAddrChange Hook 0–5 ms, Multi-Signal Triangulation, SYSTEM Daemon Service, and Timezone Guard)**: 100% fail-closed guarantee against route changes and tunnel disconnects.

---

## 🛡️ Security Architecture (Next-Gen Hybrid Shield)

ClaudeBarrier protects your session across multiple coordinated layers:

1. **Win32 Kernel Hook `NotifyAddrChange` (0–5 ms):** Direct hardware-speed intercept of Windows networking events (`iphlpapi.dll` / `tcpip.sys`). Any routing table shift triggers an instant callback without timer polling latency.
2. **Multi-Signal Triangulation:** Real-time evaluation of 3 independent signals:
   - *Signal 1 (VPN Process):* Status of WireGuard, OpenVPN, Amnezia, Tailscale, WARP, Sing-box, Clash, v2ray.
   - *Signal 2 (Kernel Route):* Integrity of default route `0.0.0.0/0` via virtual adapter (Wintun / WireGuard / TAP).
   - *Signal 3 (Pinned IP):* Concurrent asynchronous multi-endpoint verification (`api.ipify.org`, `ipinfo.io`, `ifconfig.me/ip`).
   - *Triangulation Rule:* At least 2 of 3 signals must pass. If the kernel route flips to a physical NIC, an **instant hardware fail-closed drop** is triggered.
3. **Two-Stage Process Killer:**
   - *Stage 1:* Posts `WM_CLOSE` to all windows (including background windows) to preserve app state cleanly;
   - *Stage 2:* 150 ms verification grace period;
   - *Stage 3:* Immediate force-kill of surviving verified target processes (`Process.Kill(entireProcessTree: true)`).
4. **Windows Service Daemon (`ClaudeBarrierService`):**
   - Runs as a system service under `NT AUTHORITY\SYSTEM`;
   - Connects with Tray UI via Named Pipe (`\\.\pipe\ClaudeBarrierServicePipe`) with a 5s heartbeat;
   - Ensures boot-time fail-closed protection before user login and survives tray app restarts.
5. **Timezone Guard (Anti-Fraud Protection):**
   - Detects mismatches between Windows system timezone and VPN geolocation, preventing Anthropic anti-fraud heuristic bans.
6. **Safe Launcher:**
   - Pre-flight checklist inspection ensuring WFP filters, route, and pinned IP are verified before launching Claude Desktop, Web, or CLI.

```
[ Clients: Claude Desktop / claude.ai / Claude Code CLI ]
                         │
        ┌────────────────▼────────────────┐
        │       ClaudeBarrier Hybrid       │ (Win32 Hook 0-5 ms, Multi-Signal,
        │   Tray App + System WFP Daemon  │  Named Pipe IPC, Timezone Guard)
        └────────────────┬────────────────┘
                         │
          ┌──────────────┴──────────────┐
          ▼                             ▼
[ VPN / Secure Tunnel ]        [ Physical NIC (Wi-Fi/LAN) ]
          │                             │
 ✅ Traffic Permitted          ❌ WFP KERNEL: Hardware Drop (DROP)
    (Pinned IP verified,          Anthropic CIDRs: 160.79.104.0/23
     2 of 3 signals OK)           Kernel Route Hook active (0-5 ms)
                                  Timezone Guard: synchronized
                                  🚨 Sentinel: Two-Stage Kill (<15 ms)
```

---

## ⚠️ Realistic Expectations & Technical Boundaries

ClaudeBarrier accomplishes a specific engineering goal: **100% fail-closed prevention of packet and IP leaks during VPN disconnects and routing anomalies**.

| What ClaudeBarrier Does | What ClaudeBarrier Does NOT Do |
|---|---|
| Blocks Claude network traffic when VPN drops or misroutes | Does not modify browser or device fingerprints |
| Intercepts route changes in 0–5 ms via `NotifyAddrChange` | Does not protect against account bans based on behavioral patterns or billing |
| Two-stage graceful-to-force termination of Claude processes | Does not hide the fact that a VPN IP is used from Anthropic anti-fraud |
| Guards against system timezone vs VPN geo-mismatch (Timezone Guard) | Does not turn a datacenter IP into a residential clean IP |
| Protects kernel at `NT AUTHORITY\SYSTEM` service level | Does not encrypt traffic itself (that is your VPN's responsibility) |
| Measures 100% genuine metrics (live TLS RTT to Anthropic, WFP, Gateway) | Does not proxy traffic to third parties |

> [!NOTE]
> If your VPN uses datacenter IP ranges (DigitalOcean, AWS, Hetzner, etc.) or an IP already blacklisted by Anthropic, isolating traffic will not bypass account restrictions. Use high-quality residential/clean VPN tunnels.

---

## 📥 Direct Download

| Platform (OS) | Download Packages | Description |
| :--- | :--- | :--- |
| **Windows x64** <br> `Windows 10 / 11` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.5/ClaudeBarrier-Setup-x64.exe) | Complete installer with system registration, background service, and tray |
| **Windows Portable** <br> `Windows 10 / 11` | [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.5/ClaudeBarrier-Windows-Portable-x64.zip) | Portable standalone zip (runs from any folder, zero installation) |

---

## 🔑 Licensing & Activation

Activation requires a free personal license key in the format `CB-XXXX-XXXX-XXXX-XXXX`:
* **Key Generation:** Issued via the official Telegram bot [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **HMAC-SHA256 Cryptography:** Validated offline by the core engine.
* **Windows DPAPI Security:** Stored securely in the Windows Registry using `ProtectedData.Protect` tied to the active Windows user account.

---

## 🚀 Key Features in Version 6.0.5

* **Zero-Spam Watchdog Engine:** Periodic background watchdog operates silently when no rogue Claude processes exist, eliminating tray balloon notifications and event log flooding.
* **Modal Dialog Overlap Guard:** Prevents overlapping test dialogs during concurrent socket evaluations with proper parent window anchoring.
* **VPN Route Recognition in WFP Checks:** Correctly identifies active WireGuard / AmneziaVPN tunnel routing as secured rather than false positive leaks.
* **Notification Rate-Limiting:** Cooldown throttling on Windows system tray balloon tips for quiet background operation.
* **Win32 Hook NotifyAddrChange:** Hardware-speed 0–5 ms response to any Windows routing table changes.
* **Multi-Signal Triangulation:** 3 independent vector checks with 2-of-3 consensus rule and instant fail-closed kill on physical route switch.
* **Two-Stage Process Killer:** `WM_CLOSE` to all windows -> 150 ms grace pause -> verified process termination for `Claude.exe`, `claude-desktop.exe`, `claude.exe` (CLI), and `node.exe`.
* **Hybrid System Service (`ClaudeBarrierService`):** Background Windows SYSTEM daemon with Named Pipe IPC and 5s heartbeat.
* **Timezone Guard:** Prevents anti-fraud account flags by detecting timezone-to-VPN geolocation discrepancies.
* **Safe Launcher Pre-Flight:** Pre-launch verification checklist before opening Claude.
* **WFP Rule Management in Settings:** One-click safe purge and rebuild of Windows kernel filtering rules.
* **Per-Monitor DPI v2 & WCAG 2.2 AA:** Crisp Segoe UI rendering across 125%, 150%, and 200% scaling, complete with keyboard navigation.

---

## 🔨 Building from Source

Requires .NET Framework 4.0 / 4.5+ and Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

Build outputs:
1. `dist\Uninstall_ClaudeBarrier.exe` — system cleanup tool;
2. `dist\ClaudeBarrier.exe` — main shield application and Sentinel service;
3. `dist\ClaudeBarrier-Setup-x64.exe` — standalone installer;
4. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — portable distribution.

---

## 🤖 Support & Key Bot

* Official Telegram Bot: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)
* Bot source code & Serverless Webhook: `bot/` folder in the repository.
* GitHub Issues: [skizewike/claude-antiban/issues](https://github.com/skizewike/claude-antiban/issues)

License: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
