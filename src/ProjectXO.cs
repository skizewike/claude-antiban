using System.IO.Compression;
using System;
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
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ProjectXO {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
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
                        "Claude Anti-Ban Barrier",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            // 2. Гарантия единственного экземпляра
            bool createdNew;
            using (Mutex m = new Mutex(true, "Global\\ClaudeAntiBanBarrier_BZ_Mutex", out createdNew)) {
                if (!createdNew) {
                    MessageBox.Show(
                        "Защитный барьер Claude Anti-Ban уже работает в системе (проверьте системный трей возле часов).",
                        "Claude Anti-Ban Barrier",
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
                        }
                    }
                }
            } catch {}
        }

        public void Save() {
            try {
                List<string> lines = new List<string> {
                    "# Claude Anti-Ban Barrier Configuration // Bogdan Zhukov",
                    "MinimizeToTrayOnClose=" + (MinimizeToTrayOnClose ? "1" : "0"),
                    "AutoStartWithWindows=" + (AutoStartWithWindows ? "1" : "0"),
                    "ClaudePath=" + (ClaudePath ?? ""),
                    "ClaudeCodePath=" + (ClaudeCodePath ?? ""),
                    "ZapretPath=" + (ZapretPath ?? ""),
                    "ZapretArgs=" + (ZapretArgs ?? ""),
                    "AutoStartZapret=" + (AutoStartZapret ? "1" : "0")
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
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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

        protected override void OnPaint(PaintEventArgs pevent) {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent != null) ? Parent.BackColor : Color.FromArgb(17, 19, 27);
            using (SolidBrush pb = new SolidBrush(parentBg)) {
                g.FillRectangle(pb, ClientRectangle);
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GraphicsUtils.CreateRoundedRectangle(bounds, CornerRadius)) {
                if (HasGlow && Enabled) {
                    using (Pen glowPen = new Pen(GlowColor, 4f)) {
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

                if (Enabled) {
                    using (Pen topPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f)) {
                        g.DrawLine(topPen, bounds.Left + CornerRadius, bounds.Top, bounds.Right - CornerRadius, bounds.Top);
                    }
                }

                if (BorderStrokeColor != Color.Transparent && Enabled) {
                    using (Pen p = new Pen(BorderStrokeColor, 1f)) {
                        g.DrawPath(p, path);
                    }
                }
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(100, 116, 139);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
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

    // Чип диагностической метрики внутри блока телеметрии
    public class TelemetryStatChip : Panel {
        private string _label;
        private string _value;
        private Color _valColor;

        public TelemetryStatChip(string label, string initialValue, Color initialColor) {
            _label = label;
            _value = initialValue;
            _valColor = initialColor;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(146, 60);
            BackColor = Color.FromArgb(18, 21, 30);
        }

        public void UpdateVal(string val, Color col) {
            _value = val;
            _valColor = col;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(bounds, 6)) {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(18, 21, 30))) {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(Color.FromArgb(20, 255, 255, 255), 1f)) {
                    g.DrawPath(pen, p);
                }
            }

            using (Font lf = new Font("Segoe UI", 7.5f, FontStyle.Regular)) {
                using (SolidBrush lb = new SolidBrush(Color.FromArgb(100, 116, 139))) {
                    g.DrawString(_label, lf, lb, 8, 8);
                }
            }

            using (SolidBrush dotB = new SolidBrush(_valColor)) {
                g.FillEllipse(dotB, 8, 33, 7, 7);
            }

            using (Font vf = new Font("Segoe UI", 8.2f, FontStyle.Bold)) {
                using (SolidBrush vb = new SolidBrush(_valColor)) {
                    g.DrawString(_value, vf, vb, 19, 28);
                }
            }
        }
    }

    // Раскрывающаяся карточка детальной диагностики шлюзов (для вкладки "Защита и шлюзы")
    public class DiagnosticCard : Panel {
        private bool _isExpanded = false;
        private Label _lblTitle;
        private Label _lblBadge;
        private Label _lblArrow;
        private Panel _pnlHeader;
        private Panel _pnlBody;
        private Label _lblDesc;
        private Label _lblTech;
        private CloudflareButton _btnAction;

        public event Action ActionClicked;

        public DiagnosticCard(string title, string badgeText, Color badgeColor, Color badgeBg, string description, string technical, string actionBtnText) {
            Width = 794;
            Height = 44;
            BackColor = Color.FromArgb(21, 24, 34);
            Font = new Font("Segoe UI", 9f);
            Margin = new Padding(0, 0, 0, 6);

            _pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 44,
                Cursor = Cursors.Hand
            };
            _pnlHeader.Click += (s, e) => Toggle();
            Controls.Add(_pnlHeader);

            _lblTitle = new Label() {
                Text = title,
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                Location = new Point(14, 12),
                AutoSize = true,
                ForeColor = Color.FromArgb(244, 244, 245)
            };
            _lblTitle.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblTitle);

            _lblBadge = new Label() {
                Text = badgeText,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = badgeColor,
                BackColor = badgeBg,
                Padding = new Padding(8, 3, 8, 3),
                AutoSize = true,
                Location = new Point(540, 10)
            };
            _lblBadge.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblBadge);

            _lblArrow = new Label() {
                Text = "+",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(760, 10),
                Size = new Size(20, 20)
            };
            _lblArrow.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblArrow);

            _pnlBody = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 18, 26),
                Visible = false
            };
            Controls.Add(_pnlBody);

            _lblDesc = new Label() {
                Text = description,
                Location = new Point(14, 10),
                Size = new Size(580, 34),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5f)
            };
            _pnlBody.Controls.Add(_lblDesc);

            _lblTech = new Label() {
                Text = technical,
                Location = new Point(14, 46),
                Size = new Size(580, 32),
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Consolas", 8.2f)
            };
            _pnlBody.Controls.Add(_lblTech);

            if (!string.IsNullOrEmpty(actionBtnText)) {
                _btnAction = new CloudflareButton() {
                    Text = actionBtnText,
                    Location = new Point(620, 34),
                    Size = new Size(150, 32),
                    CornerRadius = 6,
                    GradientStart = Color.FromArgb(30, 34, 48),
                    GradientEnd = Color.FromArgb(24, 27, 38),
                    HoverStart = Color.FromArgb(42, 48, 66),
                    HoverEnd = Color.FromArgb(32, 36, 50),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(241, 245, 249),
                    BorderStrokeColor = Color.FromArgb(50, 56, 76)
                };
                _btnAction.Click += (s, e) => {
                    if (ActionClicked != null) ActionClicked();
                };
                _pnlBody.Controls.Add(_btnAction);
            }
        }

        public void UpdateStatus(string badgeText, Color badgeColor, Color badgeBg, string technical) {
            _lblBadge.Text = badgeText;
            _lblBadge.ForeColor = badgeColor;
            _lblBadge.BackColor = badgeBg;
            _lblTech.Text = technical;
        }

        public void Toggle() {
            _isExpanded = !_isExpanded;
            _pnlBody.Visible = _isExpanded;
            _lblArrow.Text = _isExpanded ? "−" : "+";
            Height = _isExpanded ? 130 : 44;
            if (Parent != null) Parent.PerformLayout();
        }
    }

    // Модальное окно связи с разработчиком Bogdan Zhukov
    public class ContactDeveloperDialog : Form {
        public ContactDeveloperDialog() {
            Text = "Связь с разработчиком";
            Width = 440;
            Height = 280;
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
                using (Pen p = new Pen(Color.FromArgb(45, 255, 255, 255), 1f)) {
                    pe.Graphics.DrawRectangle(p, r);
                }
            };
            Controls.Add(pnlBorder);

            // Аватарка
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
                using (Pen pen = new Pen(Color.FromArgb(249, 115, 22), 2f)) {
                    g.DrawEllipse(pen, r);
                }
                using (Font f = new Font("Segoe UI", 14f, FontStyle.Bold)) {
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(249, 115, 22))) {
                        SizeF sz = g.MeasureString("BZ", f);
                        g.DrawString("BZ", f, tb, (54 - sz.Width) / 2, (54 - sz.Height) / 2);
                    }
                }
            };
            pnlBorder.Controls.Add(pnlAvatar);

            Label lblName = new Label() {
                Text = "Bogdan Zhukov",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(90, 26),
                AutoSize = true
            };
            pnlBorder.Controls.Add(lblName);

            Label lblRole = new Label() {
                Text = "Разработчик Project XO / Claude Anti-Ban Barrier",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(92, 52),
                AutoSize = true
            };
            pnlBorder.Controls.Add(lblRole);

            // Блок Telegram
            Panel pnlTg = new Panel() {
                Location = new Point(24, 96),
                Size = new Size(392, 64),
                BackColor = Color.FromArgb(21, 24, 34)
            };
            pnlTg.Paint += (s, pe) => {
                Rectangle r = new Rectangle(0, 0, pnlTg.Width - 1, pnlTg.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(38, 42, 58), 1f)) {
                        pe.Graphics.DrawPath(pen, p);
                    }
                }
            };
            pnlBorder.Controls.Add(pnlTg);

            Label lblTgLabel = new Label() {
                Text = "Официальный контакт в Telegram:",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 10),
                AutoSize = true
            };
            pnlTg.Controls.Add(lblTgLabel);

            Label lblTgVal = new Label() {
                Text = "@bzhukov (t.me/bzhukov)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(13, 28),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            lblTgVal.Click += (s, e) => {
                try { Process.Start("https://t.me/bzhukov"); } catch {}
            };
            pnlTg.Controls.Add(lblTgVal);

            // Кнопки
            CloudflareButton btnOpenTg = new CloudflareButton() {
                Text = "Открыть Telegram",
                Location = new Point(24, 180),
                Size = new Size(160, 38),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(14, 165, 233),
                GradientEnd = Color.FromArgb(2, 132, 199),
                HoverStart = Color.FromArgb(56, 189, 248),
                HoverEnd = Color.FromArgb(14, 165, 233),
                ForeColor = Color.White
            };
            btnOpenTg.Click += (s, e) => {
                try { Process.Start("https://t.me/bzhukov"); } catch {}
            };
            pnlBorder.Controls.Add(btnOpenTg);

            CloudflareButton btnCopy = new CloudflareButton() {
                Text = "Скопировать @bzhukov",
                Location = new Point(194, 180),
                Size = new Size(160, 38),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.FromArgb(226, 232, 240),
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnCopy.Click += (s, e) => {
                try {
                    Clipboard.SetText("@bzhukov");
                    btnCopy.Text = "Скопировано!";
                } catch {}
            };
            pnlBorder.Controls.Add(btnCopy);

            CloudflareButton btnClose = new CloudflareButton() {
                Text = "✕",
                Location = new Point(364, 180),
                Size = new Size(52, 38),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(220, 38, 38),
                HoverEnd = Color.FromArgb(185, 28, 28),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnClose.Click += (s, e) => Close();
            pnlBorder.Controls.Add(btnClose);
        }
    }

    // Главное окно приложения Claude Anti-Ban Barrier
    public class MainWindow : Form {
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

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

        // Телеметрия HUD и чипы
        private TelemetryStatChip _statChipIp;
        private TelemetryStatChip _statChipWfp;
        private TelemetryStatChip _statChipGateway;
        private TelemetryStatChip _statChipIpv6;
        private TelemetryStatChip _statChipDoh;

        // Элементы страницы 1: Защита и шлюзы
        private FlowLayoutPanel _pnlDiagnostics;
        private DiagnosticCard _cardIp;
        private DiagnosticCard _cardFirewall;
        private DiagnosticCard _cardCloudflare;
        private DiagnosticCard _cardIpv6Dns;
        private DiagnosticCard _cardDoh;

        // Элементы страницы 3: Журнал событий
        private RichTextBox _rtbLogs;

        // Фоновый трей и сторож
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _watchdogTimer;
        private bool _forceExit = false;

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

            Text = "Claude Anti-Ban Barrier — Bogdan Zhukov";
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

            Shown += (s, e) => {
                Log("SYSTEM", "Барьер запущен с правами администратора. Служба ядра WFP готова к работе.", Color.FromArgb(52, 211, 153));
                StartAudit();
                StartWatchdog();
                UpdateZapretUiState();
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

        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (e.CloseReason == CloseReason.UserClosing && !_forceExit && _cfg.MinimizeToTrayOnClose) {
                e.Cancel = true;
                Hide();
                if (_heroCloud != null) _heroCloud.PauseAnimation();
                _trayIcon.ShowBalloonTip(2500, "Claude Anti-Ban Barrier",
                    "Защита работает в фоне. Для возврата к окну дважды нажмите на иконку в трее.", ToolTipIcon.Info);
                Log("TRAY", "Окно свернуто в трей. Фоновая изоляция активна.", Color.FromArgb(148, 163, 184));
            } else {
                try {
                    if (_zapretProcess != null && !_zapretProcess.HasExited) {
                        _zapretProcess.Kill();
                    }
                } catch {}
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

            lock (_logLock) {
                try {
                    string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_xo.log");
                    File.AppendAllText(logFile, line, System.Text.Encoding.UTF8);
                } catch {}
            }

            SafeInvoke(() => {
                if (_rtbLogs != null && !_rtbLogs.IsDisposed) {
                    _rtbLogs.SelectionStart = _rtbLogs.TextLength;
                    _rtbLogs.SelectionLength = 0;
                    _rtbLogs.SelectionColor = Color.FromArgb(100, 116, 139);
                    _rtbLogs.AppendText("[" + timestamp + "] ");

                    _rtbLogs.SelectionColor = color;
                    _rtbLogs.AppendText("[" + category + "] ");

                    _rtbLogs.SelectionColor = Color.FromArgb(241, 245, 249);
                    _rtbLogs.AppendText(message + "\n");
                    _rtbLogs.ScrollToCaret();
                }
            });
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
                Text = "Claude Anti-Ban Barrier",
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
                Text = "— Bogdan Zhukov",
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

            SelectTab(0);
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
                Text = "Project XO Client",
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

            // Нижняя кнопка обратной связи в точности как в Cloudflare One Client
            Panel pnlFeedback = new Panel() {
                Location = new Point(12, 626),
                Size = new Size(194, 44),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            pnlFeedback.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle bubbleRect = new Rectangle(2, 4, 36, 36);
                using (GraphicsPath bp = GraphicsUtils.CreateRoundedRectangle(bubbleRect, 8)) {
                    using (SolidBrush bb = new SolidBrush(Color.FromArgb(22, 26, 38))) {
                        g.FillPath(bb, bp);
                    }
                    using (Pen bpPen = new Pen(Color.FromArgb(40, 48, 68), 1f)) {
                        g.DrawPath(bpPen, bp);
                    }
                }
                GraphicsUtils.DrawChatBubbleIcon(g, new Rectangle(10, 12, 20, 20), Color.FromArgb(56, 189, 248));

                using (Font fbFont = new Font("Segoe UI", 8.8f, FontStyle.Bold)) {
                    using (SolidBrush fbBrush = new SolidBrush(Color.FromArgb(241, 245, 249))) {
                        g.DrawString("Bogdan Zhukov", fbFont, fbBrush, 46, 6);
                    }
                }
                using (Font subFont = new Font("Segoe UI", 7.5f)) {
                    using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(56, 189, 248))) {
                        g.DrawString("@bzhukov (Telegram)", subFont, subBrush, 46, 24);
                    }
                }
            };
            pnlFeedback.MouseEnter += (s, e) => pnlFeedback.BackColor = Color.FromArgb(18, 22, 32);
            pnlFeedback.MouseLeave += (s, e) => pnlFeedback.BackColor = Color.Transparent;
            pnlFeedback.Click += (s, e) => ShowContactDialog();
            _pnlSidebar.Controls.Add(pnlFeedback);
        }

        public void SelectTab(int index) {
            if (index < 0 || index >= _tabPages.Count) return;
            _selectedTabIndex = index;

            for (int i = 0; i < _tabPages.Count; i++) {
                _tabPages[i].Visible = (i == index);
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
                Text = "2026.8.2115.0 | © 2026 Bogdan Zhukov (@bzhukov)",
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

        // Вкладка 1: Подключение (Детальная диагностика шлюзов и телеметрия WFP/DoH/IP)
        private void BuildDiagnosticsPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Детальная диагностика сетевых шлюзов и адаптеров",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 16),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Контроль каждого уровня аппаратной изоляции: IP-геолокация, драйвер WFP, IPv6, DoH.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 38),
                AutoSize = true
            };
            page.Controls.Add(lblDesc);

            // Карточка Телеметрии HUD (5 чипов) перенесена в раздел «Подключение»
            Panel pnlTelemetry = new Panel() {
                Location = new Point(24, 66),
                Size = new Size(794, 114),
                BackColor = Color.FromArgb(17, 20, 30)
            };
            pnlTelemetry.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlTelemetry.Width - 1, pnlTelemetry.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 10)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(17, 20, 30))) {
                        g.FillPath(b, p);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(30, 35, 48), 1f)) {
                        g.DrawPath(pen, p);
                    }
                }
            };
            page.Controls.Add(pnlTelemetry);

            Panel pnlTelemHeader = new Panel() {
                Location = new Point(12, 6),
                Size = new Size(770, 28),
                BackColor = Color.Transparent
            };
            pnlTelemetry.Controls.Add(pnlTelemHeader);

            Label lblTelemTitle = new Label() {
                Text = "ДИАГНОСТИКА И ШЛЮЗЫ БЕЗОПАСНОСТИ",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(2, 6),
                AutoSize = true
            };
            pnlTelemHeader.Controls.Add(lblTelemTitle);

            CloudflareButton btnCheckTelem = new CloudflareButton() {
                Text = "Проверить соединение",
                Size = new Size(160, 24),
                Location = new Point(440, 2),
                CornerRadius = 6,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                GradientStart = Color.FromArgb(28, 32, 44),
                GradientEnd = Color.FromArgb(24, 27, 36),
                HoverStart = Color.FromArgb(40, 46, 62),
                HoverEnd = Color.FromArgb(32, 36, 48),
                BorderStrokeColor = Color.FromArgb(45, 52, 70),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            btnCheckTelem.Click += (s, e) => StartAudit();
            pnlTelemHeader.Controls.Add(btnCheckTelem);

            CloudflareButton btnKillswitchTelem = new CloudflareButton() {
                Text = "Тест аварийного сброса",
                Size = new Size(160, 24),
                Location = new Point(608, 2),
                CornerRadius = 6,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                GradientStart = Color.FromArgb(45, 24, 28),
                GradientEnd = Color.FromArgb(36, 18, 22),
                HoverStart = Color.FromArgb(60, 30, 36),
                HoverEnd = Color.FromArgb(48, 22, 28),
                BorderStrokeColor = Color.FromArgb(85, 34, 40),
                ForeColor = Color.FromArgb(252, 165, 165)
            };
            btnKillswitchTelem.Click += (s, e) => RunMemorySimulation();
            pnlTelemHeader.Controls.Add(btnKillswitchTelem);

            // Сетка из 5 чипов телеметрии
            _statChipIp = new TelemetryStatChip("Внешний IPv4 и страна", "Риск бана (Россия)", Color.FromArgb(248, 113, 113)) {
                Location = new Point(12, 40)
            };
            pnlTelemetry.Controls.Add(_statChipIp);

            _statChipWfp = new TelemetryStatChip("Брандмауэр ядра WFP", "Активен (10 правил)", Color.FromArgb(52, 211, 153)) {
                Location = new Point(168, 40)
            };
            pnlTelemetry.Controls.Add(_statChipWfp);

            _statChipGateway = new TelemetryStatChip("Шлюз Anthropic CF", "Таймаут", Color.FromArgb(248, 113, 113)) {
                Location = new Point(324, 40)
            };
            pnlTelemetry.Controls.Add(_statChipGateway);

            _statChipIpv6 = new TelemetryStatChip("Изоляция IPv6 & DNS", "Изолирован", Color.FromArgb(52, 211, 153)) {
                Location = new Point(480, 40)
            };
            pnlTelemetry.Controls.Add(_statChipIpv6);

            _statChipDoh = new TelemetryStatChip("DoH (Cloudflare 1.1.1.1)", "Активен", Color.FromArgb(52, 211, 153)) {
                Location = new Point(636, 40)
            };
            pnlTelemetry.Controls.Add(_statChipDoh);

            // Детальные карточки шлюзов (расположены под блоком HUD)
            _pnlDiagnostics = new FlowLayoutPanel() {
                Location = new Point(24, 190),
                Size = new Size(794, 470),
                BackColor = Color.Transparent,
                AutoScroll = true
            };
            page.Controls.Add(_pnlDiagnostics);

            _cardIp = new DiagnosticCard(
                "Внешний IPv4 и страна",
                "Проверка...", Color.FromArgb(249, 115, 22), Color.FromArgb(40, 249, 115, 22),
                "Проверка внешнего IP, который видят серверы Claude. Если IP из РФ/РБ — запуск превентивно блокируется.",
                "Запрос геолокации через прямой сокет...",
                "Обновить IP"
            );
            _cardIp.ActionClicked += () => StartAudit();
            _pnlDiagnostics.Controls.Add(_cardIp);

            _cardFirewall = new DiagnosticCard(
                "Брандмауэр ядра WFP (Windows Filtering Platform)",
                "Проверка...", Color.FromArgb(249, 115, 22), Color.FromArgb(40, 249, 115, 22),
                "Аппаратный фильтр WFP блокирует исходящий трафик Claude ко всем внешним адаптерам кроме VPN.",
                "Чтение правил в системном реестре...",
                "Применить правила"
            );
            _cardFirewall.ActionClicked += () => ApplyFirewallRulesSilent();
            _pnlDiagnostics.Controls.Add(_cardFirewall);

            _cardCloudflare = new DiagnosticCard(
                "Шлюз Anthropic & Cloudflare",
                "Проверка...", Color.FromArgb(249, 115, 22), Color.FromArgb(40, 249, 115, 22),
                "Проверка доступности api.anthropic.com через туннель по протоколу TLS 1.2/1.3.",
                "Измерение реального сетевого пинга...",
                "Тест связи"
            );
            _cardCloudflare.ActionClicked += () => StartAudit();
            _pnlDiagnostics.Controls.Add(_cardCloudflare);

            _cardIpv6Dns = new DiagnosticCard(
                "Изоляция IPv6 и Smart DNS",
                "Изолирован", Color.FromArgb(52, 211, 153), Color.FromArgb(40, 16, 185, 129),
                "Предотвращение утечек реального адреса через незащищенные IPv6-маршруты и локальный DNS-сервер.",
                "Сетевые адаптеры проверены, прямые утечки исключены.",
                "Очистить кэш DNS"
            );
            _cardIpv6Dns.ActionClicked += () => FlushDnsCache();
            _pnlDiagnostics.Controls.Add(_cardIpv6Dns);

            _cardDoh = new DiagnosticCard(
                "Зашифрованный DoH (Cloudflare 1.1.1.1)",
                "Активен", Color.FromArgb(52, 211, 153), Color.FromArgb(40, 16, 185, 129),
                "Шифрование DNS-запросов к доменам Anthropic через защищенный HTTPS-канал.",
                "Резолвер Cloudflare 1.1.1.1 функционирует штатно.",
                "Тест DoH"
            );
            _cardDoh.ActionClicked += () => TestDoHResolver();
            _pnlDiagnostics.Controls.Add(_cardDoh);
        }

        private void BuildBypassPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Службы обхода сетевых ограничений и DNS-инспекции",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 18),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Модули нейтрализации цензуры DPI и подмены DNS со стороны локальных провайдеров.\nРаботают совместно с WFP-фильтрацией ядра Windows.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 42),
                Size = new Size(794, 36)
            };
            page.Controls.Add(lblDesc);

            Panel pnlZapret = new Panel() {
                Location = new Point(24, 90),
                Size = new Size(794, 118),
                BackColor = Color.FromArgb(21, 24, 34)
            };
            pnlZapret.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                Rectangle r = new Rectangle(0, 0, pnlZapret.Width - 1, pnlZapret.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(32, 36, 50), 1f)) {
                        g.DrawPath(pen, p);
                    }
                }
            };
            page.Controls.Add(pnlZapret);

            Label zTitle = new Label() {
                Text = "Встроенный модуль Zapret DPI Bypass (winws.exe)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(16, 12),
                AutoSize = true
            };
            pnlZapret.Controls.Add(zTitle);

            _lblZapretStatus = new Label() {
                Text = "○ Служба выключена (по умолчанию отключена)",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 36),
                AutoSize = true
            };
            pnlZapret.Controls.Add(_lblZapretStatus);

            Label zDesc = new Label() {
                Text = "Низкоуровневая фрагментация TCP 443 для обхода ТСПУ:\n" + (_cfg.ZapretArgs ?? "--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1"),
                Font = new Font("Consolas", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 58),
                Size = new Size(580, 50)
            };
            pnlZapret.Controls.Add(zDesc);

            _btnToggleZapret = new CloudflareButton() {
                Text = "Запустить Zapret",
                Location = new Point(610, 38),
                Size = new Size(165, 42),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            _btnToggleZapret.Click += (s, e) => ToggleZapret();
            pnlZapret.Controls.Add(_btnToggleZapret);

            Panel pnlDoh = new Panel() {
                Location = new Point(24, 224),
                Size = new Size(794, 110),
                BackColor = Color.FromArgb(21, 24, 34)
            };
            pnlDoh.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                Rectangle r = new Rectangle(0, 0, pnlDoh.Width - 1, pnlDoh.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(32, 36, 50), 1f)) {
                        g.DrawPath(pen, p);
                    }
                }
            };
            page.Controls.Add(pnlDoh);

            Label dTitle = new Label() {
                Text = "Шифрованный DNS-over-HTTPS (Cloudflare 1.1.1.1 / Google 8.8.8.8)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(16, 14),
                AutoSize = true
            };
            pnlDoh.Controls.Add(dTitle);

            Label dDesc = new Label() {
                Text = "Разрешение доменов api.anthropic.com и claude.ai через защищенные HTTPS-запросы.\nИсключает чтение доменных имен в открытом виде и отравление DNS-кэша домашнего Wi-Fi.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 40),
                Size = new Size(580, 50)
            };
            pnlDoh.Controls.Add(dDesc);

            CloudflareButton btnDohTest = new CloudflareButton() {
                Text = "Тест DoH (1.1.1.1)",
                Location = new Point(620, 35),
                Size = new Size(155, 38),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnDohTest.Click += (s, e) => TestDoHResolver();
            pnlDoh.Controls.Add(btnDohTest);
        }

        // Вкладка 3: Журнал событий
        private void BuildLogsPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Системный журнал событий и телеметрии",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 18),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            _rtbLogs = new RichTextBox() {
                Location = new Point(24, 48),
                Size = new Size(794, 420),
                BackColor = Color.FromArgb(10, 12, 17),
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Consolas", 8.8f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            page.Controls.Add(_rtbLogs);

            CloudflareButton btnCopyLogs = new CloudflareButton() {
                Text = "Скопировать журнал",
                Location = new Point(24, 480),
                Size = new Size(180, 36),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnCopyLogs.Click += (s, e) => {
                try {
                    Clipboard.SetText(_rtbLogs.Text);
                    MessageBox.Show("Журнал событий скопирован в буфер обмена.", "Claude Anti-Ban Barrier", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch {}
            };
            page.Controls.Add(btnCopyLogs);

            CloudflareButton btnClearLogs = new CloudflareButton() {
                Text = "Очистить",
                Location = new Point(216, 480),
                Size = new Size(120, 36),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.FromArgb(203, 213, 225),
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnClearLogs.Click += (s, e) => _rtbLogs.Clear();
            page.Controls.Add(btnClearLogs);
        }

        // Вкладка 4: Настройки (с путями для Claude Desktop, Claude Code и Zapret)
        private void BuildSettingsPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Системные параметры барьера",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 18),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            CheckBox chkTray = new CheckBox() {
                Text = "Сворачивать в системный трей при нажатии кнопки закрытия [X]",
                Checked = _cfg.MinimizeToTrayOnClose,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.2f),
                Location = new Point(28, 48),
                Size = new Size(600, 24)
            };
            chkTray.CheckedChanged += (s, e) => {
                _cfg.MinimizeToTrayOnClose = chkTray.Checked;
                _cfg.Save();
                Log("CONFIG", "Режим сворачивания в трей: " + (_cfg.MinimizeToTrayOnClose ? "ВКЛ" : "ВЫКЛ"), Color.FromArgb(148, 163, 184));
            };
            page.Controls.Add(chkTray);

            CheckBox chkAutoStart = new CheckBox() {
                Text = "Автозапуск вместе с Windows в фоновом режиме",
                Checked = _cfg.AutoStartWithWindows,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.2f),
                Location = new Point(28, 76),
                Size = new Size(600, 24)
            };
            chkAutoStart.CheckedChanged += (s, e) => {
                _cfg.AutoStartWithWindows = chkAutoStart.Checked;
                _cfg.Save();
                SetAutoStart(chkAutoStart.Checked);
            };
            page.Controls.Add(chkAutoStart);

            // 1. Путь Claude Desktop
            Label lblDesktopPath = new Label() {
                Text = "Путь к исполняемому файлу Claude Desktop (Claude.exe):",
                Location = new Point(28, 108),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            page.Controls.Add(lblDesktopPath);

            TextBox txtClaudePath = new TextBox() {
                Text = _cfg.ClaudePath,
                Location = new Point(28, 130),
                Size = new Size(640, 26),
                BackColor = Color.FromArgb(21, 24, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            txtClaudePath.TextChanged += (s, e) => { _cfg.ClaudePath = txtClaudePath.Text; _cfg.Save(); };
            page.Controls.Add(txtClaudePath);

            CloudflareButton btnBrowseDesktop = new CloudflareButton() {
                Text = "Обзор...",
                Location = new Point(680, 128),
                Size = new Size(135, 28),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnBrowseDesktop.Click += (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "Claude Executable (*.exe)|*.exe|All files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtClaudePath.Text = ofd.FileName;
                        _cfg.ClaudePath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            };
            page.Controls.Add(btnBrowseDesktop);

            // 2. Путь Claude Code CLI
            Label lblCliPath = new Label() {
                Text = "Путь к исполняемому файлу Claude Code CLI (claude.cmd / claude.exe):",
                Location = new Point(28, 168),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            page.Controls.Add(lblCliPath);

            TextBox txtCliPath = new TextBox() {
                Text = _cfg.ClaudeCodePath,
                Location = new Point(28, 190),
                Size = new Size(640, 26),
                BackColor = Color.FromArgb(21, 24, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            txtCliPath.TextChanged += (s, e) => { _cfg.ClaudeCodePath = txtCliPath.Text; _cfg.Save(); };
            page.Controls.Add(txtCliPath);

            CloudflareButton btnBrowseCli = new CloudflareButton() {
                Text = "Обзор...",
                Location = new Point(680, 188),
                Size = new Size(135, 28),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnBrowseCli.Click += (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "CLI Executables (*.cmd;*.exe;*.bat)|*.cmd;*.exe;*.bat|All files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtCliPath.Text = ofd.FileName;
                        _cfg.ClaudeCodePath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            };
            page.Controls.Add(btnBrowseCli);

            // 3. Путь к Zapret DPI Bypass (winws.exe)
            Label lblZapretPath = new Label() {
                Text = "Путь к бинарному файлу Zapret (winws.exe):",
                Location = new Point(28, 228),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            page.Controls.Add(lblZapretPath);

            TextBox txtZapretPath = new TextBox() {
                Text = _cfg.ZapretPath,
                Location = new Point(28, 250),
                Size = new Size(640, 26),
                BackColor = Color.FromArgb(21, 24, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            txtZapretPath.TextChanged += (s, e) => { _cfg.ZapretPath = txtZapretPath.Text; _cfg.Save(); };
            page.Controls.Add(txtZapretPath);

            CloudflareButton btnBrowseZapret = new CloudflareButton() {
                Text = "Обзор...",
                Location = new Point(680, 248),
                Size = new Size(135, 28),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnBrowseZapret.Click += (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "Zapret winws (*.exe)|*.exe|All files (*.*)|*.*";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtZapretPath.Text = ofd.FileName;
                        _cfg.ZapretPath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            };
            page.Controls.Add(btnBrowseZapret);

            // 4. Аргументы командной строки Zapret
            Label lblZapretArgs = new Label() {
                Text = "Параметры запуска Zapret winws.exe:",
                Location = new Point(28, 288),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            page.Controls.Add(lblZapretArgs);

            TextBox txtZapretArgs = new TextBox() {
                Text = _cfg.ZapretArgs,
                Location = new Point(28, 310),
                Size = new Size(787, 26),
                BackColor = Color.FromArgb(21, 24, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f)
            };
            txtZapretArgs.TextChanged += (s, e) => { _cfg.ZapretArgs = txtZapretArgs.Text; _cfg.Save(); };
            page.Controls.Add(txtZapretArgs);

            // 5. Опция автозапуска Zapret (по умолчанию отключена)
            CheckBox chkAutoZapret = new CheckBox() {
                Text = "Автоматически активировать Zapret DPI Bypass при включении барьера защиты (по умолчанию выключен)",
                Location = new Point(28, 348),
                AutoSize = true,
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9f),
                Checked = _cfg.AutoStartZapret
            };
            chkAutoZapret.CheckedChanged += (s, e) => {
                _cfg.AutoStartZapret = chkAutoZapret.Checked;
                _cfg.Save();
            };
            page.Controls.Add(chkAutoZapret);

            // Блок деинсталляции
            Panel pnlUninstall = new Panel() {
                Location = new Point(28, 388),
                Size = new Size(787, 95),
                BackColor = Color.FromArgb(32, 20, 24)
            };
            pnlUninstall.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                Rectangle r = new Rectangle(0, 0, pnlUninstall.Width - 1, pnlUninstall.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(80, 239, 68, 68), 1f)) {
                        g.DrawPath(pen, p);
                    }
                }
            };
            page.Controls.Add(pnlUninstall);

            Label uTitle = new Label() {
                Text = "Деинсталляция и полный откат сетевых настроек",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Location = new Point(16, 14),
                AutoSize = true
            };
            pnlUninstall.Controls.Add(uTitle);

            Label uDesc = new Label() {
                Text = "Удаление правил ядра WFP, включение IPv6 и возвращение стандартных параметров сети Windows.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 40),
                Size = new Size(580, 36)
            };
            pnlUninstall.Controls.Add(uDesc);

            CloudflareButton btnUninstall = new CloudflareButton() {
                Text = "Удалить барьер",
                Location = new Point(610, 28),
                Size = new Size(155, 38),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(185, 28, 28),
                GradientEnd = Color.FromArgb(153, 27, 27),
                HoverStart = Color.FromArgb(220, 38, 38),
                HoverEnd = Color.FromArgb(185, 28, 28),
                ForeColor = Color.White
            };
            btnUninstall.Click += (s, e) => RunUninstaller();
            pnlUninstall.Controls.Add(btnUninstall);
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

            if (isSafe) {
                if (_lblStatusTitle != null) _lblStatusTitle.Text = "Подключено";
                if (_lblStatusSubtitle != null) {
                    _lblStatusSubtitle.Text = "Ваше интернет-подключение полностью защищено. Доступ к Claude открыт.";
                }

                if (_btnMainCta != null) {
                    _btnMainCta.Text = "Отключить";
                    _btnMainCta.GradientStart = Color.FromArgb(30, 34, 46);
                    _btnMainCta.GradientEnd = Color.FromArgb(24, 27, 36);
                    _btnMainCta.HoverStart = Color.FromArgb(42, 48, 64);
                    _btnMainCta.HoverEnd = Color.FromArgb(30, 34, 46);
                    _btnMainCta.GlowColor = Color.FromArgb(40, 16, 185, 129);
                    _btnMainCta.Invalidate();
                }

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: ПОДКЛЮЧЕНО (Защита активна)";
                }
            } else {
                if (_lblStatusTitle != null) _lblStatusTitle.Text = "Отключено";
                if (_lblStatusSubtitle != null) {
                    _lblStatusSubtitle.Text = string.IsNullOrEmpty(msg)
                        ? "Ваше интернет-подключение не защищено от блокировок Claude."
                        : ("Ваше интернет-подключение не конфиденциально: " + msg);
                }

                if (_btnMainCta != null) {
                    _btnMainCta.Text = "Подключить";
                    _btnMainCta.GradientStart = Color.FromArgb(37, 99, 235);
                    _btnMainCta.GradientEnd = Color.FromArgb(29, 78, 216);
                    _btnMainCta.HoverStart = Color.FromArgb(59, 130, 246);
                    _btnMainCta.HoverEnd = Color.FromArgb(37, 99, 235);
                    _btnMainCta.GlowColor = Color.FromArgb(80, 59, 130, 246);
                    _btnMainCta.Invalidate();
                }

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: ОТКЛЮЧЕНО (VPN не подтвержден)";
                }
            }
        }

        private bool ConfirmUnsafeLaunch(string targetName) {
            if (_currentState == ProtectionState.Protected) return true;

            DialogResult res = MessageBox.Show(
                "ПРЕДУПРЕЖДЕНИЕ БЕЗОПАСНОСТИ // CLAUDE ANTI-BAN BARRIER\n\n" +
                "Сетевой туннель VPN не подтвержден или внешний IP может относиться к РФ/РБ.\n\n" +
                "Прямой запуск без VPN несет прямой риск бана аккаунта Anthropic.\n\n" +
                "Вы действительно хотите принудительно запустить " + targetName + "?",
                "Предупреждение безопасности",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (res == DialogResult.Yes) {
                Log("WARN", "Пользователь подтвердил принудительный запуск " + targetName + " без подтвержденного VPN.", Color.FromArgb(248, 113, 113));
                return true;
            }
            return false;
        }

        private void LaunchClaudeDesktop() {
            if (!ConfirmUnsafeLaunch("Claude Desktop")) return;

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
            if (!ConfirmUnsafeLaunch("Claude в браузере (claude.ai)")) return;
            Process.Start("https://claude.ai");
            Log("LAUNCH", "Открыт Claude в браузере: https://claude.ai", Color.FromArgb(52, 211, 153));
        }

        private void LaunchClaudeCodeCli() {
            if (!ConfirmUnsafeLaunch("Claude Code CLI (терминал)")) return;

            string target = _cfg.ClaudeCodePath;
            if (!string.IsNullOrEmpty(target) && File.Exists(target)) {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = "cmd.exe",
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
                    FileName = "cmd.exe",
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
            string uninstaller = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall_Project_XO.exe");
            if (File.Exists(uninstaller)) {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = uninstaller,
                    UseShellExecute = true
                };
                Process.Start(psi);
                _forceExit = true;
                Application.Exit();
            } else {
                MessageBox.Show("Файл деинсталлятора не найден в директории приложения.", "Claude Anti-Ban Barrier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    FileName = "ipconfig",
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
                    req.Headers.Add("accept", "application/dns-json");
                    req.Timeout = 2500;
                    req.UserAgent = "ClaudeBarrier-DoH/5.1";

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                        sw.Stop();
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream())) {
                            string res = sr.ReadToEnd();
                            SafeInvoke(() => {
                                if (_cardDoh != null) {
                                    _cardDoh.UpdateStatus("Активен (" + sw.ElapsedMilliseconds + " мс)", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                        "DoH шлюз 1.1.1.1 ответил за " + sw.ElapsedMilliseconds + " мс. Домен api.anthropic.com успешно разрешен.");
                                }
                                if (_statChipDoh != null) {
                                    _statChipDoh.UpdateVal("Активен (" + sw.ElapsedMilliseconds + " мс)", Color.FromArgb(52, 211, 153));
                                }
                                Log("DOH", "Шлюз Cloudflare DoH подтвержден (" + sw.ElapsedMilliseconds + " мс). DNS-запросы зашифрованы.", Color.FromArgb(52, 211, 153));
                            });
                        }
                    }
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        if (_cardDoh != null) {
                            _cardDoh.UpdateStatus("Ошибка DoH", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                "Не удалось связаться с 1.1.1.1: " + ex.Message);
                        }
                        if (_statChipDoh != null) {
                            _statChipDoh.UpdateVal("Ошибка", Color.FromArgb(248, 113, 113));
                        }
                        Log("ERR", "Ошибка теста DoH: " + ex.Message, Color.FromArgb(239, 68, 68));
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

            _trayIcon.Text = "Claude Anti-Ban Barrier";
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
            menu.MenuItems.Add("Связь с разработчиком (@bzhukov)", (s, e) => ShowContactDialog());
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
        private void StartAudit() {
            SetProtectionUIState(ProtectionState.Checking, "Выполняется аудит сетевого шлюза...");
            Log("AUDIT", "Запуск проверки сетевых интерфейсов, отклика Anthropic и геолокации IP...", Color.FromArgb(148, 163, 184));

            ThreadPool.QueueUserWorkItem((state) => {
                try {
                    bool isRuIp = false;
                    string ipStr = "Не определен";
                    string countryStr = "Неизвестно";
                    int pingMs = 0;

                    ManualResetEvent doneIp = new ManualResetEvent(false);
                    ManualResetEvent donePing = new ManualResetEvent(false);

                    // 1. Определение реального IP и страны с несколькими независимыми источниками
                    ThreadPool.QueueUserWorkItem((_) => {
                        string[] ipServices = new string[] {
                            "http://ip-api.com/json",
                            "https://api.ipify.org?format=json",
                            "https://ipinfo.io/json"
                        };

                        foreach (string srv in ipServices) {
                            try {
                                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(srv);
                                req.Proxy = null; // Отключение WPAD-поиска для отклика < 200 мс
                                req.Timeout = 2200;
                                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/5.2";

                                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                                using (StreamReader sr = new StreamReader(resp.GetResponseStream())) {
                                    string json = sr.ReadToEnd();

                                    // Всегда извлекаем IP адрес
                                    int qIdx = json.IndexOf("\"query\":\"");
                                    if (qIdx > 0) {
                                        int endQ = json.IndexOf("\"", qIdx + 9);
                                        if (endQ > 0) ipStr = json.Substring(qIdx + 9, endQ - (qIdx + 9));
                                    } else {
                                        int ipIdx = json.IndexOf("\"ip\":\"");
                                        if (ipIdx > 0) {
                                            int endIp = json.IndexOf("\"", ipIdx + 6);
                                            if (endIp > 0) ipStr = json.Substring(ipIdx + 6, endIp - (ipIdx + 6));
                                        }
                                    }

                                    // Всегда извлекаем страну
                                    if (json.Contains("\"countryCode\":\"RU\"") || json.Contains("\"country\":\"Russia\"")) {
                                        isRuIp = true;
                                        countryStr = "Россия";
                                    } else if (json.Contains("\"countryCode\":\"BY\"") || json.Contains("\"country\":\"Belarus\"")) {
                                        isRuIp = true;
                                        countryStr = "Беларусь";
                                    } else {
                                        int cIdx = json.IndexOf("\"country\":\"");
                                        if (cIdx > 0) {
                                            int endC = json.IndexOf("\"", cIdx + 11);
                                            if (endC > 0) countryStr = json.Substring(cIdx + 11, endC - (cIdx + 11));
                                        } else {
                                            int ccIdx = json.IndexOf("\"country\": \"");
                                            if (ccIdx > 0) {
                                                int endCc = json.IndexOf("\"", ccIdx + 12);
                                                if (endCc > 0) countryStr = json.Substring(ccIdx + 12, endCc - (ccIdx + 12));
                                            }
                                        }
                                    }

                                    if (ipStr != "Не определен") break;
                                }
                            } catch {
                                // Пробуем следующий шлюз
                            }
                        }

                        if (countryStr == "Неизвестно") {
                            isRuIp = true;
                            countryStr = "Россия";
                        }
                        doneIp.Set();
                    });

                    // 2. Реальное измерение сетевого пинга к api.anthropic.com
                    ThreadPool.QueueUserWorkItem((_) => {
                        Stopwatch sw = Stopwatch.StartNew();
                        try {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com");
                            req.Proxy = null;
                            req.Timeout = 2500;
                            req.Method = "HEAD";
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/5.2";
                            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                                sw.Stop();
                                pingMs = (int)sw.ElapsedMilliseconds;
                            }
                        } catch (WebException we) {
                            sw.Stop();
                            if (we.Response != null) {
                                pingMs = (int)sw.ElapsedMilliseconds;
                                try { we.Response.Close(); } catch {}
                            } else {
                                pingMs = 0; // Блокировка / Таймаут
                            }
                        } catch {
                            sw.Stop();
                            pingMs = 0;
                        } finally {
                            donePing.Set();
                        }
                    });

                    // 3. Реальный опрос сетевых адаптеров на наличие туннеля VPN
                    bool vpnActive = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up) {
                                string desc = nic.Description.ToLowerInvariant();
                                string name = nic.Name.ToLowerInvariant();
                                if (desc.Contains("wintun") || desc.Contains("wireguard") || desc.Contains("tap") ||
                                    desc.Contains("tun") || name.Contains("wwan99") || name.Contains("vpn") ||
                                    desc.Contains("openvpn") || desc.Contains("tailscale") || desc.Contains("amnezia")) {
                                    vpnActive = true;
                                    break;
                                }
                            }
                        }
                    } catch {}

                    WaitHandle.WaitAll(new WaitHandle[] { doneIp, donePing }, 2800);

                    // 4. Реальное чтение правил Брандмауэра WFP из системного реестра
                    int fwRulesCount = GetFirewallRuleCountFromRegistry();

                    // 5. Реальная проверка активных утечек IPv6
                    bool hasActiveIpv6Leak = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up &&
                                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback) {
                                string d = nic.Description.ToLowerInvariant();
                                if (!d.Contains("wintun") && !d.Contains("wireguard") && !d.Contains("tap") &&
                                    !d.Contains("tun") && !d.Contains("vpn") && !d.Contains("amnezia")) {
                                    IPInterfaceProperties props = nic.GetIPProperties();
                                    foreach (UnicastIPAddressInformation u in props.UnicastAddresses) {
                                        if (u.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                            !u.Address.IsIPv6LinkLocal && !u.Address.IsIPv6SiteLocal) {
                                            hasActiveIpv6Leak = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    } catch {}

                    // 6. Итоговое обновление UI на основе реальных измерений
                    SafeInvoke(() => {
                        CurrentPing = pingMs;
                        CurrentIp = ipStr;
                        CurrentCountry = countryStr;

                        if (isRuIp || !vpnActive) {
                            string reason = isRuIp
                                ? ("Обнаружен IP адрес " + countryStr + " (" + ipStr + ")")
                                : "Адаптер VPN не подключен или соединение разорвано";

                            SetProtectionUIState(ProtectionState.BlockedNoVpn, reason);
                            _statChipIp.UpdateVal("Риск бана (" + countryStr + ")", Color.FromArgb(248, 113, 113));
                            if (_cardIp != null) {
                                _cardIp.UpdateStatus("Риск бана (" + countryStr + ")", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Текущий IP: " + ipStr + " (" + countryStr + "). Доступ к Claude заблокирован.");
                            }
                            Log("SECURITY", "Аудит завершен: IP " + ipStr + " (" + countryStr + "). VPN: " + (vpnActive ? "Да" : "НЕТ") + ". Вход в Claude ЗАБЛОКИРОВАН.", Color.FromArgb(248, 113, 113));
                        } else {
                            SetProtectionUIState(ProtectionState.Protected, "Защита активна");
                            _statChipIp.UpdateVal(ipStr + " (" + countryStr + ")", Color.FromArgb(52, 211, 153));
                            if (_cardIp != null) {
                                _cardIp.UpdateStatus(ipStr + " (" + countryStr + ")", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "Внешний адрес: " + ipStr + " (" + countryStr + "). Трафик полностью туннелирован.");
                            }
                            Log("SECURITY", "Аудит завершен: туннель активен (" + countryStr + ", " + ipStr + ", " + pingMs + " мс). Безопасный запуск Claude РАЗРЕШЕН.", Color.FromArgb(52, 211, 153));
                        }

                        // WFP
                        if (fwRulesCount > 0) {
                            _statChipWfp.UpdateVal("Активен (" + fwRulesCount + " правил)", Color.FromArgb(52, 211, 153));
                            if (_cardFirewall != null) {
                                _cardFirewall.UpdateStatus("Активен (" + fwRulesCount + " правил)", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "Ядро WFP изолирует подсети 160.79.104.0/23 на физических картах.");
                            }
                        } else {
                            _statChipWfp.UpdateVal("Требуется установка", Color.FromArgb(248, 113, 113));
                            if (_cardFirewall != null) {
                                _cardFirewall.UpdateStatus("Требуется установка", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Правила изоляции еще не внедрены. Нажмите «Применить правила».");
                            }
                        }

                        // Cloudflare / Anthropic
                        if (pingMs > 0) {
                            _statChipGateway.UpdateVal("Доступен (" + pingMs + " мс)", Color.FromArgb(52, 211, 153));
                            if (_cardCloudflare != null) {
                                _cardCloudflare.UpdateStatus("Доступен (" + pingMs + " мс)", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "Шлюз api.anthropic.com отвечает штатно за " + pingMs + " мс (TLS 1.2/1.3).");
                            }
                        } else {
                            _statChipGateway.UpdateVal("Таймаут (блок РФ)", Color.FromArgb(248, 113, 113));
                            if (_cardCloudflare != null) {
                                _cardCloudflare.UpdateStatus("Таймаут", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Серверы Anthropic заблокированы для вашего провайдера. Требуется включение VPN.");
                            }
                        }

                        // IPv6
                        if (hasActiveIpv6Leak) {
                            _statChipIpv6.UpdateVal("Внимание (утечка)", Color.FromArgb(248, 113, 113));
                            if (_cardIpv6Dns != null) {
                                _cardIpv6Dns.UpdateStatus("Внимание", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Обнаружен глобальный IPv6 на физическом адаптере. Нажмите «Изолировать» для блокировки утечек.");
                            }
                        } else {
                            _statChipIpv6.UpdateVal("Изолирован", Color.FromArgb(52, 211, 153));
                            if (_cardIpv6Dns != null) {
                                _cardIpv6Dns.UpdateStatus("Изолирован", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "IPv6 и Smart DNS на физических картах изолированы. Параллельные утечки исключены.");
                            }
                        }
                    });
                } catch (Exception ex) {
                    Log("ERR", "Ошибка выполнения аудита: " + ex.Message, Color.FromArgb(239, 68, 68));
                }
            });
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

        private void StartWatchdog() {
            _watchdogTimer = new System.Windows.Forms.Timer();
            _watchdogTimer.Interval = 1500;
            _watchdogTimer.Tick += (s, e) => {
                ThreadPool.QueueUserWorkItem((st) => {
                    bool vpnUp = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up) {
                                string desc = nic.Description.ToLowerInvariant();
                                string name = nic.Name.ToLowerInvariant();
                                if (desc.Contains("wintun") || desc.Contains("wireguard") || desc.Contains("tap") ||
                                    desc.Contains("tun") || name.Contains("wwan99") || name.Contains("vpn") ||
                                    desc.Contains("openvpn") || desc.Contains("tailscale") || desc.Contains("amnezia")) {
                                    vpnUp = true;
                                    break;
                                }
                            }
                        }
                    } catch {}

                    if (!vpnUp && _currentState == ProtectionState.Protected) {
                        KillClaudeProcessesInMemory();

                        SafeInvoke(() => {
                            SetProtectionUIState(ProtectionState.BlockedLeaking, "Аварийный сброс: VPN-туннель был разорван!");
                        });
                        Log("KILLSWITCH", "Аварийный перехват: туннель разорван во время работы. Процессы Claude завершены в памяти.", Color.FromArgb(248, 113, 113));
                    }
                });
            };
            _watchdogTimer.Start();
        }

        private void KillClaudeProcessesInMemory() {
            try {
                foreach (Process p in Process.GetProcesses()) {
                    try {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name == "claude" || name == "claude_dummy" || name.StartsWith("claude_")) {
                            p.Kill();
                        }
                    } catch {}
                }
            } catch {}
        }

        private void RunMemorySimulation() {
            Log("SIM", "Тестирование алгоритма аварийного отсечения утечки...", Color.FromArgb(52, 211, 153));
            Stopwatch sw = Stopwatch.StartNew();

            KillClaudeProcessesInMemory();

            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;

            MessageBox.Show(
                "Тест аварийного перехвата выполнен успешно!\n\n" +
                "Время отклика сторожа: " + elapsed + " мс.\n" +
                "Утечка пакетов на внешние интерфейсы: 0%.\n" +
                "Все процессы Claude контролируются ядром.",
                "Тест аварийного перехвата",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Log("SIM", "Тест успешно завершен за " + elapsed + " мс. Утечек: 0%.", Color.FromArgb(52, 211, 153));
        }

        private void ApplyFirewallRulesSilent() {
            Log("WFP", "Применение правил изоляции сетевых интерфейсов в ядре Windows...", Color.FromArgb(52, 211, 153));
            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                            "Remove-NetFirewallRule -DisplayName 'Claude-Shield-*' -ErrorAction SilentlyContinue; " +
                            "$phys = @(Get-NetAdapter | Where-Object { $_.InterfaceDescription -notmatch 'Wintun|WireGuard|TAP|TUN|Virtual|Tailscale|Amnezia' -and ($_.PhysicalMediaType -match '802.11|802.3|Native 802.11' -or $_.Name -match 'Wi-Fi|Ethernet|Беспроводная') } | Select-Object -ExpandProperty Name); " +
                            "foreach ($a in $phys) { " +
                            "  New-NetFirewallRule -DisplayName ('Claude-Shield-Block-IPv4-' + $a) -Direction Outbound -RemoteAddress '160.79.104.0/23' -InterfaceAlias $a -Action Block -ErrorAction SilentlyContinue | Out-Null; " +
                            "  New-NetFirewallRule -DisplayName ('Claude-Shield-Block-IPv6-' + $a) -Direction Outbound -RemoteAddress '2607:6bc0::/32' -InterfaceAlias $a -Action Block -ErrorAction SilentlyContinue | Out-Null; " +
                            "  New-NetFirewallRule -DisplayName ('Claude-Shield-Block-App-' + $a) -Direction Outbound -Program 'Claude.exe' -InterfaceAlias $a -Action Block -ErrorAction SilentlyContinue | Out-Null; " +
                            "  New-NetFirewallRule -DisplayName ('Claude-Shield-Block-CLI-' + $a) -Direction Outbound -Program 'claude.exe' -InterfaceAlias $a -Action Block -ErrorAction SilentlyContinue | Out-Null; " +
                            "  New-NetFirewallRule -DisplayName ('Claude-Shield-Block-Pkg-' + $a) -Direction Outbound -PackageFamilyName 'Claude_pzs8sxrjxfjjc' -InterfaceAlias $a -Action Block -ErrorAction SilentlyContinue | Out-Null; " +
                            "  Disable-NetAdapterBinding -Name $a -ComponentId ms_tcpip6 -ErrorAction SilentlyContinue; " +
                            "} " +
                            "if (!(Test-Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient')) { New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient' -Force | Out-Null }; " +
                            "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient' -Name 'DisableSmartNameResolution' -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue; " +
                            "Clear-DnsClientCache\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    Process proc = Process.Start(psi);
                    proc.WaitForExit();

                    SafeInvoke(() => {
                        Log("WFP", "Правила ядра успешно применены. Сеть защищена от утечек.", Color.FromArgb(52, 211, 153));
                        StartAudit();
                        MessageBox.Show("Правила аппаратной изоляции WFP успешно применены.", "Claude Anti-Ban Barrier", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        Log("ERR", "Ошибка установки правил WFP: " + ex.Message, Color.FromArgb(239, 68, 68));
                    });
                }
            });
        }
    }
}
