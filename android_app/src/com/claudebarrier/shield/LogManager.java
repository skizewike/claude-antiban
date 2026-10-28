package com.claudebarrier.shield;

import org.json.JSONArray;
import org.json.JSONObject;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class LogManager {
    private static final int MAX_LOGS = 200;
    private static final List<LogEntry> entries = new ArrayList<>();
    private static final SimpleDateFormat sdf = new SimpleDateFormat("HH:mm:ss.SSS", Locale.US);

    public static class LogEntry {
        public final String time;
        public final String category;
        public final String message;
        public final String type; // SAFE, WARN, ERR, NET, KILL

        public LogEntry(String category, String message, String type) {
            this.time = sdf.format(new Date());
            this.category = category;
            this.message = message;
            this.type = type;
        }

        public JSONObject toJson() {
            try {
                JSONObject o = new JSONObject();
                o.put("time", time);
                o.put("cat", category);
                o.put("msg", message);
                o.put("type", type);
                return o;
            } catch (Exception e) {
                return new JSONObject();
            }
        }
    }

    public static synchronized void log(String category, String message, String type) {
        entries.add(new LogEntry(category, message, type));
        if (entries.size() > MAX_LOGS) {
            entries.remove(0);
        }
    }

    public static synchronized String getLogsJson() {
        JSONArray arr = new JSONArray();
        for (LogEntry e : entries) {
            arr.put(e.toJson());
        }
        return arr.toString();
    }

    public static synchronized void clear() {
        entries.clear();
    }
}
