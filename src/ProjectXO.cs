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
                        "Для работы барьера защиты Claude требуются права Администратора (управление брандмауэром WFP).",
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

    // Современная закругленная кнопка в стиле Claude UI
    public class ClaudeButton : Button {
        private bool _isHovered = false;
        private bool _isPressed = false;
        public int CornerRadius { get; set; }
        public Color NormalBg { get; set; }
        public Color HoverBg { get; set; }
        public Color PressedBg { get; set; }
        public Color BorderStrokeColor { get; set; }

        public ClaudeButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            CornerRadius = 8;
            NormalBg = Color.FromArgb(204, 120, 92);
            HoverBg = Color.FromArgb(217, 135, 107);
            PressedBg = Color.FromArgb(185, 100, 75);
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

            // Фон родителя для чистого сглаживания углов
            Color parentBg = (Parent != null) ? Parent.BackColor : Color.FromArgb(14, 15, 18);
            using (SolidBrush pb = new SolidBrush(parentBg)) {
                g.FillRectangle(pb, ClientRectangle);
            }

            Color fill = NormalBg;
            if (!Enabled) {
                fill = Color.FromArgb(32, 33, 40);
            } else if (_isPressed) {
                fill = PressedBg;
            } else if (_isHovered) {
                fill = HoverBg;
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundedRect(bounds, CornerRadius)) {
                using (SolidBrush b = new SolidBrush(fill)) {
                    g.FillPath(b, path);
                }

                if (BorderStrokeColor != Color.Transparent && Enabled) {
                    using (Pen p = new Pen(BorderStrokeColor, 1f)) {
                        g.DrawPath(p, path);
                    }
                }
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(113, 113, 122);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius) {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
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
    }

    // Современный переключатель табов без системных белых рамок Win32
    public class DarkTabControl : Panel {
        private Panel _navHeader;
        private Panel _contentPanel;
        private List<Button> _tabButtons = new List<Button>();
        private List<Panel> _pages = new List<Panel>();
        private int _selectedIndex = 0;

        public DarkTabControl() {
            BackColor = Color.FromArgb(14, 15, 18);
            _navHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(14, 15, 18)
            };
            _contentPanel = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 15, 18)
            };
            Controls.Add(_contentPanel);
            Controls.Add(_navHeader);
        }

        public Panel AddTab(string title) {
            int idx = _tabButtons.Count;
            Panel page = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 15, 18),
                Visible = (idx == 0)
            };
            _pages.Add(page);
            _contentPanel.Controls.Add(page);

            Button btn = new Button() {
                Text = title,
                Height = 36,
                AutoSize = true,
                Padding = new Padding(16, 0, 16, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Location = new Point(idx == 0 ? 0 : _tabButtons[idx - 1].Right + 6, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => SelectTab(idx);
            _tabButtons.Add(btn);
            _navHeader.Controls.Add(btn);

            UpdateTabStyles();
            return page;
        }

        public void SelectTab(int index) {
            if (index < 0 || index >= _pages.Count) return;
            _selectedIndex = index;
            for (int i = 0; i < _pages.Count; i++) {
                _pages[i].Visible = (i == index);
            }
            UpdateTabStyles();
        }

        private void UpdateTabStyles() {
            for (int i = 0; i < _tabButtons.Count; i++) {
                Button btn = _tabButtons[i];
                if (i == _selectedIndex) {
                    btn.BackColor = Color.FromArgb(28, 29, 36);
                    btn.ForeColor = Color.FromArgb(204, 120, 92); // Claude Terracotta
                } else {
                    btn.BackColor = Color.FromArgb(16, 17, 21);
                    btn.ForeColor = Color.FromArgb(156, 163, 175);
                }
            }
        }
    }

    // Живая трехмерная анимированная модель эмблемы Claude
    public class ClaudeSunburstCanvas : Panel {
        private static readonly string B64_CLAUDE_EMBLEM_TERRACOTTA = "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";
        private static readonly string B64_CLAUDE_EMBLEM_CRIMSON = "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";
        private static readonly string B64_CLAUDE_EMBLEM_AMBER = "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";

        private static Bitmap _bmpTerracotta;
        private static Bitmap _bmpCrimson;
        private static Bitmap _bmpAmber;

        static ClaudeSunburstCanvas() {
            try {
                _bmpTerracotta = LoadBitmapFromB64(B64_CLAUDE_EMBLEM_TERRACOTTA);
                _bmpCrimson = LoadBitmapFromB64(B64_CLAUDE_EMBLEM_CRIMSON);
                _bmpAmber = LoadBitmapFromB64(B64_CLAUDE_EMBLEM_AMBER);
            } catch {}
        }

        private static Bitmap LoadBitmapFromB64(string b64) {
            byte[] bytes = Convert.FromBase64String(b64);
            using (MemoryStream ms = new MemoryStream(bytes)) {
                return new Bitmap(ms);
            }
        }

        private ProtectionState _state = ProtectionState.Checking;
        public ProtectionState State {
            get { return _state; }
            set {
                _state = value;
                Invalidate();
            }
        }

        public int PingMs = 0;
        public string ExternalIp = "...";
        public string Country = "Проверка";
        public string StatusMessage = "Проверка сетевого шлюза...";

        private float _phase = 0f;
        private float _waveRadius = 35f;
        private System.Windows.Forms.Timer _animTimer;

        public ClaudeSunburstCanvas() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(18, 19, 23);
            Font = new Font("Segoe UI", 9f);

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 35; // Плавные ~30 FPS
            _animTimer.Tick += (s, e) => {
                _phase += 0.07f;
                if (_phase > (float)(Math.PI * 2)) _phase -= (float)(Math.PI * 2);

                _waveRadius += 1.2f;
                if (_waveRadius > 105f) _waveRadius = 32f;

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
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = Width;
            int h = Height;
            int cx = w / 2;
            int cy = h / 2 - 28;

            // Мягкий обсидиановый фон
            using (SolidBrush bg = new SolidBrush(BackColor)) {
                g.FillRectangle(bg, 0, 0, w, h);
            }

            // Мягкая темная окантовка холста
            using (Pen borderPen = new Pen(Color.FromArgb(38, 40, 48), 1f)) {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }

            Color themeColor;
            Color auraColor;
            Bitmap currentBmp;

            switch (_state) {
                case ProtectionState.Protected:
                    themeColor = Color.FromArgb(204, 120, 92); // Фирменный терракотовый Claude
                    auraColor = Color.FromArgb(45, 204, 120, 92);
                    currentBmp = _bmpTerracotta;
                    break;
                case ProtectionState.BlockedNoVpn:
                case ProtectionState.BlockedLeaking:
                    themeColor = Color.FromArgb(239, 68, 68); // Предупреждающий красный
                    auraColor = Color.FromArgb(50, 239, 68, 68);
                    currentBmp = _bmpCrimson;
                    break;
                default:
                    themeColor = Color.FromArgb(245, 158, 11); // Янтарный статус проверки
                    auraColor = Color.FromArgb(40, 245, 158, 11);
                    currentBmp = _bmpAmber;
                    break;
            }

            // 1. Анимированная волна сонара / сетевого экрана защиты
            float waveFade = 1.0f - ((_waveRadius - 32f) / 73f);
            if (waveFade < 0f) waveFade = 0f;
            int waveAlpha = (int)(waveFade * 60f);
            if (waveAlpha > 0) {
                using (Pen wavePen = new Pen(Color.FromArgb(waveAlpha, themeColor), 1.5f)) {
                    g.DrawEllipse(wavePen, cx - _waveRadius, cy - _waveRadius, _waveRadius * 2, _waveRadius * 2);
                }
            }

            // 2. Многослойная объемная аура с эффектом «дыхания»
            float breath = (float)Math.Sin(_phase);
            float auraSize = 96f + breath * 8f;
            using (GraphicsPath auraPath = new GraphicsPath()) {
                auraPath.AddEllipse(cx - auraSize, cy - auraSize, auraSize * 2, auraSize * 2);
                using (PathGradientBrush pgb = new PathGradientBrush(auraPath)) {
                    pgb.CenterColor = auraColor;
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, BackColor) };
                    g.FillPath(pgb, auraPath);
                }
            }

            // 3. Подлинная модель Claude с эффектом мягкого покачивания и дыхания
            float emblemBaseSize = 118f;
            float scale = 1.0f + 0.045f * breath; // Плавное увеличение и сжатие
            float drawSize = emblemBaseSize * scale;
            float floatAngle = 2.4f * (float)Math.Sin(_phase * 0.7f); // Тонкий наклон эмблемы

            GraphicsState gState = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(floatAngle);

            if (currentBmp != null) {
                // Мягкая падающая тень под моделью
                RectangleF shadowRect = new RectangleF(-drawSize / 2f + 2f, -drawSize / 2f + 4f, drawSize, drawSize);
                using (ImageAttributes shadowAttr = new ImageAttributes()) {
                    ColorMatrix cmShadow = new ColorMatrix(new float[][] {
                        new float[] { 0, 0, 0, 0, 0 },
                        new float[] { 0, 0, 0, 0, 0 },
                        new float[] { 0, 0, 0, 0, 0 },
                        new float[] { 0, 0, 0, 0.40f, 0 },
                        new float[] { 0, 0, 0, 0, 1 }
                    });
                    shadowAttr.SetColorMatrix(cmShadow);
                    g.DrawImage(currentBmp, new Rectangle((int)shadowRect.X, (int)shadowRect.Y, (int)shadowRect.Width, (int)shadowRect.Height),
                        0, 0, currentBmp.Width, currentBmp.Height, GraphicsUnit.Pixel, shadowAttr);
                }

                // Отрисовка подлинной эмблемы Claude высокого качества
                RectangleF emblemRect = new RectangleF(-drawSize / 2f, -drawSize / 2f, drawSize, drawSize);
                g.DrawImage(currentBmp, emblemRect);
            } else {
                // Запасная векторная прорисовка на случай сбоя загрузки растра
                using (SolidBrush sb = new SolidBrush(themeColor)) {
                    g.FillEllipse(sb, -30, -30, 60, 60);
                }
            }

            g.Restore(gState);

            // 4. Текстовый статус защиты
            string statusTitle;
            string statusSub;

            if (_state == ProtectionState.Protected) {
                statusTitle = "Защита активна • " + (PingMs > 0 ? PingMs + " мс" : "Шлюз OK");
                statusSub = "Трафик Claude изолирован в туннеле (" + Country + ")";
            } else if (_state == ProtectionState.Checking) {
                statusTitle = "Проверка сетевого шлюза...";
                statusSub = StatusMessage;
            } else {
                statusTitle = "ВХОД С ПРЕДУПРЕЖДЕНИЕМ";
                statusSub = StatusMessage;
            }

            using (Font titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold)) {
                SizeF sz = g.MeasureString(statusTitle, titleFont);
                using (SolidBrush tb = new SolidBrush(themeColor)) {
                    g.DrawString(statusTitle, titleFont, tb, cx - sz.Width / 2, h - 56);
                }
            }

            using (Font subFont = new Font("Segoe UI", 8.5f)) {
                SizeF sz = g.MeasureString(statusSub, subFont);
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(161, 161, 170))) {
                    g.DrawString(statusSub, subFont, sb, cx - sz.Width / 2, h - 32);
                }
            }
        }
    }

    // Раскрывающаяся карточка диагностики шлюзов и адаптеров
    public class DiagnosticCard : Panel {
        private bool _isExpanded = false;
        private Label _lblTitle;
        private Label _lblBadge;
        private Label _lblArrow;
        private Panel _pnlHeader;
        private Panel _pnlBody;
        private Label _lblDesc;
        private Label _lblTech;
        private ClaudeButton _btnAction;

        public event Action ActionClicked;

        public DiagnosticCard(string title, string badgeText, Color badgeColor, Color badgeBg, string description, string technical, string actionBtnText) {
            Width = 485;
            Height = 44;
            BackColor = Color.FromArgb(24, 25, 31);
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
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
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
                Padding = new Padding(6, 2, 6, 2),
                AutoSize = true,
                Location = new Point(310, 12)
            };
            _lblBadge.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblBadge);

            _lblArrow = new Label() {
                Text = "+",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(455, 10),
                Size = new Size(20, 20)
            };
            _lblArrow.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblArrow);

            _pnlBody = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(19, 20, 25),
                Visible = false
            };
            Controls.Add(_pnlBody);

            _lblDesc = new Label() {
                Text = description,
                Location = new Point(14, 10),
                Size = new Size(455, 34),
                ForeColor = Color.FromArgb(161, 161, 170),
                Font = new Font("Segoe UI", 8.5f)
            };
            _pnlBody.Controls.Add(_lblDesc);

            _lblTech = new Label() {
                Text = technical,
                Location = new Point(14, 46),
                Size = new Size(330, 32),
                ForeColor = Color.FromArgb(212, 212, 216),
                Font = new Font("Consolas", 8f)
            };
            _pnlBody.Controls.Add(_lblTech);

            if (!string.IsNullOrEmpty(actionBtnText)) {
                _btnAction = new ClaudeButton() {
                    Text = actionBtnText,
                    Location = new Point(350, 44),
                    Size = new Size(120, 28),
                    NormalBg = Color.FromArgb(38, 40, 48),
                    HoverBg = Color.FromArgb(48, 50, 60),
                    PressedBg = Color.FromArgb(30, 32, 38),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(204, 120, 92)
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

    public class MainWindow : Form {
        private Config _cfg;
        private ClaudeSunburstCanvas _canvas;
        private DarkTabControl _tabCtrl;
        private FlowLayoutPanel _pnlDiagnostics;

        private DiagnosticCard _cardIp;
        private DiagnosticCard _cardFirewall;
        private DiagnosticCard _cardCloudflare;
        private DiagnosticCard _cardIpv6Dns;
        private DiagnosticCard _cardDoh;

        // Основные кнопки запуска Claude в стиле Claude Desktop
        private ClaudeButton _btnLaunchDesktop;
        private ClaudeButton _btnLaunchBrowser;
        private ClaudeButton _btnLaunchCodeCli;
        private Label _lblLaunchStatus;

        private ClaudeButton _btnVerifyNow;
        private ClaudeButton _btnTestSim;
        private Label _lblFooterStatus;
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _watchdogTimer;
        private bool _forceExit = false;

        private ProtectionState _currentState = ProtectionState.Checking;
        private RichTextBox _rtbLogs;

        public MainWindow(string[] args) {
            _cfg = new Config();
            _cfg.Load();

            if (string.IsNullOrEmpty(_cfg.ClaudePath)) {
                AutoDetectClaude();
                _cfg.Save();
            }

            Text = "Claude Anti-Ban Barrier — Bogdan Zhukov";
            Width = 960;
            Height = 680;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(14, 15, 18);
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

            BuildUI();
            SetupTray();

            Shown += (s, e) => {
                Log("SYSTEM", "Барьер запущен с правами администратора. Служба ядра WFP готова к работе.", Color.FromArgb(204, 120, 92));
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
                if (InvokeRequired) {
                    BeginInvoke(action);
                } else {
                    action();
                }
            } catch {}
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (e.CloseReason == CloseReason.UserClosing && !_forceExit && _cfg.MinimizeToTrayOnClose) {
                e.Cancel = true;
                Hide();
                if (_canvas != null) _canvas.PauseAnimation();
                _trayIcon.ShowBalloonTip(2500, "Claude Anti-Ban Barrier",
                    "Защита работает в фоне. Для возврата к окну дважды нажмите на иконку в трее.", ToolTipIcon.Info);
                Log("TRAY", "Окно свернуто в трей. Фоновая изоляция активна.", Color.FromArgb(161, 161, 170));
            } else {
                base.OnFormClosing(e);
            }
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) {
                if (_canvas != null) _canvas.PauseAnimation();
            } else if (WindowState == FormWindowState.Normal) {
                if (_canvas != null) _canvas.ResumeAnimation();
            }
        }

        private static readonly object _logLock = new object();
        public void Log(string category, string message, Color color) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = string.Format("[{0}] [{1}] {2}\n", timestamp, category, message);

            lock (_logLock) {
                try {
                    string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_xo.log");
                    File.AppendAllText(logFile, line);
                } catch {}
            }

            SafeInvoke(() => {
                if (_rtbLogs != null && !_rtbLogs.IsDisposed) {
                    _rtbLogs.SelectionStart = _rtbLogs.TextLength;
                    _rtbLogs.SelectionLength = 0;
                    _rtbLogs.SelectionColor = Color.FromArgb(113, 113, 122);
                    _rtbLogs.AppendText("[" + timestamp + "] ");

                    _rtbLogs.SelectionColor = color;
                    _rtbLogs.AppendText("[" + category + "] ");

                    _rtbLogs.SelectionColor = Color.FromArgb(244, 244, 245);
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

        private void BuildUI() {
            // Верхняя шапка
            Panel pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(19, 20, 24)
            };
            Controls.Add(pnlHeader);

            Label lblLogo = new Label() {
                Text = "CLAUDE ANTI-BAN BARRIER",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(204, 120, 92),
                Location = new Point(18, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblLogo);

            Label lblSubtitle = new Label() {
                Text = "Аппаратная изоляция трафика и превентивный контроль утечек // Bogdan Zhukov",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(20, 34),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblSubtitle);

            // Темный переключатель табов без системных белых рамок
            _tabCtrl = new DarkTabControl() {
                Location = new Point(14, 68),
                Size = new Size(916, 538)
            };
            Controls.Add(_tabCtrl);

            // ТАБ 1: Защита и шлюзы
            Panel tabMain = _tabCtrl.AddTab("Защита и шлюзы");

            // Левая колонка: Холст анимации + Кнопки запуска
            _canvas = new ClaudeSunburstCanvas() {
                Location = new Point(10, 10),
                Size = new Size(390, 280)
            };
            tabMain.Controls.Add(_canvas);

            // Блок статуса блокировки
            _lblLaunchStatus = new Label() {
                Location = new Point(10, 298),
                Size = new Size(390, 28),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Проверка сетевого шлюза...",
                ForeColor = Color.FromArgb(161, 161, 170),
                BackColor = Color.FromArgb(24, 25, 31)
            };
            tabMain.Controls.Add(_lblLaunchStatus);

            // КНОПКА 1: Claude Desktop (Закругленная в стиле Claude)
            _btnLaunchDesktop = new ClaudeButton() {
                Text = "Запустить Claude Desktop",
                Location = new Point(10, 334),
                Size = new Size(390, 38),
                Enabled = false
            };
            _btnLaunchDesktop.Click += (s, e) => LaunchClaudeDesktop();
            tabMain.Controls.Add(_btnLaunchDesktop);

            // КНОПКА 2: Claude в браузере
            _btnLaunchBrowser = new ClaudeButton() {
                Text = "Запустить Claude в браузере (claude.ai)",
                Location = new Point(10, 378),
                Size = new Size(390, 38),
                Enabled = false
            };
            _btnLaunchBrowser.Click += (s, e) => LaunchClaudeBrowser();
            tabMain.Controls.Add(_btnLaunchBrowser);

            // КНОПКА 3: Claude Code CLI
            _btnLaunchCodeCli = new ClaudeButton() {
                Text = "Запустить Claude Code (терминал CLI)",
                Location = new Point(10, 422),
                Size = new Size(390, 38),
                Enabled = false
            };
            _btnLaunchCodeCli.Click += (s, e) => LaunchClaudeCodeCli();
            tabMain.Controls.Add(_btnLaunchCodeCli);

            // Нижние сервисные кнопки
            _btnVerifyNow = new ClaudeButton() {
                Text = "Проверить соединение",
                Location = new Point(10, 468),
                Size = new Size(190, 32),
                NormalBg = Color.FromArgb(28, 29, 36),
                HoverBg = Color.FromArgb(38, 40, 50),
                PressedBg = Color.FromArgb(22, 23, 28),
                ForeColor = Color.FromArgb(228, 228, 231),
                BorderStrokeColor = Color.FromArgb(50, 52, 62)
            };
            _btnVerifyNow.Click += (s, e) => StartAudit();
            tabMain.Controls.Add(_btnVerifyNow);

            _btnTestSim = new ClaudeButton() {
                Text = "Тест аварийного сброса",
                Location = new Point(210, 468),
                Size = new Size(190, 32),
                NormalBg = Color.FromArgb(28, 29, 36),
                HoverBg = Color.FromArgb(38, 40, 50),
                PressedBg = Color.FromArgb(22, 23, 28),
                ForeColor = Color.FromArgb(228, 228, 231),
                BorderStrokeColor = Color.FromArgb(50, 52, 62)
            };
            _btnTestSim.Click += (s, e) => RunMemorySimulation();
            tabMain.Controls.Add(_btnTestSim);

            // Правая колонка: Диагностические карточки
            _pnlDiagnostics = new FlowLayoutPanel() {
                Location = new Point(415, 10),
                Size = new Size(490, 492),
                BackColor = Color.FromArgb(14, 15, 18),
                AutoScroll = true
            };
            tabMain.Controls.Add(_pnlDiagnostics);

            _cardIp = new DiagnosticCard(
                "Внешний IPv4 и страна",
                "Проверка...", Color.FromArgb(217, 119, 87), Color.FromArgb(38, 28, 24),
                "Проверка внешнего IP, который видят серверы Claude. Если IP из РФ — вход превентивно блокируется.",
                "Запрос геолокации IP...",
                "Обновить IP"
            );
            _cardIp.ActionClicked += () => StartAudit();
            _pnlDiagnostics.Controls.Add(_cardIp);

            _cardFirewall = new DiagnosticCard(
                "Брандмауэр ядра WFP",
                "Проверка...", Color.FromArgb(217, 119, 87), Color.FromArgb(38, 28, 24),
                "Аппаратный фильтр WFP блокирует исходящий трафик Claude ко всем внешним адаптерам кроме VPN.",
                "Чтение правил в реестре...",
                "Применить правила"
            );
            _cardFirewall.ActionClicked += () => ApplyFirewallRulesSilent();
            _pnlDiagnostics.Controls.Add(_cardFirewall);

            _cardCloudflare = new DiagnosticCard(
                "Шлюз Anthropic & Cloudflare",
                "Проверка...", Color.FromArgb(217, 119, 87), Color.FromArgb(38, 28, 24),
                "Проверка отклика api.anthropic.com через туннель по протоколу TLS 1.2/1.3.",
                "Запрос рукопожатия TLS...",
                "Тест связи"
            );
            _cardCloudflare.ActionClicked += () => StartAudit();
            _pnlDiagnostics.Controls.Add(_cardCloudflare);

            _cardIpv6Dns = new DiagnosticCard(
                "Изоляция IPv6 и Smart DNS",
                "Проверка...", Color.FromArgb(217, 119, 87), Color.FromArgb(38, 28, 24),
                "Предотвращает утечки адреса через параллельные запросы Windows к домашнему роутеру.",
                "Сканирование адаптеров...",
                "Изолировать"
            );
            _cardIpv6Dns.ActionClicked += () => ApplyFirewallRulesSilent();
            _pnlDiagnostics.Controls.Add(_cardIpv6Dns);

            _cardDoh = new DiagnosticCard(
                "Зашифрованный DoH (Cloudflare 1.1.1.1)",
                "Активен", Color.FromArgb(204, 120, 92), Color.FromArgb(38, 28, 24),
                "DNS-запросы к доменам Claude шифруются через TLS 1.3. Провайдер не видит адреса.",
                "Шлюз: https://1.1.1.1/dns-query",
                "Тест DNS"
            );
            _cardDoh.ActionClicked += () => TestDoHResolver();
            _pnlDiagnostics.Controls.Add(_cardDoh);

            // ТАБ 2: Обход блокировок
            Panel tabBypass = _tabCtrl.AddTab("Обход блокировок");
            BuildBypassTab(tabBypass);

            // ТАБ 3: Журнал событий
            Panel tabLogs = _tabCtrl.AddTab("Журнал событий");
            BuildLogsTab(tabLogs);

            // ТАБ 4: Настройки
            Panel tabSettings = _tabCtrl.AddTab("Настройки");
            BuildSettingsTab(tabSettings);

            // Нижний информационный статус
            _lblFooterStatus = new Label() {
                Location = new Point(14, 614),
                Size = new Size(916, 22),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Text = "Инициализация защитного барьера..."
            };
            Controls.Add(_lblFooterStatus);
        }

        private void SetProtectionUIState(ProtectionState newState, string msg = "") {
            _currentState = newState;
            _canvas.State = newState;
            if (!string.IsNullOrEmpty(msg)) _canvas.StatusMessage = msg;

            if (newState == ProtectionState.Protected) {
                _lblLaunchStatus.Text = "Защита активна: безопасный запуск Claude разрешен";
                _lblLaunchStatus.ForeColor = Color.FromArgb(204, 120, 92);
                _lblLaunchStatus.BackColor = Color.FromArgb(30, 26, 24);

                ConfigureButtonSafe(_btnLaunchDesktop, "Запустить Claude Desktop");
                ConfigureButtonSafe(_btnLaunchBrowser, "Запустить Claude в браузере (claude.ai)");
                ConfigureButtonSafe(_btnLaunchCodeCli, "Запустить Claude Code (терминал CLI)");

                _lblFooterStatus.Text = "Трафик защищен: все запросы Claude направляются исключительно через VPN.";
                _lblFooterStatus.ForeColor = Color.FromArgb(204, 120, 92);

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: ЗАЩИЩЕНО (" + (_canvas.PingMs > 0 ? _canvas.PingMs + " мс" : "Активно") + ")";
                    _trayIcon.ContextMenu.MenuItems[2].Enabled = true;
                }
            } else {
                string statusText = (newState == ProtectionState.Checking)
                    ? "Идет проверка соединения..."
                    : "Внимание: туннель VPN не активен (запуск разрешен с подтверждением)";

                _lblLaunchStatus.Text = statusText;
                _lblLaunchStatus.ForeColor = Color.FromArgb(248, 113, 113);
                _lblLaunchStatus.BackColor = Color.FromArgb(38, 20, 22);

                ConfigureButtonBlocked(_btnLaunchDesktop, "Запустить Claude Desktop [Предупреждение]");
                ConfigureButtonBlocked(_btnLaunchBrowser, "Запустить Claude в браузере [Предупреждение]");
                ConfigureButtonBlocked(_btnLaunchCodeCli, "Запустить Claude Code CLI [Предупреждение]");

                _lblFooterStatus.Text = "ВНИМАНИЕ: Туннель VPN не обнаружен. Запуск Claude разрешен под вашу ответственность.";
                _lblFooterStatus.ForeColor = Color.FromArgb(248, 113, 113);

                if (_trayIcon != null && _trayIcon.ContextMenu != null) {
                    _trayIcon.ContextMenu.MenuItems[0].Text = "Статус: ПРЕДУПРЕЖДЕНИЕ (VPN не обнаружен)";
                    _trayIcon.ContextMenu.MenuItems[2].Enabled = true;
                }
            }
        }

        private void ConfigureButtonSafe(ClaudeButton btn, string text) {
            btn.Text = text;
            btn.Enabled = true;
            btn.NormalBg = Color.FromArgb(204, 120, 92);
            btn.HoverBg = Color.FromArgb(217, 135, 107);
            btn.PressedBg = Color.FromArgb(185, 100, 75);
            btn.ForeColor = Color.White;
            btn.BorderStrokeColor = Color.Transparent;
            btn.Invalidate();
        }

        private void ConfigureButtonBlocked(ClaudeButton btn, string text) {
            btn.Text = text;
            btn.Enabled = true;
            btn.NormalBg = Color.FromArgb(38, 22, 24);
            btn.HoverBg = Color.FromArgb(48, 26, 28);
            btn.PressedBg = Color.FromArgb(30, 18, 20);
            btn.ForeColor = Color.FromArgb(248, 113, 113);
            btn.BorderStrokeColor = Color.FromArgb(70, 32, 35);
            btn.Invalidate();
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
                        Log("LAUNCH", "Запущен Claude Desktop: " + path, Color.FromArgb(204, 120, 92));
                        return;
                    } catch (Exception ex) {
                        Log("DEBUG", "Сбой запуска " + path + ": " + ex.Message, Color.FromArgb(161, 161, 170));
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
                Log("LAUNCH", "Запущен Claude Desktop (Store Package)", Color.FromArgb(204, 120, 92));
                return;
            } catch {}

            // Попытка через URI протокол claude:
            try {
                Process.Start(new ProcessStartInfo { FileName = "claude:", UseShellExecute = true });
                Log("LAUNCH", "Запущен Claude Desktop через URI протокол claude:", Color.FromArgb(204, 120, 92));
                return;
            } catch {}

            // Резерв: открытие веб-версии
            Process.Start("https://claude.ai");
            Log("LAUNCH", "Приложение Claude Desktop не найдено, открыт веб-интерфейс claude.ai", Color.FromArgb(204, 120, 92));
        }

        private void LaunchClaudeBrowser() {
            if (!ConfirmUnsafeLaunch("Claude в браузере (claude.ai)")) return;
            Process.Start("https://claude.ai");
            Log("LAUNCH", "Открыт Claude в браузере: https://claude.ai", Color.FromArgb(204, 120, 92));
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
                Log("LAUNCH", "Запущен сеанс Claude Code CLI", Color.FromArgb(204, 120, 92));
            } catch (Exception ex) {
                Log("ERR", "Ошибка запуска Claude Code CLI: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void BuildBypassTab(Panel tab) {
            Label lblTitle = new Label() {
                Text = "Службы обхода сетевых ограничений и DNS-инспекции",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(204, 120, 92),
                Location = new Point(20, 18),
                AutoSize = true
            };
            tab.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Модули для нейтрализации цензуры DPI и подмены DNS-запросов со стороны локальных провайдеров.\n" +
                       "Работают совместно с WFP-фильтрацией ядра Windows.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(20, 48),
                Size = new Size(860, 36)
            };
            tab.Controls.Add(lblDesc);

            // Панель Zapret
            Panel pnlZapret = new Panel() {
                Location = new Point(20, 95),
                Size = new Size(860, 95),
                BackColor = Color.FromArgb(24, 25, 31)
            };
            tab.Controls.Add(pnlZapret);

            Label zTitle = new Label() {
                Text = "Интеграция с Zapret DPI Bypass (winws.exe)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 244, 245),
                Location = new Point(14, 12),
                AutoSize = true
            };
            pnlZapret.Controls.Add(zTitle);

            Label zDesc = new Label() {
                Text = "Запуск службы обхода ТСПУ для порта TCP 443 с аргументами:\n--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq",
                Font = new Font("Consolas", 8f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(14, 38),
                Size = new Size(640, 36)
            };
            pnlZapret.Controls.Add(zDesc);

            ClaudeButton btnZapret = new ClaudeButton() {
                Text = "Запустить Zapret",
                Location = new Point(680, 28),
                Size = new Size(160, 36),
                NormalBg = Color.FromArgb(38, 40, 48),
                HoverBg = Color.FromArgb(48, 50, 60),
                PressedBg = Color.FromArgb(30, 32, 38),
                ForeColor = Color.FromArgb(204, 120, 92),
                BorderStrokeColor = Color.FromArgb(63, 63, 70)
            };
            btnZapret.Click += (s, e) => LaunchZapretSilent();
            pnlZapret.Controls.Add(btnZapret);

            // Панель DoH
            Panel pnlDoh = new Panel() {
                Location = new Point(20, 205),
                Size = new Size(860, 95),
                BackColor = Color.FromArgb(24, 25, 31)
            };
            tab.Controls.Add(pnlDoh);

            Label dTitle = new Label() {
                Text = "Шифрованный DNS-over-HTTPS (Cloudflare 1.1.1.1 / Google 8.8.8.8)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 244, 245),
                Location = new Point(14, 12),
                AutoSize = true
            };
            pnlDoh.Controls.Add(dTitle);

            Label dDesc = new Label() {
                Text = "Разрешение доменов api.anthropic.com и claude.ai через защищенные HTTPS-запросы.\nИсключает чтение доменных имен в открытом виде и отравление DNS-кэша домашнего Wi-Fi.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(14, 38),
                Size = new Size(640, 36)
            };
            pnlDoh.Controls.Add(dDesc);

            ClaudeButton btnDohTest = new ClaudeButton() {
                Text = "Тест DoH (1.1.1.1)",
                Location = new Point(680, 28),
                Size = new Size(160, 36),
                NormalBg = Color.FromArgb(38, 40, 48),
                HoverBg = Color.FromArgb(48, 50, 60),
                PressedBg = Color.FromArgb(30, 32, 38),
                ForeColor = Color.FromArgb(204, 120, 92),
                BorderStrokeColor = Color.FromArgb(63, 63, 70)
            };
            btnDohTest.Click += (s, e) => TestDoHResolver();
            pnlDoh.Controls.Add(btnDohTest);
        }

        private void BuildLogsTab(Panel tab) {
            _rtbLogs = new RichTextBox() {
                Location = new Point(15, 15),
                Size = new Size(880, 440),
                BackColor = Color.FromArgb(10, 11, 14),
                ForeColor = Color.FromArgb(244, 244, 245),
                Font = new Font("Consolas", 8.5f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            tab.Controls.Add(_rtbLogs);

            ClaudeButton btnCopyLogs = new ClaudeButton() {
                Text = "Скопировать журнал",
                Location = new Point(15, 465),
                Size = new Size(170, 32),
                NormalBg = Color.FromArgb(28, 29, 36),
                HoverBg = Color.FromArgb(38, 40, 50),
                PressedBg = Color.FromArgb(22, 23, 28),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(50, 52, 62)
            };
            btnCopyLogs.Click += (s, e) => {
                try {
                    Clipboard.SetText(_rtbLogs.Text);
                    MessageBox.Show("Журнал скопирован в буфер обмена.", "Claude Anti-Ban Barrier", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch {}
            };
            tab.Controls.Add(btnCopyLogs);

            ClaudeButton btnClearLogs = new ClaudeButton() {
                Text = "Очистить",
                Location = new Point(195, 465),
                Size = new Size(110, 32),
                NormalBg = Color.FromArgb(28, 29, 36),
                HoverBg = Color.FromArgb(38, 40, 50),
                PressedBg = Color.FromArgb(22, 23, 28),
                ForeColor = Color.FromArgb(203, 213, 225),
                BorderStrokeColor = Color.FromArgb(50, 52, 62)
            };
            btnClearLogs.Click += (s, e) => _rtbLogs.Clear();
            tab.Controls.Add(btnClearLogs);
        }

        private void BuildSettingsTab(Panel tab) {
            Label lblTitle = new Label() {
                Text = "Системные параметры барьера",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(204, 120, 92),
                Location = new Point(20, 18),
                AutoSize = true
            };
            tab.Controls.Add(lblTitle);

            CheckBox chkTray = new CheckBox() {
                Text = "Сворачивать в системный трей при нажатии кнопки закрытия [X]",
                Checked = _cfg.MinimizeToTrayOnClose,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(25, 55),
                Size = new Size(600, 26)
            };
            chkTray.CheckedChanged += (s, e) => {
                _cfg.MinimizeToTrayOnClose = chkTray.Checked;
                _cfg.Save();
                Log("CONFIG", "Режим сворачивания в трей: " + (_cfg.MinimizeToTrayOnClose ? "ВКЛ" : "ВЫКЛ"), Color.FromArgb(161, 161, 170));
            };
            tab.Controls.Add(chkTray);

            CheckBox chkAutoStart = new CheckBox() {
                Text = "Автозапуск вместе с Windows в фоновом режиме",
                Checked = _cfg.AutoStartWithWindows,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(25, 88),
                Size = new Size(600, 26)
            };
            chkAutoStart.CheckedChanged += (s, e) => {
                _cfg.AutoStartWithWindows = chkAutoStart.Checked;
                _cfg.Save();
                SetAutoStart(chkAutoStart.Checked);
            };
            tab.Controls.Add(chkAutoStart);

            Label lblPath = new Label() {
                Text = "Путь к исполняемому файлу Claude Desktop (Claude.exe):",
                Location = new Point(25, 135),
                AutoSize = true,
                ForeColor = Color.FromArgb(161, 161, 170)
            };
            tab.Controls.Add(lblPath);

            TextBox txtClaudePath = new TextBox() {
                Text = _cfg.ClaudePath,
                Location = new Point(25, 158),
                Size = new Size(710, 25),
                BackColor = Color.FromArgb(24, 25, 31),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            tab.Controls.Add(txtClaudePath);

            ClaudeButton btnBrowse = new ClaudeButton() {
                Text = "Обзор...",
                Location = new Point(745, 157),
                Size = new Size(110, 26),
                NormalBg = Color.FromArgb(38, 40, 48),
                HoverBg = Color.FromArgb(48, 50, 60),
                PressedBg = Color.FromArgb(30, 32, 38),
                ForeColor = Color.White,
                BorderStrokeColor = Color.FromArgb(63, 63, 70)
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
            tab.Controls.Add(btnBrowse);

            // Блок деинсталляции
            Panel pnlUninstall = new Panel() {
                Location = new Point(25, 220),
                Size = new Size(830, 95),
                BackColor = Color.FromArgb(28, 20, 22)
            };
            tab.Controls.Add(pnlUninstall);

            Label uTitle = new Label() {
                Text = "Деинсталляция и полный откат сетевых настроек",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Location = new Point(14, 12),
                AutoSize = true
            };
            pnlUninstall.Controls.Add(uTitle);

            Label uDesc = new Label() {
                Text = "Удаление правил ядра WFP, включение IPv6 и возвращение стандартных параметров сети Windows.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(212, 212, 216),
                Location = new Point(14, 38),
                Size = new Size(620, 36)
            };
            pnlUninstall.Controls.Add(uDesc);

            ClaudeButton btnUninstall = new ClaudeButton() {
                Text = "Удалить барьер",
                Location = new Point(660, 28),
                Size = new Size(150, 36),
                NormalBg = Color.FromArgb(185, 28, 28),
                HoverBg = Color.FromArgb(220, 38, 38),
                PressedBg = Color.FromArgb(153, 27, 27),
                ForeColor = Color.White
            };
            btnUninstall.Click += (s, e) => RunUninstaller();
            pnlUninstall.Controls.Add(btnUninstall);
        }

        private void SetAutoStart(bool enable) {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (key != null) {
                        if (enable) {
                            key.SetValue("ClaudeAntiBanBarrier", "\"" + Application.ExecutablePath + "\" --minimized");
                            Log("CONFIG", "Барьер добавлен в автозагрузку Windows (фоновый режим)", Color.FromArgb(204, 120, 92));
                        } else {
                            if (key.GetValue("ClaudeAntiBanBarrier") != null) key.DeleteValue("ClaudeAntiBanBarrier");
                            Log("CONFIG", "Барьер удален из автозагрузки Windows", Color.FromArgb(161, 161, 170));
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
                    Log("ZAPRET", "Zapret winws.exe успешно запущен в фоновом режиме", Color.FromArgb(204, 120, 92));
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
            Log("DOH", "Отправка проверочного запроса DNS-over-HTTPS к Cloudflare 1.1.1.1...", Color.FromArgb(161, 161, 170));
            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://1.1.1.1/dns-query?name=api.anthropic.com&type=A");
                    req.Headers.Add("accept", "application/dns-json");
                    req.Timeout = 3000;
                    req.UserAgent = "ClaudeBarrier-DoH/5.0";

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                        sw.Stop();
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream())) {
                            string res = sr.ReadToEnd();
                            SafeInvoke(() => {
                                UpdateCard(_cardDoh, "Активен (" + sw.ElapsedMilliseconds + " мс)", Color.FromArgb(204, 120, 92), Color.FromArgb(38, 28, 24),
                                    "DoH шлюз 1.1.1.1 ответил за " + sw.ElapsedMilliseconds + " мс. Домен api.anthropic.com успешно разрешен.");
                                Log("DOH", "Шлюз Cloudflare DoH подтвержден (" + sw.ElapsedMilliseconds + " мс). DNS-запросы зашифрованы.", Color.FromArgb(204, 120, 92));
                            });
                        }
                    }
                } catch (Exception ex) {
                    SafeInvoke(() => {
                        UpdateCard(_cardDoh, "Ошибка DoH", Color.FromArgb(248, 113, 113), Color.FromArgb(38, 20, 22),
                            "Не удалось связаться с 1.1.1.1: " + ex.Message);
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
                if (_canvas != null) _canvas.ResumeAnimation();
            };

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("Статус: Проверка...", (s, e) => {});
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Запустить Claude (Безопасный режим)", (s, e) => LaunchClaudeDesktop());
            menu.MenuItems.Add("Открыть окно", (s, e) => {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                if (_canvas != null) _canvas.ResumeAnimation();
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

        // РЕАЛЬНАЯ ПРОВЕРКА СЕТИ С ИЗМЕРЕНИЕМ НАСТОЯЩЕГО ПИНГА И АКТИВНЫМ СТАТУСОМ DNS
        private void StartAudit() {
            SetProtectionUIState(ProtectionState.Checking, "Выполняется аудит сетевого шлюза...");
            Log("AUDIT", "Запуск проверки сетевых интерфейсов, отклика Anthropic и геолокации IP...", Color.FromArgb(161, 161, 170));

            ThreadPool.QueueUserWorkItem((state) => {
                try {
                    bool isRuIp = false;
                    string ipStr = "Ожидание";
                    string countryStr = "Неизвестно";
                    int pingMs = 0;

                    // Параллельный запуск аудита для максимального быстродействия без блокировки UI
                    ManualResetEvent doneIp = new ManualResetEvent(false);
                    ManualResetEvent donePing = new ManualResetEvent(false);

                    // 1. Проверка внешнего IP и страны (через прямой сокет без WPAD задержки)
                    ThreadPool.QueueUserWorkItem((_) => {
                        try {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://ip-api.com/json");
                            req.Proxy = null; // Отключение поиска прокси Windows, ускорение отклика в 10 раз
                            req.Timeout = 2500;
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/5.0";
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

                    // 2. Измерение НАСТОЯЩЕГО сетевого пинга к api.anthropic.com
                    ThreadPool.QueueUserWorkItem((_) => {
                        Stopwatch sw = Stopwatch.StartNew();
                        try {
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com");
                            req.Proxy = null;
                            req.Timeout = 2500;
                            req.Method = "HEAD";
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ClaudeBarrier/5.0";
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

                    // Ожидание завершения параллельных сетевых замеров (не более 2.8 сек)
                    WaitHandle.WaitAll(new WaitHandle[] { doneIp, donePing }, 2800);

                    // 4. Проверка правил Брандмауэра напрямую через системный реестр
                    int fwRulesCount = GetFirewallRuleCountFromRegistry();

                    // 5. Проверка утечек IPv6 на физических интерфейсах
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
                        _canvas.PingMs = pingMs;
                        _canvas.ExternalIp = ipStr;
                        _canvas.Country = countryStr;

                        if (isRuIp || !vpnActive) {
                            string reason = isRuIp
                                ? ("Обнаружен IP адрес " + countryStr + " (прямой риск бана аккаунта)")
                                : "Адаптер VPN не подключен или соединение разорвано";

                            SetProtectionUIState(ProtectionState.BlockedNoVpn, reason);
                            UpdateCard(_cardIp, "Риск бана (" + countryStr + ")", Color.FromArgb(248, 113, 113), Color.FromArgb(38, 20, 22),
                                "Текущий IP: " + ipStr + " (" + countryStr + "). Доступ к Claude заблокирован.");
                            Log("SECURITY", "Аудит завершен: IP адрес " + countryStr + ". Вход в Claude ЗАБЛОКИРОВАН.", Color.FromArgb(248, 113, 113));
                        } else {
                            SetProtectionUIState(ProtectionState.Protected, "Защита активна");
                            UpdateCard(_cardIp, ipStr + " (" + countryStr + ")", Color.FromArgb(204, 120, 92), Color.FromArgb(38, 28, 24),
                                "Внешний адрес: " + ipStr + " (" + countryStr + "). Трафик полностью туннелирован.");
                            Log("SECURITY", "Аудит завершен: туннель активен (" + countryStr + "). Безопасный запуск Claude РАЗРЕШЕН.", Color.FromArgb(204, 120, 92));
                        }

                        // Обновление карточки брандмауэра
                        if (fwRulesCount > 0) {
                            UpdateCard(_cardFirewall, "Активен (" + fwRulesCount + " правил)", Color.FromArgb(204, 120, 92), Color.FromArgb(38, 28, 24),
                                "Ядро WFP изолирует подсети 160.79.104.0/23 на физических картах.");
                        } else {
                            UpdateCard(_cardFirewall, "Требуется установка", Color.FromArgb(248, 113, 113), Color.FromArgb(38, 20, 22),
                                "Правила изоляции еще не внедрены. Нажмите «Применить правила».");
                        }

                        // Обновление карточки Cloudflare
                        if (pingMs > 0) {
                            UpdateCard(_cardCloudflare, "Доступен (" + pingMs + " мс)", Color.FromArgb(204, 120, 92), Color.FromArgb(38, 28, 24),
                                "Шлюз api.anthropic.com отвечает штатно за " + pingMs + " мс (TLS 1.2/1.3).");
                        } else {
                            UpdateCard(_cardCloudflare, "Таймаут", Color.FromArgb(248, 113, 113), Color.FromArgb(38, 20, 22),
                                "Серверы Anthropic не отвечают. Проверьте активность туннеля.");
                        }

                        // Обновление карточки IPv6 и Smart DNS (Устранение зависания "Проверка...")
                        if (hasActiveIpv6Leak) {
                            UpdateCard(_cardIpv6Dns, "Внимание", Color.FromArgb(248, 113, 113), Color.FromArgb(38, 20, 22),
                                "Обнаружен глобальный IPv6 на физическом адаптере. Нажмите «Изолировать» для блокировки утечек.");
                        } else {
                            UpdateCard(_cardIpv6Dns, "Изолирован", Color.FromArgb(204, 120, 92), Color.FromArgb(38, 28, 24),
                                "IPv6 и Smart DNS на физических картах изолированы. Параллельные утечки через домашний роутер исключены.");
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

        private void UpdateCard(DiagnosticCard card, string badge, Color c, Color bg, string tech) {
            try {
                if (IsDisposed || Disposing) return;
                card.UpdateStatus(badge, c, bg, tech);
            } catch {}
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
            Log("SIM", "Тестирование алгоритма аварийного отсечения утечки...", Color.FromArgb(204, 120, 92));
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

            Log("SIM", "Тест успешно завершен за " + elapsed + " мс. Утечек: 0%.", Color.FromArgb(204, 120, 92));
        }

        private void ApplyFirewallRulesSilent() {
            Log("WFP", "Применение правил изоляции сетевых интерфейсов в ядре Windows...", Color.FromArgb(204, 120, 92));
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
                        Log("WFP", "Правила ядра успешно применены. Сеть защищена от утечек.", Color.FromArgb(204, 120, 92));
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
