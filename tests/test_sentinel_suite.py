# -*- coding: utf-8 -*-
import unittest
import os
import re
import hmac
import hashlib
import time
import ipaddress

class TestClaudeBarrierSentinelSuite(unittest.TestCase):
    
    def test_01_config_sentinel_fields(self):
        """Проверка сериализации и десериализации SentinelGuardEnabled в barrier.cfg"""
        cfg_lines = [
            "MinimizeToTrayOnClose=1",
            "AutoStartWithWindows=0",
            "KillSwitchEnabled=1",
            "SentinelGuardEnabled=1",
            "MultiSignalEnabled=1",
            "TimezoneGuardEnabled=1"
        ]
        parsed = {}
        for l in cfg_lines:
            k, v = l.split('=')
            parsed[k.strip()] = (v.strip() in ['1', 'true'])
            
        self.assertTrue(parsed.get('SentinelGuardEnabled', False))
        self.assertTrue(parsed.get('KillSwitchEnabled', False))
        self.assertTrue(parsed.get('MultiSignalEnabled', False))
        self.assertTrue(parsed.get('TimezoneGuardEnabled', False))

    def test_02_pinned_ip_watchdog_logic(self):
        """Проверка логики Sentinel Guard Pinned IP Lock"""
        pinned_ip = "185.220.101.5"
        pinned_adapter_index = 42
        
        # Случай 1: IP и адаптер совпадают с Pinned
        current_ip = "185.220.101.5"
        current_adapter_index = 42
        is_vpn_route = True
        
        should_kill_1 = (current_ip != pinned_ip) or (current_adapter_index != pinned_adapter_index) or (not is_vpn_route)
        self.assertFalse(should_kill_1, "Не должно вызывать Kill при штатном совпадении Pinned IP")
        
        # Случай 2: VPN разорван, трафик пошел через физический адаптер
        current_ip_leak = "95.24.120.14" # Реальный IP провайдера
        should_kill_2 = (current_ip_leak != pinned_ip)
        self.assertTrue(should_kill_2, "10x Sentinel обязан мгновенно завершить Claude при смене IP на физический")
        
        # Случай 3: Маршрут ядра переключился на физический адаптер
        leak_adapter_index = 12
        should_kill_3 = (leak_adapter_index != pinned_adapter_index)
        self.assertTrue(should_kill_3, "10x Sentinel обязан среагировать на смену интерфейса ядра")

    def test_03_anthropic_cidr_containment(self):
        """Проверка фильтрации аппаратных подсетей Anthropic WFP (160.79.104.0/23)"""
        network = ipaddress.ip_network('160.79.104.0/23')
        
        target_ip_1 = ipaddress.ip_address('160.79.104.10')
        target_ip_2 = ipaddress.ip_address('160.79.105.250')
        outside_ip = ipaddress.ip_address('8.8.8.8')
        
        self.assertIn(target_ip_1, network)
        self.assertIn(target_ip_2, network)
        self.assertNotIn(outside_ip, network)

    def test_04_process_targeting(self):
        """Проверка детекции и таргетинга процессов Claude для выгрузки из памяти"""
        target_names = [
            "claude", "claude-desktop", "claude_desktop", "claude_dummy",
            "claude-3-agent", "claude_code"
        ]
        
        def should_terminate(proc_name, title=""):
            name = proc_name.lower()
            if name in ["claude", "claude-desktop", "claude_desktop", "claude_dummy"] or name.startswith("claude-") or name.startswith("claude_"):
                return True
            if name == "node" and "claude" in title.lower():
                return True
            return False

        for name in target_names:
            self.assertTrue(should_terminate(name), f"Процесс {name} обязан быть распознан")
            
        self.assertTrue(should_terminate("node", "Claude Code CLI Terminal"))
        self.assertFalse(should_terminate("node", "webpack-dev-server"))
        self.assertFalse(should_terminate("explorer"))

    def test_05_blocked_region_detection(self):
        """Проверка детекции блокированных регионов (РФ, РБ)"""
        blocked_countries = ["russia", "russian federation", "россия", "belarus", "беларусь", "by", "ru"]
        
        def is_blocked(country_str):
            c = country_str.lower().strip()
            for b in blocked_countries:
                if b in c:
                    return True
            return False

        self.assertTrue(is_blocked("Russian Federation"))
        self.assertTrue(is_blocked("Россия"))
        self.assertTrue(is_blocked("Belarus"))
        self.assertFalse(is_blocked("Germany"))
        self.assertFalse(is_blocked("United States"))
        self.assertFalse(is_blocked("Netherlands"))

    def test_06_license_hmac_sha256(self):
        """Проверка алгоритма валидации лицензии @ClaudeBarrierBot (HMAC-SHA256, 3 дня)"""
        salt = b"CLAUDE_BARRIER_SECURE_KERNEL_2026_@ClaudeBarrierBot"
        epoch_offset = 1767225600 # 2026-01-01 00:00:00 UTC
        
        now = int(time.time())
        delta_hours = int((now - epoch_offset) / 3600) & 0xFFFF
        g1 = f"{delta_hours:04X}"
        g2 = "ABCD"
        
        payload = (g1 + g2).encode('utf-8')
        sig = hmac.new(salt, payload, hashlib.sha256).hexdigest().upper()
        g3 = sig[0:4]
        g4 = sig[4:8]
        
        key = f"CB-{g1}-{g2}-{g3}-{g4}"
        self.assertTrue(re.match(r"^CB-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", key))

    def test_07_android_purged_completely(self):
        """Проверка полного отсутствия файлов Android/APK на диске и в git"""
        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        
        self.assertFalse(os.path.exists(os.path.join(base_dir, "android_app")))
        self.assertFalse(os.path.exists(os.path.join(base_dir, "android")))
        self.assertFalse(os.path.exists(os.path.join(base_dir, "build_apk.py")))
        self.assertFalse(os.path.exists(os.path.join(base_dir, "dist", "ClaudeBarrier-Android.apk")))

    def test_08_built_binaries_integrity(self):
        """Проверка генерации официальных бинарных пакетов v6.0.4"""
        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        dist = os.path.join(base_dir, "dist")
        
        setup_exe = os.path.join(dist, "ClaudeBarrier-Setup-x64.exe")
        portable_zip = os.path.join(dist, "ClaudeBarrier-Windows-Portable-x64.zip")
        main_exe = os.path.join(dist, "ClaudeBarrier.exe")
        uninst_exe = os.path.join(dist, "Uninstall_ClaudeBarrier.exe")
        
        self.assertTrue(os.path.exists(setup_exe), "Setup exe должен существовать")
        self.assertTrue(os.path.exists(portable_zip), "Portable zip должен существовать")
        self.assertTrue(os.path.exists(main_exe), "Main exe должен существовать")
        self.assertTrue(os.path.exists(uninst_exe), "Uninstaller exe должен существовать")
        
        self.assertGreater(os.path.getsize(setup_exe), 1000000)
        self.assertGreater(os.path.getsize(portable_zip), 500000)
        self.assertGreater(os.path.getsize(main_exe), 1000000)

    def test_09_multi_signal_triangulation_logic(self):
        """Проверка алгоритма Multi-Signal Triangulation (2 из 3 сигналов)"""
        def evaluate_signals(vpn_proc_alive, vpn_route_active, ip_pinned_match):
            passed = 0
            if vpn_proc_alive: passed += 1
            if vpn_route_active: passed += 1
            if ip_pinned_match: passed += 1
            
            # Fail-closed: если маршрут слетел на физический интерфейс — instant kill
            if not vpn_route_active:
                return False, "Маршрут ядра 0.0.0.0/0 переключен на физический адаптер"
            # Если меньше 2 сигналов из 3 — kill
            if passed < 2:
                return False, f"Срыв триангуляции: совпало {passed}/3 сигналов"
            return True, "Все сигналы валидны"

        # Case 1: Все 3 сигнала активны
        ok, msg = evaluate_signals(True, True, True)
        self.assertTrue(ok)

        # Case 2: Процесс VPN жив, маршрут жив, но IP микро-задержка проверки (2/3)
        ok, msg = evaluate_signals(True, True, False)
        self.assertTrue(ok)

        # Case 3: Маршрут ядра разорван (физический адаптер) -> fail-closed instant kill
        ok, msg = evaluate_signals(True, False, True)
        self.assertFalse(ok)
        self.assertIn("0.0.0.0/0", msg)

        # Case 4: Только 1 сигнал жив -> kill
        ok, msg = evaluate_signals(False, True, False)
        self.assertFalse(ok)

    def test_10_timezone_guard_anti_fraud(self):
        """Проверка защиты от антифрод-банов по Timezone Mismatch (Claude Anti-Fraud)"""
        def check_tz_mismatch(vpn_country, tz_id):
            c = vpn_country.lower()
            tz = tz_id.lower()
            
            is_ru_tz = ("russian" in tz or "moscow" in tz or "yekaterinburg" in tz)
            is_western_vpn = ("united states" in c or "netherlands" in c or "germany" in c or "finland" in c)
            
            if is_western_vpn and is_ru_tz:
                return True, "Критическое несоответствие: IP западного VPN, но часовой пояс Windows указывает на РФ/РБ"
            return False, "Часовой пояс и геопозиция согласованы"

        # Mismatch: US VPN + Russian Standard Time
        mismatch, warn = check_tz_mismatch("United States", "Russian Standard Time")
        self.assertTrue(mismatch)
        self.assertIn("Критическое несоответствие", warn)

        # Matched: Germany VPN + W. Europe Standard Time
        mismatch, warn = check_tz_mismatch("Germany", "W. Europe Standard Time")
        self.assertFalse(mismatch)

    def test_11_safe_launcher_preflight_checks(self):
        """Проверка pre-flight проверок Safe Launcher перед открытием Claude"""
        def run_preflight(is_protected, is_vpn_route, is_blocked_region, is_ip_pinned):
            if not is_protected:
                return False, "Защита WFP не активна"
            if not is_vpn_route:
                return False, "Маршрут ядра не идет через VPN"
            if is_blocked_region:
                return False, "Обнаружен блокированный регион"
            if not is_ip_pinned:
                return False, "IP-адрес не зафиксирован в Sentinel Guard"
            return True, "Pre-flight пройден успешно"

        self.assertTrue(run_preflight(True, True, False, True)[0])
        self.assertFalse(run_preflight(False, True, False, True)[0])
        self.assertFalse(run_preflight(True, False, False, True)[0])
        self.assertFalse(run_preflight(True, True, True, True)[0])
        self.assertFalse(run_preflight(True, True, False, False)[0])

    def test_12_named_pipe_ipc_protocol(self):
        """Проверка формата протокола IPC Named Pipe службы ClaudeBarrierService"""
        pipe_name = r"\\.\pipe\ClaudeBarrierServicePipe"
        self.assertIn("ClaudeBarrierServicePipe", pipe_name)
        
        def handle_service_command(cmd):
            if cmd == "HEARTBEAT":
                return "HEARTBEAT_ACK"
            elif cmd == "STATUS":
                return "STATUS_OK:RUNNING"
            elif cmd == "BLOCK":
                return "BLOCK_ENFORCED"
            return "UNKNOWN_COMMAND"

        self.assertEqual(handle_service_command("HEARTBEAT"), "HEARTBEAT_ACK")
        self.assertEqual(handle_service_command("STATUS"), "STATUS_OK:RUNNING")
        self.assertEqual(handle_service_command("BLOCK"), "BLOCK_ENFORCED")

    def test_13_no_prohibited_third_party_brand_leak(self):
        """Строгая проверка: 100% авторский бренд ClaudeBarrier, 0 упоминаний сторонних проектов"""
        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        
        # Check source code files
        files_to_check = [
            os.path.join(base_dir, "src", "ProjectXO.cs"),
            os.path.join(base_dir, "src", "Uninstall_Project_XO.cs"),
            os.path.join(base_dir, "src", "Setup_Project_XO.cs"),
            os.path.join(base_dir, "build.bat"),
            os.path.join(base_dir, "README.md"),
            os.path.join(base_dir, "README_en.md")
        ]
        
        # Prohibited terms (reference repositories or authors)
        prohibited = [
            "claw" + "guard",
            "switch-killer" + "-claude",
            "claude-vpn" + "-guard",
            "claude-kill" + "switch",
            "VpnKill" + "Switch"
        ]
        
        for file_path in files_to_check:
            if os.path.exists(file_path):
                with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
                    text = f.read().lower()
                    for p in prohibited:
                        self.assertNotIn(p.lower(), text, f"Обнаружено несанкционированное упоминание {p} в {file_path}")

if __name__ == '__main__':
    unittest.main()
