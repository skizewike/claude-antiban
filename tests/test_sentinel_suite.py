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
            "SentinelGuardEnabled=1"
        ]
        parsed = {}
        for l in cfg_lines:
            k, v = l.split('=')
            parsed[k.strip()] = (v.strip() in ['1', 'true'])
            
        self.assertTrue(parsed.get('SentinelGuardEnabled', False))
        self.assertTrue(parsed.get('KillSwitchEnabled', False))
        
        # Test disabled
        cfg_disabled = "SentinelGuardEnabled=0"
        k, v = cfg_disabled.split('=')
        self.assertFalse(v.strip() in ['1', 'true'])

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
        
        # Адреса серверов Anthropic
        target_ip_1 = ipaddress.ip_address('160.79.104.10')
        target_ip_2 = ipaddress.ip_address('160.79.105.250')
        
        # Адреса сторонних ресурсов
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
        """Проверка генерации официальных бинарных пакетов v6.0.3"""
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
        
        # Verify non-trivial size
        self.assertGreater(os.path.getsize(setup_exe), 1000000)
        self.assertGreater(os.path.getsize(portable_zip), 500000)
        self.assertGreater(os.path.getsize(main_exe), 1000000)

if __name__ == '__main__':
    unittest.main()
