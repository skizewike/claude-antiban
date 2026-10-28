package com.claudebarrier.shield;

import android.content.Context;
import android.content.SharedPreferences;

import java.nio.charset.StandardCharsets;
import java.util.Locale;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public class LicenseManager {
    private static final String HMAC_SALT = "CLAUDE_BARRIER_SECURE_KERNEL_2026_@ClaudeBarrierBot";
    private static final long REFERENCE_EPOCH_SEC = 1767225600L; // 2026-01-01 00:00:00 UTC
    public static final int KEY_VALIDITY_HOURS = 72; // 3 days

    public static int getCurrentEpochHours() {
        long nowSec = System.currentTimeMillis() / 1000L;
        return (int) ((nowSec - REFERENCE_EPOCH_SEC) / 3600L);
    }

    public static boolean validateKey(String key) {
        return validateKey(key, null);
    }

    public static boolean validateKey(String key, StringBuilder outReason) {
        if (key == null || key.trim().isEmpty()) {
            if (outReason != null) outReason.append("Ключ не введен");
            return false;
        }
        key = key.trim().toUpperCase(Locale.US);
        String[] parts = key.split("-");
        if (parts.length != 5 || !"CB".equals(parts[0])) {
            if (outReason != null) outReason.append("Неверный формат ключа (ожидается CB-XXXX-XXXX-XXXX-XXXX)");
            return false;
        }

        for (int i = 1; i < 5; i++) {
            if (parts[i].length() != 4) {
                if (outReason != null) outReason.append("Некорректная длина блока ключа");
                return false;
            }
            for (char c : parts[i].toCharArray()) {
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) {
                    if (outReason != null) outReason.append("Недопустимые шестнадцатеричные символы в ключе");
                    return false;
                }
            }
        }

        String payload = parts[1] + parts[2];
        String expectedSig = computeSignature(payload);
        String actualSig = parts[3] + parts[4];
        if (!expectedSig.equalsIgnoreCase(actualSig)) {
            if (outReason != null) outReason.append("Неверная криптографическая подпись ключа");
            return false;
        }

        try {
            int keyHours = Integer.parseInt(parts[1], 16);
            int currentHours = getCurrentEpochHours();
            int ageHours = currentHours - keyHours;

            if (ageHours < -2) {
                if (outReason != null) outReason.append("Системное время опережает дату выпуска ключа");
                return false;
            }

            if (ageHours > KEY_VALIDITY_HOURS) {
                int expiredAgo = ageHours - KEY_VALIDITY_HOURS;
                int expDays = expiredAgo / 24;
                if (outReason != null) {
                    outReason.append(String.format(Locale.US, "Срок действия ключа (3 дня) истек %s назад. Обновите ключ в @ClaudeBarrierBot",
                            expDays > 0 ? (expDays + " дн.") : (expiredAgo + " ч.")));
                }
                return false;
            }
        } catch (Exception e) {
            if (outReason != null) outReason.append("Ошибка декодирования метки времени ключа");
            return false;
        }

        return true;
    }

    public static String computeSignature(String payload) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            SecretKeySpec spec = new SecretKeySpec(HMAC_SALT.getBytes(StandardCharsets.UTF_8), "HmacSHA256");
            mac.init(spec);
            byte[] hash = mac.doFinal(payload.getBytes(StandardCharsets.UTF_8));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 4; i++) {
                sb.append(String.format("%02X", hash[i]));
            }
            return sb.toString().toUpperCase(Locale.US);
        } catch (Exception e) {
            return "";
        }
    }

    public static String getRemainingTime(String key) {
        if (key == null || key.trim().isEmpty()) return "Нет лицензии";
        key = key.trim().toUpperCase(Locale.US);
        String[] p = key.split("-");
        if (p.length != 5 || !"CB".equals(p[0])) return "Недействителен";
        try {
            int keyHours = Integer.parseInt(p[1], 16);
            int currentHours = getCurrentEpochHours();
            int ageHours = currentHours - keyHours;
            int remaining = KEY_VALIDITY_HOURS - ageHours;
            if (remaining <= 0) return "Истек";
            int days = remaining / 24;
            int hours = remaining % 24;
            if (days > 0) return String.format(Locale.US, "%d дн. %d ч.", days, hours);
            return String.format(Locale.US, "%d ч.", hours);
        } catch (Exception e) {
            return "Недействителен";
        }
    }

    public static boolean saveKey(Context context, String key) {
        if (!validateKey(key)) return false;
        SharedPreferences sp = context.getSharedPreferences(AppConstants.PREFS_NAME, Context.MODE_PRIVATE);
        return sp.edit().putString(AppConstants.KEY_LICENSE, key.trim().toUpperCase(Locale.US)).commit();
    }

    public static String getSavedKey(Context context) {
        SharedPreferences sp = context.getSharedPreferences(AppConstants.PREFS_NAME, Context.MODE_PRIVATE);
        return sp.getString(AppConstants.KEY_LICENSE, null);
    }

    public static boolean isActivated(Context context) {
        String k = getSavedKey(context);
        return validateKey(k);
    }

    public static String maskKey(String key) {
        if (key == null || key.trim().isEmpty()) return "Не активировано";
        key = key.trim().toUpperCase(Locale.US);
        String[] p = key.split("-");
        if (p.length == 5) {
            return String.format("CB-••••-••••-••••-%s", p[4]);
        }
        return "CB-••••-••••-••••";
    }
}
