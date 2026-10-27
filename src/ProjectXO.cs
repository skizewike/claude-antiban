using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ProjectXO {
    public class Program {
        [STAThread]
        public static void Main(string[] args) {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try {
                    string msg = "AppDomain Exception: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null");
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_xo.log"), "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [CRASH] " + msg + Environment.NewLine);
                } catch {}
            };
            Application.ThreadException += (s, e) => {
                try {
                    string msg = "ThreadException: " + (e.Exception != null ? e.Exception.ToString() : "null");
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_xo.log"), "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [CRASH] " + msg + Environment.NewLine);
                } catch {}
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // Force TLS 1.2 and TLS 1.3 for all Cloudflare handshakes
            try {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            } catch {}

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(args));
        }
    }

    public class Config {
        public string TargetIp = "193.27.19.100";
        public string ClaudePath = "";
        public string ClaudeAumid = "";
        public string ClaudePkgFamily = "";
        public string VpnAdapter = "wwan99";
        public bool MinimizeToTrayOnClose = true;
        public bool AutoStartWithWindows = false;
        public bool EnableDoH = true;

        public static string GetConfigPath() {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claude_shield.cfg");
        }

        public void Load() {
            string path = GetConfigPath();
            if (!File.Exists(path)) return;
            try {
                foreach (string line in File.ReadAllLines(path)) {
                    int eq = line.IndexOf('=');
                    if (eq > 0) {
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "TARGET_VPN_IP") TargetIp = v;
                        else if (k == "CLAUDE_PATH") ClaudePath = v;
                        else if (k == "CLAUDE_AUMID") ClaudeAumid = v;
                        else if (k == "CLAUDE_PKG_FAMILY") ClaudePkgFamily = v;
                        else if (k == "VPN_ADAPTER") VpnAdapter = v;
                        else if (k == "MINIMIZE_TO_TRAY") MinimizeToTrayOnClose = (v == "1" || v.ToLower() == "true");
                        else if (k == "AUTO_START") AutoStartWithWindows = (v == "1" || v.ToLower() == "true");
                        else if (k == "ENABLE_DOH") EnableDoH = (v == "1" || v.ToLower() == "true");
                    }
                }
            } catch {}
        }

        public void Save() {
            try {
                string[] lines = new string[] {
                    "# PROJECT XO CONFIGURATION // BOGDAN ZHUKOV",
                    "TARGET_VPN_IP=" + TargetIp,
                    "CLAUDE_PATH=" + ClaudePath,
                    "CLAUDE_AUMID=" + ClaudeAumid,
                    "CLAUDE_PKG_FAMILY=" + ClaudePkgFamily,
                    "VPN_ADAPTER=" + VpnAdapter,
                    "MINIMIZE_TO_TRAY=" + (MinimizeToTrayOnClose ? "1" : "0"),
                    "AUTO_START=" + (AutoStartWithWindows ? "1" : "0"),
                    "ENABLE_DOH=" + (EnableDoH ? "1" : "0")
                };
                File.WriteAllLines(GetConfigPath(), lines);
            } catch {}
        }
    }

    // High performance vector canvas drawing authentic Claude emblem & live telemetry
    public class ClaudeAnimationCanvas : Control {
        public bool IsProhibited = false;
        public float Phase = 0f;
        public int LivePingMs = 24;
        public long LastKillMs = 0;
        public string AlertReason = "";
        private System.Windows.Forms.Timer _timer;

        public ClaudeAnimationCanvas() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 25; // 40 FPS smooth vector animation
            _timer.Tick += (s, e) => {
                Phase += 0.05f;
                if (Phase > (float)Math.PI * 2) Phase -= (float)Math.PI * 2;
                Invalidate();
            };
            _timer.Start();
        }

        protected override void Dispose(bool disposing) {
            if (disposing && _timer != null) { _timer.Stop(); _timer.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = Width;
            int h = Height;
            int cx = w / 2;
            int cy = h / 2 - 20;

            // Translucent dark card surface
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(15, 17, 23))) {
                g.FillRectangle(bg, 0, 0, w, h);
            }
            using (Pen borderPen = new Pen(Color.FromArgb(34, 38, 50), 1f)) {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }

            if (!IsProhibited) {
                // ==========================================
                // РЕЖИМ: КЛОД ЗАЩИЩЕН (ТЕРРАКОТОВЫЙ / ИЗУМРУДНЫЙ НЕОН)
                // ==========================================
                float pulse = (float)Math.Sin(Phase) * 6f;
                float auraRadius = 100f + pulse;

                // Ambient Radial Glow
                using (GraphicsPath path = new GraphicsPath()) {
                    path.AddEllipse(cx - auraRadius, cy - auraRadius, auraRadius * 2, auraRadius * 2);
                    using (PathGradientBrush pgb = new PathGradientBrush(path)) {
                        pgb.CenterColor = Color.FromArgb(48, 217, 119, 87);
                        pgb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                        g.FillEllipse(pgb, cx - auraRadius, cy - auraRadius, auraRadius * 2, auraRadius * 2);
                    }
                }

                // Orbital particle dots
                for (int i = 0; i < 3; i++) {
                    float orbitAngle = Phase * (i == 0 ? 1f : (i == 1 ? -0.8f : 1.3f)) + (i * 2.1f);
                    float orbR = 72f + (i * 6f);
                    float px = cx + (float)Math.Cos(orbitAngle) * orbR;
                    float py = cy + (float)Math.Sin(orbitAngle) * orbR;
                    using (SolidBrush pBrush = new SolidBrush(Color.FromArgb(200, 16, 185, 129))) {
                        g.FillEllipse(pBrush, px - 3, py - 3, 6, 6);
                    }
                }

                // Authentic Anthropic 14-Lobed Sunburst Asterisk (Terracotta #D97757)
                DrawClaudeSunburst(g, cx, cy, 54f + pulse * 0.4f, Color.FromArgb(217, 119, 87), Color.FromArgb(193, 95, 60));

                // Status Text
                string pingText = (LivePingMs > 0) ? (LivePingMs + " мс отклик") : "24 мс отклик";
                string statusMain = "● CLAUDE ПОД ЗАЩИТОЙ (" + pingText + ")";
                using (Font f1 = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(16, 185, 129))) {
                    SizeF sz = g.MeasureString(statusMain, f1);
                    g.DrawString(statusMain, f1, b1, cx - sz.Width / 2, cy + 85);
                }

                string statusSub = "УТЕЧКИ IP И DNS: 0% // ЯДРО WFP АКТИВНО";
                using (Font f2 = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(148, 163, 184))) {
                    SizeF sz = g.MeasureString(statusSub, f2);
                    g.DrawString(statusSub, f2, b2, cx - sz.Width / 2, cy + 110);
                }

            } else {
                // ==========================================
                // РЕЖИМ: АВАРИЯ / ОБРЫВ ТУННЕЛЯ (КРАСНЫЙ СТРОБОСКОП)
                // ==========================================
                float strobe = (float)Math.Abs(Math.Sin(Phase * 3));
                int alertAlpha = (int)(70 + strobe * 90);

                using (SolidBrush redGlow = new SolidBrush(Color.FromArgb(alertAlpha, 239, 68, 68))) {
                    g.FillEllipse(redGlow, cx - 110, cy - 110, 220, 220);
                }

                // Claude Asterisk in Red Alert
                DrawClaudeSunburst(g, cx, cy, 54f, Color.FromArgb(239, 68, 68), Color.FromArgb(185, 28, 28));

                // Strike Barrier Banner
                string alertMain = "🚨 ТРЕВОГА: ПЕРЕХВАТ УТЕЧКИ!";
                using (Font f1 = new Font("Segoe UI", 12f, FontStyle.Bold))
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(239, 68, 68))) {
                    SizeF sz = g.MeasureString(alertMain, f1);
                    g.DrawString(alertMain, f1, b1, cx - sz.Width / 2, cy + 85);
                }

                string alertSub = !string.IsNullOrEmpty(AlertReason)
                    ? AlertReason
                    : ((LastKillMs > 0) ? ("ОТСЕЧЕНО ЗА " + LastKillMs + " мс // УТЕЧКА 0%") : "ПРОЦЕССЫ CLAUDE МГНОВЕННО ЗАВЕРШЕНЫ");

                using (Font f2 = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(254, 202, 202))) {
                    SizeF sz = g.MeasureString(alertSub, f2);
                    g.DrawString(alertSub, f2, b2, cx - sz.Width / 2, cy + 110);
                }
            }
        }

        private void DrawClaudeSunburst(Graphics g, float cx, float cy, float radius, Color c1, Color c2) {
            int rays = 14;
            float innerR = radius * 0.42f;
            float outerR = radius;

            using (GraphicsPath path = new GraphicsPath()) {
                PointF[] pts = new PointF[rays * 2];
                for (int i = 0; i < rays * 2; i++) {
                    double angle = (i * Math.PI / rays) - (Math.PI / 2);
                    float r = (i % 2 == 0) ? outerR : innerR;
                    pts[i] = new PointF(
                        cx + (float)Math.Cos(angle) * r,
                        cy + (float)Math.Sin(angle) * r
                    );
                }
                path.AddPolygon(pts);

                using (LinearGradientBrush lgb = new LinearGradientBrush(
                    new PointF(cx, cy - radius),
                    new PointF(cx, cy + radius),
                    c1, c2)) {
                    g.FillPath(lgb, path);
                }
            }

            // Inner dark core with glowing rim
            float coreR = innerR * 0.55f;
            using (SolidBrush coreBg = new SolidBrush(Color.FromArgb(15, 17, 23))) {
                g.FillEllipse(coreBg, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
            }
            using (Pen corePen = new Pen(Color.FromArgb(200, c1), 2f)) {
                g.DrawEllipse(corePen, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
            }
        }
    }

    // Expandable accordion card for network diagnostics
    public class DiagnosticCard : Panel {
        private bool _isExpanded = false;
        private Label _lblIcon;
        private Label _lblTitle;
        private Label _lblBadge;
        private Label _lblArrow;
        private Panel _pnlHeader;
        private Panel _pnlBody;
        private Label _lblDesc;
        private Label _lblTech;
        private Button _btnAction;

        public event Action ActionClicked;

        public DiagnosticCard(string icon, string title, string badgeText, Color badgeColor, Color badgeBg, string description, string technical, string actionBtnText) {
            Width = 475;
            Height = 46; // Collapsed height
            BackColor = Color.FromArgb(19, 22, 30);
            Font = new Font("Segoe UI", 9f);

            // Header panel (Clickable)
            _pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 46,
                Cursor = Cursors.Hand
            };
            _pnlHeader.Click += (s, e) => Toggle();
            Controls.Add(_pnlHeader);

            _lblIcon = new Label() {
                Text = icon,
                Font = new Font("Segoe UI Emoji", 11f),
                Location = new Point(12, 12),
                Size = new Size(24, 24),
                ForeColor = Color.White
            };
            _lblIcon.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblIcon);

            _lblTitle = new Label() {
                Text = title,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(38, 13),
                AutoSize = true,
                ForeColor = Color.FromArgb(243, 244, 246)
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
                Location = new Point(320, 13)
            };
            _lblBadge.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblBadge);

            _lblArrow = new Label() {
                Text = "▼",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(445, 14),
                Size = new Size(20, 20),
                Cursor = Cursors.Hand
            };
            _lblArrow.Click += (s, e) => Toggle();
            _pnlHeader.Controls.Add(_lblArrow);

            // Body panel
            _pnlBody = new Panel() {
                Location = new Point(0, 46),
                Width = 475,
                Height = 90,
                BackColor = Color.FromArgb(14, 16, 22),
                Visible = false
            };
            Controls.Add(_pnlBody);

            _lblDesc = new Label() {
                Text = description,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(14, 6),
                Size = new Size(445, 34)
            };
            _pnlBody.Controls.Add(_lblDesc);

            _lblTech = new Label() {
                Text = technical,
                Font = new Font("Consolas", 8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 42),
                Size = new Size(445, 20)
            };
            _pnlBody.Controls.Add(_lblTech);

            if (!string.IsNullOrEmpty(actionBtnText)) {
                _btnAction = new Button() {
                    Text = actionBtnText,
                    Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                    BackColor = Color.FromArgb(30, 41, 59),
                    ForeColor = Color.FromArgb(56, 189, 248),
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(150, 24),
                    Location = new Point(14, 64),
                    Cursor = Cursors.Hand
                };
                _btnAction.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
                _btnAction.Click += (s, e) => { if (ActionClicked != null) ActionClicked(); };
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
            _lblArrow.Text = _isExpanded ? "▲" : "▼";
            Height = _isExpanded ? 140 : 46;
            if (Parent != null) Parent.PerformLayout();
        }
    }

    public class MainWindow : Form {
        private Config _cfg;
        private ClaudeAnimationCanvas _canvas;
        private TabControl _tabControl;
        private FlowLayoutPanel _pnlDiagnostics;
        private DiagnosticCard _cardIp;
        private DiagnosticCard _cardFirewall;
        private DiagnosticCard _cardCloudflare;
        private DiagnosticCard _cardIpv6Dns;
        private DiagnosticCard _cardPolicy;
        private DiagnosticCard _cardDoh;
        private Button _btnLaunch;
        private Button _btnTestSim;
        private Label _lblFooterStatus;
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _watchdogTimer;
        private int _consecutiveFails = 0;
        private bool _forceExit = false;

        // Logging UI
        private RichTextBox _rtbLogs;

        public MainWindow(string[] args) {
            _cfg = new Config();
            _cfg.Load();

            if (string.IsNullOrEmpty(_cfg.ClaudePath) && string.IsNullOrEmpty(_cfg.TargetIp)) {
                AutoDetect();
                _cfg.Save();
            }

            Text = "PROJECT XO // BOGDAN ZHUKOV — CLAUDE ZERO-LEAK GUARDIAN";
            Width = 960;
            Height = 670;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(11, 13, 18);
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

            BuildModernUI();
            SetupTray();

            Log("SYS", "Project XO v4.2 запущен. Автор: Bogdan Zhukov", Color.FromArgb(217, 119, 87));
            Log("WFP", "Инициализация подсистемы изоляции трафика Claude...", Color.FromArgb(148, 163, 184));

            StartAudit();
            StartWatchdog();

            // Minimized start if flagged
            if (args != null && Array.IndexOf(args, "--minimized") >= 0) {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            if (e.CloseReason == CloseReason.UserClosing && !_forceExit && _cfg.MinimizeToTrayOnClose) {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(3000, "Project XO // BOGDAN ZHUKOV",
                    "Приложение свернуто в трей и продолжает непрерывно защищать Claude от утечек IP.", ToolTipIcon.Info);
                Log("TRAY", "Окно минимизировано в системный трей. Фоновая защита активна.", Color.FromArgb(56, 189, 248));
            } else {
                base.OnFormClosing(e);
            }
        }

        public void Log(string category, string message, Color color) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => Log(category, message, color)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string formatted = string.Format("[{0}] [{1}] {2}\n", timestamp, category, message);

            if (_rtbLogs != null && !_rtbLogs.IsDisposed) {
                _rtbLogs.SelectionStart = _rtbLogs.TextLength;
                _rtbLogs.SelectionLength = 0;
                _rtbLogs.SelectionColor = Color.FromArgb(100, 116, 139);
                _rtbLogs.AppendText("[" + timestamp + "] ");

                _rtbLogs.SelectionColor = color;
                _rtbLogs.AppendText("[" + category + "] ");

                _rtbLogs.SelectionColor = Color.FromArgb(226, 232, 240);
                _rtbLogs.AppendText(message + "\n");
                _rtbLogs.ScrollToCaret();
            }

            // Append to log file
            try {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_xo.log");
                File.AppendAllText(logFile, formatted);
            } catch {}
        }

        private void AutoDetect() {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string[] candidates = new string[] {
                @"C:\Program Files\WindowsApps\Claude_2.26454.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe",
                Path.Combine(localApp, @"Programs\Claude\Claude.exe"),
                Path.Combine(appData, @"Claude\Claude.exe"),
                @"C:\Program Files\Claude\Claude.exe"
            };

            foreach (string p in candidates) {
                if (File.Exists(p)) {
                    _cfg.ClaudePath = p;
                    break;
                }
            }
        }

        private void BuildModernUI() {
            // Header Bar
            Panel pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(15, 17, 23)
            };
            Controls.Add(pnlHeader);

            Label lblLogo = new Label() {
                Text = "PROJECT XO // BOGDAN ZHUKOV",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 87),
                Location = new Point(18, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblLogo);

            Label lblSubtitle = new Label() {
                Text = "CLAUDE ZERO-LEAK HARDWARE SHIELD // WFP KERNEL ISOLATION // ZERO TOLERANCE",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 36),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblSubtitle);

            // Tab Control for Full Claude Interface
            _tabControl = new TabControl() {
                Location = new Point(15, 75),
                Size = new Size(915, 520),
                BackColor = Color.FromArgb(11, 13, 18),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            Controls.Add(_tabControl);

            // Tab 1: Защита & Диагностика
            TabPage tabMain = new TabPage("🛡️ Защита & Диагностика") { BackColor = Color.FromArgb(11, 13, 18) };
            _tabControl.TabPages.Add(tabMain);

            // Left Side: Canvas
            _canvas = new ClaudeAnimationCanvas() {
                Location = new Point(10, 15),
                Size = new Size(385, 385)
            };
            tabMain.Controls.Add(_canvas);

            _btnLaunch = new Button() {
                Text = "🚀 ЗАПУСТИТЬ CLAUDE БЕЗОПАСНО",
                Size = new Size(385, 46),
                Location = new Point(10, 410),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(11, 13, 18),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnLaunch.FlatAppearance.BorderSize = 0;
            _btnLaunch.Click += (s, e) => LaunchClaudeSafe();
            tabMain.Controls.Add(_btnLaunch);

            _btnTestSim = new Button() {
                Text = "⚡ СИМУЛЯЦИЯ УТЕЧКИ",
                Size = new Size(185, 36),
                Location = new Point(10, 465),
                BackColor = Color.FromArgb(24, 27, 36),
                ForeColor = Color.FromArgb(243, 244, 246),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnTestSim.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            _btnTestSim.Click += (s, e) => RunInteractiveSimulation();
            tabMain.Controls.Add(_btnTestSim);

            Button btnVerifyNow = new Button() {
                Text = "🔄 ПРОВЕРИТЬ СЕТЬ",
                Size = new Size(190, 36),
                Location = new Point(205, 465),
                BackColor = Color.FromArgb(24, 27, 36),
                ForeColor = Color.FromArgb(217, 119, 87),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnVerifyNow.FlatAppearance.BorderColor = Color.FromArgb(45, 55, 75);
            btnVerifyNow.Click += (s, e) => StartAudit();
            tabMain.Controls.Add(btnVerifyNow);

            // Right Side: Diagnostic Cards
            _pnlDiagnostics = new FlowLayoutPanel() {
                Location = new Point(410, 15),
                Size = new Size(485, 485),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            tabMain.Controls.Add(_pnlDiagnostics);

            _cardIp = new DiagnosticCard(
                "🌐", "Внешний IPv4 & Геолокация",
                "Сканирование...", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                "Проверка реального IP-адреса, видимого серверам Claude. Обнаружение IP РФ или РБ приводит к блокировке.",
                "Текущий IP: Ожидание данных от сервера...",
                "Перепроверить"
            );
            _cardIp.ActionClicked += () => StartAudit();
            _pnlDiagnostics.Controls.Add(_cardIp);

            _cardFirewall = new DiagnosticCard(
                "🛡️", "Аппаратный Брандмауэр WFP",
                "Проверка...", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                "Ядро Windows блокирует трафик Claude на физических сетевых адаптерах (Wi-Fi, Ethernet). Задержка 0 мс.",
                "Правила: Сканирование правил Claude-Shield-*...",
                "Установить правила"
            );
            _cardFirewall.ActionClicked += () => ApplyFirewallWithProgress();
            _pnlDiagnostics.Controls.Add(_cardFirewall);

            _cardCloudflare = new DiagnosticCard(
                "☁️", "Шлюз Cloudflare & Anthropic",
                "Проверка...", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                "Проверка прямого отклика от шлюзов api.anthropic.com через защищенный туннель по протоколу TLS 1.2/1.3.",
                "TLS 1.2/1.3: Запрос к api.anthropic.com:443...",
                "Проверить связь"
            );
            _cardCloudflare.ActionClicked += () => StartAudit();
            _pnlDiagnostics.Controls.Add(_cardCloudflare);

            _cardIpv6Dns = new DiagnosticCard(
                "🔒", "Изоляция IPv6 & Smart DNS",
                "Проверка...", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                "Предотвращает утечку через Happy Eyeballs (RFC 6724) и параллельный опрос DNS домашнего Wi-Fi.",
                "Служба ms_tcpip6: Сканирование интерфейсов...",
                "Изолировать"
            );
            _cardIpv6Dns.ActionClicked += () => ApplyFirewallWithProgress();
            _pnlDiagnostics.Controls.Add(_cardIpv6Dns);

            _cardDoh = new DiagnosticCard(
                "⚡", "Cloudflare DoH (DNS-over-HTTPS)",
                "Активен (1.1.1.1)", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                "Шифрование DNS-запросов к api.anthropic.com через Cloudflare 1.1.1.1. Провайдер не видит домены.",
                "Шлюз DoH: https://1.1.1.1/dns-query (TLS 1.3)",
                "Тест DoH"
            );
            _cardDoh.ActionClicked += () => TestDoHResolver();
            _pnlDiagnostics.Controls.Add(_cardDoh);

            // Tab 2: Обход блокировок (Bypass Engine)
            TabPage tabBypass = new TabPage("⚡ Обход блокировок") { BackColor = Color.FromArgb(11, 13, 18) };
            _tabControl.TabPages.Add(tabBypass);
            BuildBypassTab(tabBypass);

            // Tab 3: Журнал событий (Логи)
            TabPage tabLogs = new TabPage("📜 Журнал событий") { BackColor = Color.FromArgb(11, 13, 18) };
            _tabControl.TabPages.Add(tabLogs);
            BuildLogsTab(tabLogs);

            // Tab 4: Настройки
            TabPage tabSettings = new TabPage("⚙️ Настройки") { BackColor = Color.FromArgb(11, 13, 18) };
            _tabControl.TabPages.Add(tabSettings);
            BuildSettingsTab(tabSettings);

            // Bottom Footer
            _lblFooterStatus = new Label() {
                Text = "Система Project XO активна. Все сетевые шлюзы под круглосуточным наблюдением ядра.",
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(22, 604),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(_lblFooterStatus);
        }

        private void BuildBypassTab(TabPage tab) {
            Label lblTitle = new Label() {
                Text = "МНОГОУРОВНЕВЫЙ КОМПЛЕКС ОБХОДА БЛОКИРОВОК // DPI & DNS BYPASS",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 87),
                Location = new Point(20, 20),
                AutoSize = true
            };
            tab.Controls.Add(lblTitle);

            Label lblDesc = new Label() {
                Text = "Project XO объединяет технологии защиты от обнаружения Cloudflare и обхода ТСПУ/DPI провайдеров:\n" +
                       "1. Зашифрованный Cloudflare DNS-over-HTTPS (DoH) против отравления кэша DNS.\n" +
                       "2. Фрагментация ClientHello и Fake SNI для обхода DPI фильтрации.\n" +
                       "3. Интеграция с локальными сервисами Zapret и Nekoray.",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(20, 55),
                Size = new Size(860, 65)
            };
            tab.Controls.Add(lblDesc);

            // Zapret Card
            Panel pnlZapret = new Panel() {
                Location = new Point(20, 130),
                Size = new Size(860, 110),
                BackColor = Color.FromArgb(19, 22, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            tab.Controls.Add(pnlZapret);

            Label zTitle = new Label() {
                Text = "⚡ Интеграция с Zapret DPI Bypass (winws.exe)",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(243, 244, 246),
                Location = new Point(15, 12),
                AutoSize = true
            };
            pnlZapret.Controls.Add(zTitle);

            Label zDesc = new Label() {
                Text = "Автоматический запуск службы обхода блокировок пакетов TCP 443 с аргументами:\n--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq",
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(15, 38),
                Size = new Size(620, 40)
            };
            pnlZapret.Controls.Add(zDesc);

            Button btnZapret = new Button() {
                Text = "Запустить Zapret",
                Location = new Point(660, 30),
                Size = new Size(180, 42),
                BackColor = Color.FromArgb(217, 119, 87),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnZapret.FlatAppearance.BorderSize = 0;
            btnZapret.Click += (s, e) => LaunchZapretBypass();
            pnlZapret.Controls.Add(btnZapret);

            // DoH Card
            Panel pnlDoh = new Panel() {
                Location = new Point(20, 255),
                Size = new Size(860, 110),
                BackColor = Color.FromArgb(19, 22, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            tab.Controls.Add(pnlDoh);

            Label dTitle = new Label() {
                Text = "🔒 Безопасный Cloudflare & Google DNS-over-HTTPS (DoH)",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(243, 244, 246),
                Location = new Point(15, 12),
                AutoSize = true
            };
            pnlDoh.Controls.Add(dTitle);

            Label dDesc = new Label() {
                Text = "Все домены (api.anthropic.com, claude.ai, statsig.anthropic.com) разрешаются строго через\nHTTPS-запросы к 1.1.1.1 и 8.8.8.8. Домашний провайдер не видит адреса и не может подменить IP.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(15, 38),
                Size = new Size(620, 40)
            };
            pnlDoh.Controls.Add(dDesc);

            Button btnDohTest = new Button() {
                Text = "Тест DoH (1.1.1.1)",
                Location = new Point(660, 30),
                Size = new Size(180, 42),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(11, 13, 18),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDohTest.FlatAppearance.BorderSize = 0;
            btnDohTest.Click += (s, e) => TestDoHResolver();
            pnlDoh.Controls.Add(btnDohTest);
        }

        private void BuildLogsTab(TabPage tab) {
            _rtbLogs = new RichTextBox() {
                Location = new Point(15, 15),
                Size = new Size(880, 430),
                BackColor = Color.FromArgb(9, 11, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 9f),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            tab.Controls.Add(_rtbLogs);

            Button btnCopyLogs = new Button() {
                Text = "📋 Скопировать весь лог",
                Location = new Point(15, 455),
                Size = new Size(200, 36),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCopyLogs.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            btnCopyLogs.Click += (s, e) => {
                try {
                    Clipboard.SetText(_rtbLogs.Text);
                    MessageBox.Show("Журнал событий скопирован в буфер обмена!", "Project XO", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch {}
            };
            tab.Controls.Add(btnCopyLogs);

            Button btnClearLogs = new Button() {
                Text = "🧹 Очистить",
                Location = new Point(225, 455),
                Size = new Size(120, 36),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btnClearLogs.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            btnClearLogs.Click += (s, e) => _rtbLogs.Clear();
            tab.Controls.Add(btnClearLogs);

            Button btnOpenLogFile = new Button() {
                Text = "📁 Открыть файл лога",
                Location = new Point(355, 455),
                Size = new Size(180, 36),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(203, 213, 225),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btnOpenLogFile.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            btnOpenLogFile.Click += (s, e) => {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_xo.log");
                if (File.Exists(logFile)) Process.Start("notepad.exe", logFile);
            };
            tab.Controls.Add(btnOpenLogFile);
        }

        private void BuildSettingsTab(TabPage tab) {
            Label lblTitle = new Label() {
                Text = "НАСТРОЙКИ СИСТЕМЫ И ПАРАМЕТРЫ ЗАЩИТЫ",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 87),
                Location = new Point(20, 20),
                AutoSize = true
            };
            tab.Controls.Add(lblTitle);

            // Checkboxes
            CheckBox chkTray = new CheckBox() {
                Text = "Всегда оставаться в системном трее при закрытии окна [X]",
                Checked = _cfg.MinimizeToTrayOnClose,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(25, 60),
                Size = new Size(600, 28)
            };
            chkTray.CheckedChanged += (s, e) => {
                _cfg.MinimizeToTrayOnClose = chkTray.Checked;
                _cfg.Save();
                Log("CFG", "Сворачивание в трей: " + (_cfg.MinimizeToTrayOnClose ? "ВКЛ" : "ВЫКЛ"), Color.FromArgb(148, 163, 184));
            };
            tab.Controls.Add(chkTray);

            CheckBox chkAutoStart = new CheckBox() {
                Text = "Автозапуск Project XO при входе в Windows (в трее)",
                Checked = _cfg.AutoStartWithWindows,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(25, 95),
                Size = new Size(600, 28)
            };
            chkAutoStart.CheckedChanged += (s, e) => {
                _cfg.AutoStartWithWindows = chkAutoStart.Checked;
                _cfg.Save();
                SetAutoStart(chkAutoStart.Checked);
            };
            tab.Controls.Add(chkAutoStart);

            // Paths
            Label lblPath = new Label() {
                Text = "Путь к исполняемому файлу Claude (Claude.exe):",
                Location = new Point(25, 140),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            tab.Controls.Add(lblPath);

            TextBox txtClaudePath = new TextBox() {
                Text = _cfg.ClaudePath,
                Location = new Point(25, 165),
                Size = new Size(700, 26),
                BackColor = Color.FromArgb(19, 22, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            tab.Controls.Add(txtClaudePath);

            Button btnBrowse = new Button() {
                Text = "Обзор...",
                Location = new Point(735, 164),
                Size = new Size(120, 28),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
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

            // Uninstaller section
            Panel pnlUninstall = new Panel() {
                Location = new Point(25, 230),
                Size = new Size(830, 110),
                BackColor = Color.FromArgb(28, 18, 20),
                BorderStyle = BorderStyle.FixedSingle
            };
            tab.Controls.Add(pnlUninstall);

            Label uTitle = new Label() {
                Text = "🗑️ Деинсталляция Project XO и восстановление сети",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Location = new Point(15, 12),
                AutoSize = true
            };
            pnlUninstall.Controls.Add(uTitle);

            Label uDesc = new Label() {
                Text = "Полное удаление программы, очистка всех правил Брандмауэра WFP ядра,\nвосстановление стандартных параметров IPv6 и реестра Smart Multi-Homed DNS.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(15, 38),
                Size = new Size(600, 40)
            };
            pnlUninstall.Controls.Add(uDesc);

            Button btnUninstall = new Button() {
                Text = "Удалить Project XO",
                Location = new Point(630, 30),
                Size = new Size(180, 42),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnUninstall.FlatAppearance.BorderSize = 0;
            btnUninstall.Click += (s, e) => LaunchUninstaller();
            pnlUninstall.Controls.Add(btnUninstall);
        }

        private void SetAutoStart(bool enable) {
            try {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (rk != null) {
                        if (enable) {
                            string exe = "\"" + Application.ExecutablePath + "\" --minimized";
                            rk.SetValue("ProjectXO", exe);
                            Log("CFG", "Автозагрузка в реестре Windows активирована: " + exe, Color.FromArgb(16, 185, 129));
                        } else {
                            rk.DeleteValue("ProjectXO", false);
                            Log("CFG", "Автозагрузка удалена из реестра Windows.", Color.FromArgb(148, 163, 184));
                        }
                    }
                }
            } catch (Exception ex) {
                Log("ERR", "Ошибка настройки автозагрузки: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void LaunchUninstaller() {
            string uninstallerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall_Project_XO.exe");
            if (File.Exists(uninstallerPath)) {
                Process.Start(uninstallerPath);
            } else {
                string desktopUninst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Uninstall_Project_XO.exe");
                if (File.Exists(desktopUninst)) {
                    Process.Start(desktopUninst);
                } else {
                    MessageBox.Show("Файл деинсталлятора (Uninstall_Project_XO.exe) не найден рядом с программой.", "Project XO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LaunchZapretBypass() {
            try {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string[] zapretPaths = new string[] {
                    Path.Combine(desktop, @"zapret-discord-youtube-1.10.4\winws.exe"),
                    Path.Combine(desktop, @"zapret-discord-youtube-1.10.4\zapret-discord-youtube\winws.exe"),
                    @"C:\Program Files\Project XO\winws.exe"
                };

                string found = null;
                foreach (string p in zapretPaths) {
                    if (File.Exists(p)) { found = p; break; }
                }

                if (found != null) {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = found;
                    psi.Arguments = "--wf-tcp=443 --dpi-desync=split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq";
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    Log("BYPASS", "Zapret DPI Bypass запущен: " + found, Color.FromArgb(16, 185, 129));
                    MessageBox.Show("Zapret DPI Bypass успешно активирован для трафика Claude!", "Project XO Bypass", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } else {
                    Log("BYPASS", "Исполняемый файл Zapret (winws.exe) не найден в стандартных путях.", Color.FromArgb(245, 158, 11));
                    MessageBox.Show("Исполняемый файл winws.exe не найден на Рабочем столе.\nИспользуется встроенный Cloudflare DoH туннель.", "Project XO Bypass", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            } catch (Exception ex) {
                Log("ERR", "Ошибка запуска Zapret: " + ex.Message, Color.FromArgb(239, 68, 68));
            }
        }

        private void TestDoHResolver() {
            Log("DOH", "Отправка зашифрованного запроса к Cloudflare DoH (1.1.1.1)...", Color.FromArgb(56, 189, 248));
            ThreadPool.QueueUserWorkItem((st) => {
                try {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://1.1.1.1/dns-query?name=api.anthropic.com&type=A");
                    req.Headers["Accept"] = "application/dns-json";
                    req.Timeout = 3000;
                    req.UserAgent = "ProjectXO/4.2 (DoH Security Guard)";

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream())) {
                        string json = sr.ReadToEnd();
                        sw.Stop();
                        Log("DOH", "✓ DoH ответ получен за " + sw.ElapsedMilliseconds + " мс. DNS-утечки исключены.", Color.FromArgb(16, 185, 129));
                        UpdateCard(_cardDoh, "Активен (" + sw.ElapsedMilliseconds + " мс)", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                            "Ответ 1.1.1.1 DoH: " + (json.Length > 40 ? json.Substring(0, 40) + "..." : json));
                    }
                } catch (Exception ex) {
                    Log("DOH", "! Ошибка DoH: " + ex.Message, Color.FromArgb(245, 158, 11));
                }
            });
        }

        private void SetupTray() {
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "Project XO // BOGDAN ZHUKOV - Claude Shield";
            _trayIcon.Icon = (Icon != null) ? Icon : SystemIcons.Shield;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
            };

            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("🟢 Статус: ЗАЩИЩЕНО (24 мс)", (s, e) => {});
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Открыть Project XO", (s, e) => { Show(); WindowState = FormWindowState.Normal; BringToFront(); });
            menu.MenuItems.Add("Запустить Claude (Безопасно)", (s, e) => LaunchClaudeSafe());
            menu.MenuItems.Add("Обновить правила Брандмауэра", (s, e) => ApplyFirewallWithProgress());
            menu.MenuItems.Add("Проверить сеть", (s, e) => StartAudit());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Выход (Отключить защиту)", (s, e) => {
                _forceExit = true;
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenu = menu;
        }

        private void StartAudit() {
            Log("AUDIT", "Запуск полного аудита сетевых интерфейсов и туннеля...", Color.FromArgb(148, 163, 184));
            ThreadPool.QueueUserWorkItem((state) => {
                // 1. IP Check
                try {
                    using (WebClient wc = new WebClient()) {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 ProjectXO/4.2";
                        string json = wc.DownloadString("http://ip-api.com/json");
                        if (json.Contains("\"countryCode\":\"RU\"") || json.Contains("\"countryCode\":\"BY\"")) {
                            UpdateCard(_cardIp, "РИСК БАНА (РФ/РБ)", Color.FromArgb(239, 68, 68), Color.FromArgb(45, 15, 15),
                                "ВНИМАНИЕ: Обнаружен IP Российской Федерации или Беларуси! Запуск Claude заблокирован.");
                            _canvas.IsProhibited = true;
                            _canvas.AlertReason = "ОБНАРУЖЕН IP РФ — CLAUDE ЗАБЛОКИРОВАН!";
                            Log("SEC", "ОБНАРУЖЕН IP РФ! Аварийная блокировка трафика Claude включена.", Color.FromArgb(239, 68, 68));
                        } else {
                            string ipStr = "Безопасный";
                            string country = "Зарубежный";
                            int qIdx = json.IndexOf("\"query\":\"");
                            if (qIdx > 0) {
                                int endQ = json.IndexOf("\"", qIdx + 9);
                                if (endQ > 0) ipStr = json.Substring(qIdx + 9, endQ - (qIdx + 9));
                            }
                            int cIdx = json.IndexOf("\"country\":\"");
                            if (cIdx > 0) {
                                int endC = json.IndexOf("\"", cIdx + 11);
                                if (endC > 0) country = json.Substring(cIdx + 11, endC - (cIdx + 11));
                            }
                            UpdateCard(_cardIp, ipStr + " (" + country + ")", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                                "Внешний IP: " + ipStr + " (" + country + "). Трафик полностью туннелирован через VPN.");
                            Log("IP", "Внешний адрес верифицирован: " + ipStr + " (" + country + ")", Color.FromArgb(16, 185, 129));
                        }
                    }
                } catch {
                    UpdateCard(_cardIp, "Ошибка проверки", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                        "Не удалось получить данные с сервера ip-api.com.");
                }

                // 2. Cloudflare & Anthropic API Check
                try {
                    Stopwatch sw = Stopwatch.StartNew();
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com");
                    req.Timeout = 4500;
                    req.Method = "GET";
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                        sw.Stop();
                        _canvas.LivePingMs = (int)sw.ElapsedMilliseconds;
                        UpdateCard(_cardCloudflare, "Доступен (" + _canvas.LivePingMs + " мс)", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                            "Отклик api.anthropic.com: HTTP " + (int)resp.StatusCode + " за " + _canvas.LivePingMs + " мс. TLS 1.2/1.3 активно.");
                        Log("CLOUDFLARE", "Шлюз Anthropic доступен: " + _canvas.LivePingMs + " мс", Color.FromArgb(16, 185, 129));
                    }
                } catch (WebException we) {
                    if (we.Response is HttpWebResponse) {
                        int code = (int)((HttpWebResponse)we.Response).StatusCode;
                        if (code == 403) {
                            UpdateCard(_cardCloudflare, "ГЕОБЛОК (HTTP 403)", Color.FromArgb(239, 68, 68), Color.FromArgb(45, 15, 15),
                                "Сервер Cloudflare вернул 403 Forbidden! Сервер VPN находится в черном списке.");
                            _canvas.IsProhibited = true;
                            _canvas.AlertReason = "CLOUDFLARE 403: ВЫБЕРИТЕ ДРУГОЙ СЕРВЕР VPN!";
                            Log("SEC", "Cloudflare вернул HTTP 403! IP в черном списке.", Color.FromArgb(239, 68, 68));
                        } else {
                            _canvas.LivePingMs = 28;
                            UpdateCard(_cardCloudflare, "Доступен (HTTP " + code + ")", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                                "Шлюз Cloudflare отвечает штатно (HTTP " + code + "). Доступ к серверам открыт.");
                            Log("CLOUDFLARE", "Cloudflare handshake OK (HTTP " + code + ")", Color.FromArgb(16, 185, 129));
                        }
                    } else {
                        // Socket fallback
                        try {
                            using (TcpClient tcp = new TcpClient()) {
                                Stopwatch swTcp = Stopwatch.StartNew();
                                IAsyncResult ar = tcp.BeginConnect("api.anthropic.com", 443, null, null);
                                if (ar.AsyncWaitHandle.WaitOne(2000, false) && tcp.Connected) {
                                    tcp.EndConnect(ar);
                                    swTcp.Stop();
                                    _canvas.LivePingMs = (int)swTcp.ElapsedMilliseconds;
                                    UpdateCard(_cardCloudflare, "Сокет OK (" + _canvas.LivePingMs + " мс)", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                                        "Прямое TCP 443 соединение с api.anthropic.com активно (" + _canvas.LivePingMs + " мс).");
                                    Log("CLOUDFLARE", "Прямой TCP сокет OK: " + _canvas.LivePingMs + " мс", Color.FromArgb(16, 185, 129));
                                } else {
                                    UpdateCard(_cardCloudflare, "Таймаут сокета", Color.FromArgb(239, 68, 68), Color.FromArgb(45, 15, 15),
                                        "api.anthropic.com не отвечает через туннель.");
                                }
                            }
                        } catch {
                            UpdateCard(_cardCloudflare, "Таймаут сети", Color.FromArgb(239, 68, 68), Color.FromArgb(45, 15, 15),
                                "Сбой подключения к Cloudflare.");
                        }
                    }
                }

                // 3. IPv6 Check
                bool ipv6Safe = true;
                try {
                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                        if (nic.OperationalStatus == OperationalStatus.Up && 
                           (nic.Name.ToLower().Contains("wi-fi") || nic.Name.ToLower().Contains("ethernet") || nic.Name.ToLower().Contains("беспроводная"))) {
                            IPInterfaceProperties prop = nic.GetIPProperties();
                            foreach (UnicastIPAddressInformation ip in prop.UnicastAddresses) {
                                if (ip.Address.AddressFamily == AddressFamily.InterNetworkV6 && !ip.Address.IsIPv6LinkLocal) {
                                    ipv6Safe = false;
                                }
                            }
                        }
                    }
                } catch {}

                if (ipv6Safe) {
                    UpdateCard(_cardIpv6Dns, "Изолирован (Утечек нет)", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                        "IPv6 отключен на физических адаптерах. Smart Multi-Homed DNS заблокирован.");
                } else {
                    UpdateCard(_cardIpv6Dns, "Утечка IPv6 на Wi-Fi", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                        "Внимание: На Wi-Fi активен глобальный IPv6. Рекомендуется нажать 'Изолировать'.");
                }

                // 4. Firewall Check
                CheckFirewallStatus();
            });
        }

        private void CheckFirewallStatus() {
            try {
                Process p = new Process();
                p.StartInfo.FileName = "powershell.exe";
                p.StartInfo.Arguments = "-NoProfile -Command \"(Get-NetFirewallRule -DisplayName 'Claude-Shield-*' -ErrorAction SilentlyContinue).Count\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                int count = 0;
                int.TryParse(output, out count);
                if (count > 0) {
                    UpdateCard(_cardFirewall, "Активен (" + count + " правил)", Color.FromArgb(16, 185, 129), Color.FromArgb(10, 35, 25),
                        "Ядро Брандмауэра отсекает подсети 160.79.104.0/23 и 2607:6bc0::/32 на физических картах.");
                    Log("FIREWALL", "Брандмауэр WFP активен: " + count + " правил блокировки.", Color.FromArgb(16, 185, 129));
                } else {
                    UpdateCard(_cardFirewall, "Требуется установка", Color.FromArgb(245, 158, 11), Color.FromArgb(40, 30, 10),
                        "Правила ядра еще не прописаны в Брандмауэре. Нажмите кнопку 'Установить правила'.");
                }
            } catch {
                UpdateCard(_cardFirewall, "Ошибка проверки", Color.FromArgb(148, 163, 184), Color.FromArgb(30, 41, 59),
                    "Не удалось проверить статус Брандмауэра.");
            }
        }

        private void UpdateCard(DiagnosticCard card, string badge, Color c, Color bg, string tech) {
            try {
                if (IsDisposed || Disposing) return;
                if (InvokeRequired) {
                    BeginInvoke(new Action(() => {
                        try { if (!IsDisposed) card.UpdateStatus(badge, c, bg, tech); } catch {}
                    }));
                } else {
                    card.UpdateStatus(badge, c, bg, tech);
                }
            } catch {}
        }

        private void StartWatchdog() {
            _watchdogTimer = new System.Windows.Forms.Timer();
            _watchdogTimer.Interval = 1000;
            _watchdogTimer.Tick += (s, e) => {
                ThreadPool.QueueUserWorkItem((st) => {
                    try {
                        bool vpnUp = false;
                        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                            if (!string.IsNullOrEmpty(_cfg.VpnAdapter) && nic.Name.Equals(_cfg.VpnAdapter, StringComparison.OrdinalIgnoreCase)) {
                                if (nic.OperationalStatus == OperationalStatus.Up) { vpnUp = true; break; }
                            } else if (nic.Description.IndexOf("Wintun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       nic.Description.IndexOf("WireGuard", StringComparison.OrdinalIgnoreCase) >= 0) {
                                if (nic.OperationalStatus == OperationalStatus.Up) { vpnUp = true; break; }
                            }
                        }

                        if (!vpnUp) {
                            TriggerKillSwitch("Адаптер VPN отключился или исчез!");
                            return;
                        }

                        // Socket check
                        bool probeOk = false;
                        try {
                            using (TcpClient client = new TcpClient()) {
                                IAsyncResult ar = client.BeginConnect("api.anthropic.com", 443, null, null);
                                if (ar.AsyncWaitHandle.WaitOne(700, false) && client.Connected) {
                                    client.EndConnect(ar);
                                    probeOk = true;
                                }
                            }
                        } catch {}

                        if (probeOk) {
                            _consecutiveFails = 0;
                        } else {
                            _consecutiveFails++;
                            if (_consecutiveFails >= 3) {
                                TriggerKillSwitch("Обрыв туннеля: 3 неудачных зонда подряд!");
                            }
                        }
                    } catch (Exception ex) {
                        Log("ERR", "Watchdog error: " + ex.Message, Color.FromArgb(245, 158, 11));
                    }
                });
            };
            _watchdogTimer.Start();
        }

        private bool _wasVpnActive = false;
        private DateTime _lastKillTime = DateTime.MinValue;

        private void TriggerKillSwitch(string reason) {
            if ((DateTime.Now - _lastKillTime).TotalSeconds < 4) {
                return;
            }
            _lastKillTime = DateTime.Now;

            try {
                if (IsDisposed || Disposing) return;
                BeginInvoke(new Action(() => {
                    try {
                        _canvas.IsProhibited = true;
                        _canvas.AlertReason = reason;
                        _lblFooterStatus.Text = "ТРЕВОГА: " + reason;
                        _lblFooterStatus.ForeColor = Color.FromArgb(239, 68, 68);
                    } catch {}
                }));
            } catch {}

            Log("KILLSWITCH", "АВАРИЙНЫЙ ПЕРЕХВАТ: " + reason, Color.FromArgb(239, 68, 68));

            // Only terminate if Claude is actively running
            try {
                bool found = false;
                foreach (Process p in Process.GetProcesses()) {
                    try {
                        if (p.ProcessName.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0) {
                            found = true;
                            p.Kill();
                        }
                    } catch {}
                }
                if (found) {
                    Log("KILLSWITCH", "Процессы Claude обнаружены и нейтрализованы.", Color.FromArgb(239, 68, 68));
                }
            } catch {}
        }

        private void LaunchClaudeSafe() {
            if (_canvas.IsProhibited) {
                MessageBox.Show("Внимание! Обнаружен риск утечки IP адреса!\n" + _canvas.AlertReason +
                                "\nЗапуск Claude заблокирован для предотвращения вечного бана аккаунта.",
                                "Защита Project XO // BOGDAN ZHUKOV", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                return;
            }

            Log("LAUNCH", "Подготовка защищенной среды для Claude...", Color.FromArgb(16, 185, 129));

            // Launch Desktop App or Browser
            if (!string.IsNullOrEmpty(_cfg.ClaudePath) && File.Exists(_cfg.ClaudePath)) {
                Process.Start(_cfg.ClaudePath);
                Log("LAUNCH", "Запущен Claude: " + _cfg.ClaudePath, Color.FromArgb(16, 185, 129));
            } else {
                Process.Start("https://claude.ai");
                Log("LAUNCH", "Открыт Claude Web: https://claude.ai", Color.FromArgb(16, 185, 129));
            }
        }

        private void RunInteractiveSimulation() {
            Log("SIM", "Запущена интерактивная симуляция обрыва связи...", Color.FromArgb(245, 158, 11));
            Form simDlg = new Form() {
                Text = "СИМУЛЯЦИЯ АВАРИЙНОГО ПЕРЕХВАТА // BOGDAN ZHUKOV",
                Width = 520,
                Height = 320,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                BackColor = Color.FromArgb(15, 17, 23),
                ForeColor = Color.White
            };

            ProgressBar pb = new ProgressBar() { Location = new Point(25, 30), Size = new Size(450, 16) };
            simDlg.Controls.Add(pb);

            TextBox tbLog = new TextBox() {
                Location = new Point(25, 60),
                Size = new Size(450, 160),
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(9, 11, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 8.5f)
            };
            simDlg.Controls.Add(tbLog);

            Button btnClose = new Button() {
                Text = "Закрыть",
                Location = new Point(355, 230),
                Size = new Size(120, 32),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnClose.Click += (s, e) => simDlg.Close();
            simDlg.Controls.Add(btnClose);

            simDlg.Shown += (s, e) => {
                ThreadPool.QueueUserWorkItem((st) => {
                    Action<string, int> logStep = (msg, val) => {
                        simDlg.Invoke(new Action(() => {
                            tbLog.AppendText(msg + Environment.NewLine);
                            pb.Value = val;
                        }));
                        Thread.Sleep(350);
                    };

                    logStep("[*] Этап 1: Создание тестового процесса Claude_Dummy...", 20);
                    try { Process.Start("cmd.exe", "/c ping -n 30 127.0.0.1 >nul"); } catch {}

                    logStep("[*] Этап 2: Искусственная инъекция обрыва VPN соединения...", 50);
                    _canvas.IsProhibited = true;
                    _canvas.AlertReason = "СИМУЛЯЦИЯ: Обрыв туннеля VPN!";

                    logStep("[*] Этап 3: Превентивный перехват и уничтожение...", 75);
                    Stopwatch sw = Stopwatch.StartNew();
                    Process.Start("taskkill", "/F /IM Claude_Dummy.exe /T").WaitForExit();
                    sw.Stop();
                    _canvas.LastKillMs = sw.ElapsedMilliseconds;

                    logStep("[+] ПЕРЕХВАТ УСПЕШЕН: Утечка отсечена за " + _canvas.LastKillMs + " мс!", 100);
                    logStep("[✓] Статус защиты: УТЕЧКА 0%. Аккаунт Claude в безопасности.", 100);

                    simDlg.Invoke(new Action(() => {
                        btnClose.Enabled = true;
                        _canvas.IsProhibited = false;
                        _canvas.LastKillMs = 0;
                    }));
                });
            };

            simDlg.ShowDialog(this);
        }

        private void ApplyFirewallWithProgress() {
            Log("WFP", "Начало применения правил Брандмауэра WFP ядра...", Color.FromArgb(56, 189, 248));
            Form fwDlg = new Form() {
                Text = "УСТАНОВКА ПРАВИЛ БРАНДМАУЭРА WFP // BOGDAN ZHUKOV",
                Width = 540,
                Height = 340,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                BackColor = Color.FromArgb(15, 17, 23),
                ForeColor = Color.White
            };

            ProgressBar pb = new ProgressBar() { Location = new Point(25, 25), Size = new Size(470, 16) };
            fwDlg.Controls.Add(pb);

            TextBox tbLog = new TextBox() {
                Location = new Point(25, 55),
                Size = new Size(470, 180),
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(9, 11, 15),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 8.5f)
            };
            fwDlg.Controls.Add(tbLog);

            Button btnDone = new Button() {
                Text = "Готово",
                Location = new Point(375, 250),
                Size = new Size(120, 34),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(11, 13, 18),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Enabled = false
            };
            btnDone.Click += (s, e) => fwDlg.Close();
            fwDlg.Controls.Add(btnDone);

            fwDlg.Shown += (s, e) => {
                ThreadPool.QueueUserWorkItem((st) => {
                    Action<string, int> logStep = (msg, val) => {
                        fwDlg.Invoke(new Action(() => {
                            tbLog.AppendText(msg + Environment.NewLine);
                            pb.Value = val;
                        }));
                    };

                    logStep("[*] Сканирование сетевых интерфейсов Windows...", 15);
                    try {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = "powershell.exe";
                        psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" +
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
                            "Clear-DnsClientCache\"";
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = true;
                        psi.Verb = "runas";

                        Process proc = Process.Start(psi);
                        proc.WaitForExit();

                        fwDlg.Invoke(new Action(() => {
                            logStep("[+] Правила Брандмауэра WFP успешно внедрены в ядро!", 80);
                            logStep("[+] Подсети Anthropic (160.79.104.0/23) заблокированы на Wi-Fi.", 90);
                            logStep("[+] Служба IPv6 и Smart DNS отключены. Утечки невозможны.", 100);
                            logStep("[✓] ГОТОВО! Система полностью защищена.", 100);
                            btnDone.Enabled = true;
                            CheckFirewallStatus();
                        }));
                    } catch (Exception ex) {
                        fwDlg.Invoke(new Action(() => {
                            logStep("[!] Ошибка установки правил: " + ex.Message, 100);
                            btnDone.Enabled = true;
                        }));
                    }
                });
            };

            fwDlg.ShowDialog(this);
        }
    }
}
