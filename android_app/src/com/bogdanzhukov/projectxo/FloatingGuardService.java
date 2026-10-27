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
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

public class FloatingGuardService extends Service {
    private WindowManager windowManager;
    private View floatingView;
    private WindowManager.LayoutParams params;
    private Handler handler;
    private Vibrator vibrator;
    private boolean isRunning = false;
    private ImageView imgIcon;
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
                "Project XO Guard Service",
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
            .setContentTitle("Project XO // BOGDAN ZHUKOV")
            .setContentText("Умный режим защиты Claude активен поверх окон")
            .setSmallIcon(R.drawable.ic_launcher)
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

        // Create Container Layout
        LinearLayout container = new LinearLayout(this);
        container.setOrientation(LinearLayout.HORIZONTAL);
        container.setPadding(16, 12, 20, 12);
        container.setGravity(Gravity.CENTER_VERTICAL);

        bgDrawable = new GradientDrawable();
        bgDrawable.setColor(Color.argb(235, 18, 20, 26));
        bgDrawable.setCornerRadius(32f);
        bgDrawable.setStroke(3, Color.rgb(16, 185, 129)); // Initial green safe border
        container.setBackground(bgDrawable);

        imgIcon = new ImageView(this);
        imgIcon.setImageResource(R.drawable.ic_launcher);
        LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(54, 54);
        iconParams.setMargins(0, 0, 12, 0);
        imgIcon.setLayoutParams(iconParams);
        container.addView(imgIcon);

        lblStatus = new TextView(this);
        lblStatus.setText("XO: ЗАЩИЩЕНО");
        lblStatus.setTextColor(Color.rgb(16, 185, 129));
        lblStatus.setTextSize(12f);
        container.addView(lblStatus);

        floatingView = container;

        // Drag and Touch Listener
        floatingView.setOnTouchListener(new View.OnTouchListener() {
            private int initialX, initialY;
            private float initialTouchX, initialTouchY;
            private long touchStartTime;

            @Override
            public boolean onTouch(View v, MotionEvent event) {
                switch (event.getAction()) {
                    case MotionEvent.ACTION_DOWN:
                        touchStartTime = System.currentTimeMillis();
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
                        long duration = System.currentTimeMillis() - touchStartTime;
                        float diffX = Math.abs(event.getRawX() - initialTouchX);
                        float diffY = Math.abs(event.getRawY() - initialTouchY);
                        if (duration < 250 && diffX < 10 && diffY < 10) {
                            // Tap action -> Open Project XO
                            Intent openIntent = new Intent(FloatingGuardService.this, MainActivity.class);
                            openIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                            startActivity(openIntent);
                        }
                        return true;
                }
                return false;
            }
        });

        try {
            windowManager.addView(floatingView, params);
        } catch (Exception ex) {
            Toast.makeText(this, "Не удалось создать плавающий виджет: " + ex.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    private void startMonitoringLoop() {
        isRunning = true;
        new Thread(new Runnable() {
            @Override
            public void run() {
                while (isRunning) {
                    try {
                        Thread.sleep(1200);
                    } catch (InterruptedException ignored) {}

                    final boolean vpnOk = isVpnConnected();
                    handler.post(new Runnable() {
                        @Override
                        public void run() {
                            updateStatus(vpnOk);
                        }
                    });
                }
            }
        }).start();
    }

    private boolean isVpnConnected() {
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

    private void updateStatus(boolean vpnOk) {
        if (floatingView == null || bgDrawable == null) return;
        if (vpnOk) {
            bgDrawable.setStroke(3, Color.rgb(16, 185, 129));
            lblStatus.setText("XO: ЗАЩИЩЕНО");
            lblStatus.setTextColor(Color.rgb(16, 185, 129));
        } else {
            bgDrawable.setStroke(4, Color.rgb(239, 68, 68));
            lblStatus.setText("XO: ОПАСНОСТЬ!");
            lblStatus.setTextColor(Color.rgb(239, 68, 68));
            if (vibrator != null) {
                try { vibrator.vibrate(300); } catch (Exception ignored) {}
            }
        }
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
