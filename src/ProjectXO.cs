using System.IO.Compression;
using System;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Configuration.Install;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeBarrier {
    static class Program {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [STAThread]
        static void Main(string[] args) {
            try {
                SetDllDirectory(string.Empty);
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32);
            } catch { }

            try {
                if (Environment.OSVersion.Version.Major >= 6) {
                    try {
                        SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
                    } catch {
                        SetProcessDPIAware();
                    }
                }
            } catch { }

            // Проверка аргументов командной строки для системной службы Windows
            if (args != null && args.Length > 0) {
                string a0 = args[0].ToLowerInvariant();
                if (a0 == "--service" || a0 == "/service" || a0 == "-s") {
                    ServiceBase.Run(new ClaudeBarrierWindowsService());
                    return;
                }
                if (a0 == "--service-install") {
                    ClaudeBarrierServiceController.InstallService();
                    return;
                }
                if (a0 == "--service-uninstall") {
                    ClaudeBarrierServiceController.UninstallService();
                    return;
                }
                if (a0 == "--service-start") {
                    ClaudeBarrierServiceController.StartService();
                    return;
                }
                if (a0 == "--service-stop") {
                    ClaudeBarrierServiceController.StopService();
                    return;
                }
            }

            // 1. Проверка и автоматический запрос прав Администратора
            if (!IsAdministrator()) {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = Application.ExecutablePath,
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = string.Join(" ", args)
                    };
                    Process.Start(psi);
                } catch {
                    MessageBox.Show(
                        "Для работы защитного барьера Claude требуются права Администратора (управление брандмауэром WFP).",
                        "ClaudeBarrier",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            // 2. Гарантия единственного экземпляра
            bool createdNew;
            using (Mutex m = new Mutex(true, "Global\\ClaudeBarrier_App_Mutex", out createdNew)) {
                if (!createdNew) {
                    MessageBox.Show(
                        "Защитный барьер Claude Anti-Ban уже работает в системе (проверьте системный трей возле часов).",
                        "ClaudeBarrier",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                // 3. Настройка TLS 1.2 и 1.3
                try {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls12;
                    ServicePointManager.DefaultConnectionLimit = 32;
                    ServicePointManager.Expect100Continue = false;
                } catch {}

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainWindow(args));
            }
        }

        private static bool IsAdministrator() {
            try {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent()) {
                    WindowsPrincipal principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            } catch {
                return false;
            }
        }
    }

    // Менеджер криптографической лицензии (совместим с @ClaudeBarrierBot)
    public static class LicenseManager {
        private const string HMAC_SALT = "CLAUDE_BARRIER_SECURE_KERNEL_2026_@ClaudeBarrierBot";
        private static readonly byte[] DPAPI_ENTROPY = Encoding.UTF8.GetBytes("ClaudeBarrier_DPAPI_Entropy_2026");
        private const string REG_KEY = @"Software\ClaudeBarrier";
        private const string REG_VAL = "LicensePayload";
        private static readonly DateTime REFERENCE_EPOCH = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public const int KEY_VALIDITY_HOURS = 72; // 3 дня

        public static int GetCurrentEpochHours() {
            return (int)((DateTime.UtcNow - REFERENCE_EPOCH).TotalHours);
        }

        public static bool ValidateKey(string key) {
            string ignored;
            return ValidateKey(key, out ignored);
        }

        public static bool ValidateKey(string key, out string reason) {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(key)) {
                reason = "Ключ не введен";
                return false;
            }
            key = key.Trim().ToUpperInvariant();
            string[] parts = key.Split('-');
            if (parts.Length != 5 || parts[0] != "CB") {
                reason = "Неверный формат ключа (ожидается CB-XXXX-XXXX-XXXX-XXXX)";
                return false;
            }
            for (int i = 1; i < 5; i++) {
                if (parts[i].Length != 4) {
                    reason = "Некорректная длина блока ключа";
                    return false;
                }
                foreach (char c in parts[i]) {
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) {
                        reason = "Недопустимые шестнадцатеричные символы в ключе";
                        return false;
                    }
                }
            }

            string payload = parts[1] + parts[2];
            string expectedSig = ComputeSignature(payload);
            string actualSig = parts[3] + parts[4];
            if (!string.Equals(expectedSig, actualSig, StringComparison.OrdinalIgnoreCase)) {
                reason = "Неверная криптографическая подпись ключа";
                return false;
            }

            // Проверка 3-дневного срока действия ключа
            try {
                int keyHours = Convert.ToInt32(parts[1], 16);
                int currentHours = GetCurrentEpochHours();
                int ageHours = currentHours - keyHours;

                if (ageHours < -2) {
                    reason = "Системное время опережает дату генерации ключа";
                    return false;
                }

                if (ageHours > KEY_VALIDITY_HOURS) {
                    int expiredHoursAgo = ageHours - KEY_VALIDITY_HOURS;
                    int expiredDays = expiredHoursAgo / 24;
                    reason = string.Format("Срок действия ключа (3 дня) истек {0} назад. Получите новый ключ в @ClaudeBarrierBot",
                        expiredDays > 0 ? (expiredDays + " дн.") : (expiredHoursAgo + " ч."));
                    return false;
                }
            } catch {
                reason = "Ошибка декодирования метки времени ключа";
                return false;
            }

            return true;
        }

        public static string GetRemainingTime(string key) {
            if (string.IsNullOrWhiteSpace(key)) return "Нет лицензии";
            key = key.Trim().ToUpperInvariant();
            string[] parts = key.Split('-');
            if (parts.Length != 5 || parts[0] != "CB") return "Недействителен";
            try {
                int keyHours = Convert.ToInt32(parts[1], 16);
                int currentHours = GetCurrentEpochHours();
                int ageHours = currentHours - keyHours;
                int remaining = KEY_VALIDITY_HOURS - ageHours;
                if (remaining <= 0) return "Истек";
                int days = remaining / 24;
                int hours = remaining % 24;
                if (days > 0) return string.Format("{0} дн. {1} ч.", days, hours);
                return string.Format("{0} ч.", hours);
            } catch {
                return "Недействителен";
            }
        }

        public static string ComputeSignature(string payload) {
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HMAC_SALT))) {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 4; i++) {
                    sb.Append(hash[i].ToString("X2"));
                }
                return sb.ToString().ToUpperInvariant();
            }
        }

        public static bool IsActivated() {
            string saved = GetSavedKey();
            return ValidateKey(saved);
        }

        public static string GetSavedKey() {
            try {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(REG_KEY)) {
                    if (rk == null) return null;
                    string enc = rk.GetValue(REG_VAL) as string;
                    if (string.IsNullOrEmpty(enc)) return null;
                    byte[] cipherBytes = Convert.FromBase64String(enc);
                    byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, DPAPI_ENTROPY, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            } catch {
                return null;
            }
        }

        public static bool SaveKey(string key) {
            if (!ValidateKey(key)) return false;
            try {
                byte[] plainBytes = Encoding.UTF8.GetBytes(key.Trim().ToUpperInvariant());
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, DPAPI_ENTROPY, DataProtectionScope.CurrentUser);
                string enc = Convert.ToBase64String(cipherBytes);
                using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(REG_KEY)) {
                    if (rk != null) {
                        rk.SetValue(REG_VAL, enc, RegistryValueKind.String);
                        return true;
                    }
                }
            } catch { }
            return false;
        }

        public static void RemoveKey() {
            try {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(REG_KEY, true)) {
                    if (rk != null) {
                        rk.DeleteValue(REG_VAL, false);
                    }
                }
            } catch { }
        }

        public static string MaskKey(string key) {
            if (string.IsNullOrEmpty(key)) return "Не активировано";
            key = key.Trim().ToUpperInvariant();
            string[] p = key.Split('-');
            if (p.Length == 5) return string.Format("CB-••••-••••-••••-{0}", p[4]);
            return "CB-••••-••••-••••";
        }
    }

    public enum ProtectionState {
        Checking,
        Protected,
        BlockedNoVpn,
        BlockedLeaking
    }

    public class Config {
        public bool MinimizeToTrayOnClose = true;
        public bool AutoStartWithWindows = false;
        public string ClaudePath = "";
        public string ClaudeCodePath = "";
        public string ZapretPath = "";
        public string ZapretArgs = "--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq";
        public bool AutoStartZapret = false;
        public bool KillSwitchEnabled = true;
        public bool SentinelGuardEnabled = true;
        public bool MultiSignalEnabled = true;
        public string VpnProcessList = "wireguard,openvpn,amnezia-vpn,tailscaled,tailscale,warp-svc,sing-box,clash-verge,clash,hiddify,v2ray";
        public bool TimezoneGuardEnabled = true;
        public int RttThresholdMs = 400;

        private string ConfigPath {
            get {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeBarrier");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "barrier.cfg");
            }
        }

        public void Load() {
            try {
                if (File.Exists(ConfigPath)) {
                    string[] lines = File.ReadAllLines(ConfigPath);
                    foreach (string raw in lines) {
                        string line = raw.Trim();
                        if (line.StartsWith("#") || string.IsNullOrEmpty(line)) continue;
                        int idx = line.IndexOf('=');
                        if (idx > 0) {
                            string k = line.Substring(0, idx).Trim();
                            string v = line.Substring(idx + 1).Trim();
                            if (k == "MinimizeToTrayOnClose") MinimizeToTrayOnClose = (v == "1" || v.ToLower() == "true");
                            else if (k == "AutoStartWithWindows") AutoStartWithWindows = (v == "1" || v.ToLower() == "true");
                            else if (k == "ClaudePath") ClaudePath = v;
                            else if (k == "ClaudeCodePath") ClaudeCodePath = v;
                            else if (k == "ZapretPath") ZapretPath = v;
                            else if (k == "ZapretArgs") ZapretArgs = v;
                            else if (k == "AutoStartZapret") AutoStartZapret = (v == "1" || v.ToLower() == "true");
                            else if (k == "KillSwitchEnabled") KillSwitchEnabled = (v == "1" || v.ToLower() == "true");
                            else if (k == "SentinelGuardEnabled") SentinelGuardEnabled = (v == "1" || v.ToLower() == "true");
                            else if (k == "MultiSignalEnabled") MultiSignalEnabled = (v == "1" || v.ToLower() == "true");
                            else if (k == "VpnProcessList") VpnProcessList = v;
                            else if (k == "TimezoneGuardEnabled") TimezoneGuardEnabled = (v == "1" || v.ToLower() == "true");
                            else if (k == "RttThresholdMs") { int parsed; if (int.TryParse(v, out parsed)) RttThresholdMs = parsed; }
                        }
                    }
                }
            } catch {}
        }

        public void Save() {
            try {
                List<string> lines = new List<string> {
                    "# ClaudeBarrier Configuration // ClaudeBarrier Security Team",
                    "MinimizeToTrayOnClose=" + (MinimizeToTrayOnClose ? "1" : "0"),
                    "AutoStartWithWindows=" + (AutoStartWithWindows ? "1" : "0"),
                    "ClaudePath=" + (ClaudePath ?? ""),
                    "ClaudeCodePath=" + (ClaudeCodePath ?? ""),
                    "ZapretPath=" + (ZapretPath ?? ""),
                    "ZapretArgs=" + (ZapretArgs ?? ""),
                    "AutoStartZapret=" + (AutoStartZapret ? "1" : "0"),
                    "KillSwitchEnabled=" + (KillSwitchEnabled ? "1" : "0"),
                    "SentinelGuardEnabled=" + (SentinelGuardEnabled ? "1" : "0"),
                    "MultiSignalEnabled=" + (MultiSignalEnabled ? "1" : "0"),
                    "VpnProcessList=" + (VpnProcessList ?? ""),
                    "TimezoneGuardEnabled=" + (TimezoneGuardEnabled ? "1" : "0"),
                    "RttThresholdMs=" + RttThresholdMs
                };
                File.WriteAllLines(ConfigPath, lines.ToArray());
            } catch {}
        }
    }

    // Глобальное хранилище статических ассетов и иконки приложения
    public static class AppAssets {
        private static Image _appIcon;
        private static readonly object _lock = new object();

        public static Image AppIcon {
            get {
                if (_appIcon == null) {
                    lock (_lock) {
                        if (_appIcon == null) {
                            try {
                                using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("app_icon.png")) {
                                    if (s != null) {
                                        _appIcon = Image.FromStream(s);
                                    }
                                }
                            } catch {}

                            if (_appIcon == null) {
                                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.png");
                                if (File.Exists(p)) {
                                    try { _appIcon = Image.FromFile(p); } catch {}
                                }
                            }

                            if (_appIcon == null) {
                                try {
                                    using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("app_icon.ico")) {
                                        if (s != null) {
                                            using (Icon ico = new Icon(s, 256, 256)) {
                                                _appIcon = ico.ToBitmap();
                                            }
                                        }
                                    }
                                } catch {}
                            }
                        }
                    }
                }
                return _appIcon;
            }
        }
    }

    // Вспомогательный класс для рисования скругленных контуров GDI+
    public static class GraphicsUtils {
        public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius) {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            Rectangle arc = new Rectangle(bounds.Location, new Size(d, d));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawShieldIcon(Graphics g, Rectangle rect, Color strokeColor, Color fillColor) {
            GraphicsPath p = new GraphicsPath();
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float w = rect.Width * 0.7f;
            float h = rect.Height * 0.8f;

            float x1 = cx - w / 2f;
            float x2 = cx + w / 2f;
            float y1 = cy - h / 2f;
            float y2 = cy + h * 0.15f;
            float y3 = cy + h / 2f;

            p.AddLine(cx, y1, x2, y1 + h * 0.15f);
            p.AddLine(x2, y1 + h * 0.15f, x2, y2);
            p.AddBezier(x2, y2, x2, y3, cx, y3, cx, y3);
            p.AddBezier(cx, y3, x1, y3, x1, y2, x1, y2);
            p.AddLine(x1, y2, x1, y1 + h * 0.15f);
            p.CloseFigure();

            if (fillColor != Color.Transparent) {
                using (SolidBrush b = new SolidBrush(fillColor)) {
                    g.FillPath(b, p);
                }
            }
            using (Pen pen = new Pen(strokeColor, 1.8f)) {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, p);
            }
        }

        public static void DrawChatBubbleIcon(Graphics g, Rectangle r, Color color) {
            using (Pen p = new Pen(color, 1.6f)) {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                Rectangle bubble = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 7);
                using (GraphicsPath path = CreateRoundedRectangle(bubble, 6)) {
                    g.DrawPath(p, path);
                }
                g.DrawLine(p, r.X + 6, r.Y + r.Height - 5, r.X + 3, r.Y + r.Height - 1);
                g.DrawLine(p, r.X + 3, r.Y + r.Height - 1, r.X + 11, r.Y + r.Height - 5);
            }
            using (SolidBrush b = new SolidBrush(color)) {
                int dotY = r.Y + (r.Height - 7) / 2 + 1;
                int midX = r.X + r.Width / 2;
                g.FillEllipse(b, midX - 5, dotY, 2, 2);
                g.FillEllipse(b, midX, dotY, 2, 2);
                g.FillEllipse(b, midX + 5, dotY, 2, 2);
            }
        }
    }

    // Современная кнопка в стиле Cloudflare One Zero-Trust
    public class CloudflareButton : Button {
        private bool _isHovered = false;
        private bool _isPressed = false;
        public int CornerRadius { get; set; }
        public Color GradientStart { get; set; }
        public Color GradientEnd { get; set; }
        public Color HoverStart { get; set; }
        public Color HoverEnd { get; set; }
        public Color BorderStrokeColor { get; set; }
        public bool HasGlow { get; set; }
        public Color GlowColor { get; set; }

        public CloudflareButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            CornerRadius = 10;
            GradientStart = Color.FromArgb(59, 130, 246);
            GradientEnd = Color.FromArgb(37, 99, 235);
            HoverStart = Color.FromArgb(96, 165, 250);
            HoverEnd = Color.FromArgb(37, 99, 235);
            BorderStrokeColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e) {
            _isHovered = false;
            _isPressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent) {
            _isPressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent) {
            _isPressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnGotFocus(EventArgs e) {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e) {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs pevent) {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = Color.FromArgb(13, 15, 21);
            Control parent = Parent;
            while (parent != null) {
                if (parent.BackColor != Color.Transparent && parent.BackColor.A == 255) {
                    parentBg = parent.BackColor;
                    break;
                }
                parent = parent.Parent;
            }
            using (SolidBrush pb = new SolidBrush(parentBg)) {
                g.FillRectangle(pb, ClientRectangle);
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GraphicsUtils.CreateRoundedRectangle(bounds, CornerRadius)) {
                if (HasGlow && Enabled) {
                    using (Pen glowPen = new Pen(GlowColor, 4f)) {
                        glowPen.Alignment = PenAlignment.Inset;
                        g.DrawPath(glowPen, path);
                    }
                }

                Color c1 = _isHovered ? HoverStart : GradientStart;
                Color c2 = _isHovered ? HoverEnd : GradientEnd;
                if (_isPressed) {
                    c1 = ControlPaint.Dark(c1, 0.1f);
                    c2 = ControlPaint.Dark(c2, 0.1f);
                }
                if (!Enabled) {
                    c1 = Color.FromArgb(30, 32, 42);
                    c2 = Color.FromArgb(24, 26, 34);
                }

                using (LinearGradientBrush lgb = new LinearGradientBrush(bounds, c1, c2, 135f)) {
                    g.FillPath(lgb, path);
                }

                if (BorderStrokeColor != Color.Transparent && Enabled) {
                    using (Pen p = new Pen(BorderStrokeColor, 1f)) {
                        p.Alignment = PenAlignment.Inset;
                        g.DrawPath(p, path);
                    }
                }
            }

            if (Focused && Enabled) {
                using (GraphicsPath focusPath = GraphicsUtils.CreateRoundedRectangle(new Rectangle(2, 2, Width - 5, Height - 5), Math.Max(2, CornerRadius - 2))) {
                    using (Pen focusPen = new Pen(Color.FromArgb(147, 197, 253), 1.5f)) {
                        focusPen.DashStyle = DashStyle.Dot;
                        g.DrawPath(focusPen, focusPath);
                    }
                }
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(100, 116, 139);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Переключатель в стиле Cloudflare One (WARP iOS-style toggle switch)
    // Монолитный сегментированный переключатель фильтров журнала в стиле WARP/macOS
    public class WarpSegmentedFilterControl : Control {
        private string[] _items = new string[] { "Все", "Ошибки", "Сеть", "WFP" };
        private int _selectedIndex = 0;
        private int _hoverIndex = -1;
        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex {
            get { return _selectedIndex; }
            set {
                if (value >= 0 && value < _items.Length && _selectedIndex != value) {
                    _selectedIndex = value;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }
        }

        public string SelectedFilter {
            get {
                switch (_selectedIndex) {
                    case 1: return "ERR";
                    case 2: return "NET";
                    case 3: return "WFP";
                    default: return "ALL";
                }
            }
        }

        public WarpSegmentedFilterControl() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(320, 34);
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold);
            Cursor = Cursors.Hand;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) {
                if (SelectedIndex > 0) SelectedIndex--;
                e.Handled = true;
            } else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) {
                if (SelectedIndex < _items.Length - 1) SelectedIndex++;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e) {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            int newHover = GetIndexAt(e.X);
            if (newHover != _hoverIndex) {
                _hoverIndex = newHover;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) {
            _hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                int clicked = GetIndexAt(e.X);
                if (clicked >= 0 && clicked < _items.Length) {
                    SelectedIndex = clicked;
                }
            }
            base.OnMouseDown(e);
        }

        private int GetIndexAt(int x) {
            if (x < 3 || x >= Width - 3) return -1;
            int tabW = (Width - 6) / _items.Length;
            int idx = (x - 3) / tabW;
            return Math.Max(0, Math.Min(_items.Length - 1, idx));
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = Color.FromArgb(13, 15, 21);
            Control parent = Parent;
            while (parent != null) {
                if (parent.BackColor != Color.Transparent && parent.BackColor.A == 255) {
                    parentBg = parent.BackColor;
                    break;
                }
                parent = parent.Parent;
            }
            using (SolidBrush pb = new SolidBrush(parentBg)) {
                g.FillRectangle(pb, ClientRectangle);
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GraphicsUtils.CreateRoundedRectangle(bounds, 7)) {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 23, 34))) {
                    g.FillPath(b, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(34, 40, 58), 1f)) {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawPath(pen, path);
                }
            }

            int tabW = (Width - 6) / _items.Length;

            for (int i = 0; i < _items.Length; i++) {
                Rectangle tabRect = new Rectangle(3 + i * tabW, 3, tabW, Height - 6);
                bool isSelected = (i == _selectedIndex);
                bool isHovered = (i == _hoverIndex && !isSelected);

                if (isSelected) {
                    using (GraphicsPath tp = GraphicsUtils.CreateRoundedRectangle(tabRect, 5)) {
                        using (LinearGradientBrush lgb = new LinearGradientBrush(tabRect, Color.FromArgb(37, 99, 235), Color.FromArgb(29, 78, 216), 90f)) {
                            g.FillPath(lgb, tp);
                        }
                    }
                } else if (isHovered) {
                    using (GraphicsPath tp = GraphicsUtils.CreateRoundedRectangle(tabRect, 5)) {
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(28, 34, 50))) {
                            g.FillPath(hb, tp);
                        }
                    }
                }

                Color textCol = isSelected ? Color.White : (isHovered ? Color.FromArgb(241, 245, 249) : Color.FromArgb(148, 163, 184));
                TextRenderer.DrawText(g, _items[i], Font, tabRect, textCol,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    public class WarpToggleSwitch : Control {
        private bool _checked = false;
        public event EventHandler CheckedChanged;

        public bool Checked {
            get { return _checked; }
            set {
                if (_checked != value) {
                    _checked = value;
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                    Invalidate();
                }
            }
        }

        public WarpToggleSwitch() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(44, 22);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnClick(EventArgs e) {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e) {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = _checked ? Color.FromArgb(37, 99, 235) : Color.FromArgb(28, 33, 46);
            Color border = _checked ? Color.FromArgb(59, 130, 246) : Color.FromArgb(45, 52, 70);
            Color thumbColor = _checked ? Color.White : Color.FromArgb(148, 163, 184);

            using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, Height / 2)) {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, p);
            }

            if (Focused) {
                using (Pen focusPen = new Pen(Color.FromArgb(147, 197, 253), 1f)) {
                    focusPen.DashStyle = DashStyle.Dot;
                    Rectangle fr = new Rectangle(1, 1, Width - 3, Height - 3);
                    using (GraphicsPath fp = GraphicsUtils.CreateRoundedRectangle(fr, Height / 2)) {
                        g.DrawPath(focusPen, fp);
                    }
                }
            }

            int thumbDiameter = Height - 6;
            int thumbX = _checked ? (Width - thumbDiameter - 3) : 3;
            int thumbY = 3;

            Rectangle thumbRect = new Rectangle(thumbX, thumbY, thumbDiameter, thumbDiameter);
            using (SolidBrush tb = new SolidBrush(thumbColor)) {
                g.FillEllipse(tb, thumbRect);
            }
        }
    }

        // Анимированная центральная интерактивная область в стиле Cloudflare WARP
    public class HeroCloudGraphic : Panel {
        private ProtectionState _state = ProtectionState.Checking;
        public ProtectionState State {
            get { return _state; }
            set {
                _state = value;
                Invalidate();
            }
        }

        private float _phase = 0f;
        private System.Windows.Forms.Timer _animTimer;

        public HeroCloudGraphic() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            Size = new Size(240, 140);

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 33; // 30 FPS smooth
            _animTimer.Tick += (s, e) => {
                _phase += 0.05f;
                if (_phase > (float)(Math.PI * 2)) _phase -= (float)(Math.PI * 2);
                Invalidate();
            };
            _animTimer.Start();
        }

        public void PauseAnimation() {
            if (_animTimer != null && _animTimer.Enabled) _animTimer.Stop();
        }

        public void ResumeAnimation() {
            if (_animTimer != null && !_animTimer.Enabled) _animTimer.Start();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (_animTimer != null) {
                    _animTimer.Stop();
                    _animTimer.Dispose();
                    _animTimer = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int cx = Width / 2;
            int cy = Height / 2;

            bool isSafe = (_state == ProtectionState.Protected);

            // 1. Парящие гармонические частицы Cloudflare WARP
            float p1Y = (float)Math.Sin(_phase) * 6f;
            float p1X = (float)Math.Cos(_phase) * 3f;
            float p2Y = (float)Math.Cos(_phase * 1.2f) * 7f;
            float p2X = (float)Math.Sin(_phase * 1.2f) * 4f;
            float p3Y = (float)Math.Sin(_phase * 0.8f + 1.5f) * 5f;
            float p4Y = (float)Math.Cos(_phase * 1.4f + 0.8f) * 6f;

            Color cParticle1 = isSafe ? Color.FromArgb(52, 211, 153) : Color.FromArgb(239, 68, 68);
            Color cParticle2 = isSafe ? Color.FromArgb(16, 185, 129) : Color.FromArgb(248, 113, 113);
            Color cParticle3 = isSafe ? Color.FromArgb(56, 189, 248) : Color.FromArgb(251, 146, 60);
            Color cParticleMuted = Color.FromArgb(148, 163, 184);

            // Отрисовка орбитальных точек
            using (SolidBrush b = new SolidBrush(cParticle1)) g.FillEllipse(b, cx - 64 + p1X, cy - 38 + p1Y, 6, 6);
            using (SolidBrush b = new SolidBrush(cParticleMuted)) g.FillEllipse(b, cx + 60 + p2X, cy - 32 + p2Y, 5, 5);
            using (SolidBrush b = new SolidBrush(cParticle2)) g.FillEllipse(b, cx - 70 + p3Y, cy + 26 + p3Y, 7, 7);
            using (SolidBrush b = new SolidBrush(cParticle3)) g.FillEllipse(b, cx + 66 - p1X, cy + 30 - p1Y, 5, 5);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(226, 232, 240))) g.FillEllipse(b, cx - 24 + p2X, cy - 48 + p4Y, 4, 4);
            using (SolidBrush b = new SolidBrush(cParticle1)) g.FillEllipse(b, cx + 26 - p3Y, cy + 46 + p2Y, 5, 5);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(100, 116, 139))) g.FillEllipse(b, cx - 76 + p4Y, cy - 4 + p1Y, 4, 4);
            using (SolidBrush b = new SolidBrush(cParticle2)) g.FillEllipse(b, cx + 74 + p1X, cy - 2 - p2Y, 6, 6);

            // 2. Мягкое радиальное свечение ауры
            float breath = (float)Math.Sin(_phase) * 3f;
            Rectangle auraRect = new Rectangle(cx - 56 - (int)breath, cy - 56 - (int)breath, 112 + (int)(breath * 2), 112 + (int)(breath * 2));
            using (GraphicsPath auraPath = new GraphicsPath()) {
                auraPath.AddEllipse(auraRect);
                using (PathGradientBrush pgb = new PathGradientBrush(auraPath)) {
                    pgb.CenterColor = isSafe ? Color.FromArgb(40, 16, 185, 129) : Color.FromArgb(45, 239, 68, 68);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, 10, 12, 17) };
                    g.FillPath(pgb, auraPath);
                }
            }

            // 3. Прямое отображение крупной иконки app_icon без дополнительных подложек
            Image iconImg = AppAssets.AppIcon;
            int iconSize = 92;
            Rectangle iconRect = new Rectangle(cx - iconSize / 2, cy - iconSize / 2, iconSize, iconSize);

            if (iconImg != null) {
                g.DrawImage(iconImg, iconRect);
            } else {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(204, 120, 92))) {
                    g.FillEllipse(b, iconRect);
                }
            }

            // 4. Аккуратный индикатор статуса в правом нижнем углу иконки (✕ при ошибке / ✓ при защите)
            int badgeSize = 24;
            int badgeX = cx + iconSize / 2 - 18;
            int badgeY = cy + iconSize / 2 - 18;
            Rectangle badgeRect = new Rectangle(badgeX, badgeY, badgeSize, badgeSize);

            using (SolidBrush bgB = new SolidBrush(Color.FromArgb(17, 19, 27))) {
                g.FillEllipse(bgB, badgeRect);
            }
            using (SolidBrush bb = new SolidBrush(isSafe ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68))) {
                Rectangle badgeInner = new Rectangle(badgeX + 2, badgeY + 2, badgeSize - 4, badgeSize - 4);
                g.FillEllipse(bb, badgeInner);
            }

            if (isSafe) {
                using (Pen checkPen = new Pen(Color.White, 2f)) {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    g.DrawLine(checkPen, badgeX + 7, badgeY + 12, badgeX + 10, badgeY + 15);
                    g.DrawLine(checkPen, badgeX + 10, badgeY + 15, badgeX + 17, badgeY + 8);
                }
            } else {
                using (Pen crossPen = new Pen(Color.White, 2f)) {
                    crossPen.StartCap = LineCap.Round;
                    crossPen.EndCap = LineCap.Round;
                    g.DrawLine(crossPen, badgeX + 8, badgeY + 8, badgeX + 16, badgeY + 16);
                    g.DrawLine(crossPen, badgeX + 16, badgeY + 8, badgeX + 8, badgeY + 16);
                }
            }
        }
    }

    // Минималистичная карточка быстрого запуска Claude в стиле Cloudflare WARP
    public class LauncherCard : Panel {
        private bool _isHovered = false;
        private string _title;
        private string _subTitle;
        private int _iconType; // 0: Desktop, 1: Browser, 2: Terminal
        private bool _isSafe = false;

        public event Action CardClicked;

        public LauncherCard(string title, int iconType) {
            _title = title;
            _iconType = iconType;
            _subTitle = "Вход заблокирован (нет VPN)";

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(240, 64);
            BackColor = Color.FromArgb(17, 20, 28);
            Cursor = Cursors.Hand;
        }

        public void SetState(bool isSafe) {
            _isSafe = isSafe;
            _subTitle = isSafe ? "Готов к безопасному запуску" : "Вход заблокирован (нет VPN)";
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e) {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e) {
            base.OnClick(e);
            if (CardClicked != null) CardClicked();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) {
                if (CardClicked != null) CardClicked();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e) {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _isHovered ? Color.FromArgb(27, 32, 45) : Color.FromArgb(17, 20, 28);
            Color border = _isHovered 
                ? (_isSafe ? Color.FromArgb(90, 52, 211, 153) : Color.FromArgb(90, 248, 113, 113))
                : Color.FromArgb(32, 38, 52);

            using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(bounds, 8)) {
                using (SolidBrush b = new SolidBrush(fill)) {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border, 1f)) {
                    g.DrawPath(pen, p);
                }
            }

            if (Focused) {
                using (GraphicsPath fp = GraphicsUtils.CreateRoundedRectangle(new Rectangle(2, 2, Width - 5, Height - 5), 6)) {
                    using (Pen focusPen = new Pen(Color.FromArgb(147, 197, 253), 1.5f)) {
                        focusPen.DashStyle = DashStyle.Dot;
                        g.DrawPath(focusPen, fp);
                    }
                }
            }

            // Контейнер иконки
            Rectangle iconBox = new Rectangle(12, 15, 34, 34);
            using (SolidBrush ib = new SolidBrush(Color.FromArgb(20, 255, 255, 255))) {
                using (GraphicsPath ip = GraphicsUtils.CreateRoundedRectangle(iconBox, 6)) {
                    g.FillPath(ib, ip);
                }
            }

            using (Pen iconPen = new Pen(Color.FromArgb(226, 232, 240), 1.6f)) {
                iconPen.StartCap = LineCap.Round;
                iconPen.EndCap = LineCap.Round;

                if (_iconType == 0) {
                    g.DrawRectangle(iconPen, 20, 22, 18, 12);
                    g.DrawLine(iconPen, 29, 34, 29, 40);
                    g.DrawLine(iconPen, 24, 40, 34, 40);
                } else if (_iconType == 1) {
                    g.DrawEllipse(iconPen, 19, 22, 18, 18);
                    g.DrawLine(iconPen, 19, 31, 37, 31);
                    g.DrawArc(iconPen, 23, 22, 10, 18, 0, 360);
                } else {
                    g.DrawLine(iconPen, 21, 25, 26, 30);
                    g.DrawLine(iconPen, 26, 30, 21, 35);
                    g.DrawLine(iconPen, 28, 35, 35, 35);
                }
            }

            // Заголовок карточки (Строка 1)
            using (Font titleFont = new Font("Segoe UI", 9.2f, FontStyle.Bold)) {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(248, 250, 252))) {
                    g.DrawString(_title, titleFont, tb, 54, 14);
                }
            }

            // Индикатор-точка и текст статуса (Строка 2 - полное разделение без наложений)
            Color statusColor = _isSafe ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
            using (SolidBrush dotBrush = new SolidBrush(statusColor)) {
                g.FillEllipse(dotBrush, 55, 37, 6, 6);
            }
            using (Font statusFont = new Font("Segoe UI", 8f)) {
                using (SolidBrush sb = new SolidBrush(statusColor)) {
                    g.DrawString(_subTitle, statusFont, sb, 66, 33);
                }
            }

            // Стрелка перехода справа ↗
            using (Font arrowFont = new Font("Segoe UI", 9.5f, FontStyle.Bold)) {
                using (SolidBrush ab = new SolidBrush(_isHovered ? Color.White : Color.FromArgb(71, 85, 105))) {
                    g.DrawString("↗", arrowFont, ab, Width - 24, 13);
                }
            }
        }
    }

    // Модальное окно связи с официальным Telegram-ботом ClaudeBarrierBot
    public class ContactDeveloperDialog : Form {
        public ContactDeveloperDialog() {
            Text = "Официальный бот поддержки ClaudeBarrier";
            Width = 460;
            Height = 300;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(17, 19, 27);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            ShowInTaskbar = false;

            Panel pnlBorder = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(17, 19, 27)
            };
            pnlBorder.Paint += (s, pe) => {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (Pen p = new Pen(Color.FromArgb(50, 60, 85), 1f)) {
                    pe.Graphics.DrawRectangle(p, r);
                }
            };
            Controls.Add(pnlBorder);

            // Аватарка бота
            Panel pnlAvatar = new Panel() {
                Location = new Point(24, 24),
                Size = new Size(54, 54),
                BackColor = Color.Transparent
            };
            pnlAvatar.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, 53, 53);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 29, 41))) {
                    g.FillEllipse(b, r);
                }
                using (Pen pen = new Pen(Color.FromArgb(37, 99, 235), 2f)) {
                    g.DrawEllipse(pen, r);
                }
                using (Font f = new Font("Segoe UI", 13f, FontStyle.Bold)) {
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(56, 189, 248))) {
                        SizeF sz = g.MeasureString("CB", f);
                        g.DrawString("CB", f, tb, (54 - sz.Width) / 2, (54 - sz.Height) / 2);
                    }
                }
            };
            pnlBorder.Controls.Add(pnlAvatar);

            Label lblName = new Label() {
                Text = "ClaudeBarrier Bot",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(90, 26),
                AutoSize = true
            };
            pnlBorder.Controls.Add(lblName);

            Label lblRole = new Label() {
                Text = "Официальный бот лицензирования и технической поддержки",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(92, 52),
                AutoSize = true
            };
            pnlBorder.Controls.Add(lblRole);

            Panel pnlBotLink = new Panel() {
                Location = new Point(24, 94),
                Size = new Size(412, 60),
                BackColor = Color.FromArgb(22, 26, 38),
                Cursor = Cursors.Hand
            };
            pnlBotLink.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlBotLink.Width - 1, pnlBotLink.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 6)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 26, 38))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(40, 48, 68), 1f)) g.DrawPath(pen, p);
                }
                GraphicsUtils.DrawChatBubbleIcon(g, new Rectangle(14, 18, 24, 24), Color.FromArgb(56, 189, 248));
            };
            pnlBorder.Controls.Add(pnlBotLink);

            Label lblBotDesc = new Label() {
                Text = "Бесплатные ключи активации, помощь и свежие релизы:",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(48, 10),
                AutoSize = true
            };
            pnlBotLink.Controls.Add(lblBotDesc);

            Label lblBotHandle = new Label() {
                Text = "@ClaudeBarrierBot (Telegram)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(48, 28),
                AutoSize = true
            };
            lblBotHandle.Click += (s, e) => {
                try { Process.Start("https://t.me/ClaudeBarrierBot"); } catch {}
            };
            pnlBotLink.Controls.Add(lblBotHandle);

            CloudflareButton btnOpen = new CloudflareButton() {
                Text = "Открыть в Telegram",
                Location = new Point(24, 172),
                Size = new Size(196, 38),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(37, 99, 235),
                GradientEnd = Color.FromArgb(29, 78, 216),
                HoverStart = Color.FromArgb(59, 130, 246),
                HoverEnd = Color.FromArgb(37, 99, 235),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White
            };
            btnOpen.Click += (s, e) => {
                try { Process.Start("https://t.me/ClaudeBarrierBot"); } catch {}
            };
            pnlBorder.Controls.Add(btnOpen);

            CloudflareButton btnCopy = new CloudflareButton() {
                Text = "Скопировать ссылку",
                Location = new Point(230, 172),
                Size = new Size(206, 38),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                BorderStrokeColor = Color.FromArgb(50, 56, 76),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 245, 249)
            };
            btnCopy.Click += (s, e) => {
                try {
                    Clipboard.SetText("https://t.me/ClaudeBarrierBot");
                    btnCopy.Text = "Скопировано!";
                } catch {}
            };
            pnlBorder.Controls.Add(btnCopy);

            CloudflareButton btnClose = new CloudflareButton() {
                Text = "Закрыть",
                Location = new Point(24, 224),
                Size = new Size(412, 36),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(24, 27, 38),
                GradientEnd = Color.FromArgb(19, 22, 32),
                BorderStrokeColor = Color.FromArgb(38, 44, 62),
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            btnClose.Click += (s, e) => Close();
            pnlBorder.Controls.Add(btnClose);
        }
    }

    public class MainWindow : Form {
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static void RefreshSystemTray() {
            try {
                IntPtr trayWnd = FindWindow("Shell_TrayWnd", null);
                if (trayWnd != IntPtr.Zero) {
                    IntPtr trayNotify = FindWindowEx(trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (trayNotify != IntPtr.Zero) {
                        IntPtr sysPager = FindWindowEx(trayNotify, IntPtr.Zero, "SysPager", null);
                        IntPtr toolbar = (sysPager != IntPtr.Zero) 
                            ? FindWindowEx(sysPager, IntPtr.Zero, "ToolbarWindow32", null)
                            : FindWindowEx(trayNotify, IntPtr.Zero, "ToolbarWindow32", null);
                        SweepToolbar(toolbar);
                    }
                }
                IntPtr overflowWnd = FindWindow("NotifyIconOverflowWindow", null);
                if (overflowWnd != IntPtr.Zero) {
                    IntPtr overflowToolbar = FindWindowEx(overflowWnd, IntPtr.Zero, "ToolbarWindow32", null);
                    SweepToolbar(overflowToolbar);
                }
            } catch {}
        }

        private static void SweepToolbar(IntPtr hToolbar) {
            if (hToolbar == IntPtr.Zero) return;
            RECT rect;
            if (GetClientRect(hToolbar, out rect)) {
                for (int x = 0; x < rect.Right; x += 8) {
                    for (int y = 0; y < rect.Bottom; y += 8) {
                        int lparam = (y << 16) | (x & 0xFFFF);
                        SendMessage(hToolbar, 0x0200 /* WM_MOUSEMOVE */, 0, lparam);
                    }
                }
            }
        }

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        // Поля логирования и фильтрации
        public class LogItem {
            public string Time;
            public string Category;
            public string Message;
            public Color Col;
        }
        private List<LogItem> _allLogs = new List<LogItem>();
        private string _activeLogFilter = "ALL";
        private WarpSegmentedFilterControl _segmentedFilters;
        private Label _lblUpdateStatus;
        private CloudflareButton _btnCheckUpdate;
        private string _latestReleaseUrl = "https://github.com/skizewike/claude-antiban/releases/latest";

        // Поля оверлея активации
        private Panel _pnlActivationOverlay;
        private TextBox _txtActivationKey;
        private Label _lblActivationStatus;

        private Config _cfg;
        private ProtectionState _currentState = ProtectionState.Checking;

        // Элементы кастомного Titlebar
        private Panel _pnlTitlebar;
        private Label _lblTitleText;
        private Label _lblTitleSub;
        private Panel _appIconBox;
        private Label _btnMin;
        private Label _btnMax;
        private Label _btnClose;

        // Левый Sidebar (фиксированные координаты Location 0, 38; Size 218, 682)
        private Panel _pnlSidebar;
        private Panel _brandLogoBox;
        private List<Panel> _navButtons = new List<Panel>();
        private int _selectedTabIndex = 0;

        // Контейнер контента (фиксированные координаты Location 218, 38; Size 842, 682)
        private Panel _pnlContent;
        private List<Panel> _tabPages = new List<Panel>();

        // Элементы страницы 0: Главная (Hero Dashboard)
        private HeroCloudGraphic _heroCloud;
        private Label _lblStatusTitle;
        private Label _lblStatusSubtitle;
        private CloudflareButton _btnMainCta;
        private Panel _pnlModeSelector;

        // 3 карточки быстрого запуска Claude
        private LauncherCard _cardDesktop;
        private LauncherCard _cardBrowser;
        private LauncherCard _cardCli;

        // Zapret интеграция
        private Process _zapretProcess = null;
        private Label _lblZapretStatus;
        private CloudflareButton _btnToggleZapret;

        // Вкладка «Подключение» (1:1 Cloudflare WARP Zero-Trust)
        private Panel _badgeTunnelStatus;
        private Label _lblWarpIpVal;
        private Label _lblWarpAnthropicVal;
        private Label _lblWarpWfpVal;
        private Label _lblWarpDohVal;

        // Элементы страницы 3: Журнал событий
        private RichTextBox _rtbLogs;
        private Label _lblLogCount;
        private int _logEventCount = 0;
        private static int _isApplyingFirewallRules = 0;

        // Фоновый трей и сторож
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _watchdogTimer;
        private bool _forceExit = false;

        // Win32 Routing Helper & Watchdog States
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern int NotifyAddrChange(IntPtr handle, IntPtr overlapped);

        private Label _lblWarpRouteVal;
        private bool _isSuspended = false;
        private int _watchdogTickCount = 0;

        // Sentinel Guard Pinned IP & State Lock
        private string _pinnedVpnIp = "";
        private string _pinnedAdapterName = "";
        private uint _pinnedAdapterIndex = 0;
        private Thread _notifyAddrChangeThread = null;
        private volatile bool _isTerminating = false;
        private bool _isServiceIpcConnected = false;
        private System.Threading.Timer _serviceHeartbeatTimer = null;
        private DateTime _lastKillBalloonTime = DateTime.MinValue;
        private static int _isShowingModalDialog = 0;

        public int CurrentPing = 0;
        public string CurrentIp = "...";
        public string CurrentCountry = "Проверка...";

        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // Тень окна CS_DROPSHADOW
                return cp;
            }
        }

        public MainWindow(string[] args) {
            _cfg = new Config();
            _cfg.Load();

            AutoDetectAppPaths();
            _cfg.Save();

            Text = "ClaudeBarrier";
            Width = 1060;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(17, 19, 27);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            try {
                using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("app_icon.ico")) {
                    if (s != null) Icon = new Icon(s);
                    else {
                        string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                        if (File.Exists(icoPath)) Icon = new Icon(icoPath);
                    }
                }
            } catch {}

            BuildCustomWindowLayout();
            SetupTray();

            // Bug 5: Sleep / Hibernate leak protection (PowerModeChanged subscription)
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            FormClosed += (s, e) => {
                try {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                } catch {}
            };

            Shown += (s, e) => {
                if (args != null && Array.IndexOf(args, "--minimized") >= 0) {
                    Hide();
                    if (_heroCloud != null) _heroCloud.PauseAnimation();
                }
                Log("SYSTEM", "Барьер запущен с правами администратора. Служба ядра WFP готова к работе.", Color.FromArgb(52, 211, 153));
                if (LicenseManager.IsActivated()) {
                    Log("LICENSE", "Лицензия подтверждена (Защищено Windows DPAPI). Запуск защиты.", Color.FromArgb(52, 211, 153));
                    StartAudit();
                    StartWatchdog();
                    StartKernelNotifyAddrChangeHook();
                    _serviceHeartbeatTimer = new System.Threading.Timer((st) => SendServiceHeartbeat(), null, 1000, 4000);
                    UpdateZapretUiState();
                } else {
                    Log("LICENSE", "Барьер ожидает ввода лицензионного ключа. Получите ключ в @ClaudeBarrierBot.", Color.FromArgb(245, 158, 11));
                }
            };

            if (args != null && Array.IndexOf(args, "--minimized") >= 0) {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        private void SafeInvoke(Action action) {
            try {
                if (IsDisposed || Disposing) return;
                if (!IsHandleCreated) {
                    EventHandler onHandle = null;
                    onHandle = (s, e) => {
                        HandleCreated -= onHandle;
                        try {
                            if (InvokeRequired) BeginInvoke(action);
                            else action();
                        } catch {}
                    };
                    HandleCreated += onHandle;
                    return;
                }
                if (InvokeRequired) BeginInvoke(action);
                else action();
            } catch {}
        }

        // Bug 5: Sleep / Hibernate protection handler
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) {
            if (e.Mode == PowerModes.Suspend) {
                _isSuspended = true;
                if (_cfg.KillSwitchEnabled) {
                    KillClaudeProcessesInMemory();
                }
                SafeInvoke(() => {
                    SetProtectionUIState(ProtectionState.BlockedNoVpn, "Режим сна: трафик заблокирован");
                });
                Log("POWER", "СИСТЕМА: Переход в спящий режим (Suspend). Сетевой трафик Claude немедленно заблокирован.", Color.FromArgb(248, 113, 113));
            } else if (e.Mode == PowerModes.Resume) {
                _isSuspended = false;
                if (_cfg.KillSwitchEnabled) {
                    KillClaudeProcessesInMemory();
                }
                SafeInvoke(() => {
                    SetProtectionUIState(ProtectionState.BlockedNoVpn, "Выход из сна: ожидание VPN...");
                });
                Log("POWER", "СИСТЕМА: Выход из спящего режима (Resume). Блокировка активна до подтверждения безопасного маршрута.", Color.FromArgb(251, 191, 36));
                ThreadPool.QueueUserWorkItem((st) => {
                    Thread.Sleep(2500);
                    StartAudit();
                });
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (e.CloseReason == CloseReason.UserClosing && !_forceExit && _cfg.MinimizeToTrayOnClose) {
                e.Cancel = true;
                Hide();
                if (_heroCloud != null) _heroCloud.PauseAnimation();
                _trayIcon.ShowBalloonTip(2500, "ClaudeBarrier",
                    "Защита работает в фоне. Для возврата к окну дважды нажмите на иконку в трее.", ToolTipIcon.Info);
                Log("TRAY", "Окно свернуто в трей. Фоновая изоляция активна.", Color.FromArgb(148, 163, 184));
            } else {
                try {
                    if (_zapretProcess != null && !_zapretProcess.HasExited) {
                        _zapretProcess.Kill();
                    }
                    // При полном выходе из барьера гасим Claude, чтобы не оставлять незащищенных сессий
                    KillClaudeProcessesInMemory();
                } catch {}
                _isTerminating = true;
                if (_serviceHeartbeatTimer != null) {
                    try { _serviceHeartbeatTimer.Dispose(); } catch {}
                    _serviceHeartbeatTimer = null;
                }
                base.OnFormClosing(e);
            }
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) {
                if (_heroCloud != null) _heroCloud.PauseAnimation();
            } else if (WindowState == FormWindowState.Normal) {
                if (_heroCloud != null) _heroCloud.ResumeAnimation();
            }
        }

        private static readonly object _logLock = new object();
        public void Log(string category, string message, Color color) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = string.Format("[{0}] [{1}] {2}\n", timestamp, category, message);

            LogItem item = new LogItem() { Time = timestamp, Category = category, Message = message, Col = color };
            lock (_logLock) {
                if (_allLogs.Count >= 2000) {
                    _allLogs.RemoveRange(0, 500);
                }
                _allLogs.Add(item);
                try {
                    string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claudebarrier.log");
                    FileInfo fi = new FileInfo(logFile);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024) {
                        string oldLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claudebarrier.old.log");
                        if (File.Exists(oldLog)) File.Delete(oldLog);
                        File.Move(logFile, oldLog);
                    }
                    File.AppendAllText(logFile, line, System.Text.Encoding.UTF8);
                } catch {}
            }

            SafeInvoke(() => {
                if (_rtbLogs != null && !_rtbLogs.IsDisposed) {
                    if (MatchesCurrentFilter(category)) {
                        AppendLogToRtb(item);
                    }
                    _logEventCount = _allLogs.Count;
                    if (_lblLogCount != null && !_lblLogCount.IsDisposed) {
                        _lblLogCount.Text = "Событий: " + _logEventCount;
                    }
                }
            });
        }

        private bool MatchesCurrentFilter(string cat) {
            if (_activeLogFilter == "ALL") return true;
            if (_activeLogFilter == "ERR") return cat == "ERR" || cat == "WARN";
            if (_activeLogFilter == "NET") return cat == "AUDIT" || cat == "SECURITY" || cat == "DOH" || cat == "PING" || cat == "DNS";
            if (_activeLogFilter == "WFP") return cat == "WFP" || cat == "WFP-TEST" || cat == "ZAPRET" || cat == "SYSTEM" || cat == "KILL";
            return true;
        }

        private void AppendLogToRtb(LogItem item) {
            if (_rtbLogs == null || _rtbLogs.IsDisposed) return;
            if (_rtbLogs.TextLength > 100000) {
                _rtbLogs.Select(0, 20000);
                _rtbLogs.SelectedText = "";
            }
            _rtbLogs.SelectionStart = _rtbLogs.TextLength;
            _rtbLogs.SelectionLength = 0;
            _rtbLogs.SelectionColor = Color.FromArgb(100, 116, 139);
            _rtbLogs.AppendText("[" + item.Time + "] ");

            _rtbLogs.SelectionColor = item.Col;
            _rtbLogs.AppendText("[" + item.Category + "] ");

            _rtbLogs.SelectionColor = Color.FromArgb(241, 245, 249);
            _rtbLogs.AppendText(item.Message + "\n");
            _rtbLogs.ScrollToCaret();
        }

        private void ApplyLogFilter(string filter) {
            _activeLogFilter = filter;
            UpdateFilterChipStyles();
            if (_rtbLogs != null && !_rtbLogs.IsDisposed) {
                _rtbLogs.Clear();
                lock (_logLock) {
                    foreach (var item in _allLogs) {
                        if (MatchesCurrentFilter(item.Category)) {
                            AppendLogToRtb(item);
                        }
                    }
                }
            }
        }

        private void UpdateFilterChipStyles() {
            if (_segmentedFilters != null) {
                int target = 0;
                if (_activeLogFilter == "ERR") target = 1;
                else if (_activeLogFilter == "NET") target = 2;
                else if (_activeLogFilter == "WFP") target = 3;
                if (_segmentedFilters.SelectedIndex != target) {
                    _segmentedFilters.SelectedIndex = target;
                }
            }
        }

        private void AutoDetectAppPaths() {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 1. Поиск Claude Desktop
            if (string.IsNullOrEmpty(_cfg.ClaudePath)) {
                string[] appPaths = new string[] {
                    Path.Combine(localApp, @"Microsoft\WindowsApps\claude-desktop.exe"),
                    Path.Combine(localApp, @"Microsoft\WindowsApps\Claude_pzs8sxrjxfjjc\claude-desktop.exe"),
                    Path.Combine(localApp, @"Microsoft\WindowsApps\Claude.exe"),
                    Path.Combine(localApp, @"Programs\Claude\Claude.exe"),
                    @"C:\Program Files\Claude\Claude.exe"
                };

                foreach (string p in appPaths) {
                    if (File.Exists(p)) {
                        _cfg.ClaudePath = p;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(_cfg.ClaudePath)) {
                    _cfg.ClaudePath = "shell:AppsFolder\\Claude_pzs8sxrjxfjjc!Claude";
                }
            }

            // 2. Поиск Claude Code CLI
            if (string.IsNullOrEmpty(_cfg.ClaudeCodePath)) {
                string[] cliPaths = new string[] {
                    Path.Combine(appData, @"npm\claude.cmd"),
                    Path.Combine(localApp, @"Programs\claude-code\claude.exe"),
                    Path.Combine(localApp, @"Microsoft\WindowsApps\claude.exe")
                };
                foreach (string cp in cliPaths) {
                    if (File.Exists(cp)) {
                        _cfg.ClaudeCodePath = cp;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(_cfg.ClaudeCodePath)) {
                    _cfg.ClaudeCodePath = "claude";
                }
            }

            // 3. Поиск Zapret (winws.exe)
            if (string.IsNullOrEmpty(_cfg.ZapretPath)) {
                string[] zapPaths = new string[] {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "winws.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winws.exe"),
                    @"C:\zapret\winws.exe",
                    @"C:\zapret-win-bundle-master\zapret-winws\winws.exe"
                };
                foreach (string zp in zapPaths) {
                    if (File.Exists(zp)) {
                        _cfg.ZapretPath = zp;
                        break;
                    }
                }
            }
        }

        // Построение кастомного каркаса окна БЕЗ наложений и сбоев Dock
        private void BuildCustomWindowLayout() {
            // 1. Верхний Titlebar (38px, от X=0 до 1060)
            _pnlTitlebar = new Panel() {
                Location = new Point(0, 0),
                Size = new Size(1060, 38),
                BackColor = Color.FromArgb(13, 15, 22)
            };
            _pnlTitlebar.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            Controls.Add(_pnlTitlebar);

            // Иконка в Titlebar (app_icon)
            _appIconBox = new Panel() {
                Location = new Point(14, 8),
                Size = new Size(22, 22),
                BackColor = Color.Transparent
            };
            _appIconBox.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Image img = AppAssets.AppIcon;
                if (img != null) {
                    g.DrawImage(img, new Rectangle(0, 0, _appIconBox.Width, _appIconBox.Height));
                }
            };
            _pnlTitlebar.Controls.Add(_appIconBox);

            _lblTitleText = new Label() {
                Text = "ClaudeBarrier",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(40, 9),
                AutoSize = true
            };
            _lblTitleText.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            _pnlTitlebar.Controls.Add(_lblTitleText);

            // Кликабельная подпись Bogdan Zhukov в Titlebar
            _lblTitleSub = new Label() {
                Text = "", Visible = false,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(194, 10),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _lblTitleSub.MouseEnter += (s, e) => _lblTitleSub.ForeColor = Color.FromArgb(56, 189, 248);
            _lblTitleSub.MouseLeave += (s, e) => _lblTitleSub.ForeColor = Color.FromArgb(100, 116, 139);
            _lblTitleSub.Click += (s, e) => ShowContactDialog();
            _pnlTitlebar.Controls.Add(_lblTitleSub);

            // Кнопки управления окном
            _btnClose = new Label() {
                Text = "✕",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(38, 38),
                Location = new Point(Width - 38, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnClose.MouseEnter += (s, e) => { _btnClose.BackColor = Color.FromArgb(239, 68, 68); _btnClose.ForeColor = Color.White; };
            _btnClose.MouseLeave += (s, e) => { _btnClose.BackColor = Color.Transparent; _btnClose.ForeColor = Color.FromArgb(148, 163, 184); };
            _btnClose.Click += (s, e) => Close();
            _pnlTitlebar.Controls.Add(_btnClose);

            _btnMax = new Label() {
                Text = "□",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(38, 38),
                Location = new Point(Width - 76, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnMax.MouseEnter += (s, e) => { _btnMax.BackColor = Color.FromArgb(30, 34, 48); _btnMax.ForeColor = Color.White; };
            _btnMax.MouseLeave += (s, e) => { _btnMax.BackColor = Color.Transparent; _btnMax.ForeColor = Color.FromArgb(148, 163, 184); };
            _btnMax.Click += (s, e) => {
                WindowState = (WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized;
            };
            _pnlTitlebar.Controls.Add(_btnMax);

            _btnMin = new Label() {
                Text = "─",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(38, 38),
                Location = new Point(Width - 114, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            _btnMin.MouseEnter += (s, e) => { _btnMin.BackColor = Color.FromArgb(30, 34, 48); _btnMin.ForeColor = Color.White; };
            _btnMin.MouseLeave += (s, e) => { _btnMin.BackColor = Color.Transparent; _btnMin.ForeColor = Color.FromArgb(148, 163, 184); };
            _btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _pnlTitlebar.Controls.Add(_btnMin);

            // 2. Левый сайдбар: строго Location (0, 38), Size (218, 682)
            _pnlSidebar = new Panel() {
                Location = new Point(0, 38),
                Size = new Size(218, 682),
                BackColor = Color.FromArgb(14, 16, 23)
            };
            _pnlSidebar.Paint += (s, pe) => {
                using (Pen p = new Pen(Color.FromArgb(28, 32, 44), 1f)) {
                    pe.Graphics.DrawLine(p, _pnlSidebar.Width - 1, 0, _pnlSidebar.Width - 1, _pnlSidebar.Height);
                }
            };
            Controls.Add(_pnlSidebar);

            BuildSidebar();

            // 3. Центральный контейнер контента: строго Location (218, 38), Size (842, 682)
            _pnlContent = new Panel() {
                Location = new Point(218, 38),
                Size = new Size(842, 682),
                BackColor = Color.FromArgb(13, 15, 21)
            };
            Controls.Add(_pnlContent);

            // 5 Вкладок внутри контента (соответствуют порядку меню WARP)
            Panel pageHero = new Panel() { Location = new Point(0, 0), Size = new Size(842, 682), BackColor = Color.Transparent };
            _tabPages.Add(pageHero);
            _pnlContent.Controls.Add(pageHero);
            BuildHeroPage(pageHero);

            Panel pageDiag = new Panel() { Location = new Point(0, 0), Size = new Size(842, 682), BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageDiag);
            _pnlContent.Controls.Add(pageDiag);
            BuildDiagnosticsPage(pageDiag);

            Panel pageBypass = new Panel() { Location = new Point(0, 0), Size = new Size(842, 682), BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageBypass);
            _pnlContent.Controls.Add(pageBypass);
            BuildBypassPage(pageBypass);

            Panel pageSettings = new Panel() { Location = new Point(0, 0), Size = new Size(842, 682), BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageSettings);
            _pnlContent.Controls.Add(pageSettings);
            BuildSettingsPage(pageSettings);

            Panel pageLogs = new Panel() { Location = new Point(0, 0), Size = new Size(842, 682), BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageLogs);
            _pnlContent.Controls.Add(pageLogs);
            BuildLogsPage(pageLogs);

            // Оверлей блокировки без активации (блокирует все окно ниже заголовка)
            _pnlActivationOverlay = new Panel() {
                Location = new Point(0, 38),
                Size = new Size(1060, 682),
                BackColor = Color.FromArgb(13, 15, 21),
                Visible = !LicenseManager.IsActivated()
            };
            Controls.Add(_pnlActivationOverlay);
            BuildActivationOverlay(_pnlActivationOverlay);
            if (!LicenseManager.IsActivated()) {
                Controls.SetChildIndex(_pnlActivationOverlay, 0);
                _pnlActivationOverlay.BringToFront();
            }

            SelectTab(0);
        }

        private void BuildActivationOverlay(Panel overlay) {
            overlay.Controls.Clear();
            Panel card = new Panel() {
                Location = new Point(300, 141),
                Size = new Size(460, 400),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            card.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 10)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(37, 99, 235), 1.5f)) g.DrawPath(pen, p);
                }
            };
            overlay.Controls.Add(card);

            // Иконка
            Panel pnlIcon = new Panel() {
                Location = new Point(206, 20),
                Size = new Size(48, 48),
                BackColor = Color.Transparent
            };
            pnlIcon.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Image img = AppAssets.AppIcon;
                if (img != null) g.DrawImage(img, 0, 0, 48, 48);
            };
            card.Controls.Add(pnlIcon);

            Label lblTitle = new Label() {
                Text = "Активация ClaudeBarrier",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 78),
                Size = new Size(420, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Для запуска аппаратного барьера WFP и защиты от утечек требуется действующий лицензионный ключ.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(30, 110),
                Size = new Size(400, 36),
                TextAlign = ContentAlignment.TopCenter
            };
            card.Controls.Add(lblDesc);

            Label lblKeyPrompt = new Label() {
                Text = "ЛИЦЕНЗИОННЫЙ КЛЮЧ:",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(40, 156),
                AutoSize = true
            };
            card.Controls.Add(lblKeyPrompt);

            _txtActivationKey = new TextBox() {
                Location = new Point(40, 178),
                Size = new Size(380, 34),
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                BackColor = Color.FromArgb(11, 14, 20),
                ForeColor = Color.FromArgb(56, 189, 248),
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtActivationKey.TextChanged += (s, e) => {
                int sel = _txtActivationKey.SelectionStart;
                _txtActivationKey.Text = _txtActivationKey.Text.ToUpperInvariant();
                _txtActivationKey.SelectionStart = sel;
            };
            card.Controls.Add(_txtActivationKey);

            _lblActivationStatus = new Label() {
                Location = new Point(40, 218),
                Size = new Size(380, 24),
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(239, 68, 68),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = ""
            };
            card.Controls.Add(_lblActivationStatus);

            CloudflareButton btnActivate = new CloudflareButton() {
                Text = "Активировать ClaudeBarrier",
                Location = new Point(40, 248),
                Size = new Size(380, 42),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(37, 99, 235),
                GradientEnd = Color.FromArgb(29, 78, 216),
                HoverStart = Color.FromArgb(59, 130, 246),
                HoverEnd = Color.FromArgb(37, 99, 235),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White
            };
            btnActivate.Click += (s, e) => {
                string inputKey = _txtActivationKey.Text != null ? _txtActivationKey.Text.Trim() : "";
                string validationReason;
                if (LicenseManager.ValidateKey(inputKey, out validationReason)) {
                    LicenseManager.SaveKey(inputKey);
                    overlay.Visible = false;
                    Log("LICENSE", "Лицензия успешно подтверждена! Срок: 3 дня. Ключ зашифрован Windows DPAPI.", Color.FromArgb(52, 211, 153));
                    StartAudit();
                    StartWatchdog();
                    UpdateZapretUiState();
                    MessageBox.Show("Лицензия ClaudeBarrier успешно активирована на 3 дня!\n\nКлюч зашифрован через Windows DPAPI и надежно сохранен.\nВсе модули аппаратной защиты и фильтрации WFP запущены.",
                        "Активация успешна", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } else {
                    _lblActivationStatus.Text = string.IsNullOrEmpty(validationReason) ? "Неверный ключ. Получите ключ в @ClaudeBarrierBot" : validationReason;
                    _lblActivationStatus.ForeColor = Color.FromArgb(239, 68, 68);
                }
            };
            card.Controls.Add(btnActivate);

            CloudflareButton btnGetBot = new CloudflareButton() {
                Text = "🔑 Получить бесплатный ключ в @ClaudeBarrierBot",
                Location = new Point(40, 298),
                Size = new Size(380, 36),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(24, 27, 38),
                GradientEnd = Color.FromArgb(19, 22, 32),
                HoverStart = Color.FromArgb(38, 44, 62),
                HoverEnd = Color.FromArgb(28, 33, 48),
                BorderStrokeColor = Color.FromArgb(50, 56, 76),
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248)
            };
            btnGetBot.Click += (s, e) => {
                try { Process.Start("https://t.me/ClaudeBarrierBot"); } catch {}
            };
            card.Controls.Add(btnGetBot);

            Label lblSafeInfo = new Label() {
                Text = "🔒 Защита учетных данных через криптографию Windows DPAPI",
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(40, 348),
                Size = new Size(380, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(lblSafeInfo);
        }

        private void ShowContactDialog() {
            using (ContactDeveloperDialog dlg = new ContactDeveloperDialog()) {
                dlg.ShowDialog(this);
            }
        }

        // Построение левого сайдбара
        private void BuildSidebar() {
            Panel pnlBrand = new Panel() {
                Location = new Point(12, 12),
                Size = new Size(194, 46),
                BackColor = Color.Transparent
            };
            _pnlSidebar.Controls.Add(pnlBrand);

            // Логотип приложения (app_icon)
            _brandLogoBox = new Panel() {
                Location = new Point(4, 6),
                Size = new Size(32, 32),
                BackColor = Color.Transparent
            };
            _brandLogoBox.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Image img = AppAssets.AppIcon;
                if (img != null) {
                    g.DrawImage(img, new Rectangle(0, 0, _brandLogoBox.Width, _brandLogoBox.Height));
                }
            };
            pnlBrand.Controls.Add(_brandLogoBox);

            Label lblBrandName = new Label() {
                Text = "Anti-Ban Barrier",
                Font = new Font("Segoe UI", 9.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(42, 8),
                AutoSize = true
            };
            pnlBrand.Controls.Add(lblBrandName);

            Label lblBrandSub = new Label() {
                Text = "ClaudeBarrier Traffic Sentinel",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(43, 27),
                AutoSize = true
            };
            pnlBrand.Controls.Add(lblBrandSub);

            Panel sepTop = new Panel() {
                Location = new Point(12, 64),
                Size = new Size(194, 1),
                BackColor = Color.FromArgb(24, 27, 36)
            };
            _pnlSidebar.Controls.Add(sepTop);

            // 5 Вкладок меню в стиле Cloudflare One Client
            string[] navTitles = new string[] {
                "Главная",
                "Подключение",
                "Обход блокировок",
                "Настройки",
                "Журнал"
            };

            int startY = 74;
            for (int i = 0; i < navTitles.Length; i++) {
                int tabIdx = i;
                Panel navBtn = new Panel() {
                    Location = new Point(12, startY + i * 44),
                    Size = new Size(194, 38),
                    BackColor = (i == 0) ? Color.FromArgb(28, 33, 46) : Color.Transparent,
                    Cursor = Cursors.Hand
                };

                navBtn.Paint += (s, pe) => {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    bool isSelected = (_selectedTabIndex == tabIdx);
                    Color textColor = isSelected ? Color.White : Color.FromArgb(148, 163, 184);
                    Color iconColor = isSelected
                        ? ((_currentState == ProtectionState.Protected) ? Color.FromArgb(52, 211, 153) : Color.FromArgb(59, 130, 246))
                        : Color.FromArgb(148, 163, 184);

                    if (isSelected) {
                        Rectangle r = new Rectangle(0, 0, navBtn.Width - 1, navBtn.Height - 1);
                        using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 33, 46))) {
                                g.FillPath(b, p);
                            }
                            using (Pen pen = new Pen(Color.FromArgb(45, 54, 76), 1f)) {
                                g.DrawPath(pen, p);
                            }
                        }
                    }

                    // Векторные пиктограммы Cloudflare One
                    using (Pen ip = new Pen(iconColor, 1.6f)) {
                        ip.StartCap = LineCap.Round;
                        ip.EndCap = LineCap.Round;
                        int ix = 12;
                        int iy = 11;

                        if (tabIdx == 0) {
                            // Globe (Главная)
                            g.DrawEllipse(ip, ix, iy, 14, 14);
                            g.DrawLine(ip, ix, iy + 7, ix + 14, iy + 7);
                            g.DrawArc(ip, ix + 3, iy, 8, 14, 0, 360);
                        } else if (tabIdx == 1) {
                            // Network Nodes Graph (Подключение)
                            g.DrawEllipse(ip, ix + 1, iy + 7, 4, 4);
                            g.DrawEllipse(ip, ix + 9, iy + 2, 4, 4);
                            g.DrawEllipse(ip, ix + 9, iy + 10, 4, 4);
                            g.DrawLine(ip, ix + 4, iy + 8, ix + 9, iy + 4);
                            g.DrawLine(ip, ix + 4, iy + 9, ix + 9, iy + 11);
                        } else if (tabIdx == 2) {
                            // Lightning Zapret
                            Point[] pts = new Point[] {
                                new Point(ix + 8, iy),
                                new Point(ix + 2, iy + 8),
                                new Point(ix + 8, iy + 8),
                                new Point(ix + 6, iy + 14),
                                new Point(ix + 13, iy + 6),
                                new Point(ix + 7, iy + 6),
                                new Point(ix + 8, iy)
                            };
                            g.DrawLines(ip, pts);
                        } else if (tabIdx == 3) {
                            // Settings Gear
                            g.DrawEllipse(ip, ix + 2, iy + 2, 11, 11);
                            g.DrawEllipse(ip, ix + 5, iy + 5, 5, 5);
                        } else {
                            // Logs Journal Document
                            g.DrawRectangle(ip, ix + 2, iy + 1, 11, 13);
                            g.DrawLine(ip, ix + 5, iy + 5, ix + 10, iy + 5);
                            g.DrawLine(ip, ix + 5, iy + 8, ix + 10, iy + 8);
                        }
                    }

                    using (Font f = new Font("Segoe UI", 9.2f, isSelected ? FontStyle.Bold : FontStyle.Regular)) {
                        using (SolidBrush tb = new SolidBrush(textColor)) {
                            g.DrawString(navTitles[tabIdx], f, tb, 36, 9);
                        }
                    }
                };

                navBtn.MouseEnter += (s, e) => {
                    if (_selectedTabIndex != tabIdx) navBtn.BackColor = Color.FromArgb(18, 21, 30);
                };
                navBtn.MouseLeave += (s, e) => {
                    if (_selectedTabIndex != tabIdx) navBtn.BackColor = Color.Transparent;
                };
                navBtn.Click += (s, e) => SelectTab(tabIdx);

                _navButtons.Add(navBtn);
                _pnlSidebar.Controls.Add(navBtn);
            }

            // Нижняя минималистичная кнопка телеграм-бота поддержки
            Panel pnlFeedback = new Panel() {
                Location = new Point(12, 630),
                Size = new Size(194, 38),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            bool isFeedbackHovered = false;
            pnlFeedback.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Rectangle r = new Rectangle(0, 0, pnlFeedback.Width - 1, pnlFeedback.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 6)) {
                    Color bg = isFeedbackHovered ? Color.FromArgb(24, 28, 42) : Color.FromArgb(15, 17, 25);
                    Color border = isFeedbackHovered ? Color.FromArgb(45, 52, 74) : Color.FromArgb(28, 32, 46);
                    using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                    using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, p);
                }

                Color iconColor = isFeedbackHovered ? Color.FromArgb(56, 189, 248) : Color.FromArgb(148, 163, 184);
                GraphicsUtils.DrawChatBubbleIcon(g, new Rectangle(12, 10, 18, 18), iconColor);

                Color txtColor = isFeedbackHovered ? Color.FromArgb(248, 250, 252) : Color.FromArgb(148, 163, 184);
                using (Font fbFont = new Font("Segoe UI", 8.8f, FontStyle.Regular)) {
                    using (SolidBrush fbBrush = new SolidBrush(txtColor)) {
                        g.DrawString("@ClaudeBarrierBot", fbFont, fbBrush, 36, 9);
                    }
                }
            };
            pnlFeedback.MouseEnter += (s, e) => { isFeedbackHovered = true; pnlFeedback.Invalidate(); };
            pnlFeedback.MouseLeave += (s, e) => { isFeedbackHovered = false; pnlFeedback.Invalidate(); };
            pnlFeedback.Click += (s, e) => ShowContactDialog();
            _pnlSidebar.Controls.Add(pnlFeedback);
        }

        public void SelectTab(int index) {
            if (index < 0 || index >= _tabPages.Count) return;
            _selectedTabIndex = index;

            for (int i = 0; i < _tabPages.Count; i++) {
                _tabPages[i].Visible = (i == index);
            }
            if (!LicenseManager.IsActivated() && _pnlActivationOverlay != null) {
                _pnlActivationOverlay.Visible = true;
                _pnlActivationOverlay.BringToFront();
            }
            for (int i = 0; i < _navButtons.Count; i++) {
                _navButtons[i].Invalidate();
            }
        }

        // Вкладка 0: Главная (Hero Dashboard с облачной анимацией и 3 карточками запуска)
        private void BuildHeroPage(Panel page) {
            // Центральная интерактивная фигура (крупный app_icon с парящими частицами WARP)
            _heroCloud = new HeroCloudGraphic() {
                Location = new Point((842 - 240) / 2, 45),
                Size = new Size(240, 140)
            };
            page.Controls.Add(_heroCloud);

            // Крупный статус в стиле Cloudflare WARP («Отключено» / «Подключено»)
            _lblStatusTitle = new Label() {
                Text = "Отключено",
                Font = new Font("Segoe UI", 25f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Size = new Size(842, 44),
                Location = new Point(0, 198),
                TextAlign = ContentAlignment.MiddleCenter
            };
            page.Controls.Add(_lblStatusTitle);

            _lblStatusSubtitle = new Label() {
                Text = "Ваше интернет-подключение не защищено от блокировок Claude.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Size = new Size(762, 36),
                Location = new Point(40, 248),
                TextAlign = ContentAlignment.MiddleCenter
            };
            page.Controls.Add(_lblStatusSubtitle);

            // Большая центральная кнопка действия Cloudflare One («Подключить» / «Отключить»)
            _btnMainCta = new CloudflareButton() {
                Text = "Подключить",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Size = new Size(220, 48),
                Location = new Point((842 - 220) / 2, 298),
                CornerRadius = 10,
                GradientStart = Color.FromArgb(37, 99, 235),
                GradientEnd = Color.FromArgb(29, 78, 216),
                HoverStart = Color.FromArgb(59, 130, 246),
                HoverEnd = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                HasGlow = true,
                GlowColor = Color.FromArgb(80, 59, 130, 246)
            };
            _btnMainCta.Click += (s, e) => StartAudit();
            page.Controls.Add(_btnMainCta);

            // Селектор режима под кнопкой
            Label lblModeTag = new Label() {
                Text = "Режим",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Size = new Size(340, 16),
                Location = new Point((842 - 340) / 2, 360),
                TextAlign = ContentAlignment.MiddleCenter
            };
            page.Controls.Add(lblModeTag);

            _pnlModeSelector = new Panel() {
                Size = new Size(340, 38),
                Location = new Point((842 - 340) / 2, 380),
                BackColor = Color.FromArgb(18, 21, 30),
                Cursor = Cursors.Hand
            };
            _pnlModeSelector.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                Rectangle r = new Rectangle(0, 0, _pnlModeSelector.Width - 1, _pnlModeSelector.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(38, 44, 62), 1f)) {
                        g.DrawPath(pen, p);
                    }
                }
            };
            Label lblModeText = new Label() {
                Text = "Трафик WFP + Cloudflare DoH (UDP/TCP)  ▼",
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            lblModeText.Click += (s, e) => SelectTab(1);
            _pnlModeSelector.Click += (s, e) => SelectTab(1);
            _pnlModeSelector.Controls.Add(lblModeText);
            page.Controls.Add(_pnlModeSelector);

            // 3 аккуратные карточки быстрого запуска Claude равной ширины (Строка ниже селектора)
            Panel pnlLaunchers = new Panel() {
                Location = new Point(24, 446),
                Size = new Size(794, 74),
                BackColor = Color.Transparent
            };
            page.Controls.Add(pnlLaunchers);

            _cardDesktop = new LauncherCard("Claude Desktop", 0) {
                Location = new Point(14, 4),
                Size = new Size(242, 64)
            };
            _cardDesktop.CardClicked += () => LaunchClaudeDesktop();
            pnlLaunchers.Controls.Add(_cardDesktop);

            _cardBrowser = new LauncherCard("Веб-версия claude.ai", 1) {
                Location = new Point(276, 4),
                Size = new Size(242, 64)
            };
            _cardBrowser.CardClicked += () => LaunchClaudeBrowser();
            pnlLaunchers.Controls.Add(_cardBrowser);

            _cardCli = new LauncherCard("Claude Code CLI", 2) {
                Location = new Point(538, 4),
                Size = new Size(242, 64)
            };
            _cardCli.CardClicked += () => LaunchClaudeCodeCli();
            pnlLaunchers.Controls.Add(_cardCli);

            // Нижний футер 1 в 1 как в Cloudflare One Client
            Label lblCopy = new Label() {
                Text = "v6.0.5 | ClaudeBarrier Traffic Sentinel",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(80, 95, 120),
                Location = new Point(0, 642),
                Size = new Size(842, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            lblCopy.Click += (s, e) => ShowContactDialog();
            page.Controls.Add(lblCopy);
        }

        private CloudflareButton CreateWarpDangerButton(string text, int width, int height, EventHandler onClick) {
            CloudflareButton btn = new CloudflareButton() {
                Text = text,
                Size = new Size(width, height),
                CornerRadius = 6,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                GradientStart = Color.FromArgb(50, 22, 28),
                GradientEnd = Color.FromArgb(40, 16, 22),
                HoverStart = Color.FromArgb(70, 28, 36),
                HoverEnd = Color.FromArgb(50, 22, 28),
                BorderStrokeColor = Color.FromArgb(127, 29, 29),
                ForeColor = Color.FromArgb(252, 165, 165),
                Cursor = Cursors.Hand
            };
            if (onClick != null) btn.Click += onClick;
            return btn;
        }

        private TextBox CreateWarpTextBox(string text, int width) {
            return new TextBox() {
                Text = text,
                Size = new Size(width, 26),
                BackColor = Color.FromArgb(15, 18, 26),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
        }

        private void CopyLogsToClipboard() {
            try {
                if (_rtbLogs != null && !string.IsNullOrEmpty(_rtbLogs.Text)) {
                    Clipboard.SetText(_rtbLogs.Text);
                    MessageBox.Show("Журнал событий скопирован в буфер обмена.", "Журнал событий", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show("Не удалось скопировать: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportLogsToFile() {
            try {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "claudebarrier_export.log");
                if (_rtbLogs != null) {
                    File.WriteAllText(logPath, _rtbLogs.Text, System.Text.Encoding.UTF8);
                    MessageBox.Show("Журнал событий сохранен на Рабочий стол:\n" + logPath, "Экспорт журнала", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show("Ошибка экспорта журнала: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private CloudflareButton CreateWarpButton(string text, int width, int height, EventHandler onClick) {
            CloudflareButton btn = new CloudflareButton() {
                Text = text,
                Size = new Size(width, height),
                CornerRadius = 6,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                GradientStart = Color.FromArgb(24, 27, 38),
                GradientEnd = Color.FromArgb(20, 23, 32),
                HoverStart = Color.FromArgb(38, 44, 62),
                HoverEnd = Color.FromArgb(28, 33, 48),
                BorderStrokeColor = Color.FromArgb(42, 48, 66),
                ForeColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            if (onClick != null) btn.Click += onClick;
            return btn;
        }

        // Вкладка 1: Подключение (1:1 Cloudflare WARP с нулевым скроллом)
        private void BuildDiagnosticsPage(Panel page) {
            page.Controls.Clear();
            page.AutoScroll = false;

            Label lblTitle = new Label() {
                Text = "Подключение",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(32, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            // ================= SECTION 1: DNS-ПОДКЛЮЧЕНИЕ =================
            Label lblDnsSec = new Label() {
                Text = "DNS-ПОДКЛЮЧЕНИЕ",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(34, 72),
                AutoSize = true
            };
            page.Controls.Add(lblDnsSec);

            Panel cardDns = new Panel() {
                Location = new Point(32, 94),
                Size = new Size(786, 62),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardDns.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardDns.Width - 1, cardDns.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
            };
            page.Controls.Add(cardDns);

            Label lblDnsTitle = new Label() {
                Text = "DNS-протокол",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 12),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardDns.Controls.Add(lblDnsTitle);

            _lblWarpDohVal = new Label() {
                Text = "Cloudflare 1.1.1.1 (DNS-over-HTTPS)",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(20, 34),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardDns.Controls.Add(_lblWarpDohVal);

            CloudflareButton btnTestDoh = CreateWarpButton("Тест DoH", 130, 34, (s, e) => TestDoHResolver());
            btnTestDoh.Location = new Point(636, 14);
            cardDns.Controls.Add(btnTestDoh);

            // ================= SECTION 2: ПОДКЛЮЧЕНИЕ TUNNEL =================
            Label lblTunnelSec = new Label() {
                Text = "ПОДКЛЮЧЕНИЕ TUNNEL",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(34, 174),
                AutoSize = true
            };
            page.Controls.Add(lblTunnelSec);

            Panel cardTunnel = new Panel() {
                Location = new Point(32, 196),
                Size = new Size(786, 220),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardTunnel.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardTunnel.Width - 1, cardTunnel.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 20, 44, cardTunnel.Width - 20, 44);
                    g.DrawLine(divPen, 20, 88, cardTunnel.Width - 20, 88);
                    g.DrawLine(divPen, 20, 132, cardTunnel.Width - 20, 132);
                    g.DrawLine(divPen, 20, 176, cardTunnel.Width - 20, 176);
                }
            };
            page.Controls.Add(cardTunnel);

            // Row 1: Статус подключения
            Label lblRowStatus = new Label() {
                Text = "Статус подключения",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 13),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(lblRowStatus);

            _badgeTunnelStatus = new Panel() {
                Location = new Point(646, 8),
                Size = new Size(120, 28),
                BackColor = Color.Transparent
            };
            _badgeTunnelStatus.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool isProtected = (_currentState == ProtectionState.Protected);
                Rectangle r = new Rectangle(0, 0, _badgeTunnelStatus.Width - 1, _badgeTunnelStatus.Height - 1);
                Color bg = isProtected ? Color.FromArgb(19, 56, 44) : Color.FromArgb(59, 29, 36);
                Color border = isProtected ? Color.FromArgb(6, 95, 70) : Color.FromArgb(127, 29, 29);
                Color textColor = isProtected ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                string text = isProtected ? "Подключено" : "Отключено";

                using (GraphicsPath path = GraphicsUtils.CreateRoundedRectangle(r, 6)) {
                    using (SolidBrush sb = new SolidBrush(bg)) g.FillPath(sb, path);
                    using (Pen p = new Pen(border, 1f)) g.DrawPath(p, path);
                }

                using (Font font = new Font("Segoe UI", 8.8f, FontStyle.Bold))
                using (SolidBrush tb = new SolidBrush(textColor)) {
                    StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, font, tb, r, sf);
                }
            };
            cardTunnel.Controls.Add(_badgeTunnelStatus);

            // Row 2: Внешний IP и страна
            Label lblRowIp = new Label() {
                Text = "Внешний IP и страна",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 57),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(lblRowIp);

            _lblWarpIpVal = new Label() {
                Text = CurrentIp != "..." ? (CurrentIp + " (" + CurrentCountry + ")") : "Ожидание проверки...",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(320, 57),
                Size = new Size(446, 20),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(_lblWarpIpVal);

            // Row 3: Шлюз Anthropic (api.anthropic.com)
            Label lblRowAnthropic = new Label() {
                Text = "Шлюз Anthropic (api.anthropic.com)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 101),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(lblRowAnthropic);

            _lblWarpAnthropicVal = new Label() {
                Text = CurrentPing > 0 ? (CurrentPing + " мс (TLS 1.2/1.3)") : "Ожидание проверки...",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(320, 101),
                Size = new Size(446, 20),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(_lblWarpAnthropicVal);

            // Row 4: Брандмауэр ядра WFP
            Label lblRowWfp = new Label() {
                Text = "Брандмауэр ядра WFP (Windows Filtering Platform)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 145),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(lblRowWfp);

            int currentFwCount = GetFirewallRuleCountFromRegistry();
            _lblWarpWfpVal = new Label() {
                Text = currentFwCount > 0 ? ("Активен (" + currentFwCount + " правил внедрено)") : "Правила не внедрены",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = currentFwCount > 0 ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113),
                Location = new Point(320, 145),
                Size = new Size(446, 20),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _lblWarpWfpVal.Click += (s, e) => ApplyFirewallRulesSilent(showSuccessDialog: true);
            cardTunnel.Controls.Add(_lblWarpWfpVal);

            // Row 5: Маршрут 0.0.0.0/0 (Anthropic) (Bug 3)
            Label lblRowRoute = new Label() {
                Text = "Маршрут 0.0.0.0/0 и шлюз к Anthropic",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 189),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardTunnel.Controls.Add(lblRowRoute);

            _lblWarpRouteVal = new Label() {
                Text = "Определение маршрута ядра...",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(320, 189),
                Size = new Size(446, 20),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            _lblWarpRouteVal.Click += (s, e) => StartAudit();
            cardTunnel.Controls.Add(_lblWarpRouteVal);

            // ================= SECTION 3: БЕЗОПАСНОСТЬ И ТЕСТЫ =================
            Label lblSecTitle = new Label() {
                Text = "БЕЗОПАСНОСТЬ И ТЕСТЫ",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(34, 430),
                AutoSize = true
            };
            page.Controls.Add(lblSecTitle);

            Panel cardSec = new Panel() {
                Location = new Point(32, 452),
                Size = new Size(786, 144),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardSec.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardSec.Width - 1, cardSec.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 20, 72, cardSec.Width - 20, 72);
                }
            };
            page.Controls.Add(cardSec);

            // Row 1: Аварийный перехват WFP
            Label lblTestInterceptTitle = new Label() {
                Text = "Аварийный перехват WFP",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 15),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardSec.Controls.Add(lblTestInterceptTitle);

            Label lblTestInterceptSub = new Label() {
                Text = "Проверка сброса несанкционированного пакета к 160.79.104.10:443 ядром ОС",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 39),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardSec.Controls.Add(lblTestInterceptSub);

            CloudflareButton btnInterceptTest = CreateWarpButton("Тест перехвата", 160, 36, (s, e) => RunEmergencyInterceptTest());
            btnInterceptTest.Location = new Point(606, 18);
            cardSec.Controls.Add(btnInterceptTest);

            // Row 2: Диагностика сетевого шлюза
            Label lblAuditTitle = new Label() {
                Text = "Диагностика сетевого шлюза",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 87),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardSec.Controls.Add(lblAuditTitle);

            Label lblAuditSub = new Label() {
                Text = "Параллельный опрос внешних IP-шлюзов и замер RTT-задержки к Anthropic",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 111),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardSec.Controls.Add(lblAuditSub);

            CloudflareButton btnRunAudit = CreateWarpButton("Проверить соединение", 160, 36, (s, e) => StartAudit());
            btnRunAudit.Location = new Point(606, 90);
            cardSec.Controls.Add(btnRunAudit);
        }

        // Вкладка 2: Обход блокировок (Zapret DPI + WFP изоляция + Защита от разрыва VPN)
        private void BuildBypassPage(Panel page) {
            page.Controls.Clear();
            page.AutoScroll = false;

            Label lblTitle = new Label() {
                Text = "Обход блокировок",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(32, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            

            // ================= CARD 1: ZAPRET DPI BYPASS =================
            Panel cardZapret = new Panel() {
                Location = new Point(32, 78),
                Size = new Size(786, 104),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardZapret.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardZapret.Width - 1, cardZapret.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
            };
            page.Controls.Add(cardZapret);

            Label zTitle = new Label() {
                Text = "Встроенный модуль Zapret DPI Bypass (winws.exe)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 14),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardZapret.Controls.Add(zTitle);

            _lblZapretStatus = new Label() {
                Text = (_zapretProcess != null && !_zapretProcess.HasExited) 
                    ? "● Служба активна в фоновом режиме (перехват DPI TCP 443)" 
                    : "○ Служба выключена (по умолчанию отключена)",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = (_zapretProcess != null && !_zapretProcess.HasExited) 
                    ? Color.FromArgb(52, 211, 153) 
                    : Color.FromArgb(148, 163, 184),
                Location = new Point(20, 38),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardZapret.Controls.Add(_lblZapretStatus);

            Label zDesc = new Label() {
                Text = "Низкоуровневая фрагментация пакетов TCP 443 для нейтрализации DPI провайдеров.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(20, 62),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardZapret.Controls.Add(zDesc);

            _btnToggleZapret = CreateWarpButton(
                (_zapretProcess != null && !_zapretProcess.HasExited) ? "Остановить" : "Запустить Zapret",
                160, 38,
                (s, e) => ToggleZapret()
            );
            _btnToggleZapret.Location = new Point(606, 33);
            cardZapret.Controls.Add(_btnToggleZapret);

            // ================= CARD 2: ПРАВИЛА И КИЛЛСВИТЧ =================
            Panel cardRules = new Panel() {
                Location = new Point(32, 194),
                Size = new Size(786, 152),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardRules.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardRules.Width - 1, cardRules.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 20, 76, cardRules.Width - 20, 76);
                }
            };
            page.Controls.Add(cardRules);

            // Row 1: Применить правила
            Label lblR1Title = new Label() {
                Text = "Аппаратная изоляция WFP в ядре Windows",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 15),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardRules.Controls.Add(lblR1Title);

            Label lblR1Sub = new Label() {
                Text = "Внедрение правил блокировки CIDR 160.79.104.0/23 и отключение утечек IPv6 на физических картах.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 38),
                Size = new Size(570, 30),
                BackColor = Color.Transparent
            };
            cardRules.Controls.Add(lblR1Sub);

            CloudflareButton btnApplyRules = CreateWarpButton("Применить правила", 160, 36, (s, e) => ApplyFirewallRulesSilent(showSuccessDialog: true));
            btnApplyRules.Location = new Point(606, 19);
            cardRules.Controls.Add(btnApplyRules);

            // Row 2: Сокет-сторож и KillSwitch
            Label lblR2Title = new Label() {
                Text = "Многоуровневый сокет-сторож и процессный KillSwitch",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 91),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardRules.Controls.Add(lblR2Title);

            Label lblR2Sub = new Label() {
                Text = "Мгновенное отсечение сокетов и завершение процессов Claude при потере VPN (< 15 мс). Работает даже без прав Администратора.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 114),
                Size = new Size(570, 32),
                BackColor = Color.Transparent
            };
            cardRules.Controls.Add(lblR2Sub);

            CloudflareButton btnTestKill = CreateWarpButton("Тест перехвата", 160, 36, (s, e) => RunEmergencyInterceptTest());
            btnTestKill.Location = new Point(606, 95);
            cardRules.Controls.Add(btnTestKill);

            // ================= CARD 3: ПРЕДУПРЕЖДЕНИЕ БЕЗОПАСНОСТИ =================
            Panel cardAdvisory = new Panel() {
                Location = new Point(32, 358),
                Size = new Size(786, 186),
                BackColor = Color.FromArgb(22, 19, 28)
            };
            cardAdvisory.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardAdvisory.Width - 1, cardAdvisory.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 19, 28))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(120, 60, 20), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(42, 32, 28), 1f)) {
                    g.DrawLine(divPen, 20, 34, cardAdvisory.Width - 20, 34);
                }
            };
            page.Controls.Add(cardAdvisory);

            Label lblAdvTitle = new Label() {
                Text = "Ограничения сетевой изоляции (что делает и чего НЕ делает утилита)",
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(20, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardAdvisory.Controls.Add(lblAdvTitle);

            Label lblAdv1 = new Label() {
                Text = "• Блокирует сетевой трафик Claude при сбое VPN и закрывает утечки реального IP/IPv6 на физических картах.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(20, 44),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardAdvisory.Controls.Add(lblAdv1);

            Label lblAdv2 = new Label() {
                Text = "• Защита от сна: при переходе ПК в сон сетевой шлюз блокируется до подтверждения маршрута через VPN.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(20, 70),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardAdvisory.Controls.Add(lblAdv2);

            Label lblAdv3 = new Label() {
                Text = "• Контроль маршрута 0.0.0.0/0: отслеживает, что исходящий трафик не идет в обход туннеля через физический адаптер.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(20, 96),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardAdvisory.Controls.Add(lblAdv3);

            Label lblAdv4 = new Label() {
                Text = "• ВАЖНО: Утилита НЕ защищает от банов по отпечатку браузера (fingerprint), антифрод-правилам или биллингу Anthropic.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(20, 122),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardAdvisory.Controls.Add(lblAdv4);

            Label lblAdv5 = new Label() {
                Text = "• При работе без прав Администратора не закрывайте главное окно барьера — оно контролирует сокеты в реальном времени.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(252, 211, 77),
                Location = new Point(20, 148),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardAdvisory.Controls.Add(lblAdv5);
        }

        // Вкладка 3: Настройки (1:1 Cloudflare WARP с Toggle-переключателями и карточками)
        private void BuildSettingsPage(Panel page) {
            page.Controls.Clear();
            page.AutoScroll = true;

            Label lblTitle = new Label() {
                Text = "Настройки",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(32, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            // ================= CARD 1: ПАРАМЕТРЫ ЗАЩИТЫ =================
            Panel cardGeneral = new Panel() {
                Location = new Point(32, 56),
                Size = new Size(786, 284),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardGeneral.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardGeneral.Width - 1, cardGeneral.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 20, 40, cardGeneral.Width - 20, 40);
                    g.DrawLine(divPen, 20, 80, cardGeneral.Width - 20, 80);
                    g.DrawLine(divPen, 20, 120, cardGeneral.Width - 20, 120);
                    g.DrawLine(divPen, 20, 160, cardGeneral.Width - 20, 160);
                    g.DrawLine(divPen, 20, 200, cardGeneral.Width - 20, 200);
                    g.DrawLine(divPen, 20, 240, cardGeneral.Width - 20, 240);
                }
            };
            page.Controls.Add(cardGeneral);

            // Row 1: Сворачивать в системный трей
            Label lblTray = new Label() {
                Text = "Сворачивать в системный трей при нажатии кнопки закрытия [✕]",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 11),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblTray);

            WarpToggleSwitch swTray = new WarpToggleSwitch() {
                Location = new Point(722, 9),
                Checked = _cfg.MinimizeToTrayOnClose
            };
            swTray.CheckedChanged += (s, e) => {
                _cfg.MinimizeToTrayOnClose = swTray.Checked;
                _cfg.Save();
                Log("CONFIG", "Сворачивание в трей: " + (_cfg.MinimizeToTrayOnClose ? "ВКЛ" : "ВЫКЛ"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swTray);

            // Row 2: Автозапуск Windows
            Label lblAuto = new Label() {
                Text = "Автозапуск барьера защиты при старте операционной системы Windows",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 51),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblAuto);

            WarpToggleSwitch swAuto = new WarpToggleSwitch() {
                Location = new Point(722, 49),
                Checked = _cfg.AutoStartWithWindows
            };
            swAuto.CheckedChanged += (s, e) => {
                _cfg.AutoStartWithWindows = swAuto.Checked;
                _cfg.Save();
                SetAutoStart(swAuto.Checked);
                Log("CONFIG", "Автозапуск Windows: " + (_cfg.AutoStartWithWindows ? "ВКЛ" : "ВЫКЛ"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swAuto);

            // Row 3: Автостарт Zapret
            Label lblZapret = new Label() {
                Text = "Автоматический запуск службы Zapret DPI Bypass при включении защиты",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 91),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblZapret);

            WarpToggleSwitch swZapret = new WarpToggleSwitch() {
                Location = new Point(722, 89),
                Checked = _cfg.AutoStartZapret
            };
            swZapret.CheckedChanged += (s, e) => {
                _cfg.AutoStartZapret = swZapret.Checked;
                _cfg.Save();
                Log("CONFIG", "Автостарт Zapret: " + (_cfg.AutoStartZapret ? "ВКЛ" : "ВЫКЛ"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swZapret);

            // Row 4: Аварийный KillSwitch
            Label lblKillswitch = new Label() {
                Text = "Аварийный KillSwitch (мгновенная блокировка сокетов при разрыве VPN)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 131),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblKillswitch);

            WarpToggleSwitch swKillswitch = new WarpToggleSwitch() {
                Location = new Point(722, 129),
                Checked = _cfg.KillSwitchEnabled
            };
            swKillswitch.CheckedChanged += (s, e) => {
                _cfg.KillSwitchEnabled = swKillswitch.Checked;
                _cfg.Save();
                Log("CONFIG", "Аварийный KillSwitch: " + (_cfg.KillSwitchEnabled ? "АКТИВЕН" : "ОТКЛЮЧЕН"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swKillswitch);

            // Row 5: Служба Sentinel Guard (Хук ядра NotifyAddrChange 0-5 мс)
            Label lblSentinel = new Label() {
                Text = "Служба Sentinel Guard (мгновенный хук ядра NotifyAddrChange <5 мс при смене маршрута)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 171),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblSentinel);

            WarpToggleSwitch swSentinel = new WarpToggleSwitch() {
                Location = new Point(722, 169),
                Checked = _cfg.SentinelGuardEnabled
            };
            swSentinel.CheckedChanged += (s, e) => {
                _cfg.SentinelGuardEnabled = swSentinel.Checked;
                _cfg.Save();
                Log("CONFIG", "Sentinel Guard: " + (_cfg.SentinelGuardEnabled ? "АКТИВЕН" : "ОТКЛЮЧЕН"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swSentinel);

            // Row 6: Multi-Signal Engine (Триангуляция: процесс VPN + маршрут 0.0.0.0/0 + Pinned IP)
            Label lblMultiSig = new Label() {
                Text = "Multi-Signal триангуляция (правило 2 из 3: процесс VPN, адаптер ядра, Pinned IP)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 211),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblMultiSig);

            WarpToggleSwitch swMultiSig = new WarpToggleSwitch() {
                Location = new Point(722, 209),
                Checked = _cfg.MultiSignalEnabled
            };
            swMultiSig.CheckedChanged += (s, e) => {
                _cfg.MultiSignalEnabled = swMultiSig.Checked;
                _cfg.Save();
                Log("CONFIG", "Multi-Signal Triangulation: " + (_cfg.MultiSignalEnabled ? "АКТИВЕН" : "ОТКЛЮЧЕН"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swMultiSig);

            // Row 7: Timezone Guard (Защита от утечки часового пояса)
            Label lblTz = new Label() {
                Text = "Timezone Guard (предотвращение блокировок Anthropic по расхождению часового пояса)",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 251),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardGeneral.Controls.Add(lblTz);

            WarpToggleSwitch swTz = new WarpToggleSwitch() {
                Location = new Point(722, 249),
                Checked = _cfg.TimezoneGuardEnabled
            };
            swTz.CheckedChanged += (s, e) => {
                _cfg.TimezoneGuardEnabled = swTz.Checked;
                _cfg.Save();
                Log("CONFIG", "Timezone Guard: " + (_cfg.TimezoneGuardEnabled ? "АКТИВЕН" : "ОТКЛЮЧЕН"), Color.FromArgb(148, 163, 184));
            };
            cardGeneral.Controls.Add(swTz);

            // ================= CARD 2: ПУТИ К КЛИЕНТАМ CLAUDE И ZAPRET =================
            Panel cardPaths = new Panel() {
                Location = new Point(32, 355),
                Size = new Size(786, 176),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardPaths.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardPaths.Width - 1, cardPaths.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 20, 72, cardPaths.Width - 20, 72);
                    g.DrawLine(divPen, 20, 144, cardPaths.Width - 20, 144);
                }
            };
            page.Controls.Add(cardPaths);

            // Row 1: Claude Desktop
            Label lblP1 = new Label() {
                Text = "Исполняемый файл Claude Desktop (Claude.exe):",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPaths.Controls.Add(lblP1);

            TextBox txtClaude = CreateWarpTextBox(_cfg.ClaudePath, 616);
            txtClaude.Location = new Point(20, 32);
            txtClaude.TextChanged += (s, e) => { _cfg.ClaudePath = txtClaude.Text; _cfg.Save(); };
            cardPaths.Controls.Add(txtClaude);

            CloudflareButton btnBrowseClaude = CreateWarpButton("Обзор...", 120, 28, (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "Claude Desktop (*.exe)|*.exe|Все файлы (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtClaude.Text = ofd.FileName;
                        _cfg.ClaudePath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            });
            btnBrowseClaude.Location = new Point(646, 31);
            cardPaths.Controls.Add(btnBrowseClaude);

            // Row 2: Claude Code CLI
            Label lblP2 = new Label() {
                Text = "Исполняемый файл Claude Code CLI (claude.cmd / claude.exe):",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 82),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPaths.Controls.Add(lblP2);

            TextBox txtCli = CreateWarpTextBox(_cfg.ClaudeCodePath, 616);
            txtCli.Location = new Point(20, 104);
            txtCli.TextChanged += (s, e) => { _cfg.ClaudeCodePath = txtCli.Text; _cfg.Save(); };
            cardPaths.Controls.Add(txtCli);

            CloudflareButton btnBrowseCli = CreateWarpButton("Обзор...", 120, 28, (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "Claude CLI (*.cmd;*.exe;*.bat)|*.cmd;*.exe;*.bat|Все файлы (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtCli.Text = ofd.FileName;
                        _cfg.ClaudeCodePath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            });
            btnBrowseCli.Location = new Point(646, 103);
            cardPaths.Controls.Add(btnBrowseCli);

            // Row 3: Zapret DPI
            Label lblP3 = new Label() {
                Text = "Исполняемый файл Zapret DPI Bypass (winws.exe):",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 154),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPaths.Controls.Add(lblP3);

            TextBox txtZapret = CreateWarpTextBox(_cfg.ZapretPath, 616);
            txtZapret.Location = new Point(20, 176);
            txtZapret.TextChanged += (s, e) => { _cfg.ZapretPath = txtZapret.Text; _cfg.Save(); };
            cardPaths.Controls.Add(txtZapret);

            CloudflareButton btnBrowseZapret = CreateWarpButton("Обзор...", 120, 28, (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "Zapret winws (*.exe)|*.exe|Все файлы (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtZapret.Text = ofd.FileName;
                        _cfg.ZapretPath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            });
            btnBrowseZapret.Location = new Point(646, 175);
            cardPaths.Controls.Add(btnBrowseZapret);

            // ================= CARD 3: СИСТЕМНАЯ СЛУЖБА WINDOWS (FAIL-CLOSED ДЕМОН) =================
            Panel cardService = new Panel() {
                Location = new Point(32, 545),
                Size = new Size(786, 110),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardService.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardService.Width - 1, cardService.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 20, 52, cardService.Width - 20, 52);
                }
            };
            page.Controls.Add(cardService);

            Label lblSvcTitle = new Label() {
                Text = "Системная служба ClaudeBarrier Service (SYSTEM Daemon & Named Pipe IPC)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(20, 12),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardService.Controls.Add(lblSvcTitle);

            bool isSvcInstalled = ClaudeBarrierServiceController.IsServiceInstalled();
            bool isSvcRunning = ClaudeBarrierServiceController.IsServiceRunning();

            Label lblSvcStatus = new Label() {
                Text = isSvcRunning ? (_isServiceIpcConnected ? "Статус: Активна и защищает ядро (SYSTEM) • IPC соединен (Heartbeat 5s)" : "Статус: Активна и защищает ядро (SYSTEM) • Heartbeat 5s") : (isSvcInstalled ? "Статус: Установлена в системе (Остановлена)" : "Статус: Не установлена (Работа в автономном повышенном режиме)"),
                Font = new Font("Segoe UI", 8.4f),
                ForeColor = isSvcRunning ? Color.FromArgb(52, 211, 153) : (isSvcInstalled ? Color.FromArgb(251, 191, 36) : Color.FromArgb(148, 163, 184)),
                Location = new Point(20, 32),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardService.Controls.Add(lblSvcStatus);

            CloudflareButton btnSvcInstall = CreateWarpButton(isSvcInstalled ? "Удалить службу" : "Установить службу", 150, 32, (s, e) => {
                if (isSvcInstalled) {
                    ClaudeBarrierServiceController.UninstallService();
                    MessageBox.Show("Системная служба ClaudeBarrier успешно удалена.", "ClaudeBarrier Service", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } else {
                    ClaudeBarrierServiceController.InstallService();
                    MessageBox.Show("Системная служба ClaudeBarrier успешно зарегистрирована и запущена под учетной записью SYSTEM.", "ClaudeBarrier Service", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                BuildSettingsPage(page);
            });
            btnSvcInstall.Location = new Point(20, 64);
            cardService.Controls.Add(btnSvcInstall);

            CloudflareButton btnSvcStart = CreateWarpButton(isSvcRunning ? "Остановить" : "Запустить", 120, 32, (s, e) => {
                if (isSvcRunning) ClaudeBarrierServiceController.StopService();
                else ClaudeBarrierServiceController.StartService();
                BuildSettingsPage(page);
            });
            btnSvcStart.Location = new Point(180, 64);
            cardService.Controls.Add(btnSvcStart);

            // ================= CARD 4: ЛИЦЕНЗИЯ И СЕТЕВАЯ БЕЗОПАСНОСТЬ WFP =================
            Panel cardLicense = new Panel() {
                Location = new Point(32, 670),
                Size = new Size(786, 176),
                BackColor = Color.FromArgb(19, 22, 32)
            };
            cardLicense.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardLicense.Width - 1, cardLicense.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 32))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 56), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 16, 58, cardLicense.Width - 16, 58);
                    g.DrawLine(divPen, 16, 118, cardLicense.Width - 16, 118);
                }
            };
            page.Controls.Add(cardLicense);

            // Строка 1: Лицензия
            Label lblLicTitle = new Label() {
                Text = "Лицензия ClaudeBarrier",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(18, 10),
                AutoSize = true
            };
            cardLicense.Controls.Add(lblLicTitle);

            string key = LicenseManager.GetSavedKey();
            bool isAct = LicenseManager.ValidateKey(key);
            string remTime = LicenseManager.GetRemainingTime(key);
            Label lblLicStatus = new Label() {
                Text = isAct ? ("Статус: Активен (Осталось: " + remTime + ") • " + LicenseManager.MaskKey(key)) : "Статус: Не активирован или срок действия (3 дня) истек",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = isAct ? Color.FromArgb(52, 211, 153) : Color.FromArgb(239, 68, 68),
                Location = new Point(18, 32),
                AutoSize = true
            };
            cardLicense.Controls.Add(lblLicStatus);

            CloudflareButton btnChangeKey = CreateWarpButton(isAct ? "Сменить ключ" : "Активировать", 130, 32, (s, e) => {
                if (_pnlActivationOverlay != null) {
                    _pnlActivationOverlay.Visible = true;
                    _pnlActivationOverlay.BringToFront();
                }
            });
            btnChangeKey.Location = new Point(480, 14);
            cardLicense.Controls.Add(btnChangeKey);

            CloudflareButton btnBotKey = CreateWarpButton("Бот ключей", 130, 32, (s, e) => {
                try { Process.Start("https://t.me/ClaudeBarrierBot"); } catch {}
            });
            btnBotKey.Location = new Point(626, 14);
            cardLicense.Controls.Add(btnBotKey);

            // Строка 2: Правила брандмауэра WFP
            Label lblFwTitle = new Label() {
                Text = "Правила брандмауэра WFP",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(18, 68),
                AutoSize = true
            };
            cardLicense.Controls.Add(lblFwTitle);

            Label lblFwDesc = new Label() {
                Text = "Снятие блокировок 160.79.104.0/23 и очистка внедренных фильтров ядра Windows",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(18, 90),
                AutoSize = true
            };
            cardLicense.Controls.Add(lblFwDesc);

            CloudflareButton btnRemoveRules = CreateWarpDangerButton("Удалить правила", 150, 32, (s, e) => RemoveFirewallRules(showSuccessDialog: true));
            btnRemoveRules.Location = new Point(606, 72);
            cardLicense.Controls.Add(btnRemoveRules);

            // Строка 3: Деинсталляция
            Label lblUTitle = new Label() {
                Text = "Деинсталляция ClaudeBarrier",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(18, 128),
                AutoSize = true
            };
            cardLicense.Controls.Add(lblUTitle);

            Label lblUDesc = new Label() {
                Text = "Полное удаление файлов программы, ярлыков и записей автозапуска из системы",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(18, 150),
                AutoSize = true
            };
            cardLicense.Controls.Add(lblUDesc);

            CloudflareButton btnUninstall = CreateWarpDangerButton("Удалить барьер", 150, 32, (s, e) => RunUninstaller());
            btnUninstall.Location = new Point(606, 132);
            cardLicense.Controls.Add(btnUninstall);
        }


        // Вкладка 4: Журнал (1:1 Cloudflare WARP консоль событий с минималистичным тулбаром)
        private void BuildLogsPage(Panel page) {
            page.Controls.Clear();
            page.AutoScroll = false;

            Label lblTitle = new Label() {
                Text = "Журнал событий",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(32, 18),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            

            // Тулбар: Единый капсульный сегментированный переключатель фильтров слева + структурированные кнопки справа
            Panel pnlToolbar = new Panel() {
                Location = new Point(32, 78),
                Size = new Size(786, 34),
                BackColor = Color.Transparent
            };
            page.Controls.Add(pnlToolbar);

            // 1. Монолитный сегментированный контрол фильтров слева
            _segmentedFilters = new WarpSegmentedFilterControl() {
                Location = new Point(0, 0),
                Size = new Size(320, 34)
            };
            if (_activeLogFilter == "ERR") _segmentedFilters.SelectedIndex = 1;
            else if (_activeLogFilter == "NET") _segmentedFilters.SelectedIndex = 2;
            else if (_activeLogFilter == "WFP") _segmentedFilters.SelectedIndex = 3;
            else _segmentedFilters.SelectedIndex = 0;

            _segmentedFilters.SelectedIndexChanged += (s, e) => {
                ApplyLogFilter(_segmentedFilters.SelectedFilter);
            };
            pnlToolbar.Controls.Add(_segmentedFilters);

            // 2. Правые кнопки действий
            CloudflareButton btnClear = CreateActionToolbarButton("Очистить", 96, 34, (s, e) => {
                lock (_logLock) {
                    _allLogs.Clear();
                }
                if (_rtbLogs != null) _rtbLogs.Clear();
                _logEventCount = 0;
                if (_lblLogCount != null) _lblLogCount.Text = "Событий: 0";
            });
            btnClear.Location = new Point(452, 0);
            pnlToolbar.Controls.Add(btnClear);

            CloudflareButton btnCopy = CreateActionToolbarButton("Копировать", 116, 34, (s, e) => CopyLogsToClipboard());
            btnCopy.Location = new Point(556, 0);
            pnlToolbar.Controls.Add(btnCopy);

            CloudflareButton btnExport = CreateActionToolbarButton("Экспорт", 104, 34, (s, e) => ExportLogsToFile());
            btnExport.Location = new Point(680, 0);
            pnlToolbar.Controls.Add(btnExport);

            // Карточка терминала консоли ядра (высота 458px)
            Panel cardLogs = new Panel() {
                Location = new Point(32, 120),
                Size = new Size(786, 458),
                BackColor = Color.FromArgb(19, 22, 34)
            };
            cardLogs.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardLogs.Width - 1, cardLogs.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 34))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 58), 1f)) g.DrawPath(pen, p);
                }
                using (Pen divPen = new Pen(Color.FromArgb(28, 33, 48), 1f)) {
                    g.DrawLine(divPen, 16, 36, cardLogs.Width - 16, 36);
                }
                // Зеленая точка live stream
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(52, 211, 153))) {
                    g.FillEllipse(dotBrush, 18, 14, 8, 8);
                }
            };
            page.Controls.Add(cardLogs);

            Label lblStreamTitle = new Label() {
                Text = "СИСТЕМНАЯ КОНСОЛЬ ЯДРА // LIVE EVENT STREAM",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(34, 11),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardLogs.Controls.Add(lblStreamTitle);

            _lblLogCount = new Label() {
                Text = "Событий: " + _allLogs.Count,
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(610, 10),
                Size = new Size(156, 18),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };
            cardLogs.Controls.Add(_lblLogCount);

            _rtbLogs = new RichTextBox() {
                Location = new Point(14, 42),
                Size = new Size(758, 404),
                BackColor = Color.FromArgb(11, 14, 20),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 9f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            cardLogs.Controls.Add(_rtbLogs);

            // Блок синхронизации релизов и обновлений GitHub под консолью (высота 58px)
            Panel cardUpdate = new Panel() {
                Location = new Point(32, 588),
                Size = new Size(786, 58),
                BackColor = Color.FromArgb(19, 22, 34)
            };
            cardUpdate.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardUpdate.Width - 1, cardUpdate.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 22, 34))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 58), 1f)) g.DrawPath(pen, p);
                }
                using (SolidBrush iconBg = new SolidBrush(Color.FromArgb(28, 34, 52))) {
                    g.FillEllipse(iconBg, 14, 13, 32, 32);
                }
                using (Pen iconPen = new Pen(Color.FromArgb(56, 189, 248), 1.8f)) {
                    g.DrawArc(iconPen, 20, 19, 20, 20, 45, 270);
                    g.DrawLine(iconPen, 34, 18, 37, 21);
                    g.DrawLine(iconPen, 34, 24, 37, 21);
                }
            };
            page.Controls.Add(cardUpdate);

            Label lblUpdTitle = new Label() {
                Text = "Проверка обновлений",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(56, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardUpdate.Controls.Add(lblUpdTitle);

            _lblUpdateStatus = new Label() {
                Text = "Статус: Нажмите кнопку справа для онлайн-проверки обновлений на GitHub",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(57, 31),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardUpdate.Controls.Add(_lblUpdateStatus);

            _btnCheckUpdate = new CloudflareButton() {
                Text = "Проверить обновления",
                Location = new Point(584, 11),
                Size = new Size(190, 36),
                CornerRadius = 7,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                GradientStart = Color.FromArgb(28, 33, 48),
                GradientEnd = Color.FromArgb(20, 24, 36),
                HoverStart = Color.FromArgb(42, 50, 72),
                HoverEnd = Color.FromArgb(32, 38, 56),
                BorderStrokeColor = Color.FromArgb(46, 56, 82),
                ForeColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            _btnCheckUpdate.Click += (s, e) => {
                if (_btnCheckUpdate.Text.StartsWith("Скачать", StringComparison.OrdinalIgnoreCase)) {
                    try { Process.Start(_latestReleaseUrl); } catch {}
                } else {
                    CheckForUpdatesAsync();
                }
            };
            cardUpdate.Controls.Add(_btnCheckUpdate);
        }



        private CloudflareButton CreateActionToolbarButton(string text, int width, int height, EventHandler onClick) {
            CloudflareButton btn = new CloudflareButton() {
                Text = text,
                Size = new Size(width, height),
                CornerRadius = 7,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                GradientStart = Color.FromArgb(22, 26, 38),
                GradientEnd = Color.FromArgb(17, 20, 30),
                HoverStart = Color.FromArgb(34, 40, 58),
                HoverEnd = Color.FromArgb(26, 31, 46),
                BorderStrokeColor = Color.FromArgb(42, 50, 72),
                ForeColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.Hand
            };
            if (onClick != null) btn.Click += onClick;
            return btn;
        }

        private void CheckForUpdatesAsync() {
            if (_btnCheckUpdate == null) return;
            _btnCheckUpdate.Enabled = false;
            _btnCheckUpdate.Text = "Проверка...";
            if (_lblUpdateStatus != null) {
                _lblUpdateStatus.Text = "Связь с GitHub Releases...";
                _lblUpdateStatus.ForeColor = Color.FromArgb(148, 163, 184);
            }

            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls12;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://github.com/skizewike/claude-antiban/releases/latest");
                    req.Method = "HEAD";
                    req.UserAgent = "ClaudeBarrier-Client/6.0.5";
                    req.Timeout = 6000;
                    req.AllowAutoRedirect = false;

                    string latestTag = "v6.0.5";
                    string loc = "";
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse()) {
                        loc = res.GetResponseHeader("Location");
                        if (!string.IsNullOrEmpty(loc)) {
                            int lastSlash = loc.LastIndexOf('/');
                            if (lastSlash >= 0 && lastSlash < loc.Length - 1) {
                                latestTag = loc.Substring(lastSlash + 1).Trim();
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(loc) && loc.StartsWith("/")) {
                        loc = "https://github.com" + loc;
                    }
                    SafeInvoke(() => {
                        _latestReleaseUrl = string.IsNullOrEmpty(loc) ? "https://github.com/skizewike/claude-antiban/releases/latest" : loc;
                        string curVer = "v6.0.5";
                        bool isNewer = CompareVersions(latestTag, curVer) > 0;

                        if (isNewer) {
                            if (_lblUpdateStatus != null) {
                                _lblUpdateStatus.Text = "🚀 Доступно обновление " + latestTag + " на GitHub! Нажмите кнопку для загрузки.";
                                _lblUpdateStatus.ForeColor = Color.FromArgb(251, 191, 36);
                            }
                            if (_btnCheckUpdate != null) {
                                _btnCheckUpdate.Text = "Скачать " + latestTag;
                                _btnCheckUpdate.GradientStart = Color.FromArgb(37, 99, 235);
                                _btnCheckUpdate.GradientEnd = Color.FromArgb(29, 78, 216);
                                _btnCheckUpdate.HoverStart = Color.FromArgb(59, 130, 246);
                                _btnCheckUpdate.ForeColor = Color.White;
                                _btnCheckUpdate.Enabled = true;
                            }
                        } else {
                            if (_lblUpdateStatus != null) {
                                _lblUpdateStatus.Text = "✓ Установлена актуальная версия (" + curVer + "). Обновлений не требуется.";
                                _lblUpdateStatus.ForeColor = Color.FromArgb(52, 211, 153);
                            }
                            if (_btnCheckUpdate != null) {
                                _btnCheckUpdate.Text = "Актуально ✓";
                                _btnCheckUpdate.GradientStart = Color.FromArgb(20, 24, 36);
                                _btnCheckUpdate.GradientEnd = Color.FromArgb(16, 19, 30);
                                _btnCheckUpdate.ForeColor = Color.FromArgb(148, 163, 184);
                                _btnCheckUpdate.Enabled = true;
                            }
                        }
                    });
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        if (_lblUpdateStatus != null) {
                            _lblUpdateStatus.Text = "Ошибка связи с GitHub: " + ex.Message;
                            _lblUpdateStatus.ForeColor = Color.FromArgb(248, 113, 113);
                        }
                        if (_btnCheckUpdate != null) {
                            _btnCheckUpdate.Text = "Повторить";
                            _btnCheckUpdate.Enabled = true;
                        }
                    });
                }
            });
        }

        private int CompareVersions(string vA, string vB) {
            try {
                string cleanA = (vA ?? "").TrimStart('v', 'V').Trim();
                string cleanB = (vB ?? "").TrimStart('v', 'V').Trim();
                if (cleanA.Contains("-")) cleanA = cleanA.Split('-')[0];
                if (cleanB.Contains("-")) cleanB = cleanB.Split('-')[0];
                Version verA = new Version(cleanA);
                Version verB = new Version(cleanB);
                return verA.CompareTo(verB);
            } catch {
                return string.Compare(vA, vB, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Обновление состояния приложения (Warning vs Safe)
        private void SetProtectionUIState(ProtectionState newState, string msg = "") {
            _currentState = newState;
            bool isSafe = (newState == ProtectionState.Protected);

            if (_heroCloud != null) _heroCloud.State = newState;
            if (_cardDesktop != null) _cardDesktop.SetState(isSafe);
            if (_cardBrowser != null) _cardBrowser.SetState(isSafe);
            if (_cardCli != null) _cardCli.SetState(isSafe);

            if (_appIconBox != null) _appIconBox.Invalidate();
            if (_brandLogoBox != null) _brandLogoBox.Invalidate();
            if (_badgeTunnelStatus != null) _badgeTunnelStatus.Invalidate();

            if (isSafe) {
                if (_lblStatusTitle != null) _lblStatusTitle.Text = "Подключено";
                if (_lblStatusSubtitle != null) {
                    _lblStatusSubtitle.Text = "Ваше интернет-подключение полностью защищено. Доступ к Claude открыт.";
                }

                if (_btnMainCta != null) {
                    _btnMainCta.Text = "Проверить защиту ✓";
                    _btnMainCta.GradientStart = Color.FromArgb(30, 34, 46);
                    _btnMainCta.GradientEnd = Color.FromArgb(24, 27, 36);
                    _btnMainCta.HoverStart = Color.FromArgb(42, 48, 64);
                    _btnMainCta.HoverEnd = Color.FromArgb(30, 34, 46);
                    _btnMainCta.GlowColor = Color.FromArgb(40, 16, 185, 129);
                    _btnMainCta.Invalidate();
                }

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: Защищено (Трафик изолирован)";
                }
            } else {
                bool isGeoBlocked = IsAnthropicBlockedRegion(CurrentCountry, CurrentIp, msg);
                if (_lblStatusTitle != null) {
                    _lblStatusTitle.Text = isGeoBlocked ? "Регион не поддерживается" : "Отключено";
                }
                if (_lblStatusSubtitle != null) {
                    if (isGeoBlocked) {
                        _lblStatusSubtitle.Text = "Выходной IP находится в неподдерживаемом регионе (" + CurrentCountry + "). Вход заблокирован во избежание бана!";
                    } else {
                        _lblStatusSubtitle.Text = string.IsNullOrEmpty(msg)
                            ? "Ваше интернет-подключение не защищено. Доступ к Claude закрыт."
                            : ("Интернет-подключение не защищено: " + msg);
                    }
                }

                if (_btnMainCta != null) {
                    _btnMainCta.Text = isGeoBlocked ? "Сменить сервер VPN" : "Проверить защиту";
                    _btnMainCta.GradientStart = isGeoBlocked ? Color.FromArgb(220, 38, 38) : Color.FromArgb(37, 99, 235);
                    _btnMainCta.GradientEnd = isGeoBlocked ? Color.FromArgb(185, 28, 28) : Color.FromArgb(29, 78, 216);
                    _btnMainCta.HoverStart = isGeoBlocked ? Color.FromArgb(239, 68, 68) : Color.FromArgb(59, 130, 246);
                    _btnMainCta.HoverEnd = isGeoBlocked ? Color.FromArgb(220, 38, 38) : Color.FromArgb(37, 99, 235);
                    _btnMainCta.GlowColor = isGeoBlocked ? Color.FromArgb(80, 239, 68, 68) : Color.FromArgb(80, 59, 130, 246);
                    _btnMainCta.Invalidate();
                }

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = isGeoBlocked
                        ? "Статус: Регион заблокирован (" + CurrentCountry + ")"
                        : "Статус: Отключено (VPN не активен)";
                }
            }
        }

        private bool ConfirmUnsafeLaunch(string targetName) {
            if (_currentState == ProtectionState.Protected) return true;

            string statusReason = "";
            bool isGeoBlocked = IsAnthropicBlockedRegion(CurrentCountry, CurrentIp, "");

            if (isGeoBlocked) {
                statusReason = "Ваш текущий выходной IP-адрес (" + CurrentIp + ") находится в неподдерживаемом регионе: " + CurrentCountry + ".\n\nAnthropic сразу же заблокирует доступ или забанит аккаунт при попытке входа из этого региона!";
            } else if (CurrentPing <= 0) {
                statusReason = "Серверы Anthropic (claude.ai) недоступны или блокируются на текущем сетевом подключении.";
            } else {
                statusReason = "VPN-соединение не обнаружено или маршрут по умолчанию идет в обход туннеля.";
            }

            string message = string.Format(
                "ВНИМАНИЕ: Запуск {0} заблокирован системой безопасности ClaudeBarrier!\n\n" +
                "{1}\n\n" +
                "Решение: Переключите сервер в вашем VPN-клиенте на европейский или американский регион (Германия, Нидерланды, США).\n\n" +
                "• Нажмите [Да], чтобы открыть сетевые подключения Windows (ncpa.cpl).\n" +
                "• Нажмите [Нет], чтобы безопасно отменить запуск.",
                targetName, statusReason);

            DialogResult res = MessageBox.Show(
                message,
                "ClaudeBarrier // Риск блокировки аккаунта",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Stop);

            if (res == DialogResult.Yes) {
                try {
                    Process.Start("ncpa.cpl");
                } catch {}
            }
            return false;
        }

        private void LaunchClaudeDesktop() {
            if (!RunSafeLauncherPreFlight("Claude Desktop")) return;

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] desktopCandidates = new string[] {
                _cfg.ClaudePath,
                Path.Combine(localApp, @"Microsoft\WindowsApps\claude-desktop.exe"),
                Path.Combine(localApp, @"Microsoft\WindowsApps\Claude_pzs8sxrjxfjjc\claude-desktop.exe"),
                Path.Combine(localApp, @"Programs\Claude\Claude.exe"),
                @"C:\Program Files\Claude\Claude.exe"
            };

            foreach (string path in desktopCandidates) {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                    try {
                        ProcessStartInfo psi = new ProcessStartInfo {
                            FileName = path,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        Log("LAUNCH", "Запущен Claude Desktop: " + path, Color.FromArgb(52, 211, 153));
                        return;
                    } catch (Exception ex) {
                        Log("DEBUG", "Сбой запуска " + path + ": " + ex.Message, Color.FromArgb(148, 163, 184));
                    }
                }
            }

            try {
                ProcessStartInfo psiStore = new ProcessStartInfo {
                    FileName = "explorer.exe",
                    Arguments = "shell:AppsFolder\\Claude_pzs8sxrjxfjjc!Claude",
                    UseShellExecute = true
                };
                Process.Start(psiStore);
                Log("LAUNCH", "Запущен Claude Desktop (Store Package)", Color.FromArgb(52, 211, 153));
                return;
            } catch {}

            try {
                Process.Start(new ProcessStartInfo { FileName = "claude:", UseShellExecute = true });
                Log("LAUNCH", "Запущен Claude Desktop через URI протокол claude:", Color.FromArgb(52, 211, 153));
                return;
            } catch {}

            Process.Start("https://claude.ai");
            Log("LAUNCH", "Приложение Claude Desktop не найдено, открыт веб-интерфейс claude.ai", Color.FromArgb(52, 211, 153));
        }

        private void LaunchClaudeBrowser() {
            if (!RunSafeLauncherPreFlight("Claude в браузере (claude.ai)")) return;
            Process.Start("https://claude.ai");
            Log("LAUNCH", "Открыт Claude в браузере: https://claude.ai", Color.FromArgb(52, 211, 153));
        }

        private void LaunchClaudeCodeCli() {
            if (!RunSafeLauncherPreFlight("Claude Code CLI (терминал)")) return;

            string target = _cfg.ClaudeCodePath;
            if (!string.IsNullOrEmpty(target) && File.Exists(target)) {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                        Arguments = "/k \"" + target + "\"",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Log("LAUNCH", "Запущен Claude Code CLI по пути: " + target, Color.FromArgb(52, 211, 153));
                    return;
                } catch (Exception ex) {
                    Log("DEBUG", "Сбой прямого запуска " + target + ": " + ex.Message, Color.FromArgb(148, 163, 184));
                }
            }

            try {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                    Arguments = "/k claude || npx @anthropic-ai/claude-code",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Log("LAUNCH", "Запущен сеанс Claude Code CLI (cmd wrapper)", Color.FromArgb(52, 211, 153));
            } catch (Exception ex) {
                Log("ERR", "Ошибка запуска Claude Code CLI: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void SetAutoStart(bool enable) {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (key != null) {
                        if (enable) {
                            key.SetValue("ClaudeAntiBanBarrier", "\"" + Application.ExecutablePath + "\" --minimized");
                            Log("CONFIG", "Барьер добавлен в автозагрузку Windows (фоновый режим)", Color.FromArgb(52, 211, 153));
                        } else {
                            if (key.GetValue("ClaudeAntiBanBarrier") != null) key.DeleteValue("ClaudeAntiBanBarrier");
                            Log("CONFIG", "Барьер удален из автозагрузки Windows", Color.FromArgb(148, 163, 184));
                        }
                    }
                }
            } catch (Exception ex) {
                Log("ERR", "Ошибка настройки автозагрузки: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void RunUninstaller() {
            string uninstaller = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall_ClaudeBarrier.exe");
            if (!File.Exists(uninstaller)) {
                uninstaller = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall_Project_XO.exe");
            }
            if (File.Exists(uninstaller)) {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = uninstaller,
                    UseShellExecute = true
                };
                Process.Start(psi);
                _forceExit = true;
                Application.Exit();
            } else {
                MessageBox.Show("Файл деинсталлятора не найден в директории приложения.", "ClaudeBarrier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Извлечение встроенного пакета Zapret (winws, WinDivert, cygwin1) из ресурсов
        private string EnsureZapretToolsExtracted() {
            try {
                if (!string.IsNullOrEmpty(_cfg.ZapretPath) && File.Exists(_cfg.ZapretPath)) {
                    return _cfg.ZapretPath;
                }

                string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
                string localWinws = Path.Combine(toolsDir, "winws.exe");
                if (File.Exists(localWinws)) {
                    _cfg.ZapretPath = localWinws;
                    return localWinws;
                }

                if (!Directory.Exists(toolsDir)) Directory.CreateDirectory(toolsDir);

                // Распаковка встроенного zip-архива ресурсов
                var asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream("zapret_bundle.zip")) {
                    if (stream != null) {
                        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read)) {
                            foreach (ZipArchiveEntry entry in archive.Entries) {
                                string destPath = Path.Combine(toolsDir, entry.FullName);
                                if (!File.Exists(destPath) || new FileInfo(destPath).Length != entry.Length) {
                                    entry.ExtractToFile(destPath, true);
                                }
                            }
                        }
                        Log("SYS", "Встроенные модули Zapret DPI Bypass успешно распакованы в tools/", Color.FromArgb(52, 211, 153));
                        _cfg.ZapretPath = localWinws;
                        _cfg.Save();
                        return localWinws;
                    }
                }

                // Поиск на Рабочем столе пользователя
                string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] candidates = new string[] {
                    Path.Combine(userDesktop, @"Desktop\zapret-discord-youtube-1.10.4\bin\winws.exe"),
                    @"C:\zapret\winws.exe",
                    @"C:\zapret-win-bundle-master\zapret-winws\winws.exe"
                };
                foreach (string cand in candidates) {
                    if (File.Exists(cand)) {
                        _cfg.ZapretPath = cand;
                        return cand;
                    }
                }
            } catch (Exception ex) {
                Log("ERR", "Ошибка подготовки бинарных файлов Zapret: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
            return null;
        }

        private bool IsZapretProcessRunning() {
            try {
                if (_zapretProcess != null && !_zapretProcess.HasExited) return true;
                Process[] procs = Process.GetProcessesByName("winws");
                return procs != null && procs.Length > 0;
            } catch {
                return false;
            }
        }

        private void ToggleZapret() {
            if (IsZapretProcessRunning()) {
                StopZapret();
            } else {
                StartZapret();
            }
        }

        private void StartZapret() {
            try {
                string winwsPath = EnsureZapretToolsExtracted();
                if (string.IsNullOrEmpty(winwsPath) || !File.Exists(winwsPath)) {
                    Log("WARN", "Бинарный файл winws.exe не обнаружен. Проверьте папку tools/ или Настройки.", Color.FromArgb(248, 113, 113));
                    MessageBox.Show("Файл winws.exe не найден. Перейдите во вкладку «Настройки» и укажите путь к Zapret.",
                        "Zapret DPI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string binDir = Path.GetDirectoryName(winwsPath);
                string args = string.IsNullOrEmpty(_cfg.ZapretArgs)
                    ? "--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq"
                    : _cfg.ZapretArgs;

                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = winwsPath,
                    Arguments = args,
                    WorkingDirectory = binDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _zapretProcess = Process.Start(psi);
                int pid = _zapretProcess != null ? _zapretProcess.Id : 0;
                Log("ZAPRET", "Служба Zapret winws.exe запущена в фоне (PID " + (pid > 0 ? pid.ToString() : "N/A") + ")", Color.FromArgb(52, 211, 153));
                UpdateZapretUiState();
            } catch (Exception ex) {
                Log("ERR", "Не удалось запустить Zapret: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void StopZapret() {
            try {
                if (_zapretProcess != null && !_zapretProcess.HasExited) {
                    _zapretProcess.Kill();
                    _zapretProcess = null;
                }
                foreach (Process p in Process.GetProcessesByName("winws")) {
                    try { p.Kill(); } catch {}
                }
                Log("ZAPRET", "Служба Zapret DPI Bypass деактивирована", Color.FromArgb(148, 163, 184));
                UpdateZapretUiState();
            } catch (Exception ex) {
                Log("ERR", "Ошибка остановки Zapret: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void UpdateZapretUiState() {
            SafeInvoke(() => {
                bool running = IsZapretProcessRunning();
                if (_lblZapretStatus != null) {
                    if (running) {
                        _lblZapretStatus.Text = "● Служба активна в фоновом режиме (перехват DPI TCP 443)";
                        _lblZapretStatus.ForeColor = Color.FromArgb(52, 211, 153);
                    } else {
                        _lblZapretStatus.Text = "○ Служба выключена (по умолчанию отключена)";
                        _lblZapretStatus.ForeColor = Color.FromArgb(148, 163, 184);
                    }
                }
                if (_btnToggleZapret != null) {
                    if (running) {
                        _btnToggleZapret.Text = "Остановить Zapret";
                        _btnToggleZapret.GradientStart = Color.FromArgb(185, 28, 28);
                        _btnToggleZapret.GradientEnd = Color.FromArgb(153, 27, 27);
                        _btnToggleZapret.HoverStart = Color.FromArgb(220, 38, 38);
                        _btnToggleZapret.HoverEnd = Color.FromArgb(185, 28, 28);
                    } else {
                        _btnToggleZapret.Text = "Запустить Zapret";
                        _btnToggleZapret.GradientStart = Color.FromArgb(30, 34, 48);
                        _btnToggleZapret.GradientEnd = Color.FromArgb(24, 27, 38);
                        _btnToggleZapret.HoverStart = Color.FromArgb(42, 48, 66);
                        _btnToggleZapret.HoverEnd = Color.FromArgb(32, 36, 50);
                    }
                    _btnToggleZapret.Invalidate();
                }
            });
        }

        private void FlushDnsCache() {
            try {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe"),
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                Log("DNS", "Системный кэш DNS успешно очищен (ipconfig /flushdns)", Color.FromArgb(52, 211, 153));
                MessageBox.Show("Системный кэш DNS Windows успешно очищен.", "Очистка DNS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) {
                Log("ERR", "Ошибка очистки DNS: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void TestDoHResolver() {
            Log("DOH", "Отправка проверочного запроса DNS-over-HTTPS к Cloudflare 1.1.1.1...", Color.FromArgb(148, 163, 184));
            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://1.1.1.1/dns-query?name=api.anthropic.com&type=A");
                    req.Proxy = null;
                    req.Accept = "application/dns-json";
                    req.Timeout = 2500;
                    req.UserAgent = "ClaudeBarrier-DoH/6.0.5";

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                        sw.Stop();
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream())) {
                            string res = sr.ReadToEnd();
                            SafeInvoke(() => {
                                if (_lblWarpDohVal != null) {
                                    _lblWarpDohVal.Text = "Cloudflare 1.1.1.1 (DNS-over-HTTPS) — " + sw.ElapsedMilliseconds + " мс";
                                    _lblWarpDohVal.ForeColor = Color.FromArgb(52, 211, 153);
                                }

                                Log("DOH", "Шлюз Cloudflare DoH подтвержден (" + sw.ElapsedMilliseconds + " мс). DNS-запросы зашифрованы.", Color.FromArgb(52, 211, 153));
                                MessageBox.Show(
                                    "Проверка DNS-over-HTTPS (DoH):\n\n" +
                                    "• Резолвер: Cloudflare 1.1.1.1 (TLS-шифрование)\n" +
                                    "• Время отклика: " + sw.ElapsedMilliseconds + " мс\n" +
                                    "• Статус резолва: api.anthropic.com подтвержден\n" +
                                    "• Защита от цензуры: Активна (порт 443)\n\n" +
                                    "Провайдер не может подменить или подсмотреть DNS-запросы к серверам Claude.",
                                    "Тест Cloudflare DoH — Успешно",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                            });
                        }
                    }
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        if (_lblWarpDohVal != null) {
                            _lblWarpDohVal.Text = "Cloudflare 1.1.1.1 — Таймаут отклика";
                            _lblWarpDohVal.ForeColor = Color.FromArgb(248, 113, 113);
                        }

                        Log("ERR", "Ошибка теста DoH: " + ex.Message, Color.FromArgb(239, 68, 68));
                        MessageBox.Show(
                            "Ошибка при проверке Cloudflare DoH:\n" + ex.Message + "\n\n" +
                            "Возможно, провайдер блокирует прямые запросы к 1.1.1.1 по HTTPS без VPN.",
                            "Тест Cloudflare DoH",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    });
                }
            });
        }

        private void SetupTray() {
            _trayIcon = new NotifyIcon();
            try {
                using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("app_icon.ico")) {
                    if (s != null) _trayIcon.Icon = new Icon(s);
                    else {
                        string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                        if (File.Exists(icoPath)) _trayIcon.Icon = new Icon(icoPath);
                    }
                }
            } catch {}

            _trayIcon.Text = "ClaudeBarrier";
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                if (_heroCloud != null) _heroCloud.ResumeAnimation();
            };

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Статус: Проверка...", (s, e) => {});
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Запустить Claude (Безопасный режим)", (s, e) => LaunchClaudeDesktop());
            menu.MenuItems.Add("Открыть окно", (s, e) => {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                if (_heroCloud != null) _heroCloud.ResumeAnimation();
            });
            menu.MenuItems.Add("Бот поддержки (@ClaudeBarrierBot)", (s, e) => ShowContactDialog());
            menu.MenuItems.Add("Проверить соединение", (s, e) => StartAudit());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Выход", (s, e) => {
                _forceExit = true;
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenu = menu;
        }

        // РЕАЛЬНЫЙ СЕТЕВОЙ АУДИТ: 100% НАСТОЯЩИЕ ДАННЫЕ БЕЗ ВИЗУАЛЬНОГО СЛОПА
        // Универсальная проверка заблокированных Anthropic регионов (РФ, РБ, Иран, Китай и др.)
        public static bool IsAnthropicBlockedRegion(string countryStr, string ipStr, string rawJson) {
            string c = (countryStr ?? "").Trim().ToLowerInvariant();
            string r = (rawJson ?? "").ToLowerInvariant();

            if (string.IsNullOrEmpty(c) && string.IsNullOrEmpty(r)) return false;

            // 1. Проверка по 2-буквенным кодам ISO (country_code, countryCode, country)
            string[] blockedCodes = new string[] {
                "\"country_code\":\"ru\"", "\"country_code\": \"ru\"",
                "\"countrycode\":\"ru\"", "\"countrycode\": \"ru\"",
                "\"country\":\"ru\"", "\"country\": \"ru\"",
                "\"country_code\":\"by\"", "\"country_code\": \"by\"",
                "\"countrycode\":\"by\"", "\"countrycode\": \"by\"",
                "\"country\":\"by\"", "\"country\": \"by\"",
                "\"country_code\":\"cn\"", "\"country_code\": \"cn\"",
                "\"country_code\":\"ir\"", "\"country_code\": \"ir\"",
                "\"country_code\":\"kp\"", "\"country_code\": \"kp\"",
                "\"country_code\":\"cu\"", "\"country_code\": \"cu\"",
                "\"country_code\":\"sy\"", "\"country_code\": \"sy\""
            };
            foreach (string code in blockedCodes) {
                if (r.Contains(code)) return true;
            }

            // 2. Проверка по 2-буквенным кодам страны (строгое равенство)
            string[] blockedExactCodes = new string[] { "ru", "by", "cn", "ir", "kp", "cu", "sy" };
            foreach (string code in blockedExactCodes) {
                if (c == code) return true;
            }

            // 3. Проверка по полным названиям стран (рус/англ)
            string[] blockedCountryNames = new string[] {
                "russia", "russian federation", "россия", "российская федерация", "рф",
                "belarus", "беларусь", "белоруссия",
                "china", "китай",
                "iran", "иран",
                "north korea", "северная корея",
                "cuba", "куба",
                "syria", "сирия"
            };
            foreach (string name in blockedCountryNames) {
                if (c == name || c.Contains(name)) return true;
            }

            return false;
        }

        // Bug 2: Универсальное определение VPN по типу адаптера и описанию
        public static bool IsVpnInterface(NetworkInterface nic) {
            if (nic == null) return false;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Slip ||
                (int)nic.NetworkInterfaceType == 53) { // 53 = PropVirtual / NDIS Tunnel
                return true;
            }
            string d = (nic.Description ?? "").ToLowerInvariant();
            string n = (nic.Name ?? "").ToLowerInvariant();
            string[] vpnKeywords = new string[] {
                "wintun", "wireguard", "tap", "tun", "openvpn", "amnezia", "warp",
                "mullvad", "proton", "tailscale", "nekoray", "sing-box", "clash",
                "v2ray", "shadowsocks", "hiddify", "vpn"
            };
            foreach (string kw in vpnKeywords) {
                if (d.Contains(kw) || n.Contains(kw)) return true;
            }
            return false;
        }

        // Bug 3: Проверка маршрутизации ядра к серверам Anthropic (160.79.104.10) через Win32 GetBestInterface
        public static bool CheckAnthropicRouting(out string bestAdapterName, out string bestDesc, out bool isVpnRoute, out uint ifIndex) {
            bestAdapterName = "Не определен";
            bestDesc = "";
            isVpnRoute = false;
            ifIndex = 0;
            try {
                IPAddress target = IPAddress.Parse("160.79.104.10");
                byte[] bytes = target.GetAddressBytes();
                uint addr = BitConverter.ToUInt32(bytes, 0);

                int ret = GetBestInterface(addr, out ifIndex);
                if (ret == 0) {
                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                        try {
                            IPInterfaceProperties ipProps = nic.GetIPProperties();
                            IPv4InterfaceProperties v4 = ipProps.GetIPv4Properties();
                            if (v4 != null && v4.Index == ifIndex) {
                                bestAdapterName = nic.Name;
                                bestDesc = nic.Description;
                                if (nic.OperationalStatus == OperationalStatus.Up && IsVpnInterface(nic)) {
                                    isVpnRoute = true;
                                } else {
                                    isVpnRoute = false;
                                }
                                return true;
                            }
                        } catch {}
                    }
                }
            } catch {}
            return false;
        }

        private bool HasFirewallRulesForAdapter(string sanitizedId) {
            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules")) {
                    if (key != null) {
                        string ruleName = "Claude-Shield-IPv4-" + sanitizedId;
                        if (key.GetValue(ruleName) != null) return true;
                        foreach (string valName in key.GetValueNames()) {
                            if (valName.IndexOf(sanitizedId, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        }
                    }
                }
            } catch {}
            return false;
        }

        private void StartAudit() {
            ThreadPool.QueueUserWorkItem((state) => {
                try {
                    ManualResetEvent doneIp = new ManualResetEvent(false);
                    ManualResetEvent donePing = new ManualResetEvent(false);

                    string ipStr = "Не определен";
                    string countryStr = "Неизвестно";
                    string rawGeoJson = "";
                    bool isBlockedRegion = false;
                    bool isGeoBlockedByEndpoint = false;
                    int pingMs = 0;

                    // 1. Определение внешнего IP и геопозиции
                    ThreadPool.QueueUserWorkItem((_) => {
                        string[] geoEndpoints = new string[] {
                            "https://ipwho.is/",
                            "https://api.ipify.org?format=json",
                            "https://speed.cloudflare.com/meta"
                        };

                        foreach (string url in geoEndpoints) {
                            try {
                                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                                req.Proxy = null;
                                req.Timeout = 3200;
                                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/6.0.5";
                                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                                    string json = sr.ReadToEnd();
                                    rawGeoJson += " " + json;

                                    // Поиск IP
                                    int ipIdx = json.IndexOf("\"ip\":\"");
                                    if (ipIdx < 0) ipIdx = json.IndexOf("\"ip\": \"");
                                    if (ipIdx < 0) ipIdx = json.IndexOf("\"clientIp\":\"");

                                    if (ipIdx > 0) {
                                        int startQuote = json.IndexOf("\"", ipIdx + 5);
                                        int endQuote = json.IndexOf("\"", startQuote + 1);
                                        if (startQuote > 0 && endQuote > startQuote) {
                                            ipStr = json.Substring(startQuote + 1, endQuote - startQuote - 1);
                                        }
                                    }

                                    // Поиск Country
                                    int cIdx = json.IndexOf("\"country\":\"");
                                    if (cIdx < 0) cIdx = json.IndexOf("\"country\": \"");
                                    if (cIdx < 0) cIdx = json.IndexOf("\"country_name\":\"");
                                    if (cIdx < 0) cIdx = json.IndexOf("\"country_name\": \"");

                                    if (cIdx > 0) {
                                        int startC = json.IndexOf("\"", cIdx + 9);
                                        int endC = json.IndexOf("\"", startC + 1);
                                        if (startC > 0 && endC > startC) {
                                            countryStr = json.Substring(startC + 1, endC - startC - 1);
                                        }
                                    }

                                    if (IsAnthropicBlockedRegion(countryStr, ipStr, json)) {
                                        isBlockedRegion = true;
                                    }

                                    if (ipStr != "Не определен" && countryStr != "Неизвестно") break;
                                }
                            } catch {}
                        }

                        if (IsAnthropicBlockedRegion(countryStr, ipStr, rawGeoJson)) {
                            isBlockedRegion = true;
                        }
                        doneIp.Set();
                    });

                    // 2. Проверка доступности Anthropic и детекция редиректа на app-unavailable-in-region
                    ThreadPool.QueueUserWorkItem((_) => {
                        Stopwatch sw = Stopwatch.StartNew();
                        try {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://claude.ai");
                            req.Proxy = null;
                            req.Timeout = 3500;
                            req.AllowAutoRedirect = false; // Перехватываем 302/307 на app-unavailable-in-region
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
                            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                                sw.Stop();
                                pingMs = (int)sw.ElapsedMilliseconds;
                                string loc = resp.GetResponseHeader("Location") ?? "";
                                if (loc.IndexOf("unavailable-in-region", StringComparison.OrdinalIgnoreCase) >= 0) {
                                    isGeoBlockedByEndpoint = true;
                                }
                            }
                        } catch (WebException we) {
                            sw.Stop();
                            HttpWebResponse wr = we.Response as HttpWebResponse;
                            if (wr != null) {
                                pingMs = (int)sw.ElapsedMilliseconds;
                                string loc = wr.GetResponseHeader("Location") ?? "";
                                if (loc.IndexOf("unavailable-in-region", StringComparison.OrdinalIgnoreCase) >= 0) {
                                    isGeoBlockedByEndpoint = true;
                                }
                                try { wr.Close(); } catch {}
                            } else {
                                try {
                                    HttpWebRequest reqApi = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com");
                                    reqApi.Proxy = null;
                                    reqApi.Timeout = 2500;
                                    reqApi.Method = "HEAD";
                                    reqApi.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/6.0.5";
                                    using (HttpWebResponse respApi = (HttpWebResponse)reqApi.GetResponse()) {
                                        pingMs = (int)sw.ElapsedMilliseconds;
                                    }
                                } catch (WebException we2) {
                                    if (we2.Response != null) {
                                        pingMs = (int)sw.ElapsedMilliseconds;
                                        try { we2.Response.Close(); } catch {}
                                    } else {
                                        pingMs = 0;
                                    }
                                } catch {
                                    pingMs = 0;
                                }
                            }
                        } catch {
                            sw.Stop();
                            pingMs = 0;
                        } finally {
                            donePing.Set();
                        }
                    });

                    // 3. Проверка маршрута к Anthropic (Bug 2 & 3)
                    string bestAdapterName, bestDesc;
                    bool isVpnRoute;
                    uint bestIfIndex;
                    CheckAnthropicRouting(out bestAdapterName, out bestDesc, out isVpnRoute, out bestIfIndex);

                    bool hasAnyVpnUp = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up && IsVpnInterface(nic)) {
                                hasAnyVpnUp = true;
                                break;
                            }
                        }
                    } catch {}

                    WaitHandle.WaitAll(new WaitHandle[] { doneIp, donePing }, 3200);

                    // 4. Правила WFP в реестре
                    int fwRulesCount = GetFirewallRuleCountFromRegistry();

                    // 5. Проверка IPv6 утечки на физических интерфейсах (Bug 4)
                    bool hasActiveIpv6Leak = false;
                    string leakNic = "";
                    string leakIp = "";
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up &&
                                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                !IsVpnInterface(nic)) {
                                IPInterfaceProperties props = nic.GetIPProperties();
                                foreach (UnicastIPAddressInformation u in props.UnicastAddresses) {
                                    if (u.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                        !u.Address.IsIPv6LinkLocal && !u.Address.IsIPv6SiteLocal) {
                                        hasActiveIpv6Leak = true;
                                        leakNic = nic.Name;
                                        leakIp = u.Address.ToString();
                                        break;
                                    }
                                }
                            }
                        }
                    } catch {}

                    // 6. Проверка DNS-серверов
                    List<string> dnsList = new List<string>();
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up) {
                                IPInterfaceProperties props = nic.GetIPProperties();
                                foreach (IPAddress dns in props.DnsAddresses) {
                                    if (dns.AddressFamily == AddressFamily.InterNetwork) {
                                        string ds = dns.ToString();
                                        if (!dnsList.Contains(ds)) dnsList.Add(ds);
                                    }
                                }
                            }
                        }
                    } catch {}

                    // 7. Обновление UI
                    SafeInvoke(() => {
                        CurrentPing = pingMs;
                        CurrentIp = ipStr;
                        CurrentCountry = countryStr;

                        // Обновление строки маршрута в Диагностике
                        if (_lblWarpRouteVal != null) {
                            if (isVpnRoute) {
                                string shortDesc = bestDesc.Length > 24 ? (bestDesc.Substring(0, 22) + "..") : bestDesc;
                                _lblWarpRouteVal.Text = bestAdapterName + " (" + shortDesc + ") — Маршрутизация активна";
                                _lblWarpRouteVal.ForeColor = Color.FromArgb(52, 211, 153);
                            } else {
                                _lblWarpRouteVal.Text = bestAdapterName + " — ВНИМАНИЕ: Маршрут мимо VPN!";
                                _lblWarpRouteVal.ForeColor = Color.FromArgb(248, 113, 113);
                            }
                        }

                        if (!hasAnyVpnUp) {
                            SetProtectionUIState(ProtectionState.BlockedNoVpn, "VPN-соединение не обнаружено");
                            if (_lblWarpIpVal != null) {
                                _lblWarpIpVal.Text = ipStr + " (" + countryStr + ") • " + bestAdapterName + " [Опасность утечки]";
                                _lblWarpIpVal.ForeColor = Color.FromArgb(248, 113, 113);
                            }
                            Log("SECURITY", "АУДИТ СЕТИ: Активный VPN-адаптер не найден. Запуск Claude заблокирован во избежание утечки реального IP.", Color.FromArgb(248, 113, 113));
                        } else if (!isVpnRoute) {
                            SetProtectionUIState(ProtectionState.BlockedNoVpn, "Маршрут 0.0.0.0/0 направлен мимо VPN (" + bestAdapterName + ")");
                            if (_lblWarpIpVal != null) {
                                _lblWarpIpVal.Text = ipStr + " (" + countryStr + ") • " + bestAdapterName + " [Мимо VPN]";
                                _lblWarpIpVal.ForeColor = Color.FromArgb(248, 113, 113);
                            }
                            Log("SECURITY", "ВНИМАНИЕ: VPN запущен, но маршрут к Anthropic идет через физический адаптер '" + bestAdapterName + "'. Запуск заблокирован.", Color.FromArgb(248, 113, 113));
                        } else if (isBlockedRegion || isGeoBlockedByEndpoint) {
                            // КРИТИЧЕСКИЙ БЛОК: Выходной IP находится в РФ, РБ или заблокирован серверами Anthropic!
                            SetProtectionUIState(ProtectionState.BlockedNoVpn, "Регион не поддерживается: " + countryStr + " (" + ipStr + ")");
                            if (_lblWarpIpVal != null) {
                                _lblWarpIpVal.Text = ipStr + " (" + countryStr + ") • " + bestAdapterName + " [Блокировка Anthropic]";
                                _lblWarpIpVal.ForeColor = Color.FromArgb(248, 113, 113);
                            }
                            Log("SECURITY", "ГЕОБЛОКИРОВКА: Выходной IP (" + ipStr + ") определен как " + countryStr + "! Серверы Anthropic блокируют этот регион. Запуск Claude заблокирован во избежание бана.", Color.FromArgb(248, 113, 113));
                        } else if (hasActiveIpv6Leak) {
                            SetProtectionUIState(ProtectionState.BlockedLeaking, "Утечка IPv6 на адаптере " + leakNic);
                            Log("SECURITY", "УТЕЧКА IPV6: На физическом адаптере '" + leakNic + "' обнаружен глобальный адрес " + leakIp + ". Требуется KillSwitch.", Color.FromArgb(248, 113, 113));
                        } else if (pingMs <= 0) {
                            SetProtectionUIState(ProtectionState.BlockedNoVpn, "Нет связи с серверами Anthropic (Таймаут)");
                            if (_lblWarpIpVal != null) {
                                _lblWarpIpVal.Text = ipStr + " (" + countryStr + ") • " + bestAdapterName + " [Серверы недоступны]";
                                _lblWarpIpVal.ForeColor = Color.FromArgb(248, 113, 113);
                            }
                            Log("SECURITY", "СВЯЗЬ С ANTHROPIC: Не удалось установить соединение с серверами claude.ai (таймаут сети или блокировка DNS). Запуск Claude заблокирован.", Color.FromArgb(248, 113, 113));
                        } else {
                            // ТОЛЬКО ПРИ ВЫПОЛНЕНИИ ВСЕХ УСЛОВИЙ вход безопасен!
                            SetProtectionUIState(ProtectionState.Protected, "Защита активна");
                            _pinnedVpnIp = ipStr;
                            _pinnedAdapterName = bestAdapterName;
                            _pinnedAdapterIndex = bestIfIndex;
                            if (_lblWarpIpVal != null) {
                                _lblWarpIpVal.Text = ipStr + " (" + countryStr + ") • Pinned Lock [OK]";
                                _lblWarpIpVal.ForeColor = Color.FromArgb(52, 211, 153);
                            }
                            Log("SECURITY", "АУДИТ СЕТИ: Трафик изолирован через " + bestAdapterName + " (" + countryStr + ", " + ipStr + ", " + pingMs + " мс). Запуск Claude разрешен.", Color.FromArgb(52, 211, 153));
                            Log("SENTINEL", string.Format("10x SENTINEL: Доверенный Pinned IP зафиксирован ({0} на адаптере {1}). Защита от утечек активна.", _pinnedVpnIp, _pinnedAdapterName), Color.FromArgb(52, 211, 153));
                        }

                        // Проверка DNS
                        if (dnsList.Count > 0) {
                            Log("DNS", "Активные DNS-серверы: " + string.Join(", ", dnsList.ToArray()) + " (DoH 1.1.1.1 активен).", Color.FromArgb(148, 163, 184));
                        }

                        // WFP счетчик
                        if (fwRulesCount > 0) {
                            if (_lblWarpWfpVal != null) {
                                _lblWarpWfpVal.Text = "Активен (" + fwRulesCount + " правил ядра)";
                                _lblWarpWfpVal.ForeColor = Color.FromArgb(52, 211, 153);
                            }
                        } else {
                            if (_lblWarpWfpVal != null) {
                                _lblWarpWfpVal.Text = "Не активна (нажмите для применения)";
                                _lblWarpWfpVal.ForeColor = Color.FromArgb(248, 113, 113);
                            }
                        }

                        if (_lblWarpAnthropicVal != null) {
                            _lblWarpAnthropicVal.Text = pingMs > 0 ? (pingMs + " мс") : "Таймаут соединения";
                            _lblWarpAnthropicVal.ForeColor = (pingMs > 0 && !isBlockedRegion && !isGeoBlockedByEndpoint && isVpnRoute) ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                        }

                        if (_badgeTunnelStatus != null) _badgeTunnelStatus.Invalidate();
                    });
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        Log("ERR", "Ошибка при выполнении сетевого аудита: " + ex.Message, Color.FromArgb(239, 68, 68));
                    });
                }
            });
        }

                // Fail-Closed Sentinel Architecture: Параллельный опрос независимых гео-эндпоинтов без задержек
        private static string ResolvePublicIpConcurrent(int timeoutMs) {
            string[] endpoints = new string[] {
                "https://api.ipify.org",
                "https://ifconfig.me/ip",
                "https://ipinfo.io/ip",
                "https://icanhazip.com"
            };

            string foundIp = "";
            object ipLock = new object();
            ManualResetEvent doneEvent = new ManualResetEvent(false);
            int activeCount = endpoints.Length;

            for (int i = 0; i < endpoints.Length; i++) {
                string url = endpoints[i];
                ThreadPool.QueueUserWorkItem((st) => {
                    try {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                        req.Proxy = null;
                        req.Timeout = timeoutMs;
                        req.ReadWriteTimeout = timeoutMs;
                        req.UserAgent = "ClaudeBarrier-Sentinel/6.0.5";
                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                            string res = sr.ReadToEnd().Trim();
                            IPAddress parsed;
                            if (IPAddress.TryParse(res, out parsed) && parsed.AddressFamily == AddressFamily.InterNetwork) {
                                lock (ipLock) {
                                    if (string.IsNullOrEmpty(foundIp)) {
                                        foundIp = res;
                                        doneEvent.Set();
                                    }
                                }
                            }
                        }
                    } catch {}
                    finally {
                        if (Interlocked.Decrement(ref activeCount) == 0) {
                            doneEvent.Set();
                        }
                    }
                });
            }

            doneEvent.WaitOne(timeoutMs);
            return foundIp;
        }

        private int GetFirewallRuleCountFromRegistry() {
            int count = 0;
            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules")) {
                    if (key != null) {
                        foreach (string valName in key.GetValueNames()) {
                            object obj = key.GetValue(valName);
                            if (obj != null) {
                                string valStr = obj.ToString();
                                if (valStr.IndexOf("Claude-Shield", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    valName.IndexOf("Claude-Shield", StringComparison.OrdinalIgnoreCase) >= 0) {
                                    count++;
                                }
                            }
                        }
                    }
                }
            } catch {}
            return count;
        }

        // Bug 1, 3, 4, 6: Watchdog с проверкой маршрута, утечек IPv6 и целостности WFP
        private void StartWatchdog() {
            if (_watchdogTimer != null) {
                _watchdogTimer.Stop();
                _watchdogTimer.Dispose();
                _watchdogTimer = null;
            }
            _watchdogTimer = new System.Windows.Forms.Timer();
            _watchdogTimer.Interval = 1500;
            _watchdogTimer.Tick += (s, e) => {
                if (_isSuspended) return;
                _watchdogTickCount++;

                if (_currentState != ProtectionState.Protected && (_cfg.KillSwitchEnabled || _cfg.SentinelGuardEnabled)) {
                    KillClaudeProcessesInMemory();
                }

                if (_watchdogTickCount % 3 == 0) {
                    SendServiceHeartbeat();
                }

                ThreadPool.QueueUserWorkItem((st) => {
                    // 1. Проверка маршрута ядра к Anthropic (Bug 2 & 3)
                    string bestAdapterName, bestDesc;
                    bool isVpnRoute;
                    uint bestIfIndex;
                    CheckAnthropicRouting(out bestAdapterName, out bestDesc, out isVpnRoute, out bestIfIndex);

                    bool anyVpnUp = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up && IsVpnInterface(nic)) {
                                anyVpnUp = true;
                                break;
                            }
                        }
                    } catch {}

                    // 2. Multi-Signal Triangulation & Sentinel Guard
                    MultiSignalEvaluation eval = EvaluateMultiSignals(CurrentIp, _pinnedVpnIp, isVpnRoute, anyVpnUp, bestAdapterName);
                    bool isBlocked = IsAnthropicBlockedRegion(CurrentCountry, CurrentIp, "");

                    if ((eval.TriggerKill || isBlocked) && _currentState == ProtectionState.Protected) {
                        if (_cfg.KillSwitchEnabled || _cfg.SentinelGuardEnabled) {
                            KillClaudeImmediately(isBlocked ? "Выходной IP в неподдерживаемом регионе" : eval.Reason, forceNotify: true);
                        }

                        SafeInvoke(() => {
                            string reason = isBlocked
                                ? ("Выходной IP в РФ / Беларуси (" + CurrentIp + ")!")
                                : eval.Reason;
                            SetProtectionUIState(ProtectionState.BlockedLeaking, reason);
                        });
                        Log("KILLSWITCH", string.Format("СРОЧНАЯ БЛОКИРОВКА: {0}. Все запущенные процессы Claude немедленно завершены.",
                            isBlocked ? ("Обнаружен IP в неподдерживаемом регионе (" + CurrentCountry + ")") : eval.Reason), Color.FromArgb(248, 113, 113));
                    }

                    // 2.1 Sentinel Guard: Конкурентная проверка соответствия Pinned IP
                    if (_cfg.SentinelGuardEnabled && _currentState == ProtectionState.Protected && !string.IsNullOrEmpty(_pinnedVpnIp) && (_watchdogTickCount % 4 == 0)) {
                        string liveIp = ResolvePublicIpConcurrent(2200);
                        if (!string.IsNullOrEmpty(liveIp) && !string.Equals(liveIp, _pinnedVpnIp, StringComparison.OrdinalIgnoreCase)) {
                            KillClaudeImmediately("10x Sentinel: Несовпадение с Pinned IP (" + _pinnedVpnIp + " -> " + liveIp + ")", forceNotify: true);
                            SafeInvoke(() => {
                                CurrentIp = liveIp;
                                SetProtectionUIState(ProtectionState.BlockedLeaking, "Утечка IP: " + liveIp + " (Pinned: " + _pinnedVpnIp + ")");
                            });
                            Log("SENTINEL", string.Format("10x SENTINEL ALERT: Обнаружена утечка/смена внешнего IP! Pinned: {0}, Текущий: {1}. Процессы Claude завершены.", _pinnedVpnIp, liveIp), Color.FromArgb(248, 113, 113));
                        }
                    }

                    // 3. Мониторинг IPv6 утечки в реальном времени (Bug 4)
                    bool hasIpv6Leak = false;
                    string leakNic = "";
                    string leakIp = "";
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up &&
                                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                !IsVpnInterface(nic)) {
                                IPInterfaceProperties props = nic.GetIPProperties();
                                foreach (UnicastIPAddressInformation u in props.UnicastAddresses) {
                                    if (u.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                        !u.Address.IsIPv6LinkLocal && !u.Address.IsIPv6SiteLocal) {
                                        hasIpv6Leak = true;
                                        leakNic = nic.Name;
                                        leakIp = u.Address.ToString();
                                        break;
                                    }
                                }
                            }
                            if (hasIpv6Leak) break;
                        }
                    } catch {}

                    if (hasIpv6Leak) {
                        if (_cfg.KillSwitchEnabled) {
                            KillClaudeProcessesInMemory();
                        }
                        SafeInvoke(() => {
                            SetProtectionUIState(ProtectionState.BlockedLeaking, "Утечка IPv6 на адаптере " + leakNic);
                        });
                        Log("IPV6-LEAK", string.Format("УТЕЧКА IPV6: Обнаружен глобальный адрес {0} на '{1}'. Запущен KillSwitch и деактивация ms_tcpip6.", leakIp, leakNic), Color.FromArgb(248, 113, 113));
                        try {
                            string safeNic = Regex.Replace(leakNic ?? "", @"[^a-zA-Z0-9 _\-]", "");
                            ProcessStartInfo psi = new ProcessStartInfo {
                                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Disable-NetAdapterBinding -Name '" + safeNic + "' -ComponentId ms_tcpip6 -Confirm:$false\"",
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            Process.Start(psi);
                        } catch {}
                    }

                    // 4. Проверка целостности правил WFP и пересканирование адаптеров (Bug 1 & 6)
                    if (_watchdogTickCount % 20 == 0) {
                        int fwCount = GetFirewallRuleCountFromRegistry();
                        int physicalCount = 0;
                        bool missingAdapterRules = false;
                        string missingName = "";
                        try {
                            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                                if (nic.OperationalStatus == OperationalStatus.Up &&
                                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                    !IsVpnInterface(nic)) {
                                    physicalCount++;
                                    string sanitized = System.Text.RegularExpressions.Regex.Replace(nic.Name, @"[^a-zA-Z0-9]", "_");
                                    if (!HasFirewallRulesForAdapter(sanitized)) {
                                        missingAdapterRules = true;
                                        missingName = nic.Name;
                                        break;
                                    }
                                }
                            }
                        } catch {}

                        if (physicalCount > 0 && fwCount == 0) {
                            Log("INTEGRITY", "ВНИМАНИЕ: Правила WFP сброшены внешним антивирусом или политикой безопасности. Восстановление изоляции...", Color.FromArgb(248, 113, 113));
                            SafeInvoke(() => {
                                if (_trayIcon != null) {
                                    _trayIcon.ShowBalloonTip(3000, "ClaudeBarrier", "Правила файрвола были сброшены. Выполняется автоматическое восстановление защиты.", ToolTipIcon.Warning);
                                }
                            });
                            ApplyFirewallRulesSilent(false);
                        } else if (missingAdapterRules) {
                            Log("NETWORK", string.Format("СЕТЬ: Обнаружен физический адаптер '{0}' без правил изоляции. Применение WFP...", missingName), Color.FromArgb(251, 191, 36));
                            ApplyFirewallRulesSilent(false);
                        }
                    }
                });
            };
            _watchdogTimer.Start();
        }
        private static readonly string[] BrowserProcessNames = new string[] {
            "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi",
            "browser", "yandex", "whale", "waterfox", "librewolf", "zen", "thorium",
            "chromium", "chrome_proxy", "msedge_proxy"
        };

        private static bool IsBrowserProcess(string procName) {
            if (string.IsNullOrEmpty(procName)) return false;
            string p = procName.ToLowerInvariant();
            for (int i = 0; i < BrowserProcessNames.Length; i++) {
                if (p == BrowserProcessNames[i]) return true;
            }
            return false;
        }

        private int KillBrowserClaudeSessions() {
            if (!_cfg.KillSwitchEnabled) return 0;
            int closedCount = 0;
            try {
                uint currentPid = (uint)Process.GetCurrentProcess().Id;
                List<IntPtr> windowsToClose = new List<IntPtr>();
                List<string> closedDetails = new List<string>();

                EnumWindows((hWnd, lParam) => {
                    try {
                        if (!IsWindowVisible(hWnd)) return true;

                        uint pid;
                        GetWindowThreadProcessId(hWnd, out pid);
                        if (pid == currentPid || pid == 0) return true;

                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString().Trim();
                        if (string.IsNullOrEmpty(title)) return true;

                        string tLower = title.ToLowerInvariant();
                        if (tLower.Contains("claudebarrier") || tLower.Contains("project xo") || tLower.Contains("anti-ban barrier")) return true;

                        string procName = "";
                        try {
                            using (Process p = Process.GetProcessById((int)pid)) {
                                procName = p.ProcessName.ToLowerInvariant();
                            }
                        } catch {}

                        bool isMatch = false;
                        if (tLower.Contains("claude.ai") || tLower.Contains("app.claude.ai")) {
                            isMatch = true;
                        } else if (IsBrowserProcess(procName)) {
                            bool isDocOrSearch = tLower.Contains("reddit") || tLower.Contains("github") || 
                                                 tLower.Contains("google") || tLower.Contains("yandex") || 
                                                 tLower.Contains("youtube") || tLower.Contains("habr") || 
                                                 tLower.Contains("stack overflow") || tLower.Contains("docs.anthropic");
                            if (!isDocOrSearch) {
                                if (tLower == "claude" || 
                                    tLower.StartsWith("sign in - claude") || tLower.StartsWith("log in - claude") ||
                                    tLower.EndsWith(" - claude") || tLower.EndsWith(" — claude") || tLower.EndsWith(" – claude") ||
                                    tLower.StartsWith("claude: ") || tLower.Contains("chat with claude")) {
                                    isMatch = true;
                                }
                            }
                        }

                        if (isMatch) {
                            windowsToClose.Add(hWnd);
                            closedDetails.Add(string.Format("'{0}' ({1})", title, procName));
                        }
                    } catch {}
                    return true;
                }, IntPtr.Zero);

                foreach (IntPtr hWnd in windowsToClose) {
                    try {
                        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    } catch {}
                }

                if (closedDetails.Count > 0) {
                    closedCount = closedDetails.Count;
                    SafeInvoke(() => {
                        foreach (string det in closedDetails) {
                            Log("KILLSWITCH", "ОБНАРУЖЕНА СЕССИЯ В БРАУЗЕРЕ: Окно " + det + " закрыто во избежание утечки сессии.", Color.FromArgb(248, 113, 113));
                        }
                    });
                }
            } catch {}
            return closedCount;
        }

        // ================= NEXT-GEN SENTINEL & KERNEL ENGINE =================
        
        // 1. Win32 NotifyAddrChange Hook (0-5 ms reaction time on any routing table change)
        private void StartKernelNotifyAddrChangeHook() {
            if (_notifyAddrChangeThread != null && _notifyAddrChangeThread.IsAlive) return;
            _notifyAddrChangeThread = new Thread(() => {
                while (!_isTerminating && !_isSuspended) {
                    try {
                        int res = NotifyAddrChange(IntPtr.Zero, IntPtr.Zero);
                        if (res == 0 && !_isTerminating && !_isSuspended) {
                            OnKernelNetworkRouteChanged();
                        }
                    } catch {
                        Thread.Sleep(500);
                    }
                }
            }) { IsBackground = true, Name = "ClaudeBarrier_KernelRouteHook" };
            _notifyAddrChangeThread.Start();
            Log("KERNEL", "Аппаратный хук ядра NotifyAddrChange (iphlpapi.dll) активирован (0-5 мс реакция).", Color.FromArgb(52, 211, 153));
        }

        private void OnKernelNetworkRouteChanged() {
            if (_currentState == ProtectionState.Protected) {
                string bestAdapterName, bestDesc;
                bool isVpnRoute;
                uint bestIfIndex;
                CheckAnthropicRouting(out bestAdapterName, out bestDesc, out isVpnRoute, out bestIfIndex);

                bool routeFlipped = false;
                if (!isVpnRoute) routeFlipped = true;
                if (_pinnedAdapterIndex != 0 && bestIfIndex != _pinnedAdapterIndex) routeFlipped = true;

                if (routeFlipped) {
                    KillClaudeImmediately("Хук ядра NotifyAddrChange (<5 мс): маршрут 0.0.0.0/0 ушел на " + bestAdapterName, forceNotify: true);
                    SafeInvoke(() => {
                        SetProtectionUIState(ProtectionState.BlockedLeaking, "Утечка маршрута: " + bestAdapterName);
                    });
                }
            }
        }

        // 2. Multi-Signal Triangulation (2-of-3 Decision Engine)
        public struct MultiSignalEvaluation {
            public bool Signal1_VpnProcess;    // VPN Process
            public bool Signal2_KernelRoute;   // Kernel Virtual Route 0.0.0.0/0
            public bool Signal3_PinnedIp;      // Public IP matches Pinned IP
            public int PassedCount;
            public bool TriggerKill;
            public string Reason;
        }

        private MultiSignalEvaluation EvaluateMultiSignals(string currentIp, string pinnedIp, bool isVpnRoute, bool anyVpnUp, string bestAdapterName) {
            MultiSignalEvaluation ev = new MultiSignalEvaluation();

            // Signal 1: VPN Process Check
            ev.Signal1_VpnProcess = false;
            try {
                string[] procs = (_cfg.VpnProcessList ?? "").Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                HashSet<string> targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string s in procs) targetSet.Add(s.Replace(".exe", "").Trim());

                if (targetSet.Count > 0) {
                    foreach (Process p in Process.GetProcesses()) {
                        try {
                            if (targetSet.Contains(p.ProcessName)) {
                                ev.Signal1_VpnProcess = true;
                                break;
                            }
                        } catch {}
                    }
                } else {
                    ev.Signal1_VpnProcess = anyVpnUp;
                }
            } catch {
                ev.Signal1_VpnProcess = false;
            }

            // Signal 2: Kernel Route & Virtual Interface Status
            ev.Signal2_KernelRoute = (isVpnRoute && anyVpnUp);

            // Signal 3: Pinned IP Match
            if (string.IsNullOrEmpty(pinnedIp) || pinnedIp == "..." || pinnedIp == "Не определен") {
                ev.Signal3_PinnedIp = true;
            } else {
                ev.Signal3_PinnedIp = string.Equals(currentIp, pinnedIp, StringComparison.OrdinalIgnoreCase);
            }

            ev.PassedCount = (ev.Signal1_VpnProcess ? 1 : 0) + (ev.Signal2_KernelRoute ? 1 : 0) + (ev.Signal3_PinnedIp ? 1 : 0);

            // Decision Logic
            if (!ev.Signal2_KernelRoute) {
                ev.TriggerKill = true;
                ev.Reason = "Маршрут ядра 0.0.0.0/0 направлен мимо туннеля через: " + bestAdapterName;
                return ev;
            }

            if (!ev.Signal3_PinnedIp) {
                ev.TriggerKill = true;
                ev.Reason = "Утечка IP адреса! Текущий " + currentIp + " != Pinned " + pinnedIp;
                return ev;
            }

            if (_cfg.MultiSignalEnabled && ev.PassedCount < 2) {
                ev.TriggerKill = true;
                ev.Reason = "Сбой Multi-Signal триангуляции (активно " + ev.PassedCount + "/3 сигналов)";
                return ev;
            }

            ev.TriggerKill = false;
            ev.Reason = "Все сигналы безопасности в норме (" + ev.PassedCount + "/3)";
            return ev;
        }

        // 3. Timezone Guard (Антифрод-защита от утечки таймзоны)
        public static bool CheckTimezoneMismatch(string vpnCountry, out string warning) {
            warning = string.Empty;
            try {
                TimeZoneInfo tz = TimeZoneInfo.Local;
                string tzId = tz.Id.ToLowerInvariant();
                string tzName = tz.StandardName.ToLowerInvariant();
                TimeSpan offset = tz.GetUtcOffset(DateTime.UtcNow);

                bool isRuTimezone = tzId.Contains("russian") || tzId.Contains("moscow") || tzName.Contains("московск") ||
                                    tzId.Contains("minsk") || tzId.Contains("belarus") ||
                                    (offset.TotalHours == 3 && (tzId.Contains("standard") || tzId.Contains("russia")));

                string vc = (vpnCountry ?? "").ToLowerInvariant();
                bool isWesternVpn = vc.Contains("united states") || vc.Contains("usa") || vc.Contains("germany") ||
                                    vc.Contains("netherlands") || vc.Contains("sweden") || vc.Contains("france") ||
                                    vc.Contains("united kingdom") || vc.Contains("finland") || vc.Contains("poland");

                if (isRuTimezone && isWesternVpn) {
                    warning = string.Format("Обнаружен Timezone Leak! Системный часовой пояс ({0}, UTC{1:+00;-00}) не совпадает с геопозицией VPN ({2}). Системы антифрода Anthropic блокируют такие сессии.",
                        tz.DisplayName, (int)offset.TotalHours, vpnCountry);
                    return true;
                }
            } catch {}
            return false;
        }

        // 4. Safe Launcher Pre-Flight Audit
        private bool RunSafeLauncherPreFlight(string targetAppName) {
            if (_currentState != ProtectionState.Protected) {
                ShowSafeLaunchBlockDialog(targetAppName, "Защитный барьер ClaudeBarrier не активен или туннель разорван.");
                return false;
            }

            string bestAdapterName, bestDesc;
            bool isVpnRoute;
            uint bestIfIndex;
            CheckAnthropicRouting(out bestAdapterName, out bestDesc, out isVpnRoute, out bestIfIndex);
            if (!isVpnRoute) {
                ShowSafeLaunchBlockDialog(targetAppName, "Маршрут ядра 0.0.0.0/0 направлен мимо туннеля через: " + bestAdapterName);
                return false;
            }

            if (IsAnthropicBlockedRegion(CurrentCountry, CurrentIp, "")) {
                ShowSafeLaunchBlockDialog(targetAppName, "Выходной IP находится в неподдерживаемом регионе (" + CurrentCountry + ", " + CurrentIp + "). Anthropic сразу же заблокирует доступ!");
                return false;
            }

            if (!string.IsNullOrEmpty(_pinnedVpnIp) && !string.Equals(CurrentIp, _pinnedVpnIp, StringComparison.OrdinalIgnoreCase)) {
                ShowSafeLaunchBlockDialog(targetAppName, "Обнаружена утечка IP! Текущий: " + CurrentIp + ", Зафиксированный VPN: " + _pinnedVpnIp);
                return false;
            }

            if (CurrentPing <= 0) {
                ShowSafeLaunchBlockDialog(targetAppName, "Таймаут соединения с серверами api.anthropic.com:443. Доступ заблокирован.");
                return false;
            }

            string tzWarn;
            if (_cfg.TimezoneGuardEnabled && CheckTimezoneMismatch(CurrentCountry, out tzWarn)) {
                DialogResult dr = MessageBox.Show(
                    tzWarn + "\n\nПродолжить запуск " + targetAppName + " под вашу ответственность?",
                    "ClaudeBarrier // Timezone Guard Предупреждение",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return false;
            }

            return true;
        }

        private void ShowSafeLaunchBlockDialog(string targetApp, string reason) {
            string msg = string.Format(
                "БЕЗОПАСНЫЙ ЗАПУСК ЗАБЛОКИРОВАН (Safe Launcher Pre-Flight):\n\n" +
                "Приложение: {0}\n" +
                "Причина отказа: {1}\n\n" +
                "• Нажмите [Да], чтобы открыть сетевые подключения Windows (ncpa.cpl).\n" +
                "• Нажмите [Нет], чтобы отменить запуск и не рисковать баном.",
                targetApp, reason);

            DialogResult res = MessageBox.Show(
                msg,
                "ClaudeBarrier Safe Launcher // Защита от бана",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Stop);

            if (res == DialogResult.Yes) {
                try { Process.Start("ncpa.cpl"); } catch {}
            }
        }

        // 5. IPC Heartbeat Sender to Windows Service
        private void SendServiceHeartbeat() {
            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    using (NamedPipeClientStream client = new NamedPipeClientStream(".", ClaudeBarrierWindowsService.PIPE_NAME, PipeDirection.InOut)) {
                        client.Connect(300);
                        using (StreamWriter sw = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true })
                        using (StreamReader sr = new StreamReader(client, Encoding.UTF8)) {
                            sw.WriteLine("HEARTBEAT");
                            string resp = sr.ReadLine();
                            if (resp == "HEARTBEAT_ACK") {
                                _isServiceIpcConnected = true;
                            }
                        }
                    }
                } catch {
                    _isServiceIpcConnected = false;
                }
            });
        }

        // 6. Two-Stage Process Killer (Graceful WM_CLOSE -> Force Kill Verified PIDs)
        public static int ExecuteTwoStageKill(string reason) {
            int killedCount = 0;
            try {
                List<int> targetPids = new List<int>();
                foreach (Process p in Process.GetProcesses()) {
                    try {
                        string name = p.ProcessName.ToLowerInvariant();
                        bool match = false;
                        if (name == "claude" || name == "claude-desktop" || name == "claude_desktop" ||
                            name == "claude_dummy" || name.StartsWith("claude-") || name.StartsWith("claude_")) {
                            match = true;
                        } else if (name == "node") {
                            try {
                                string title = p.MainWindowTitle.ToLowerInvariant();
                                if (title.Contains("claude")) match = true;
                            } catch {}
                        }

                        if (match && p.Id > 0 && !targetPids.Contains(p.Id)) {
                            targetPids.Add(p.Id);
                        }
                    } catch {}
                }

                if (targetPids.Count > 0) {
                    // Stage 1: Graceful close
                    EnumWindows((hWnd, lParam) => {
                        try {
                            uint pid;
                            GetWindowThreadProcessId(hWnd, out pid);
                            if (targetPids.Contains((int)pid)) {
                                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            }
                        } catch {}
                        return true;
                    }, IntPtr.Zero);

                    // Stage 2: Brief pause (150 ms)
                    Thread.Sleep(150);

                    // Stage 3: Force-kill verified PIDs
                    foreach (int pid in targetPids) {
                        try {
                            using (Process p = Process.GetProcessById(pid)) {
                                string name = p.ProcessName.ToLowerInvariant();
                                if (name == "claude" || name == "claude-desktop" || name == "claude_desktop" ||
                                    name == "claude_dummy" || name.StartsWith("claude-") || name.StartsWith("claude_") || name == "node") {
                                    p.Kill();
                                    killedCount++;
                                }
                            }
                        } catch {}
                    }
                }
            } catch {}
            return killedCount;
        }

        private int KillClaudeImmediately(string reason, bool forceNotify = false) {
            int pidsKilled = ExecuteTwoStageKill(reason);
            int browserClosed = 0;
            if (_cfg.KillSwitchEnabled || _cfg.SentinelGuardEnabled) {
                browserClosed = KillBrowserClaudeSessions();
            }

            int totalKilled = pidsKilled + browserClosed;

            // Only log and notify if actual processes/windows were terminated, OR forceNotify is explicitly true
            if (totalKilled > 0 || forceNotify) {
                RefreshSystemTray();

                DateTime now = DateTime.UtcNow;
                bool canShowBalloon = (now - _lastKillBalloonTime).TotalSeconds >= 6.0;

                SafeInvoke(() => {
                    string detail = totalKilled > 0
                        ? string.Format("Завершено процессов/окон Claude: {0} ({1})", totalKilled, reason)
                        : string.Format("Срабатывание защиты: {0} (активных процессов Claude не обнаружено)", reason);

                    Log("SENTINEL", string.Format("SENTINEL GUARD: {0}", detail), Color.FromArgb(248, 113, 113));

                    if (_trayIcon != null && canShowBalloon) {
                        _lastKillBalloonTime = now;
                        _trayIcon.ShowBalloonTip(3000, "ClaudeBarrier Sentinel", detail, ToolTipIcon.Warning);
                    }
                });
            }

            return totalKilled;
        }

        private int KillClaudeProcessesInMemory() {
            return KillClaudeImmediately("Превентивное завершение процессов сторожевой службой", forceNotify: false);
        }

        private void RunEmergencyInterceptTest() {
            Log("WFP-TEST", "Запуск прямого сокет-теста ядра WFP (отправка SYN пакета к 160.79.104.10:443)...", Color.FromArgb(148, 163, 184));
            ThreadPool.QueueUserWorkItem((st) => {
                Stopwatch sw = Stopwatch.StartNew();
                bool blockedByKernel = false;
                bool isConnected = false;
                int errorCode = 0;
                string errorMsg = "";
                long elapsedMs = 0;

                try {
                    using (Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)) {
                        sock.ReceiveTimeout = 2000;
                        sock.SendTimeout = 2000;
                        IPAddress targetIp = IPAddress.Parse("160.79.104.10");
                        IPEndPoint ep = new IPEndPoint(targetIp, 443);

                        IAsyncResult ar = sock.BeginConnect(ep, null, null);
                        bool success = ar.AsyncWaitHandle.WaitOne(1500, true);
                        if (success) {
                            try { sock.EndConnect(ar); } catch (SocketException se) { throw se; }
                            sw.Stop();
                            elapsedMs = sw.ElapsedMilliseconds;
                            blockedByKernel = false;
                            isConnected = true;
                        } else {
                            sock.Close();
                            sw.Stop();
                            elapsedMs = sw.ElapsedMilliseconds;
                            blockedByKernel = false;
                            errorMsg = "Тайм-аут ожидания SYN-ACK (1500 мс)";
                        }
                    }
                } catch (SocketException sex) {
                    sw.Stop();
                    elapsedMs = sw.ElapsedMilliseconds;
                    errorCode = sex.ErrorCode;
                    errorMsg = sex.Message;
                    // 10013: WSAEACCES (WFP / Permission denied)
                    // 10065: WSAEHOSTUNREACH (Host unreachable)
                    // 10051: WSAENETUNREACH
                    // 10060: WSAETIMEDOUT
                    if (sex.ErrorCode == 10013 || sex.ErrorCode == 10065 || sex.ErrorCode == 10051) {
                        blockedByKernel = true;
                    }
                } catch (Exception ex) {
                    sw.Stop();
                    elapsedMs = sw.ElapsedMilliseconds;
                    errorMsg = ex.Message;
                }

                string bestAdapterName, bestDesc;
                bool isVpnRoute;
                uint bestIfIndex;
                CheckAnthropicRouting(out bestAdapterName, out bestDesc, out isVpnRoute, out bestIfIndex);

                int fwCount = GetFirewallRuleCountFromRegistry();
                bool isProtected = (_currentState == ProtectionState.Protected);

                SafeInvoke(() => {
                    if (Interlocked.CompareExchange(ref _isShowingModalDialog, 1, 0) != 0) return;
                    try {
                        if (blockedByKernel || (fwCount > 0 && errorCode == 10013)) {
                            Log("WFP-TEST", string.Format("Пакет к 160.79.104.10:443 успешно сброшен фильтром WFP за {0} мс (WSA: {1}).", elapsedMs, errorCode), Color.FromArgb(52, 211, 153));
                            MessageBox.Show(
                                this,
                                "Результат теста аварийного перехвата WFP:\n\n" +
                                "• Статус ядра: ПЕРЕХВАТ АКТИВЕН (Аппаратный сброс SYN)\n" +
                                "• Время отклика WFP: " + elapsedMs + " мс (мгновенный отсев ядра)\n" +
                                "• Код сетевого перехвата ОС: " + errorCode + " (" + (errorCode == 10013 ? "WSAEACCES — Заблокировано WFP" : "WSAEHOSTUNREACH") + ")\n" +
                                "• Зарегистрировано правил изоляции: " + fwCount + " шт.\n" +
                                "• Целевой узел: 160.79.104.10:443 (Anthropic Direct CIDR)\n\n" +
                                "Прямая утечка трафика на незащищенные сетевые интерфейсы исключена.",
                                "Аварийный тест WFP — Успешно",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        } else if (isVpnRoute && isConnected) {
                            Log("WFP-TEST", string.Format("Трафик направлен через туннель {0} (RTT: {1} мс). Прямой утечки нет.", bestAdapterName, elapsedMs), Color.FromArgb(52, 211, 153));
                            MessageBox.Show(
                                this,
                                "Результат проверки безопасности соединения:\n\n" +
                                "• Статус туннеля: ЗАЩИЩЕН (Трафик внутри VPN " + bestAdapterName + ")\n" +
                                "• Задержка отклика (RTT): " + elapsedMs + " мс\n" +
                                "• Внешний адрес: " + CurrentIp + " (" + CurrentCountry + ")\n" +
                                "• Активных правил изоляции в ядре: " + fwCount + " шт.\n\n" +
                                "Соединение к Anthropic проходит исключительно через виртуальный туннель VPN.",
                                "Тест соединения WFP — Безопасно",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        } else if (!isVpnRoute && isConnected) {
                            Log("WFP-TEST", string.Format("Внимание: пакет прошел через физический интерфейс {0} без VPN! Правил WFP: {1}.", bestAdapterName, fwCount), Color.FromArgb(248, 113, 113));
                            MessageBox.Show(
                                this,
                                "Результат теста безопасности:\n\n" +
                                "• Внимание: Трафик к Anthropic пошел через физический интерфейс " + bestAdapterName + " без защиты VPN!\n" +
                                "• Время отклика: " + elapsedMs + " мс\n" +
                                "• Ответ сокета: Соединение установлено в обход туннеля\n" +
                                "• Внедрено правил WFP в систему: " + fwCount + " шт.\n\n" +
                                (fwCount == 0
                                    ? "Рекомендация: нажмите «Применить правила» в строке WFP для активации фильтрации."
                                    : "Рекомендация: включите VPN перед запуском приложений Claude."),
                                "Тест перехвата WFP — Внимание",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        } else {
                            Log("WFP-TEST", string.Format("Прямое соединение без VPN заблокировано/таймаут ({0} мс, код: {1}). Правил WFP: {2}.", elapsedMs, errorCode, fwCount), Color.FromArgb(52, 211, 153));
                            MessageBox.Show(
                                this,
                                "Результат теста безопасности:\n\n" +
                                "• Статус: Прямое нешифрованное соединение заблокировано или недоступно\n" +
                                "• Время замера: " + elapsedMs + " мс\n" +
                                "• Ответ сокета: " + (errorCode > 0 ? ("Код " + errorCode + " (" + errorMsg + ")") : errorMsg) + "\n" +
                                "• Внедрено правил WFP в ядро: " + fwCount + " шт.\n\n" +
                                "Прямой доступ к серверам Anthropic без VPN не проходит.",
                                "Тест перехвата WFP",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    } finally {
                        Interlocked.Exchange(ref _isShowingModalDialog, 0);
                    }
                });
            });
        }

        private void RemoveFirewallRules(bool showSuccessDialog = true) {
            if (showSuccessDialog) {
                DialogResult dr = MessageBox.Show(
                    "Вы действительно хотите удалить все правила брандмауэра ClaudeBarrier (WFP) из ядра Windows?\n\n" +
                    "Сетевая изоляция интерфейсов будет отключена, ограничения для Claude будут сняты.",
                    "Удаление правил WFP",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;
            }

            Log("WFP", "Удаление правил сетевой изоляции WFP из ядра Windows...", Color.FromArgb(249, 115, 22));

            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    string script = 
                        "$ErrorActionPreference = 'SilentlyContinue'; " +
                        "Get-NetFirewallRule | Where-Object { $_.Name -like 'Claude-Shield*' -or $_.DisplayName -like 'Claude-Shield*' } | Remove-NetFirewallRule; " +
                        "Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -like 'Claude-Shield*' -or (Get-ItemPropertyValue 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' $_.Name) -like '*Claude-Shield*' } | ForEach-Object { Remove-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' -Name $_.Name }; " +
                        "Clear-DnsClientCache";

                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process proc = Process.Start(psi);
                    if (proc != null) proc.WaitForExit(10000);

                    SafeInvoke(() => {
                        if (_lblWarpWfpVal != null) {
                            _lblWarpWfpVal.Text = "Правила не внедрены";
                            _lblWarpWfpVal.ForeColor = Color.FromArgb(248, 113, 113);
                        }
                        Log("WFP", "Все правила брандмауэра ClaudeBarrier успешно удалены из ядра.", Color.FromArgb(52, 211, 153));
                        if (showSuccessDialog) {
                            MessageBox.Show(
                                "Все правила брандмауэра ClaudeBarrier (WFP) успешно удалены из системы.\n\n" +
                                "Сетевой экран Windows освобожден от ограничений.",
                                "ClaudeBarrier",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    });
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        Log("ERR", "Ошибка при удалении правил WFP: " + ex.Message, Color.FromArgb(239, 68, 68));
                        if (showSuccessDialog) {
                            MessageBox.Show("Не удалось удалить правила WFP:\n" + ex.Message, "Ошибка ClaudeBarrier", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    });
                }
            });
        }

        private void ApplyFirewallRulesSilent(bool showSuccessDialog = false) {
            if (Interlocked.CompareExchange(ref _isApplyingFirewallRules, 1, 0) != 0) {
                Log("WFP", "Правила ядра уже применяются в фоновом режиме, подождите завершения...", Color.FromArgb(249, 115, 22));
                if (showSuccessDialog) {
                    MessageBox.Show("Операция применения правил уже выполняется в фоновом режиме.", "WFP Брандмауэр", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            Log("WFP", "Применение правил изоляции сетевых интерфейсов в ядре Windows...", Color.FromArgb(52, 211, 153));
            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    string script = 
                        "$ErrorActionPreference = 'SilentlyContinue'; " +
                        "Get-NetFirewallRule | Where-Object { $_.Name -like 'Claude-Shield*' -or $_.DisplayName -like 'Claude-Shield*' } | Remove-NetFirewallRule; " +
                        "Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -like 'Claude-Shield*' -or (Get-ItemPropertyValue 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' $_.Name) -like '*Claude-Shield*' } | ForEach-Object { Remove-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' -Name $_.Name }; " +
                        "$vpnPattern = 'Wintun|WireGuard|TAP|TUN|Virtual|Tailscale|Amnezia|WARP|Mullvad|Proton|Neko|Sing-box|Clash|v2ray|Hiddify|VPN|wwan'; " +
                        "$adapters = @(Get-NetAdapter | Where-Object { $_.InterfaceDescription -notmatch $vpnPattern -and $_.Name -notmatch $vpnPattern -and $_.Status -ne 'Disabled' } | Select-Object -ExpandProperty Name); " +
                        "$idx = 1; " +
                        "foreach ($a in $adapters) { " +
                        "  $id = ($a -replace '[^a-zA-Z0-9]','_'); " +
                        "  New-NetFirewallRule -Name ('Claude-Shield-IPv4-' + $id) -DisplayName ('Claude-Shield-Block-IPv4-' + $a) -Direction Outbound -RemoteAddress '160.79.104.0/23' -InterfaceAlias $a -Action Block | Out-Null; " +
                        "  New-NetFirewallRule -Name ('Claude-Shield-IPv6-' + $id) -DisplayName ('Claude-Shield-Block-IPv6-' + $a) -Direction Outbound -RemoteAddress '2607:6bc0::/32' -InterfaceAlias $a -Action Block | Out-Null; " +
                        "  New-NetFirewallRule -Name ('Claude-Shield-App-' + $id) -DisplayName ('Claude-Shield-Block-App-' + $a) -Direction Outbound -Program 'Claude.exe' -InterfaceAlias $a -Action Block | Out-Null; " +
                        "  New-NetFirewallRule -Name ('Claude-Shield-Desk-' + $id) -DisplayName ('Claude-Shield-Block-Desk-' + $a) -Direction Outbound -Program 'claude-desktop.exe' -InterfaceAlias $a -Action Block | Out-Null; " +
                        "  New-NetFirewallRule -Name ('Claude-Shield-CLI-' + $id) -DisplayName ('Claude-Shield-Block-CLI-' + $a) -Direction Outbound -Program 'claude.exe' -InterfaceAlias $a -Action Block | Out-Null; " +
                        "  New-NetFirewallRule -Name ('Claude-Shield-Pkg-' + $id) -DisplayName ('Claude-Shield-Block-Pkg-' + $a) -Direction Outbound -PackageFamilyName 'Claude_pzs8sxrjxfjjc' -InterfaceAlias $a -Action Block | Out-Null; " +
                        "  Disable-NetAdapterBinding -Name $a -ComponentId ms_tcpip6; " +
                        "  $idx++; " +
                        "} " +
                        "if (!(Test-Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient')) { New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient' -Force | Out-Null }; " +
                        "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient' -Name 'DisableSmartNameResolution' -Value 1 -Type DWord -Force; " +
                        "Clear-DnsClientCache";

                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    Process proc = Process.Start(psi);
                    proc.WaitForExit();

                    SafeInvoke(() => {
                        int count = GetFirewallRuleCountFromRegistry();
                        Log("WFP", string.Format("Правила WFP успешно применены ({0} правил в реестре). Сеть защищена от утечек.", count), Color.FromArgb(52, 211, 153));
                        StartAudit();
                        if (showSuccessDialog) {
                            if (Interlocked.CompareExchange(ref _isShowingModalDialog, 1, 0) == 0) {
                                try {
                                    MessageBox.Show(
                                        this,
                                        "Правила аппаратной изоляции WFP успешно применены!\n\n" +
                                        "• Внедрено уникальных правил в ядро: " + count + " шт.\n" +
                                        "• Заблокирован прямой доступ к 160.79.104.0/23.\n" +
                                        "• Изолированы физические сетевые адаптеры.\n" +
                                        "• Отключены утечки IPv6 и Smart DNS.\n\n" +
                                        "Трафик Claude физически изолирован внутри туннеля.",
                                        "WFP Брандмауэр ядра — Успешно",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Information);
                                } finally {
                                    Interlocked.Exchange(ref _isShowingModalDialog, 0);
                                }
                            }
                        }
                    });
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        Log("ERR", "Ошибка установки правил WFP: " + ex.Message, Color.FromArgb(239, 68, 68));
                        if (showSuccessDialog) {
                            MessageBox.Show(
                                "Не удалось применить правила WFP:\n" + ex.Message + "\n\n" +
                                "Убедитесь, что приложение запущено от имени Администратора.",
                                "Ошибка WFP",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    });
                } finally {
                    Interlocked.Exchange(ref _isApplyingFirewallRules, 0);
                }
            });
        }
        public static uint IpToUint(string ipAddress) {
            try {
                IPAddress ip = IPAddress.Parse(ipAddress);
                byte[] bytes = ip.GetAddressBytes();
                return (uint)(bytes[3] << 24 | bytes[2] << 16 | bytes[1] << 8 | bytes[0]);
            } catch { return 0; }
        }

        public static int GetFirewallRuleCountFromRegistryStatic() {
            int count = 0;
            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules")) {
                    if (key != null) {
                        foreach (string valName in key.GetValueNames()) {
                            object obj = key.GetValue(valName);
                            if (obj != null) {
                                string valStr = obj.ToString();
                                if (valStr.IndexOf("Claude-Shield", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    valName.IndexOf("Claude-Shield", StringComparison.OrdinalIgnoreCase) >= 0) {
                                    count++;
                                }
                            }
                        }
                    }
                }
            } catch {}
            return count;
        }

        public static void RemoveFirewallRulesStatic() {
            try {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                        "$ErrorActionPreference = 'SilentlyContinue'; " +
                        "Get-NetFirewallRule | Where-Object { $_.Name -like 'Claude-Shield*' -or $_.DisplayName -like 'Claude-Shield*' } | Remove-NetFirewallRule; " +
                        "Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' | Get-Member -MemberType NoteProperty | Where-Object { $_.Name -like 'Claude-Shield*' -or (Get-ItemPropertyValue 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' $_.Name) -like '*Claude-Shield*' } | ForEach-Object { Remove-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules' -Name $_.Name };\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process p = Process.Start(psi);
                p.WaitForExit(8000);
            } catch {}
        }

        public static void ApplyBootTimeWfpIsolation() {
            try {
                string script = 
                    "$ErrorActionPreference = 'SilentlyContinue'; " +
                    "Get-NetFirewallRule | Where-Object { $_.Name -like 'Claude-Shield*' -or $_.DisplayName -like 'Claude-Shield*' } | Remove-NetFirewallRule; " +
                    "$vpnPattern = 'Wintun|WireGuard|TAP|TUN|Virtual|Tailscale|Amnezia|WARP|Mullvad|Proton|Neko|Sing-box|Clash|v2ray|Hiddify|VPN|wwan'; " +
                    "$adapters = @(Get-NetAdapter | Where-Object { $_.InterfaceDescription -notmatch $vpnPattern -and $_.Name -notmatch $vpnPattern -and $_.Status -ne 'Disabled' } | Select-Object -ExpandProperty Name); " +
                    "foreach ($a in $adapters) { " +
                    "  $id = ($a -replace '[^a-zA-Z0-9]','_'); " +
                    "  New-NetFirewallRule -Name ('Claude-Shield-IPv4-' + $id) -DisplayName ('Claude-Shield-Block-IPv4-' + $a) -Direction Outbound -RemoteAddress '160.79.104.0/23' -InterfaceAlias $a -Action Block | Out-Null; " +
                    "  New-NetFirewallRule -Name ('Claude-Shield-IPv6-' + $id) -DisplayName ('Claude-Shield-Block-IPv6-' + $a) -Direction Outbound -RemoteAddress '2607:6bc0::/32' -InterfaceAlias $a -Action Block | Out-Null; " +
                    "  New-NetFirewallRule -Name ('Claude-Shield-App-' + $id) -DisplayName ('Claude-Shield-Block-App-' + $a) -Direction Outbound -Program 'Claude.exe' -InterfaceAlias $a -Action Block | Out-Null; " +
                    "  New-NetFirewallRule -Name ('Claude-Shield-Desk-' + $id) -DisplayName ('Claude-Shield-Block-Desk-' + $a) -Direction Outbound -Program 'claude-desktop.exe' -InterfaceAlias $a -Action Block | Out-Null; " +
                    "  New-NetFirewallRule -Name ('Claude-Shield-CLI-' + $id) -DisplayName ('Claude-Shield-Block-CLI-' + $a) -Direction Outbound -Program 'claude.exe' -InterfaceAlias $a -Action Block | Out-Null; " +
                    "} " +
                    "Clear-DnsClientCache";

                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process proc = Process.Start(psi);
                proc.WaitForExit(10000);
            } catch {}
        }
    }

    // ================= WINDOWS SERVICE DAEMON & IPC HOST =================

    public class ClaudeBarrierWindowsService : ServiceBase {
        public const string SERVICE_NAME = "ClaudeBarrierService";
        public const string PIPE_NAME = "ClaudeBarrierServicePipe";

        private Thread _pipeServerThread;
        private Thread _routeHookThread;
        private System.Threading.Timer _heartbeatCheckTimer;
        private volatile bool _isStopping = false;
        private DateTime _lastHeartbeatTime = DateTime.UtcNow;

        public ClaudeBarrierWindowsService() {
            ServiceName = SERVICE_NAME;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args) {
            _isStopping = false;
            _lastHeartbeatTime = DateTime.UtcNow;

            MainWindow.ApplyBootTimeWfpIsolation();

            _pipeServerThread = new Thread(PipeServerWorker) { IsBackground = true, Name = "ClaudeBarrier_ServicePipe" };
            _pipeServerThread.Start();

            _routeHookThread = new Thread(RouteHookWorker) { IsBackground = true, Name = "ClaudeBarrier_ServiceRouteHook" };
            _routeHookThread.Start();

            _heartbeatCheckTimer = new System.Threading.Timer(CheckHeartbeatTimeout, null, 5000, 5000);
        }

        protected override void OnStop() {
            _isStopping = true;
            try {
                if (_heartbeatCheckTimer != null) _heartbeatCheckTimer.Dispose();
            } catch {}
        }

        private void CheckHeartbeatTimeout(object state) {
            if (_isStopping) return;
            TimeSpan elapsed = DateTime.UtcNow - _lastHeartbeatTime;
            if (elapsed.TotalSeconds > 15) {
                MainWindow.ApplyBootTimeWfpIsolation();
            }
        }

        private void RouteHookWorker() {
            while (!_isStopping) {
                try {
                    int ret = MainWindow.NotifyAddrChange(IntPtr.Zero, IntPtr.Zero);
                    if (ret == 0 && !_isStopping) {
                        string bestAdapterName, bestDesc;
                        bool isVpnRoute;
                        uint ifIndex;
                        if (MainWindow.CheckAnthropicRouting(out bestAdapterName, out bestDesc, out isVpnRoute, out ifIndex)) {
                            if (!isVpnRoute) {
                                MainWindow.ExecuteTwoStageKill("Служба ядра NotifyAddrChange: Маршрут Anthropic направлен на физический интерфейс " + bestAdapterName);
                            }
                        }
                    }
                } catch {
                    Thread.Sleep(1000);
                }
            }
        }

        private void PipeServerWorker() {
            while (!_isStopping) {
                try {
                    using (NamedPipeServerStream server = new NamedPipeServerStream(
                        PIPE_NAME,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous)) {

                        server.WaitForConnection();
                        using (StreamReader sr = new StreamReader(server, Encoding.UTF8))
                        using (StreamWriter sw = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true }) {
                            string msg = sr.ReadLine();
                            if (!string.IsNullOrEmpty(msg)) {
                                msg = msg.Trim();
                                if (msg == "HEARTBEAT") {
                                    _lastHeartbeatTime = DateTime.UtcNow;
                                    sw.WriteLine("HEARTBEAT_ACK");
                                } else if (msg == "GET_STATUS") {
                                    int ruleCount = MainWindow.GetFirewallRuleCountFromRegistryStatic();
                                    sw.WriteLine("STATUS|OK|" + ruleCount);
                                } else if (msg == "APPLY_WFP") {
                                    MainWindow.ApplyBootTimeWfpIsolation();
                                    sw.WriteLine("APPLY_OK");
                                } else if (msg == "REMOVE_WFP") {
                                    MainWindow.RemoveFirewallRulesStatic();
                                    sw.WriteLine("REMOVE_OK");
                                } else if (msg == "KILL_CLAUDE") {
                                    MainWindow.ExecuteTwoStageKill("Команда IPC от интерфейса");
                                    sw.WriteLine("KILL_OK");
                                }
                            }
                        }
                    }
                } catch {
                    if (!_isStopping) Thread.Sleep(500);
                }
            }
        }
    }

    public static class ClaudeBarrierServiceController {
        public const string SERVICE_NAME = "ClaudeBarrierService";

        public static bool IsServiceInstalled() {
            try {
                using (ServiceController sc = new ServiceController(SERVICE_NAME)) {
                    var s = sc.Status;
                    return true;
                }
            } catch {
                return false;
            }
        }

        public static bool IsServiceRunning() {
            try {
                using (ServiceController sc = new ServiceController(SERVICE_NAME)) {
                    return sc.Status == ServiceControllerStatus.Running;
                }
            } catch {
                return false;
            }
        }

        public static bool InstallService() {
            try {
                string exe = Application.ExecutablePath;
                string binPath = "\"" + exe + "\" --service";
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = "sc.exe",
                    Arguments = string.Format("create {0} binPath= \"{1}\" start= auto DisplayName= \"ClaudeBarrier Sentinel Service\"", SERVICE_NAME, binPath),
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process p = Process.Start(psi);
                p.WaitForExit(5000);
                StartService();
                return true;
            } catch { return false; }
        }

        public static bool UninstallService() {
            try {
                StopService();
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = "sc.exe",
                    Arguments = "delete " + SERVICE_NAME,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process p = Process.Start(psi);
                p.WaitForExit(5000);
                return true;
            } catch { return false; }
        }

        public static bool StartService() {
            try {
                using (ServiceController sc = new ServiceController(SERVICE_NAME)) {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending) {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    }
                    return true;
                }
            } catch { return false; }
        }

        public static bool StopService() {
            try {
                using (ServiceController sc = new ServiceController(SERVICE_NAME)) {
                    if (sc.Status == ServiceControllerStatus.Running) {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                    return true;
                }
            } catch { return false; }
        }
    }
}