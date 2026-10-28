package com.claudebarrier.shield;

public final class AppConstants {
    private AppConstants() {}

    public static final String TARGET_CLAUDE_PKG = "com.anthropic.claude";
    public static final String PREFS_NAME = "claude_barrier_prefs";
    public static final String KEY_LICENSE = "license_key";
    public static final String KEY_FLOATING_HEAD = "pref_floating_head";
    public static final String KEY_PREEMPTIVE_GUARD = "pref_preemptive_guard";
    public static final String KEY_SENTINEL_MONITOR = "pref_sentinel_monitor";
    public static final String KEY_PING_THRESHOLD = "pref_ping_threshold"; // ms (default 1200)

    public static final String CHANNEL_ID = "claude_barrier_sentinel";
    public static final int NOTIF_ID = 101;
}
