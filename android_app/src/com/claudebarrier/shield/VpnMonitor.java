package com.claudebarrier.shield;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class VpnMonitor {
    public interface VpnListener {
        void onVpnStatusChanged(boolean isConnected, long pingMs, String country, String ip, boolean isDegraded);
        void onVpnDropped();
    }

    private final Context context;
    private final Handler mainHandler;
    private VpnListener listener;
    private boolean isMonitoring = false;
    private boolean lastVpnState = false;
    private ConnectivityManager.NetworkCallback networkCallback;

    public VpnMonitor(Context context) {
        this.context = context.getApplicationContext();
        this.mainHandler = new Handler(Looper.getMainLooper());
    }

    public void setListener(VpnListener listener) {
        this.listener = listener;
    }

    public boolean isVpnActive() {
        try {
            ConnectivityManager cm = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                Network activeNet = cm.getActiveNetwork();
                if (activeNet != null) {
                    NetworkCapabilities caps = cm.getNetworkCapabilities(activeNet);
                    return caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN);
                }
            } else if (cm != null) {
                for (Network net : cm.getAllNetworks()) {
                    NetworkCapabilities caps = cm.getNetworkCapabilities(net);
                    if (caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) {
                        return true;
                    }
                }
            }
        } catch (Exception ignored) {}
        return false;
    }

    public void startMonitoring() {
        if (isMonitoring) return;
        isMonitoring = true;
        lastVpnState = isVpnActive();

        // 1. Register OS kernel-level network callback (<10 ms notification)
        try {
            ConnectivityManager cm = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                NetworkRequest req = new NetworkRequest.Builder()
                        .addTransportType(NetworkCapabilities.TRANSPORT_VPN)
                        .build();

                networkCallback = new ConnectivityManager.NetworkCallback() {
                    @Override
                    public void onAvailable(Network network) {
                        lastVpnState = true;
                        LogManager.log("NET", "VPN туннель подключен (onAvailable).", "SAFE");
                        notifyListener(true, 0, "Защищено", "", false);
                    }

                    @Override
                    public void onLost(Network network) {
                        lastVpnState = false;
                        LogManager.log("KILLSWITCH", "АВАРИЯ: VPN туннель разорван (onLost)! Запуск перехвата.", "KILL");
                        notifyDropped();
                    }

                    @Override
                    public void onCapabilitiesChanged(Network network, NetworkCapabilities caps) {
                        boolean hasVpn = caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN);
                        if (lastVpnState && !hasVpn) {
                            lastVpnState = false;
                            LogManager.log("KILLSWITCH", "Потеря транспорта TRANSPORT_VPN! Экстренный сброс.", "KILL");
                            notifyDropped();
                        } else if (!lastVpnState && hasVpn) {
                            lastVpnState = true;
                            notifyListener(true, 0, "Защищено", "", false);
                        }
                    }
                };
                cm.registerNetworkCallback(req, networkCallback);
            }
        } catch (Exception e) {
            LogManager.log("WARN", "Не удалось зарегистрировать NetworkCallback: " + e.getMessage(), "WARN");
        }

        // 2. Start Smart Pre-Drop Sentinel background probing thread
        new Thread(new Runnable() {
            @Override
            public void run() {
                while (isMonitoring) {
                    try {
                        Thread.sleep(3500);
                    } catch (InterruptedException ignored) {}

                    boolean vpn = isVpnActive();
                    if (lastVpnState && !vpn) {
                        lastVpnState = false;
                        notifyDropped();
                        continue;
                    }
                    lastVpnState = vpn;

                    if (!vpn) {
                        notifyListener(false, 0, "Отключено", "", false);
                        continue;
                    }

                    // Probe latency to Anthropic
                    long pingMs = probeAnthropicLatency();
                    boolean degraded = (pingMs > 1200 || pingMs == -1);

                    if (degraded) {
                        LogManager.log("SENTINEL", "Предупреждение: задержка шлюза " + pingMs + " мс (деградация связи).", "WARN");
                    }

                    notifyListener(true, pingMs > 0 ? pingMs : 0, "Защищенный туннель", "", degraded);
                }
            }
        }).start();
    }

    public void stopMonitoring() {
        isMonitoring = false;
        try {
            if (networkCallback != null) {
                ConnectivityManager cm = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
                if (cm != null) cm.unregisterNetworkCallback(networkCallback);
            }
        } catch (Exception ignored) {}
    }

    private long probeAnthropicLatency() {
        try {
            long start = System.currentTimeMillis();
            URL u = new URL("https://api.anthropic.com");
            HttpURLConnection conn = (HttpURLConnection) u.openConnection();
            conn.setRequestMethod("HEAD");
            conn.setConnectTimeout(2500);
            conn.setReadTimeout(2500);
            conn.setRequestProperty("User-Agent", "ClaudeBarrier-Sentinel/6.0");
            conn.getResponseCode();
            long elapsed = System.currentTimeMillis() - start;
            conn.disconnect();
            return elapsed;
        } catch (Exception e) {
            return -1;
        }
    }

    private void notifyListener(final boolean isConnected, final long pingMs, final String country, final String ip, final boolean isDegraded) {
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                if (listener != null) {
                    listener.onVpnStatusChanged(isConnected, pingMs, country, ip, isDegraded);
                }
            }
        });
    }

    private void notifyDropped() {
        mainHandler.post(new Runnable() {
            @Override
            public void run() {
                if (listener != null) {
                    listener.onVpnDropped();
                }
            }
        });
    }
}
