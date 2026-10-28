package com.claudebarrier.shield;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import org.json.JSONObject;

public class MainActivity extends Activity implements VpnMonitor.VpnListener {
    private WebView webView;
    private Handler handler;
    private VpnMonitor vpnMonitor;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        handler = new Handler(Looper.getMainLooper());

        webView = (WebView) findViewById(R.id.webview);
        WebSettings ws = webView.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setAllowFileAccess(true);
        ws.setAllowContentAccess(true);
        ws.setCacheMode(WebSettings.LOAD_NO_CACHE);

        webView.setWebViewClient(new WebViewClient());
        webView.setWebChromeClient(new WebChromeClient());
        webView.addJavascriptInterface(new BarrierBinding(), "AndroidBarrier");

        webView.loadUrl("file:///android_asset/index.html");

        // Start background guard service
        startGuardService();

        vpnMonitor = new VpnMonitor(this);
        vpnMonitor.setListener(this);
        vpnMonitor.startMonitoring();
    }

    private void startGuardService() {
        try {
            Intent sIntent = new Intent(this, FloatingGuardService.class);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(sIntent);
            } else {
                startService(sIntent);
            }
        } catch (Exception e) {
            LogManager.log("ERR", "Не удалось запустить FloatingGuardService: " + e.getMessage(), "ERR");
        }
    }

    @Override
    public void onVpnStatusChanged(final boolean isConnected, final long pingMs, final String country, final String ip, final boolean isDegraded) {
        handler.post(new Runnable() {
            @Override
            public void run() {
                if (webView != null) {
                    String js = String.format(
                            "if (window.onVpnStateUpdate) { window.onVpnStateUpdate(%b, %d, '%s', '%s', %b); }",
                            isConnected, pingMs, country, ip, isDegraded
                    );
                    webView.evaluateJavascript(js, null);
                }
            }
        });
    }

    @Override
    public void onVpnDropped() {
        handler.post(new Runnable() {
            @Override
            public void run() {
                if (webView != null) {
                    webView.evaluateJavascript("if (window.onVpnDropped) { window.onVpnDropped(); }", null);
                }
            }
        });
    }

    @Override
    protected void onDestroy() {
        if (vpnMonitor != null) vpnMonitor.stopMonitoring();
        super.onDestroy();
    }

    public class BarrierBinding {
        @JavascriptInterface
        public boolean isVpnConnected() {
            return vpnMonitor != null && vpnMonitor.isVpnActive();
        }

        @JavascriptInterface
        public boolean isAccessibilityEnabled() {
            return ClaudeAccessibilityService.isServiceRunning();
        }

        @JavascriptInterface
        public void requestAccessibility() {
            try {
                Intent intent = new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);
                startActivity(intent);
                showToast("Включите службу «ClaudeBarrier Sentinel Guard»");
            } catch (Exception e) {
                showToast("Не удалось открыть настройки спец. возможностей");
            }
        }

        @JavascriptInterface
        public boolean isOverlayPermitted() {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                return Settings.canDrawOverlays(MainActivity.this);
            }
            return true;
        }

        @JavascriptInterface
        public void requestOverlayPermission() {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                try {
                    Intent intent = new Intent(
                            Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                            Uri.parse("package:" + getPackageName())
                    );
                    startActivity(intent);
                    showToast("Разрешите наложение поверх других окон");
                } catch (Exception e) {
                    showToast("Ошибка запроса разрешения наложения");
                }
            }
        }

        @JavascriptInterface
        public void openVpnSettings() {
            try {
                Intent intent = new Intent(Settings.ACTION_VPN_SETTINGS);
                startActivity(intent);
            } catch (Exception e) {
                try {
                    startActivity(new Intent(Settings.ACTION_SETTINGS));
                } catch (Exception ignored) {}
            }
        }

        @JavascriptInterface
        public boolean isActivated() {
            return LicenseManager.isActivated(MainActivity.this);
        }

        @JavascriptInterface
        public String getLicenseInfo() {
            try {
                String key = LicenseManager.getSavedKey(MainActivity.this);
                boolean act = LicenseManager.validateKey(key);
                String rem = LicenseManager.getRemainingTime(key);
                String masked = LicenseManager.maskKey(key);

                JSONObject o = new JSONObject();
                o.put("isActivated", act);
                o.put("remainingTime", rem);
                o.put("maskedKey", masked);
                return o.toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        @JavascriptInterface
        public boolean activateKey(String key) {
            StringBuilder sb = new StringBuilder();
            boolean valid = LicenseManager.validateKey(key, sb);
            if (valid) {
                LicenseManager.saveKey(MainActivity.this, key);
                LogManager.log("AUTH", "Лицензия успешно активирована! Срок: 3 дня.", "SAFE");
                showToast("Лицензия ClaudeBarrier успешно подтверждена!");
                return true;
            } else {
                String reason = sb.toString();
                showToast(reason.isEmpty() ? "Неверный ключ активации" : reason);
                LogManager.log("ERR", "Ошибка активации: " + reason, "ERR");
                return false;
            }
        }

        @JavascriptInterface
        public void triggerEmergencyKillTest() {
            LogManager.log("KILLSWITCH", "Тестовый запуск аварийного сброса (KillSwitch Test)...", "KILL");
            if (vpnMonitor != null) {
                vpnMonitor.setListener(MainActivity.this);
            }
            onVpnDropped();

            ClaudeAccessibilityService acc = ClaudeAccessibilityService.getInstance();
            if (acc != null) {
                acc.emergencyKillAndPurgeClaude("ТЕСТ KILLSWITCH: Сброс Claude на Home и выгрузка из ОЗУ.");
            } else {
                showToast("Тест KillSwitch: Включите Accessibility для выгрузки из Recents");
            }
        }

        @JavascriptInterface
        public void launchClaude() {
            boolean vpn = isVpnConnected();
            SharedPreferences sp = getSharedPreferences(AppConstants.PREFS_NAME, Context.MODE_PRIVATE);
            boolean preemptive = sp.getBoolean(AppConstants.KEY_PREEMPTIVE_GUARD, true);

            if (!vpn && preemptive) {
                showToast("⛔ Запуск Claude заблокирован: VPN отключен!");
                LogManager.log("KILLSWITCH", "Попытка запуска Claude без VPN заблокирована!", "KILL");
                return;
            }

            try {
                Intent launchIntent = getPackageManager().getLaunchIntentForPackage(AppConstants.TARGET_CLAUDE_PKG);
                if (launchIntent != null) {
                    startActivity(launchIntent);
                    LogManager.log("APP", "Запуск официального приложения Claude под защитой.", "SAFE");
                } else {
                    Intent browserIntent = new Intent(Intent.ACTION_VIEW, Uri.parse("https://claude.ai"));
                    startActivity(browserIntent);
                    LogManager.log("APP", "Claude не установлен в системе. Открытие web claude.ai.", "SAFE");
                }
            } catch (Exception e) {
                showToast("Не удалось запустить Claude: " + e.getMessage());
            }
        }

        @JavascriptInterface
        public void toggleFloatingHead(boolean enable) {
            SharedPreferences sp = getSharedPreferences(AppConstants.PREFS_NAME, Context.MODE_PRIVATE);
            sp.edit().putBoolean(AppConstants.KEY_FLOATING_HEAD, enable).apply();
            LogManager.log("SETTING", "Плавающий индикатор: " + (enable ? "ВКЛ" : "ВЫКЛ"), "SAFE");
        }

        @JavascriptInterface
        public void togglePreemptiveGuard(boolean enable) {
            SharedPreferences sp = getSharedPreferences(AppConstants.PREFS_NAME, Context.MODE_PRIVATE);
            sp.edit().putBoolean(AppConstants.KEY_PREEMPTIVE_GUARD, enable).apply();
            LogManager.log("SETTING", "Превентивный барьер запуска: " + (enable ? "ВКЛ" : "ВЫКЛ"), "SAFE");
        }

        @JavascriptInterface
        public String getLogs() {
            return LogManager.getLogsJson();
        }

        @JavascriptInterface
        public void clearLogs() {
            LogManager.clear();
        }

        @JavascriptInterface
        public void showToast(final String msg) {
            handler.post(new Runnable() {
                @Override
                public void run() {
                    Toast.makeText(MainActivity.this, msg, Toast.LENGTH_SHORT).show();
                }
            });
        }
    }
}
