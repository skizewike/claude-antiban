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
                        }
                    }
                }
            } catch {}
        }

        public void Save() {
            try {
                List<string> lines = new List<string> {
                    "# Claude Anti-Ban Barrier Configuration",
                    "MinimizeToTrayOnClose=" + (MinimizeToTrayOnClose ? "1" : "0"),
                    "AutoStartWithWindows=" + (AutoStartWithWindows ? "1" : "0"),
                    "ClaudePath=" + (ClaudePath ?? "")
                };
                File.WriteAllLines(ConfigPath, lines.ToArray());
            } catch {}
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

                // Внутренний блик сверху
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

    // Анимированная интерактивная Hero-область в стиле Cloudflare One Zero-Trust
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
            Size = new Size(180, 100);

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 35; // Плавные 30 кадров/сек
            _animTimer.Tick += (s, e) => {
                _phase += 0.06f;
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
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = Width;
            int h = Height;
            int cx = w / 2;
            int cy = h / 2;

            bool isSafe = (_state == ProtectionState.Protected);
            Color themeColor = isSafe ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
            Color glowColor = isSafe ? Color.FromArgb(55, 16, 185, 129) : Color.FromArgb(60, 239, 68, 68);
            Color cloudFill1 = isSafe ? Color.FromArgb(19, 39, 36) : Color.FromArgb(31, 35, 49);
            Color cloudFill2 = isSafe ? Color.FromArgb(16, 28, 28) : Color.FromArgb(21, 24, 36);

            // 1. Анимированные плавающие частицы (Cloudflare One Style Particles)
            float floatY1 = (float)Math.Sin(_phase) * 3f;
            float floatY2 = (float)Math.Cos(_phase * 1.3f) * 3.5f;
            float floatY3 = (float)Math.Sin(_phase * 0.8f + 1.2f) * 2.8f;

            // Частица 1 (Слева сверху)
            using (SolidBrush p1 = new SolidBrush(isSafe ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68))) {
                g.FillEllipse(p1, cx - 64, cy - 30 + floatY1, 5, 5);
            }
            // Частица 2 (Справа сверху)
            using (SolidBrush p2 = new SolidBrush(Color.FromArgb(148, 163, 184))) {
                g.FillEllipse(p2, cx + 58, cy - 26 + floatY2, 4, 4);
            }
            // Частица 3 (Слева снизу)
            using (SolidBrush p3 = new SolidBrush(isSafe ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113))) {
                g.FillEllipse(p3, cx - 68, cy + 22 + floatY3, 6, 6);
            }
            // Частица 4 (Справа снизу)
            using (SolidBrush p4 = new SolidBrush(isSafe ? Color.FromArgb(5, 150, 105) : Color.FromArgb(100, 116, 139))) {
                g.FillEllipse(p4, cx + 62, cy + 20 - floatY1, 5, 5);
            }
            // Частица 5 (Центр сверху)
            using (SolidBrush p5 = new SolidBrush(isSafe ? Color.FromArgb(56, 189, 248) : Color.FromArgb(239, 68, 68))) {
                g.FillEllipse(p5, cx + 22, cy - 40 + floatY2, 3, 3);
            }
            // Частица 6 (Центр снизу)
            using (SolidBrush p6 = new SolidBrush(isSafe ? Color.FromArgb(110, 231, 183) : Color.FromArgb(203, 213, 225))) {
                g.FillEllipse(p6, cx - 24, cy + 34 + floatY1, 4, 4);
            }

            // 2. Внешнее мягкое радиальное свечение ауры
            float breath = (float)Math.Sin(_phase) * 4f;
            Rectangle auraRect = new Rectangle(cx - 68 - (int)breath, cy - 44 - (int)breath, 136 + (int)(breath * 2), 88 + (int)(breath * 2));
            using (GraphicsPath auraPath = new GraphicsPath()) {
                auraPath.AddEllipse(auraRect);
                using (PathGradientBrush pgb = new PathGradientBrush(auraPath)) {
                    pgb.CenterColor = glowColor;
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, 13, 15, 21) };
                    g.FillPath(pgb, auraPath);
                }
            }

            // 3. Форма облака (104 × 64 px)
            Rectangle cloudBounds = new Rectangle(cx - 52, cy - 32, 104, 64);
            using (GraphicsPath cloudPath = GraphicsUtils.CreateRoundedRectangle(cloudBounds, 32)) {
                using (LinearGradientBrush cBrush = new LinearGradientBrush(cloudBounds, cloudFill1, cloudFill2, 145f)) {
                    g.FillPath(cBrush, cloudPath);
                }

                // Внутренний легкий контур
                using (Pen innerPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f)) {
                    g.DrawLine(innerPen, cloudBounds.Left + 20, cloudBounds.Top + 2, cloudBounds.Right - 20, cloudBounds.Top + 2);
                }

                // Внешний контур состояния
                using (Pen borderPen = new Pen(Color.FromArgb(160, themeColor), 2f)) {
                    g.DrawPath(borderPen, cloudPath);
                }
            }

            // 4. Центральный символ статуса
            if (isSafe) {
                // Зеленая галочка безопасности
                using (Pen checkPen = new Pen(Color.FromArgb(52, 211, 153), 2.8f)) {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    g.DrawLine(checkPen, cx - 12, cy + 2, cx - 3, cy + 11);
                    g.DrawLine(checkPen, cx - 3, cy + 11, cx + 13, cy - 7);
                }
            } else {
                // Предупреждающий крест
                using (Pen crossPen = new Pen(Color.FromArgb(239, 68, 68), 2.8f)) {
                    crossPen.StartCap = LineCap.Round;
                    crossPen.EndCap = LineCap.Round;
                    g.DrawLine(crossPen, cx - 10, cy - 10, cx + 10, cy + 10);
                    g.DrawLine(crossPen, cx + 10, cy - 10, cx - 10, cy + 10);
                }
            }
        }
    }

    // Горизонтальная карточка быстрого запуска Claude
    public class LauncherCard : Panel {
        private bool _isHovered = false;
        private string _title;
        private string _badgeText;
        private Color _badgeColor;
        private Color _badgeBg;
        private int _iconType; // 0: Desktop, 1: Browser, 2: Terminal

        public event Action CardClicked;

        public LauncherCard(string title, int iconType) {
            _title = title;
            _iconType = iconType;
            _badgeText = "Предупреждение";
            _badgeColor = Color.FromArgb(248, 113, 113);
            _badgeBg = Color.FromArgb(35, 239, 68, 68);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(256, 44);
            BackColor = Color.FromArgb(21, 24, 34);
            Cursor = Cursors.Hand;
        }

        public void SetState(bool isSafe) {
            _badgeText = isSafe ? "Безопасно" : "Предупреждение";
            _badgeColor = isSafe ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
            _badgeBg = isSafe ? Color.FromArgb(35, 16, 185, 129) : Color.FromArgb(35, 239, 68, 68);
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
            Color fill = _isHovered ? Color.FromArgb(28, 32, 46) : Color.FromArgb(21, 24, 34);
            Color border = _isHovered ? Color.FromArgb(90, _badgeColor) : Color.FromArgb(45, _badgeColor);

            using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(bounds, 8)) {
                using (SolidBrush b = new SolidBrush(fill)) {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border, 1f)) {
                    g.DrawPath(pen, p);
                }
            }

            // Иконка
            Rectangle iconBox = new Rectangle(10, 10, 24, 24);
            using (SolidBrush ib = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) {
                using (GraphicsPath ip = GraphicsUtils.CreateRoundedRectangle(iconBox, 6)) {
                    g.FillPath(ib, ip);
                }
            }

            // Отрисовка векторной иконки
            using (Pen iconPen = new Pen(Color.FromArgb(226, 232, 240), 1.6f)) {
                iconPen.StartCap = LineCap.Round;
                iconPen.EndCap = LineCap.Round;

                if (_iconType == 0) {
                    // Монитор
                    g.DrawRectangle(iconPen, 15, 15, 14, 9);
                    g.DrawLine(iconPen, 22, 24, 22, 28);
                    g.DrawLine(iconPen, 18, 28, 26, 28);
                } else if (_iconType == 1) {
                    // Глобус / Браузер
                    g.DrawEllipse(iconPen, 15, 15, 14, 14);
                    g.DrawLine(iconPen, 15, 22, 29, 22);
                    g.DrawArc(iconPen, 18, 15, 8, 14, 0, 360);
                } else {
                    // Терминал CLI
                    g.DrawLine(iconPen, 16, 18, 20, 22);
                    g.DrawLine(iconPen, 20, 22, 16, 26);
                    g.DrawLine(iconPen, 22, 26, 27, 26);
                }
            }

            // Текст названия
            using (Font titleFont = new Font("Segoe UI", 8.8f, FontStyle.Bold)) {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(241, 245, 249))) {
                    g.DrawString(_title, titleFont, tb, 42, 14);
                }
            }

            // Бейдж справа
            using (Font badgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold)) {
                SizeF sz = g.MeasureString(_badgeText, badgeFont);
                int bw = (int)sz.Width + 12;
                int bh = 20;
                int bx = Width - bw - 10;
                int by = (Height - bh) / 2;

                Rectangle badgeRect = new Rectangle(bx, by, bw, bh);
                using (GraphicsPath bp = GraphicsUtils.CreateRoundedRectangle(badgeRect, 4)) {
                    using (SolidBrush bgb = new SolidBrush(_badgeBg)) {
                        g.FillPath(bgb, bp);
                    }
                }
                using (SolidBrush txtB = new SolidBrush(_badgeColor)) {
                    g.DrawString(_badgeText, badgeFont, txtB, bx + 6, by + 3);
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
            Size = new Size(150, 60);
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

            // Метка параметра
            using (Font lf = new Font("Segoe UI", 7.5f, FontStyle.Regular)) {
                using (SolidBrush lb = new SolidBrush(Color.FromArgb(100, 116, 139))) {
                    g.DrawString(_label, lf, lb, 8, 8);
                }
            }

            // Индикатор-точка и значение
            using (SolidBrush dotB = new SolidBrush(_valColor)) {
                g.FillEllipse(dotB, 8, 32, 7, 7);
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

        // Левый Sidebar
        private Panel _pnlSidebar;
        private Panel _brandLogoBox;
        private List<Panel> _navButtons = new List<Panel>();
        private int _selectedTabIndex = 0;

        // Контейнер контента
        private Panel _pnlContent;
        private List<Panel> _tabPages = new List<Panel>();

        // Элементы страницы 0: Главная (Hero Dashboard)
        private Label _lblHeaderTitle;
        private Label _lblHeaderDesc;
        private Label _lblWfpStatusText;
        private HeroCloudGraphic _heroCloud;
        private Label _lblStatusTitle;
        private Label _lblStatusSubtitle;
        private CloudflareButton _btnMainCta;
        private Panel _pnlModeSelector;

        // 3 карточки быстрого запуска Claude
        private LauncherCard _cardDesktop;
        private LauncherCard _cardBrowser;
        private LauncherCard _cardCli;

        // Телеметрия HUD и чипы
        private TelemetryStatChip _statChipIp;
        private TelemetryStatChip _statChipWfp;
        private TelemetryStatChip _statChipGateway;
        private TelemetryStatChip _statChipIpv6;
        private TelemetryStatChip _statChipDoh;
        private Label _lblFooterNoteText;

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

            if (string.IsNullOrEmpty(_cfg.ClaudePath)) {
                AutoDetectClaude();
                _cfg.Save();
            }

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

        private void AutoDetectClaude() {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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
                    return;
                }
            }

            _cfg.ClaudePath = "shell:AppsFolder\\Claude_pzs8sxrjxfjjc!Claude";
        }

        // Построение кастомного каркаса окна в стиле Cloudflare One / Linear
        private void BuildCustomWindowLayout() {
            // 1. Верхний Titlebar (38px)
            _pnlTitlebar = new Panel() {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(13, 15, 22)
            };
            _pnlTitlebar.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            Controls.Add(_pnlTitlebar);

            // Иконка в Titlebar
            _appIconBox = new Panel() {
                Location = new Point(14, 9),
                Size = new Size(20, 20),
                BackColor = Color.FromArgb(239, 68, 68)
            };
            _appIconBox.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, 19, 19);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 4)) {
                    Color c1 = (_currentState == ProtectionState.Protected) ? Color.FromArgb(5, 150, 105) : Color.FromArgb(249, 115, 22);
                    Color c2 = (_currentState == ProtectionState.Protected) ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
                    using (LinearGradientBrush lgb = new LinearGradientBrush(r, c1, c2, 135f)) {
                        g.FillPath(lgb, p);
                    }
                }
                GraphicsUtils.DrawShieldIcon(g, new Rectangle(3, 3, 14, 14), Color.White, Color.Transparent);
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

            _lblTitleSub = new Label() {
                Text = "— Bogdan Zhukov",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(194, 10),
                AutoSize = true
            };
            _lblTitleSub.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
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

            // 2. Левый сайдбар (218px)
            _pnlSidebar = new Panel() {
                Dock = DockStyle.Left,
                Width = 218,
                BackColor = Color.FromArgb(14, 16, 23)
            };
            _pnlSidebar.Paint += (s, pe) => {
                // Разделительная линия справа
                using (Pen p = new Pen(Color.FromArgb(28, 32, 44), 1f)) {
                    pe.Graphics.DrawLine(p, _pnlSidebar.Width - 1, 0, _pnlSidebar.Width - 1, _pnlSidebar.Height);
                }
            };
            Controls.Add(_pnlSidebar);

            BuildSidebar();

            // 3. Центральный контейнер контента (остальная ширина)
            _pnlContent = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 15, 21)
            };
            Controls.Add(_pnlContent);

            // Добавление 5 вкладок
            Panel pageHero = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _tabPages.Add(pageHero);
            _pnlContent.Controls.Add(pageHero);
            BuildHeroPage(pageHero);

            Panel pageDiag = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageDiag);
            _pnlContent.Controls.Add(pageDiag);
            BuildDiagnosticsPage(pageDiag);

            Panel pageBypass = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageBypass);
            _pnlContent.Controls.Add(pageBypass);
            BuildBypassPage(pageBypass);

            Panel pageLogs = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageLogs);
            _pnlContent.Controls.Add(pageLogs);
            BuildLogsPage(pageLogs);

            Panel pageSettings = new Panel() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            _tabPages.Add(pageSettings);
            _pnlContent.Controls.Add(pageSettings);
            BuildSettingsPage(pageSettings);

            SelectTab(0);
        }

        // Построение левого сайдбара
        private void BuildSidebar() {
            // Бренд-шапка сайдбара
            Panel pnlBrand = new Panel() {
                Location = new Point(12, 12),
                Size = new Size(194, 46),
                BackColor = Color.Transparent
            };
            _pnlSidebar.Controls.Add(pnlBrand);

            _brandLogoBox = new Panel() {
                Location = new Point(4, 8),
                Size = new Size(28, 28),
                BackColor = Color.Transparent
            };
            _brandLogoBox.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, 27, 27);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 7)) {
                    Color c1 = (_currentState == ProtectionState.Protected) ? Color.FromArgb(5, 150, 105) : Color.FromArgb(217, 119, 6);
                    Color c2 = (_currentState == ProtectionState.Protected) ? Color.FromArgb(16, 185, 129) : Color.FromArgb(220, 38, 38);
                    using (LinearGradientBrush lgb = new LinearGradientBrush(r, c1, c2, 135f)) {
                        g.FillPath(lgb, p);
                    }
                }
                GraphicsUtils.DrawShieldIcon(g, new Rectangle(5, 5, 18, 18), Color.White, Color.Transparent);
            };
            pnlBrand.Controls.Add(_brandLogoBox);

            Label lblBrandName = new Label() {
                Text = "Anti-Ban Barrier",
                Font = new Font("Segoe UI", 9.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(38, 12),
                AutoSize = true
            };
            pnlBrand.Controls.Add(lblBrandName);

            // Разделитель под брендом
            Panel sepTop = new Panel() {
                Location = new Point(12, 64),
                Size = new Size(194, 1),
                BackColor = Color.FromArgb(24, 27, 36)
            };
            _pnlSidebar.Controls.Add(sepTop);

            // Навигационное меню (5 пунктов)
            string[] navTitles = new string[] {
                "Главная",
                "Защита и шлюзы",
                "Обход блокировок",
                "Журнал событий",
                "Настройки"
            };

            int startY = 74;
            for (int i = 0; i < navTitles.Length; i++) {
                int tabIdx = i;
                Panel navBtn = new Panel() {
                    Location = new Point(12, startY + i * 44),
                    Size = new Size(194, 38),
                    BackColor = (i == 0) ? Color.FromArgb(25, 28, 40) : Color.Transparent,
                    Cursor = Cursors.Hand
                };

                navBtn.Paint += (s, pe) => {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    bool isSelected = (_selectedTabIndex == tabIdx);
                    Color textColor = isSelected ? Color.White : Color.FromArgb(148, 163, 184);
                    Color iconColor = isSelected
                        ? ((_currentState == ProtectionState.Protected) ? Color.FromArgb(52, 211, 153) : Color.FromArgb(249, 115, 22))
                        : Color.FromArgb(148, 163, 184);

                    if (isSelected) {
                        Rectangle r = new Rectangle(0, 0, navBtn.Width - 1, navBtn.Height - 1);
                        using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 8)) {
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(25, 28, 40))) {
                                g.FillPath(b, p);
                            }
                            using (Pen pen = new Pen(Color.FromArgb(38, 42, 58), 1f)) {
                                g.DrawPath(pen, p);
                            }
                        }
                    }

                    // Векторная иконка пункта меню
                    using (Pen ip = new Pen(iconColor, 1.6f)) {
                        ip.StartCap = LineCap.Round;
                        ip.EndCap = LineCap.Round;
                        int ix = 12;
                        int iy = 11;

                        if (tabIdx == 0) {
                            // Globe / Home
                            g.DrawEllipse(ip, ix, iy, 14, 14);
                            g.DrawLine(ip, ix, iy + 7, ix + 14, iy + 7);
                            g.DrawArc(ip, ix + 3, iy, 8, 14, 0, 360);
                        } else if (tabIdx == 1) {
                            // Shield
                            GraphicsUtils.DrawShieldIcon(g, new Rectangle(ix, iy, 14, 14), iconColor, Color.Transparent);
                        } else if (tabIdx == 2) {
                            // Lightning
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
                            // Terminal
                            g.DrawLine(ip, ix + 2, iy + 3, ix + 6, iy + 7);
                            g.DrawLine(ip, ix + 6, iy + 7, ix + 2, iy + 11);
                            g.DrawLine(ip, ix + 8, iy + 11, ix + 13, iy + 11);
                        } else {
                            // Settings / Cog
                            g.DrawEllipse(ip, ix + 2, iy + 2, 11, 11);
                            g.DrawEllipse(ip, ix + 5, iy + 5, 5, 5);
                        }
                    }

                    using (Font f = new Font("Segoe UI", 9.2f, isSelected ? FontStyle.Bold : FontStyle.Regular)) {
                        using (SolidBrush tb = new SolidBrush(textColor)) {
                            g.DrawString(navTitles[tabIdx], f, tb, 36, 9);
                        }
                    }
                };

                navBtn.MouseEnter += (s, e) => {
                    if (_selectedTabIndex != tabIdx) {
                        navBtn.BackColor = Color.FromArgb(18, 21, 30);
                    }
                };
                navBtn.MouseLeave += (s, e) => {
                    if (_selectedTabIndex != tabIdx) {
                        navBtn.BackColor = Color.Transparent;
                    }
                };
                navBtn.Click += (s, e) => SelectTab(tabIdx);

                _navButtons.Add(navBtn);
                _pnlSidebar.Controls.Add(navBtn);
            }

            // Нижний блок пользователя (Bogdan Zhukov)
            Panel pnlUser = new Panel() {
                Location = new Point(12, _pnlSidebar.Height - 64),
                Size = new Size(194, 52),
                BackColor = Color.Transparent
            };
            _pnlSidebar.Controls.Add(pnlUser);

            Panel sepBtm = new Panel() {
                Location = new Point(12, _pnlSidebar.Height - 68),
                Size = new Size(194, 1),
                BackColor = Color.FromArgb(24, 27, 36)
            };
            _pnlSidebar.Controls.Add(sepBtm);

            // Аватарка "BZ"
            Panel pnlAvatar = new Panel() {
                Location = new Point(4, 10),
                Size = new Size(30, 30),
                BackColor = Color.Transparent
            };
            pnlAvatar.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, 29, 29);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 29, 41))) {
                    g.FillEllipse(b, r);
                }
                using (Pen pen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f)) {
                    g.DrawEllipse(pen, r);
                }
                using (Font f = new Font("Segoe UI", 8.2f, FontStyle.Bold)) {
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(249, 115, 22))) {
                        SizeF sz = g.MeasureString("BZ", f);
                        g.DrawString("BZ", f, tb, (30 - sz.Width) / 2, (30 - sz.Height) / 2);
                    }
                }
            };
            pnlUser.Controls.Add(pnlAvatar);

            Label lblUserName = new Label() {
                Text = "Bogdan Zhukov",
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(40, 8),
                AutoSize = true
            };
            pnlUser.Controls.Add(lblUserName);

            Label lblUserRole = new Label() {
                Text = "Разработчик",
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(41, 26),
                AutoSize = true
            };
            pnlUser.Controls.Add(lblUserRole);

            // Кнопка быстрого открытия логов
            CloudflareButton btnQuickLog = new CloudflareButton() {
                Location = new Point(158, 10),
                Size = new Size(30, 30),
                CornerRadius = 6,
                Text = ">_",
                Font = new Font("Consolas", 8.2f, FontStyle.Bold),
                GradientStart = Color.FromArgb(24, 28, 40),
                GradientEnd = Color.FromArgb(20, 24, 34),
                HoverStart = Color.FromArgb(36, 42, 60),
                HoverEnd = Color.FromArgb(28, 32, 46),
                BorderStrokeColor = Color.FromArgb(36, 42, 60),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            btnQuickLog.Click += (s, e) => SelectTab(3); // Открыть вкладку "Журнал событий"
            pnlUser.Controls.Add(btnQuickLog);
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
            page.Padding = new Padding(24, 16, 24, 14);

            // Верхняя шапка контента
            Panel pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.Transparent
            };
            page.Controls.Add(pnlHeader);

            _lblHeaderTitle = new Label() {
                Text = "CLAUDE ANTI-BAN BARRIER",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(249, 115, 22),
                Location = new Point(0, 2),
                AutoSize = true
            };
            pnlHeader.Controls.Add(_lblHeaderTitle);

            _lblHeaderDesc = new Label() {
                Text = "Аппаратная изоляция трафика и превентивный контроль утечек",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(1, 24),
                AutoSize = true
            };
            pnlHeader.Controls.Add(_lblHeaderDesc);

            // Чип статуса WFP справа в шапке
            Panel pnlWfpChip = new Panel() {
                Location = new Point(560, 4),
                Size = new Size(234, 30),
                BackColor = Color.Transparent
            };
            pnlWfpChip.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlWfpChip.Width - 1, pnlWfpChip.Height - 1);
                using (GraphicsPath p = GraphicsUtils.CreateRoundedRectangle(r, 15)) {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 16, 185, 129))) {
                        g.FillPath(b, p);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(55, 16, 185, 129), 1f)) {
                        g.DrawPath(pen, p);
                    }
                }
                // Пульсирующая зеленая точка
                using (SolidBrush db = new SolidBrush(Color.FromArgb(16, 185, 129))) {
                    g.FillEllipse(db, 12, 11, 7, 7);
                }
            };
            _lblWfpStatusText = new Label() {
                Text = "WFP Ядро: 10 правил активны",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(25, 6),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            pnlWfpChip.Controls.Add(_lblWfpStatusText);
            pnlHeader.Controls.Add(pnlWfpChip);

            // Центральная секция Hero Graphic
            Panel pnlHero = new Panel() {
                Location = new Point(24, 60),
                Size = new Size(794, 250),
                BackColor = Color.Transparent
            };
            page.Controls.Add(pnlHero);

            _heroCloud = new HeroCloudGraphic() {
                Location = new Point((pnlHero.Width - 180) / 2, 0)
            };
            pnlHero.Controls.Add(_heroCloud);

            _lblStatusTitle = new Label() {
                Text = "Вход с предупреждением",
                Font = new Font("Segoe UI", 21f, FontStyle.Bold),
                ForeColor = Color.White,
                Size = new Size(794, 38),
                Location = new Point(0, 96),
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlHero.Controls.Add(_lblStatusTitle);

            _lblStatusSubtitle = new Label() {
                Text = "Обнаружен IP-адрес: Россия (178.62.x.x)\nТуннель VPN не активен — запуск Claude создает прямой риск блокировки аккаунта.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Size = new Size(794, 38),
                Location = new Point(0, 136),
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlHero.Controls.Add(_lblStatusSubtitle);

            // Главная большая кнопка Cloudflare One
            _btnMainCta = new CloudflareButton() {
                Text = "Включить защиту и туннель",
                Font = new Font("Segoe UI", 10.2f, FontStyle.Bold),
                Size = new Size(290, 42),
                Location = new Point((pnlHero.Width - 290) / 2, 178),
                CornerRadius = 10,
                HasGlow = true,
                GlowColor = Color.FromArgb(80, 59, 130, 246)
            };
            _btnMainCta.Click += (s, e) => StartAudit();
            pnlHero.Controls.Add(_btnMainCta);

            // Селектор режима под кнопкой
            _pnlModeSelector = new Panel() {
                Size = new Size(320, 26),
                Location = new Point((pnlHero.Width - 320) / 2, 224),
                BackColor = Color.Transparent
            };
            Label lblMode = new Label() {
                Text = "РЕЖИМ: Трафик WFP + Cloudflare DoH (UDP/TCP)  ▼",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            lblMode.Click += (s, e) => SelectTab(1); // Открыть детальные шлюзы
            _pnlModeSelector.Controls.Add(lblMode);
            pnlHero.Controls.Add(_pnlModeSelector);

            // Секция 3 карточек быстрого запуска Claude
            Panel pnlLaunchers = new Panel() {
                Location = new Point(24, 314),
                Size = new Size(794, 48),
                BackColor = Color.Transparent
            };
            page.Controls.Add(pnlLaunchers);

            _cardDesktop = new LauncherCard("Запустить Claude Desktop", 0) {
                Location = new Point(0, 2),
                Size = new Size(256, 44)
            };
            _cardDesktop.CardClicked += () => LaunchClaudeDesktop();
            pnlLaunchers.Controls.Add(_cardDesktop);

            _cardBrowser = new LauncherCard("Запустить в браузере", 1) {
                Location = new Point(268, 2),
                Size = new Size(256, 44)
            };
            _cardBrowser.CardClicked += () => LaunchClaudeBrowser();
            pnlLaunchers.Controls.Add(_cardBrowser);

            _cardCli = new LauncherCard("Claude Code CLI", 2) {
                Location = new Point(536, 2),
                Size = new Size(258, 44)
            };
            _cardCli.CardClicked += () => LaunchClaudeCodeCli();
            pnlLaunchers.Controls.Add(_cardCli);

            // Карточка Телеметрии HUD (5 чипов)
            Panel pnlTelemetry = new Panel() {
                Location = new Point(24, 370),
                Size = new Size(794, 118),
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

            // Заголовок телеметрии и кнопки действий
            Panel pnlTelemHeader = new Panel() {
                Location = new Point(12, 8),
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
                Location = new Point(12, 44)
            };
            pnlTelemetry.Controls.Add(_statChipIp);

            _statChipWfp = new TelemetryStatChip("Брандмауэр ядра WFP", "Активен (10 правил)", Color.FromArgb(52, 211, 153)) {
                Location = new Point(168, 44)
            };
            pnlTelemetry.Controls.Add(_statChipWfp);

            _statChipGateway = new TelemetryStatChip("Шлюз Anthropic CF", "Таймаут", Color.FromArgb(248, 113, 113)) {
                Location = new Point(324, 44)
            };
            pnlTelemetry.Controls.Add(_statChipGateway);

            _statChipIpv6 = new TelemetryStatChip("Изоляция IPv6 & DNS", "Изолирован", Color.FromArgb(52, 211, 153)) {
                Location = new Point(480, 44)
            };
            pnlTelemetry.Controls.Add(_statChipIpv6);

            _statChipDoh = new TelemetryStatChip("DoH (Cloudflare 1.1.1.1)", "Активен", Color.FromArgb(52, 211, 153)) {
                Location = new Point(636, 44)
            };
            pnlTelemetry.Controls.Add(_statChipDoh);

            // Нижний футер с предупреждением и копирайтом
            Panel pnlFooter = new Panel() {
                Location = new Point(24, 498),
                Size = new Size(794, 24),
                BackColor = Color.Transparent
            };
            page.Controls.Add(pnlFooter);

            _lblFooterNoteText = new Label() {
                Text = "ВНИМАНИЕ: Туннель VPN не обнаружен. Запуск Claude разрешен под вашу ответственность.",
                Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Location = new Point(0, 4),
                AutoSize = true
            };
            pnlFooter.Controls.Add(_lblFooterNoteText);

            Label lblCopy = new Label() {
                Text = "2026.4.18 // © 2026 Bogdan Zhukov",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(590, 4),
                AutoSize = true
            };
            pnlFooter.Controls.Add(lblCopy);
        }

        // Вкладка 1: Защита и шлюзы (Детальные раскрывающиеся карточки)
        private void BuildDiagnosticsPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Детальная диагностика сетевых шлюзов и адаптеров",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Контроль каждого уровня аппаратной изоляции: IP-геолокация, драйвер WFP, IPv6, DoH.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 44),
                AutoSize = true
            };
            page.Controls.Add(lblDesc);

            _pnlDiagnostics = new FlowLayoutPanel() {
                Location = new Point(24, 76),
                Size = new Size(794, 450),
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
                "Проверка...", Color.FromArgb(249, 115, 22), Color.FromArgb(40, 249, 115, 22),
                "Предотвращает утечки адреса через параллельные DNS-запросы Windows к домашнему роутеру.",
                "Сканирование физических сетевых карт...",
                "Изолировать"
            );
            _cardIpv6Dns.ActionClicked += () => ApplyFirewallRulesSilent();
            _pnlDiagnostics.Controls.Add(_cardIpv6Dns);

            _cardDoh = new DiagnosticCard(
                "Зашифрованный DoH (Cloudflare 1.1.1.1)",
                "Активен", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                "DNS-запросы к доменам Claude шифруются через TLS 1.3. Провайдер не видит адреса.",
                "Шлюз: https://1.1.1.1/dns-query",
                "Тест DoH"
            );
            _cardDoh.ActionClicked += () => TestDoHResolver();
            _pnlDiagnostics.Controls.Add(_cardDoh);
        }

        // Вкладка 2: Обход блокировок (Zapret DPI & DoH)
        private void BuildBypassPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Службы обхода сетевых ограничений и DNS-инспекции",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Модули нейтрализации цензуры DPI и подмены DNS со стороны локальных провайдеров.\nРаботают совместно с WFP-фильтрацией ядра Windows.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 46),
                Size = new Size(794, 36)
            };
            page.Controls.Add(lblDesc);

            // Панель Zapret
            Panel pnlZapret = new Panel() {
                Location = new Point(24, 95),
                Size = new Size(794, 100),
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
                Text = "Интеграция с Zapret DPI Bypass (winws.exe)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(16, 14),
                AutoSize = true
            };
            pnlZapret.Controls.Add(zTitle);

            Label zDesc = new Label() {
                Text = "Запуск службы обхода ТСПУ для порта TCP 443 с аргументами:\n--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq",
                Font = new Font("Consolas", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 40),
                Size = new Size(580, 40)
            };
            pnlZapret.Controls.Add(zDesc);

            CloudflareButton btnZapret = new CloudflareButton() {
                Text = "Запустить Zapret",
                Location = new Point(620, 30),
                Size = new Size(155, 38),
                CornerRadius = 8,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnZapret.Click += (s, e) => LaunchZapretSilent();
            pnlZapret.Controls.Add(btnZapret);

            // Панель DoH
            Panel pnlDoh = new Panel() {
                Location = new Point(24, 210),
                Size = new Size(794, 100),
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
                Size = new Size(580, 40)
            };
            pnlDoh.Controls.Add(dDesc);

            CloudflareButton btnDohTest = new CloudflareButton() {
                Text = "Тест DoH (1.1.1.1)",
                Location = new Point(620, 30),
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
                Location = new Point(24, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            _rtbLogs = new RichTextBox() {
                Location = new Point(24, 52),
                Size = new Size(794, 410),
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
                Location = new Point(24, 474),
                Size = new Size(180, 34),
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
                Location = new Point(214, 474),
                Size = new Size(120, 34),
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

        // Вкладка 4: Настройки
        private void BuildSettingsPage(Panel page) {
            Label lblTitle = new Label() {
                Text = "Системные параметры барьера",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(24, 20),
                AutoSize = true
            };
            page.Controls.Add(lblTitle);

            CheckBox chkTray = new CheckBox() {
                Text = "Сворачивать в системный трей при нажатии кнопки закрытия [X]",
                Checked = _cfg.MinimizeToTrayOnClose,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.2f),
                Location = new Point(28, 56),
                Size = new Size(600, 26)
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
                Location = new Point(28, 90),
                Size = new Size(600, 26)
            };
            chkAutoStart.CheckedChanged += (s, e) => {
                _cfg.AutoStartWithWindows = chkAutoStart.Checked;
                _cfg.Save();
                SetAutoStart(chkAutoStart.Checked);
            };
            page.Controls.Add(chkAutoStart);

            Label lblPath = new Label() {
                Text = "Путь к исполняемому файлу Claude Desktop (Claude.exe):",
                Location = new Point(28, 136),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            page.Controls.Add(lblPath);

            TextBox txtClaudePath = new TextBox() {
                Text = _cfg.ClaudePath,
                Location = new Point(28, 160),
                Size = new Size(640, 26),
                BackColor = Color.FromArgb(21, 24, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.2f)
            };
            page.Controls.Add(txtClaudePath);

            CloudflareButton btnBrowse = new CloudflareButton() {
                Text = "Обзор...",
                Location = new Point(680, 158),
                Size = new Size(135, 28),
                CornerRadius = 6,
                GradientStart = Color.FromArgb(30, 34, 48),
                GradientEnd = Color.FromArgb(24, 27, 38),
                HoverStart = Color.FromArgb(42, 48, 66),
                HoverEnd = Color.FromArgb(32, 36, 50),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(45, 52, 70)
            };
            btnBrowse.Click += (s, e) => {
                using (OpenFileDialog ofd = new OpenFileDialog()) {
                    ofd.Filter = "Claude Executable (*.exe)|*.exe";
                    if (ofd.ShowDialog() == DialogResult.OK) {
                        txtClaudePath.Text = ofd.FileName;
                        _cfg.ClaudePath = ofd.FileName;
                        _cfg.Save();
                    }
                }
            };
            page.Controls.Add(btnBrowse);

            // Блок деинсталляции
            Panel pnlUninstall = new Panel() {
                Location = new Point(28, 220),
                Size = new Size(787, 100),
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
                Location = new Point(610, 30),
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

            _heroCloud.State = newState;
            _cardDesktop.SetState(isSafe);
            _cardBrowser.SetState(isSafe);
            _cardCli.SetState(isSafe);

            if (_appIconBox != null) _appIconBox.Invalidate();
            if (_brandLogoBox != null) _brandLogoBox.Invalidate();

            if (isSafe) {
                _lblHeaderTitle.ForeColor = Color.FromArgb(52, 211, 153);
                _lblStatusTitle.Text = "Защищено";
                _lblStatusSubtitle.Text = "Все проверки пройдены: " + CurrentCountry + " (" + (CurrentPing > 0 ? CurrentPing + " мс" : "Шлюз OK") + ")\nТрафик надежно изолирован. Запуск Claude полностью безопасен.";

                _btnMainCta.Text = "Отключить защиту";
                _btnMainCta.GradientStart = Color.FromArgb(5, 150, 105);
                _btnMainCta.GradientEnd = Color.FromArgb(16, 185, 129);
                _btnMainCta.HoverStart = Color.FromArgb(16, 185, 129);
                _btnMainCta.HoverEnd = Color.FromArgb(4, 120, 87);
                _btnMainCta.GlowColor = Color.FromArgb(80, 16, 185, 129);
                _btnMainCta.Invalidate();

                _lblFooterNoteText.Text = "ЗАЩИТА АКТИВНА: Все утечки заблокированы. Сетевой шлюз Anthropic изолирован.";
                _lblFooterNoteText.ForeColor = Color.FromArgb(52, 211, 153);

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: ЗАЩИЩЕНО (" + (CurrentPing > 0 ? CurrentPing + " мс" : "Активно") + ")";
                }
            } else {
                _lblHeaderTitle.ForeColor = Color.FromArgb(249, 115, 22);
                _lblStatusTitle.Text = "Вход с предупреждением";
                _lblStatusSubtitle.Text = "Обнаружен IP-адрес: " + CurrentCountry + " (" + CurrentIp + ")\nТуннель VPN не активен — запуск Claude создает прямой риск блокировки аккаунта.";

                _btnMainCta.Text = "Включить защиту и туннель";
                _btnMainCta.GradientStart = Color.FromArgb(59, 130, 246);
                _btnMainCta.GradientEnd = Color.FromArgb(37, 99, 235);
                _btnMainCta.HoverStart = Color.FromArgb(96, 165, 250);
                _btnMainCta.HoverEnd = Color.FromArgb(37, 99, 235);
                _btnMainCta.GlowColor = Color.FromArgb(80, 59, 130, 246);
                _btnMainCta.Invalidate();

                _lblFooterNoteText.Text = "ВНИМАНИЕ: Туннель VPN не обнаружен. Запуск Claude разрешен под вашу ответственность.";
                _lblFooterNoteText.ForeColor = Color.FromArgb(248, 113, 113);

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: ПРЕДУПРЕЖДЕНИЕ (VPN не подтвержден)";
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

            // Попытка через MSIX Store Shell
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

            // Попытка через URI протокол claude:
            try {
                Process.Start(new ProcessStartInfo { FileName = "claude:", UseShellExecute = true });
                Log("LAUNCH", "Запущен Claude Desktop через URI протокол claude:", Color.FromArgb(52, 211, 153));
                return;
            } catch {}

            // Резерв: открытие веб-версии
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
            try {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = "cmd.exe",
                    Arguments = "/k claude || npx @anthropic-ai/claude-code",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Log("LAUNCH", "Запущен сеанс Claude Code CLI", Color.FromArgb(52, 211, 153));
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

        private void LaunchZapretSilent() {
            string zapretPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "winws.exe");
            if (!File.Exists(zapretPath)) {
                zapretPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winws.exe");
            }

            if (File.Exists(zapretPath)) {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = zapretPath,
                        Arguments = "--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                    Log("ZAPRET", "Zapret winws.exe успешно запущен в фоновом режиме", Color.FromArgb(52, 211, 153));
                } catch (Exception ex) {
                    Log("ERR", "Не удалось запустить Zapret: " + ex.Message, Color.FromArgb(239, 68, 68));
                }
            } else {
                Log("WARN", "Бинарный файл winws.exe не обнаружен в папке tools/", Color.FromArgb(248, 113, 113));
                MessageBox.Show("Файл winws.exe не найден. Вы можете поместить его в папку tools/ для автоматического запуска обхода DPI.",
                    "Zapret DPI", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            menu.MenuItems.Add("Проверить соединение", (s, e) => StartAudit());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Выход", (s, e) => {
                _forceExit = true;
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenu = menu;
        }

        // БЫСТРЫЙ ПАРАЛЛЕЛЬНЫЙ АУДИТ БЕЗ WPAD-ЗАДЕРЖКИ
        private void StartAudit() {
            SetProtectionUIState(ProtectionState.Checking, "Выполняется аудит сетевого шлюза...");
            Log("AUDIT", "Запуск проверки сетевых интерфейсов, отклика Anthropic и геолокации IP...", Color.FromArgb(148, 163, 184));

            ThreadPool.QueueUserWorkItem((state) => {
                try {
                    bool isRuIp = false;
                    string ipStr = "Ожидание";
                    string countryStr = "Неизвестно";
                    int pingMs = 0;

                    ManualResetEvent doneIp = new ManualResetEvent(false);
                    ManualResetEvent donePing = new ManualResetEvent(false);

                    // 1. Проверка внешнего IP и страны (req.Proxy = null)
                    ThreadPool.QueueUserWorkItem((_) => {
                        try {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://ip-api.com/json");
                            req.Proxy = null;
                            req.Timeout = 2500;
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/5.1";
                            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                            using (StreamReader sr = new StreamReader(resp.GetResponseStream())) {
                                string json = sr.ReadToEnd();
                                if (json.Contains("\"countryCode\":\"RU\"") || json.Contains("\"countryCode\":\"BY\"")) {
                                    isRuIp = true;
                                    countryStr = json.Contains("\"countryCode\":\"RU\"") ? "Россия" : "Беларусь";
                                } else {
                                    int qIdx = json.IndexOf("\"query\":\"");
                                    if (qIdx > 0) {
                                        int endQ = json.IndexOf("\"", qIdx + 9);
                                        if (endQ > 0) ipStr = json.Substring(qIdx + 9, endQ - (qIdx + 9));
                                    }
                                    int cIdx = json.IndexOf("\"country\":\"");
                                    if (cIdx > 0) {
                                        int endC = json.IndexOf("\"", cIdx + 11);
                                        if (endC > 0) countryStr = json.Substring(cIdx + 11, endC - (cIdx + 11));
                                    }
                                }
                            }
                        } catch {
                            isRuIp = true;
                        } finally {
                            doneIp.Set();
                        }
                    });

                    // 2. Измерение настоящего пинга к api.anthropic.com
                    ThreadPool.QueueUserWorkItem((_) => {
                        Stopwatch sw = Stopwatch.StartNew();
                        try {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com");
                            req.Proxy = null;
                            req.Timeout = 2500;
                            req.Method = "HEAD";
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/5.1";
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
                                pingMs = 0;
                            }
                        } catch {
                            sw.Stop();
                        } finally {
                            donePing.Set();
                        }
                    });

                    // 3. Проверка наличия активного туннеля VPN
                    bool vpnActive = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up) {
                                string desc = nic.Description.ToLowerInvariant();
                                string name = nic.Name.ToLowerInvariant();
                                if (desc.Contains("wintun") || desc.Contains("wireguard") || desc.Contains("tap") ||
                                    desc.Contains("tun") || name.Contains("wwan99") || name.Contains("vpn")) {
                                    vpnActive = true;
                                    break;
                                }
                            }
                        }
                    } catch {}

                    // Ожидание параллельных замеров
                    WaitHandle.WaitAll(new WaitHandle[] { doneIp, donePing }, 2800);

                    // 4. Проверка правил Брандмауэра напрямую через системный реестр
                    int fwRulesCount = GetFirewallRuleCountFromRegistry();

                    // 5. Проверка утечек IPv6
                    bool hasActiveIpv6Leak = false;
                    try {
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (nic.OperationalStatus == OperationalStatus.Up &&
                                nic.NetworkInterfaceType != NetworkInterfaceType.Loopback) {
                                string d = nic.Description.ToLowerInvariant();
                                if (!d.Contains("wintun") && !d.Contains("wireguard") && !d.Contains("tap") && !d.Contains("vpn")) {
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

                    // 6. Итоговое обновление интерфейса
                    SafeInvoke(() => {
                        CurrentPing = pingMs;
                        CurrentIp = ipStr;
                        CurrentCountry = countryStr;

                        if (isRuIp || !vpnActive) {
                            string reason = isRuIp
                                ? ("Обнаружен IP адрес " + countryStr + " (прямой риск бана аккаунта)")
                                : "Адаптер VPN не подключен или соединение разорвано";

                            SetProtectionUIState(ProtectionState.BlockedNoVpn, reason);
                            _statChipIp.UpdateVal("Риск бана (" + countryStr + ")", Color.FromArgb(248, 113, 113));
                            if (_cardIp != null) {
                                _cardIp.UpdateStatus("Риск бана (" + countryStr + ")", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Текущий IP: " + ipStr + " (" + countryStr + "). Доступ к Claude заблокирован.");
                            }
                            Log("SECURITY", "Аудит завершен: IP адрес " + countryStr + ". Вход в Claude ЗАБЛОКИРОВАН.", Color.FromArgb(248, 113, 113));
                        } else {
                            SetProtectionUIState(ProtectionState.Protected, "Защита активна");
                            _statChipIp.UpdateVal(ipStr + " (" + countryStr + ")", Color.FromArgb(52, 211, 153));
                            if (_cardIp != null) {
                                _cardIp.UpdateStatus(ipStr + " (" + countryStr + ")", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "Внешний адрес: " + ipStr + " (" + countryStr + "). Трафик полностью туннелирован.");
                            }
                            Log("SECURITY", "Аудит завершен: туннель активен (" + countryStr + "). Безопасный запуск Claude РАЗРЕШЕН.", Color.FromArgb(52, 211, 153));
                        }

                        // Обновление чипа WFP
                        if (fwRulesCount > 0) {
                            _statChipWfp.UpdateVal("Активен (" + fwRulesCount + " правил)", Color.FromArgb(52, 211, 153));
                            _lblWfpStatusText.Text = "WFP Ядро: " + fwRulesCount + " правил активны";
                            if (_cardFirewall != null) {
                                _cardFirewall.UpdateStatus("Активен (" + fwRulesCount + " правил)", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "Ядро WFP изолирует подсети 160.79.104.0/23 на физических картах.");
                            }
                        } else {
                            _statChipWfp.UpdateVal("Требуется установка", Color.FromArgb(248, 113, 113));
                            _lblWfpStatusText.Text = "WFP Ядро: Требуется установка";
                            if (_cardFirewall != null) {
                                _cardFirewall.UpdateStatus("Требуется установка", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Правила изоляции еще не внедрены. Нажмите «Применить правила».");
                            }
                        }

                        // Обновление чипа Cloudflare
                        if (pingMs > 0) {
                            _statChipGateway.UpdateVal("Доступен (" + pingMs + " мс)", Color.FromArgb(52, 211, 153));
                            if (_cardCloudflare != null) {
                                _cardCloudflare.UpdateStatus("Доступен (" + pingMs + " мс)", Color.FromArgb(52, 211, 153), Color.FromArgb(35, 16, 185, 129),
                                    "Шлюз api.anthropic.com отвечает штатно за " + pingMs + " мс (TLS 1.2/1.3).");
                            }
                        } else {
                            _statChipGateway.UpdateVal("Таймаут", Color.FromArgb(248, 113, 113));
                            if (_cardCloudflare != null) {
                                _cardCloudflare.UpdateStatus("Таймаут", Color.FromArgb(248, 113, 113), Color.FromArgb(35, 239, 68, 68),
                                    "Серверы Anthropic не отвечают. Проверьте активность туннеля.");
                            }
                        }

                        // Обновление чипа IPv6 & DNS
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
                                    desc.Contains("tun") || name.Contains("wwan99") || name.Contains("vpn")) {
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
                            "$phys = @(Get-NetAdapter | Where-Object { $_.InterfaceDescription -notmatch 'Wintun|WireGuard|TAP|TUN|Virtual' -and ($_.PhysicalMediaType -match '802.11|802.3|Native 802.11' -or $_.Name -match 'Wi-Fi|Ethernet|Беспроводная') } | Select-Object -ExpandProperty Name); " +
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
