package com.bogdanzhukov.projectxo;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.os.Vibrator;
import android.provider.Settings;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class MainActivity extends Activity {
    private WebView webView;
    private Handler handler;
    private Vibrator vibrator;
    private boolean isMonitoring = true;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        handler = new Handler(Looper.getMainLooper());
        vibrator = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);

        webView = (WebView) findViewById(R.id.webview);
        WebSettings ws = webView.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setAllowFileAccess(true);
        ws.setAllowContentAccess(true);

        webView.setWebViewClient(new WebViewClient());
        webView.setWebChromeClient(new WebChromeClient());
        webView.addJavascriptInterface(new XOBinding(), "AndroidXO");

        webView.loadUrl("file:///android_asset/index.html");
    }

    private boolean isVpnActive() {
        try {
            ConnectivityManager cm = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                Network activeNet = cm.getActiveNetwork();
                if (activeNet != null) {
                    NetworkCapabilities caps = cm.getNetworkCapabilities(activeNet);
                    return caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN);
                }
            }
        } catch (Exception ignored) {}
        return false;
    }

    @Override
    protected void onDestroy() {
        isMonitoring = false;
        super.onDestroy();
    }

    public class XOBinding {
        @JavascriptInterface
        public void openVpnSettings() {
            try {
                Intent intent = new Intent(Settings.ACTION_VPN_SETTINGS);
                startActivity(intent);
            } catch (Exception e) {
                try {
                    Intent intent = new Intent(Settings.ACTION_SETTINGS);
                    startActivity(intent);
                } catch (Exception ignored) {}
            }
        }

        @JavascriptInterface
        public void launchClaude() {
            try {
                Intent launchIntent = getPackageManager().getLaunchIntentForPackage("com.anthropic.claude");
                if (launchIntent != null) {
                    startActivity(launchIntent);
                } else {
                    Intent browserIntent = new Intent(Intent.ACTION_VIEW, Uri.parse("https://claude.ai"));
                    startActivity(browserIntent);
                }
            } catch (Exception e) {
                Intent browserIntent = new Intent(Intent.ACTION_VIEW, Uri.parse("https://claude.ai"));
                startActivity(browserIntent);
            }
        }

        @JavascriptInterface
        public void launchBrowser() {
            try {
                Intent browserIntent = new Intent(Intent.ACTION_VIEW, Uri.parse("https://claude.ai"));
                startActivity(browserIntent);
            } catch (Exception ignored) {}
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
                if (!Settings.canDrawOverlays(MainActivity.this)) {
                    Intent intent = new Intent(
                        Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                        Uri.parse("package:" + getPackageName())
                    );
                    startActivity(intent);
                    showToast("Разрешите показ поверх других приложений для виджета защиты");
                } else {
                    showToast("Разрешение поверх окон уже предоставлено");
                }
            }
        }

        @JavascriptInterface
        public void toggleSmartOverlay(boolean enable) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(MainActivity.this)) {
                requestOverlayPermission();
                return;
            }
            try {
                Intent serviceIntent = new Intent(MainActivity.this, FloatingGuardService.class);
                if (enable) {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                        startForegroundService(serviceIntent);
                    } else {
                        startService(serviceIntent);
                    }
                    showToast("Виджет защиты Claude активен");
                } else {
                    stopService(serviceIntent);
                    showToast("Виджет защиты выключен");
                }
            } catch (Exception ex) {
                showToast("Ошибка запуска виджета: " + ex.getMessage());
            }
        }

        @JavascriptInterface
        public void requestBatteryOptimizationExemption() {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                try {
                    PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
                    if (pm != null && !pm.isIgnoringBatteryOptimizations(getPackageName())) {
                        Intent intent = new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS);
                        intent.setData(Uri.parse("package:" + getPackageName()));
                        startActivity(intent);
                    } else {
                        showToast("Оптимизация батареи уже настроена для постоянной защиты");
                    }
                } catch (Exception ex) {
                    try {
                        Intent intent = new Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS);
                        startActivity(intent);
                    } catch (Exception ignored) {}
                }
            }
        }

        @JavascriptInterface
        public void vibratePhone(int ms) {
            if (vibrator != null) {
                try {
                    vibrator.vibrate(ms);
                } catch (Exception ignored) {}
            }
        }

        @JavascriptInterface
        public boolean isVpnConnected() {
            return isVpnActive();
        }

        @JavascriptInterface
        public void fetchAuditData() {
            new Thread(new Runnable() {
                @Override
                public void run() {
                    boolean vpn = isVpnActive();
                    String ip = "Ожидание";
                    String country = "Неизвестно";
                    boolean isRu = false;
                    long pingMs = 0;

                    // Нативный замер пинга до api.anthropic.com
                    try {
                        long start = System.currentTimeMillis();
                        URL u = new URL("https://api.anthropic.com");
                        HttpURLConnection conn = (HttpURLConnection) u.openConnection();
                        conn.setRequestMethod("HEAD");
                        conn.setConnectTimeout(3500);
                        conn.setReadTimeout(3500);
                        conn.setRequestProperty("User-Agent", "ClaudeBarrier-Android/5.0");
                        conn.getResponseCode();
                        pingMs = System.currentTimeMillis() - start;
                        conn.disconnect();
                    } catch (Exception e) {
                        try {
                            long start2 = System.currentTimeMillis();
                            URL u2 = new URL("https://1.1.1.1");
                            HttpURLConnection conn2 = (HttpURLConnection) u2.openConnection();
                            conn2.setRequestMethod("HEAD");
                            conn2.setConnectTimeout(2500);
                            conn2.getResponseCode();
                            pingMs = System.currentTimeMillis() - start2;
                            conn2.disconnect();
                        } catch (Exception ignored) {}
                    }

                    // Нативный запрос IP и страны через HTTP
                    try {
                        URL url = new URL("http://ip-api.com/json");
                        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
                        conn.setConnectTimeout(4000);
                        conn.setReadTimeout(4000);
                        conn.setRequestProperty("User-Agent", "Mozilla/5.0 ClaudeBarrier");
                        BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream()));
                        StringBuilder sb = new StringBuilder();
                        String line;
                        while ((line = reader.readLine()) != null) sb.append(line);
                        reader.close();
                        conn.disconnect();

                        JSONObject json = new JSONObject(sb.toString());
                        String cc = json.optString("countryCode", "");
                        if ("RU".equalsIgnoreCase(cc) || "BY".equalsIgnoreCase(cc)) {
                            isRu = true;
                            country = "RU".equalsIgnoreCase(cc) ? "Россия" : "Беларусь";
                        } else {
                            ip = json.optString("query", "Защищен");
                            country = json.optString("country", "Зарубежный шлюз");
                        }
                    } catch (Exception ex) {
                        if (!vpn) isRu = true;
                    }

                    final String resIp = ip;
                    final String resCountry = country;
                    final boolean resIsRu = isRu;
                    final boolean resVpn = vpn;
                    final long resPing = pingMs;

                    handler.post(new Runnable() {
                        @Override
                        public void run() {
                            if (webView != null) {
                                String js = String.format(
                                    "if (window.onAuditResult) { window.onAuditResult(%b, '%s', '%s', %b, %d); }",
                                    resVpn, resIp, resCountry, resIsRu, resPing
                                );
                                webView.evaluateJavascript(js, null);
                            }
                        }
                    });
                }
            }).start();
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
