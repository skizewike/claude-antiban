package com.bogdanzhukov.projectxo;

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
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
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

public class FloatingGuardService extends Service {
    private WindowManager windowManager;
    private View floatingView;
    private WindowManager.LayoutParams params;
    private Handler handler;
    private Vibrator vibrator;
    private boolean isRunning = false;
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
        startMonitoringLoop();
    }

    private void startForegroundNotification() {
        String channelId = "xo_guard_channel";
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel chan = new NotificationChannel(
                channelId,
                "Claude Anti-Ban Guard",
                NotificationManager.IMPORTANCE_LOW
            );
            chan.setDescription("Фоновый мониторинг утечек Claude");
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
            builder = new Notification.Builder(this, channelId);
        } else {
            builder = new Notification.Builder(this);
        }

        Notification notification = builder
            .setContentTitle("Claude Anti-Ban Barrier")
            .setContentText("Защита Claude активна поверх окон")
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .build();

        startForeground(101, notification);
    }

    private void createFloatingHead() {
        int layoutType;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            layoutType = WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY;
        } else {
            layoutType = WindowManager.LayoutParams.TYPE_PHONE;
        }

        params = new WindowManager.LayoutParams(
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.WRAP_CONTENT,
            layoutType,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT
        );

        params.gravity = Gravity.TOP | Gravity.START;
        params.x = 40;
        params.y = 200;

        LinearLayout container = new LinearLayout(this);
        container.setOrientation(LinearLayout.HORIZONTAL);
        container.setPadding(20, 14, 20, 14);
        container.setGravity(Gravity.CENTER_VERTICAL);

        bgDrawable = new GradientDrawable();
        bgDrawable.setColor(Color.parseColor("#E61A1B22"));
        bgDrawable.setCornerRadius(28);
        bgDrawable.setStroke(3, Color.parseColor("#CC785C"));
        container.setBackground(bgDrawable);

        lblStatus = new TextView(this);
        lblStatus.setText("CLAUDE SHIELD: OK");
        lblStatus.setTextColor(Color.parseColor("#CC785C"));
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
                        initialX = params.x;
                        initialY = params.y;
                        initialTouchX = event.getRawX();
                        initialTouchY = event.getRawY();
                        return true;
                    case MotionEvent.ACTION_MOVE:
                        params.x = initialX + (int) (event.getRawX() - initialTouchX);
                        params.y = initialY + (int) (event.getRawY() - initialTouchY);
                        try {
                            windowManager.updateViewLayout(floatingView, params);
                        } catch (Exception ignored) {}
                        return true;
                    case MotionEvent.ACTION_UP:
                        if (Math.abs(event.getRawX() - initialTouchX) < 10 && Math.abs(event.getRawY() - initialTouchY) < 10) {
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
            windowManager.addView(floatingView, params);
        } catch (Exception ignored) {}
    }

    private void startMonitoringLoop() {
        isRunning = true;
        new Thread(new Runnable() {
            @Override
            public void run() {
                while (isRunning) {
                    try {
                        Thread.sleep(1500);
                    } catch (InterruptedException ignored) {}

                    final boolean vpn = isVpnActive();
                    handler.post(new Runnable() {
                        @Override
                        public void run() {
                            updateStatus(vpn);
                        }
                    });
                }
            }
        }).start();
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

    private void updateStatus(boolean isSafe) {
        if (lblStatus == null || bgDrawable == null) return;
        if (isSafe) {
            lblStatus.setText("CLAUDE: ЗАЩИЩЕНО");
            lblStatus.setTextColor(Color.parseColor("#CC785C"));
            bgDrawable.setStroke(3, Color.parseColor("#CC785C"));
        } else {
            lblStatus.setText("ВНИМАНИЕ: НЕТ VPN!");
            lblStatus.setTextColor(Color.parseColor("#EF4444"));
            bgDrawable.setStroke(3, Color.parseColor("#EF4444"));
            if (vibrator != null) {
                try {
                    vibrator.vibrate(300);
                } catch (Exception ignored) {}
            }
        }
        try {
            floatingView.invalidate();
        } catch (Exception ignored) {}
    }

    @Override
    public void onDestroy() {
        isRunning = false;
        if (floatingView != null && windowManager != null) {
            try {
                windowManager.removeView(floatingView);
            } catch (Exception ignored) {}
        }
        super.onDestroy();
    }
}
