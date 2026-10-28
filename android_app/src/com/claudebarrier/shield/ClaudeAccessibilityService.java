package com.claudebarrier.shield;

import android.accessibilityservice.AccessibilityService;
import android.app.ActivityManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;
import android.view.accessibility.AccessibilityEvent;
import android.widget.Toast;

public class ClaudeAccessibilityService extends AccessibilityService {
    private static ClaudeAccessibilityService instance;
    private Handler handler;
    private ActivityManager activityManager;
    private long lastInterceptTime = 0;
    private VpnMonitor vpnMonitorInstance;

    public static ClaudeAccessibilityService getInstance() {
        return instance;
    }

    public static boolean isServiceRunning() {
        return instance != null;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        instance = this;
        handler = new Handler(Looper.getMainLooper());
        activityManager = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
        LogManager.log("GUARD", "Служба Accessibility Sentinel подключена к системе.", "SAFE");
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
        if (event == null) return;
        CharSequence pkg = event.getPackageName();
        if (pkg == null) return;

        String pkgName = pkg.toString();
        if (AppConstants.TARGET_CLAUDE_PKG.equals(pkgName)) {
            long now = System.currentTimeMillis();
            if (now - lastInterceptTime < 2000) {
                return;
            }
            // Claude is attempting to enter foreground
            SharedPreferences sp = getSharedPreferences(AppConstants.PREFS_NAME, Context.MODE_PRIVATE);
            boolean preemptiveGuard = sp.getBoolean(AppConstants.KEY_PREEMPTIVE_GUARD, true);

            if (vpnMonitorInstance == null) {
                vpnMonitorInstance = new VpnMonitor(this);
            }
            boolean isVpnSafe = vpnMonitorInstance.isVpnActive();

            if (!isVpnSafe && preemptiveGuard) {
                lastInterceptTime = now;
                // VPN is OFF! Preemptively intercept and boot to Home
                LogManager.log("KILLSWITCH", "ПРЕВЕНТИВНЫЙ БАРЬЕР: Запуск Claude без VPN заблокирован!", "KILL");
                emergencyKillAndPurgeClaude("Запуск Claude заблокирован: безопасный VPN выключен!");
            }
        }
    }

    @Override
    public void onInterrupt() {
        LogManager.log("WARN", "Accessibility Sentinel прерван системой.", "WARN");
    }

    @Override
    public void onDestroy() {
        instance = null;
        super.onDestroy();
    }

    public void emergencyKillAndPurgeClaude(final String alertMsg) {
        // 1. Immediately drop to Home (<20 ms)
        try {
            performGlobalAction(GLOBAL_ACTION_HOME);
        } catch (Exception ignored) {}

        // 2. Kill background processes
        if (activityManager != null) {
            try {
                activityManager.killBackgroundProcesses(AppConstants.TARGET_CLAUDE_PKG);
            } catch (Exception ignored) {}
        }

        // 3. Purge from Recents / Overview so no screenshot lingers
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                try {
                    // Quick gesture to clear recents if possible
                    performGlobalAction(GLOBAL_ACTION_RECENTS);
                } catch (Exception ignored) {}
            }
        }, 150);

        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                try {
                    performGlobalAction(GLOBAL_ACTION_HOME);
                } catch (Exception ignored) {}
            }
        }, 400);

        // 4. Alert user
        if (alertMsg != null && !alertMsg.isEmpty()) {
            handler.post(new Runnable() {
                @Override
                public void run() {
                    Toast.makeText(ClaudeAccessibilityService.this, "🛡 " + alertMsg, Toast.LENGTH_LONG).show();
                }
            });
        }
    }
}
