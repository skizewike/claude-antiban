package com.claudebarrier.shield;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.Vibrator;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.LinearLayout;
import android.widget.TextView;

public class FloatingGuardService extends Service implements VpnMonitor.VpnListener {
    private WindowManager windowManager;
    private View floatingView;
    private View emergencyOverlayView;
    private WindowManager.LayoutParams floatingParams;
    private Handler handler;
    private Vibrator vibrator;
    private VpnMonitor vpnMonitor;
    private TextView lblStatus;
    private GradientDrawable bgDrawable;

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        handler = new Handler(Looper.getMainLooper());
        vibrator = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);
        windowManager = (WindowManager) getSystemService(Context.WINDOW_SERVICE);

        startForegroundNotification();
        createFloatingHead();

        vpnMonitor = new VpnMonitor(this);
        vpnMonitor.setListener(this);
        vpnMonitor.startMonitoring();

        LogManager.log("INIT", "Фоновый сторожевой сервис ClaudeBarrier запущен.", "SAFE");
    }

    private void startForegroundNotification() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel chan = new NotificationChannel(
                    AppConstants.CHANNEL_ID,
                    "ClaudeBarrier Sentinel Guard",
                    NotificationManager.IMPORTANCE_LOW
            );
            chan.setDescription("Постоянная аппаратная защита сессий Claude");
            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.createNotificationChannel(chan);
        }

        Intent notificationIntent = new Intent(this, MainActivity.class);
        PendingIntent pendingIntent = PendingIntent.getActivity(
                this, 0, notificationIntent,
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.M ? PendingIntent.FLAG_IMMUTABLE : 0
        );

        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(this, AppConstants.CHANNEL_ID);
        } else {
            builder = new Notification.Builder(this);
        }

        Notification notification = builder
                .setContentTitle("ClaudeBarrier Shield")
                .setContentText("Мониторинг VPN и превентивная защита Claude активны")
                .setSmallIcon(R.mipmap.ic_launcher)
                .setContentIntent(pendingIntent)
                .setOngoing(true)
                .build();

        startForeground(AppConstants.NOTIF_ID, notification);
    }

    private void createFloatingHead() {
        int layoutType;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            layoutType = WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY;
        } else {
            layoutType = WindowManager.LayoutParams.TYPE_PHONE;
        }

        floatingParams = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                layoutType,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
                PixelFormat.TRANSLUCENT
        );

        floatingParams.gravity = Gravity.TOP | Gravity.START;
        floatingParams.x = 40;
        floatingParams.y = 180;

        LinearLayout container = new LinearLayout(this);
        container.setOrientation(LinearLayout.HORIZONTAL);
        container.setPadding(20, 14, 20, 14);
        container.setGravity(Gravity.CENTER_VERTICAL);

        bgDrawable = new GradientDrawable();
        bgDrawable.setColor(Color.parseColor("#E612151F"));
        bgDrawable.setCornerRadius(28);
        bgDrawable.setStroke(3, Color.parseColor("#34D399"));
        container.setBackground(bgDrawable);

        lblStatus = new TextView(this);
        lblStatus.setText("CLAUDE: SAFE");
        lblStatus.setTextColor(Color.parseColor("#34D399"));
        lblStatus.setTextSize(11);
        lblStatus.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        container.addView(lblStatus);

        floatingView = container;

        floatingView.setOnTouchListener(new View.OnTouchListener() {
            private int initialX;
            private int initialY;
            private float initialTouchX;
            private float initialTouchY;

            @Override
            public boolean onTouch(View v, MotionEvent event) {
                switch (event.getAction()) {
                    case MotionEvent.ACTION_DOWN:
                        initialX = floatingParams.x;
                        initialY = floatingParams.y;
                        initialTouchX = event.getRawX();
                        initialTouchY = event.getRawY();
                        return true;
                    case MotionEvent.ACTION_MOVE:
                        floatingParams.x = initialX + (int) (event.getRawX() - initialTouchX);
                        floatingParams.y = initialY + (int) (event.getRawY() - initialTouchY);
                        try {
                            windowManager.updateViewLayout(floatingView, floatingParams);
                        } catch (Exception ignored) {}
                        return true;
                    case MotionEvent.ACTION_UP:
                        if (Math.abs(event.getRawX() - initialTouchX) < 12 && Math.abs(event.getRawY() - initialTouchY) < 12) {
                            // Tap: Return to MainActivity
                            Intent appIntent = new Intent(FloatingGuardService.this, MainActivity.class);
                            appIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                            startActivity(appIntent);
                        }
                        return true;
                }
                return false;
            }
        });

        try {
            windowManager.addView(floatingView, floatingParams);
        } catch (Exception ignored) {}
    }

    @Override
    public void onVpnStatusChanged(boolean isConnected, long pingMs, String country, String ip, boolean isDegraded) {
        if (lblStatus == null || bgDrawable == null) return;
        if (!isConnected) {
            lblStatus.setText("CLAUDE: OFF");
            lblStatus.setTextColor(Color.parseColor("#94A3B8"));
            bgDrawable.setStroke(3, Color.parseColor("#475569"));
        } else if (isDegraded) {
            lblStatus.setText("WARN: LAGGING");
            lblStatus.setTextColor(Color.parseColor("#F59E0B"));
            bgDrawable.setStroke(3, Color.parseColor("#F59E0B"));
        } else {
            lblStatus.setText(pingMs > 0 ? ("CLAUDE: " + pingMs + "ms") : "CLAUDE: SAFE");
            lblStatus.setTextColor(Color.parseColor("#34D399"));
            bgDrawable.setStroke(3, Color.parseColor("#34D399"));
        }
        try {
            floatingView.invalidate();
        } catch (Exception ignored) {}
    }

    @Override
    public void onVpnDropped() {
        // Trigger emergency overlay & killswitch immediately
        showEmergencyOverlay();

        ClaudeAccessibilityService acc = ClaudeAccessibilityService.getInstance();
        if (acc != null) {
            acc.emergencyKillAndPurgeClaude("АВАРИЙНЫЙ KILLSWITCH: Разрыв VPN! Claude уничтожен.");
        }

        if (vibrator != null) {
            try {
                // Urgent vibration alarm
                vibrator.vibrate(new long[]{0, 250, 100, 250, 100, 400}, -1);
            } catch (Exception ignored) {}
        }
    }

    private void showEmergencyOverlay() {
        if (emergencyOverlayView != null) return;

        int layoutType;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            layoutType = WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY;
        } else {
            layoutType = WindowManager.LayoutParams.TYPE_PHONE;
        }

        final WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.MATCH_PARENT,
                layoutType,
                WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT
        );

        LinearLayout curtain = new LinearLayout(this);
        curtain.setBackgroundColor(Color.parseColor("#D9EF4444"));
        curtain.setGravity(Gravity.CENTER);

        TextView tv = new TextView(this);
        tv.setText("🚨 CLAUDEBARRIER // KILLSWITCH TRIGGERED\n\nVPN ТУННЕЛЬ РАЗОРВАН\nПРОЦЕСС CLAUDE УНИЧТОЖЕН");
        tv.setTextColor(Color.WHITE);
        tv.setTextSize(16);
        tv.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        tv.setGravity(Gravity.CENTER);
        curtain.addView(tv);

        emergencyOverlayView = curtain;
        try {
            windowManager.addView(emergencyOverlayView, params);
        } catch (Exception ignored) {}

        // Remove overlay after 2 seconds
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (emergencyOverlayView != null && windowManager != null) {
                    try {
                        windowManager.removeView(emergencyOverlayView);
                    } catch (Exception ignored) {}
                    emergencyOverlayView = null;
                }
            }
        }, 2000);
    }

    @Override
    public void onDestroy() {
        if (vpnMonitor != null) vpnMonitor.stopMonitoring();
        if (floatingView != null && windowManager != null) {
            try {
                windowManager.removeView(floatingView);
            } catch (Exception ignored) {}
        }
        if (emergencyOverlayView != null && windowManager != null) {
            try {
                windowManager.removeView(emergencyOverlayView);
            } catch (Exception ignored) {}
        }
        super.onDestroy();
    }
}
