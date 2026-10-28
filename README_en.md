<div align="center">

[Русский RU](README.md) / [English EN](README_en.md)

<br />

<img src="app_icon.png" width="120" alt="ClaudeBarrier Logo" />

# ClaudeBarrier
**Network Traffic Isolation Screen & Local Kill-Switch for Anthropic Claude**

<p align="center">
  <a href="https://github.com/skizewike/claude-antiban/releases/latest"><img src="https://img.shields.io/badge/release-v6.0.3-0078D4?style=for-the-badge&logo=github&logoColor=white" alt="Release" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="License" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-6366f1?style=for-the-badge" alt="Platforms" />
  <a href="https://t.me/ClaudeBarrierBot"><img src="https://img.shields.io/badge/Telegram_Bot-@ClaudeBarrierBot-229ED9?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram Bot" /></a>
</p>

</div>

---

## What is ClaudeBarrier?

**ClaudeBarrier** is an open-source network security utility designed to prevent accidental leaks of your real ISP IP address and network packets when using **Anthropic Claude** (Claude Desktop, `claude.ai` web interface, and `Claude Code CLI`) over VPN connections.

The application features a dark user interface: segmented log toolbar, direct GitHub release checks, kernel network routing diagnostics, and secure configuration encryption via Windows DPAPI.

---

## ⚠️ Realistic Expectations & Limitations

ClaudeBarrier solves a specific technical problem: **preventing packet and IP leaks through physical network adapters when VPN tunnels fail or drop**. It is not a silver bullet against all account bans.

| What ClaudeBarrier Does | What ClaudeBarrier Does NOT Do |
|---|---|
| Blocks Claude traffic when VPN drops or misroutes | Does not modify browser or device fingerprints |
| Prevents exposure of your real ISP IPv4 and IPv6 addresses | Does not protect against account bans based on behavioral patterns or billing |
| Actively terminates IPv6 leaks on physical network adapters | Does not hide the fact that a VPN IP is used from Anthropic anti-fraud |
| Immediately kills Claude processes on tunnel drop or PC sleep | Does not turn a datacenter IP into a residential clean IP |
| Verifies default route 0.0.0.0/0 and WFP rule integrity | Does not encrypt traffic itself (that is your VPN's responsibility) |

> [!NOTE]
> If your VPN uses common datacenter IP ranges (DigitalOcean, AWS, Hetzner, etc.) or an IP already on Anthropic's blocklist, isolating traffic will not bypass account restrictions. Use high-quality residential/clean VPN tunnels.

---

## 📥 Direct Download

| Platform (OS) | Download Packages |
| :--- | :--- |
| **Windows** <br> `Windows 10 / 11 (x64)` | [![OfficialSetup x64](https://img.shields.io/badge/OfficialSetup-x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Setup-x64.exe) <br><br> [![Portable x64](https://img.shields.io/badge/Portable-x64-334155?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Windows-Portable-x64.zip) |
| **Android** <br> `Android 8.0+` | [![APK Universal](https://img.shields.io/badge/APK-Universal-3DDC84?style=for-the-badge&logo=android&logoColor=white)](https://github.com/skizewike/claude-antiban/releases/download/v6.0.3/ClaudeBarrier-Android.apk) |

---

## 🔑 Licensing & Activation

Activation requires a free license key formatted as `CB-XXXX-XXXX-XXXX-XXXX`:
* **Key Issuance:** Generated via the official Telegram bot [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot).
* **HMAC-SHA256 Cryptography:** Validated offline by the core.
* **Windows DPAPI Security:** Stored securely in the registry using Windows Data Protection API bound to the current user profile.

---

## 🚀 Key Features in Version 6.0.3

* **Universal VPN Detection:** Auto-discovers any modern VPN client (WireGuard, AmneziaVPN, OpenVPN, TAP/TUN, WARP, Mullvad, ProtonVPN, Tailscale, Shadowsocks, Sing-box, Clash, Hiddify) by interface characteristics and driver description.
* **Kernel Route 0.0.0.0/0 Verification:** Real-time Win32 `GetBestInterface` query for destination `160.79.104.10` ensures packets strictly egress through the VPN tunnel and not physical interfaces.
* **Sleep / Hibernate Leak Protection:** Listens to `PowerModeChanged`. Terminates Claude processes immediately on system suspend; holds the network block on resume until VPN routing is re-established.
* **Dynamic Adapter Rescan:** Detects newly connected physical interfaces (USB Wi-Fi dongles, docking stations) and auto-deploys WFP isolation rules without restarting.
* **Firewall Rule Integrity Watchdog:** Periodically audits active WFP filters; auto-restores isolation rules if removed by third-party antivirus or Windows updates.
* **Real-Time IPv6 Leak Monitor:** Continuously monitors physical adapters for global IPv6 addresses and disables `ms_tcpip6` dynamically.
* **DNS Leak Audit:** Identifies ISP DNS queries and warns the user if encrypted DNS (DoH / Smart DNS) is not active.
* **Failsafe Launch Guard:** Rejects unsafe Claude launches when VPN is offline and offers a one-click shortcut to Windows Network Connections (`ncpa.cpl`).
* **Embedded Zapret DPI Bypass:** Desync packet modifications to counter DPI blocks on Anthropic domains.

---

## 🛡️ Security Architecture

```
[ User Clients: Claude Desktop / Web / Claude Code CLI ]
                              │
                    ┌─────────▼─────────┐
                    │   ClaudeBarrier   │ (Watchdog, Sleep Guard & KillSwitch)
                    └─────────┬─────────┘
                              │
               ┌──────────────┴──────────────┐
               ▼                             ▼
    [ VPN / Secure Tunnel ]       [ Physical NIC (Wi-Fi / LAN) ]
               │                             │
    ✅ Traffic Permitted          ❌ WFP KERNEL: Packets Dropped (DROP)
       (Route 0.0.0.0/0 verified)         CIDRs: 160.79.104.0/23
                                          IPv6: ms_tcpip6 deactivated
                                          DNS: Smart Multi-Homed disabled
```

---

## 🔨 Building from Source

Prerequisites: .NET Framework 4.5+ and Python 3.8+:

```cmd
git clone https://github.com/skizewike/claude-antiban.git
cd claude-antiban
build.bat
```

The pipeline outputs:
1. `dist\ClaudeBarrier.exe` — Main executable;
2. `dist\ClaudeBarrier-Setup-x64.exe` — Standalone installer;
3. `dist\ClaudeBarrier-Windows-Portable-x64.zip` — Portable zip;
4. `dist\ClaudeBarrier-Android.apk` — Android Sentinel client.

---

## 🤖 Support & Key Bot

* Official Telegram Bot: [**@ClaudeBarrierBot**](https://t.me/ClaudeBarrierBot)
* Bot Source: `bot/` directory in repo.
* GitHub Issues: [skizewike/claude-antiban/issues](https://github.com/skizewike/claude-antiban/issues)

License: [MIT](LICENSE) © 2026 ClaudeBarrier Security Team.
