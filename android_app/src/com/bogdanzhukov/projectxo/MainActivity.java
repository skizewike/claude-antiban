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
import android.os.Vibrator;
import android.provider.Settings;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import java.net.InetSocketAddress;
import java.net.Socket;

public class MainActivity extends Activity {
    private WebView webView;
    private Handler handler;
    private Vibrator vibrator;
    private boolean isMonitoring = true;
    private int consecutiveFails = 0;

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

        startNetworkWatchdog();
    }

    private void startNetworkWatchdog() {
        new Thread(new Runnable() {
            @Override
            public void run() {
                while (isMonitoring) {
                    try {
                        Thread.sleep(1000);
                    } catch (InterruptedException ignored) {}

                    final boolean vpnActive = isVpnActive();
                    boolean probeOk = false;

                    try {
                        Socket socket = new Socket();
                        socket.connect(new InetSocketAddress("api.anthropic.com", 443), 1500);
                        probeOk = socket.isConnected();
                        socket.close();
                    } catch (Exception ignored) {}

                    if (vpnActive && probeOk) {
                        consecutiveFails = 0;
                    } else {
                        consecutiveFails++;
                        if (consecutiveFails >= 3) {
                            final String reason = !vpnActive ? "VPN отключился или оборвался!" : "Обрыв связи с серверами Claude!";
                            handler.post(new Runnable() {
                                @Override
                                public void run() {
                                    triggerEmergencyAlarm(reason);
                                }
                            });
                        }
                    }
                }
            }
        }).start();
    }

    private boolean isVpnActive() {
        try {
            ConnectivityManager cm = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm != null) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                    Network activeNet = cm.getActiveNetwork();
                    if (activeNet != null) {
                        NetworkCapabilities caps = cm.getNetworkCapabilities(activeNet);
                        if (caps != null) {
                            return caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN);
                        }
                    }
                }
            }
        } catch (Exception ignored) {}
        return false;
    }

    private void triggerEmergencyAlarm(final String reason) {
        if (vibrator != null) {
            try {
                vibrator.vibrate(500);
            } catch (Exception ignored) {}
        }
        if (webView != null) {
            webView.evaluateJavascript("if (window.triggerProhibited) { window.triggerProhibited('" + reason + "'); }", null);
        }
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
