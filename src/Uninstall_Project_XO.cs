using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Windows.Forms;
using System.Security.Principal;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ProjectXO.Uninstaller {
    public class UninstallerProgram {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [STAThread]
        public static void Main(string[] args) {
            try {
                SetDllDirectory(string.Empty);
                SetDefaultDllDirectories(0x00001000 | 0x00000800);
            } catch { }

            WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = Application.ExecutablePath,
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return;
                } catch {
                    MessageBox.Show("Для деинсталляции требуются права администратора.", "ClaudeBarrier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallerForm());
        }
    }

    public class ModernDarkProgressBar : Control {
        private int _value = 0;
        public int Value {
            get { return _value; }
            set {
                _value = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        public ModernDarkProgressBar() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 14;
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = UninstallerForm.CreateRoundedRect(r, 4)) {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 27, 41))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(35, 43, 64), 1f)) g.DrawPath(pen, p);
            }

            if (_value > 0) {
                int fillW = (int)((Width - 2) * (_value / 100.0));
                if (fillW > 4) {
                    Rectangle fillRect = new Rectangle(1, 1, fillW, Height - 2);
                    using (GraphicsPath fp = UninstallerForm.CreateRoundedRect(fillRect, 3)) {
                        using (LinearGradientBrush lgb = new LinearGradientBrush(fillRect, Color.FromArgb(220, 38, 38), Color.FromArgb(248, 113, 113), 0f)) {
                            g.FillPath(lgb, fp);
                        }
                    }
                }
            }
        }
    }

    public class UninstButton : Button {
        private bool _isHovered = false;
        private bool _isPressed = false;
        public int CornerRadius { get; set; }
        public Color GradientStart { get; set; }
        public Color GradientEnd { get; set; }
        public Color HoverStart { get; set; }
        public Color HoverEnd { get; set; }
        public Color BorderColor { get; set; }

        public UninstButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            CornerRadius = 7;
            GradientStart = Color.FromArgb(220, 38, 38);
            GradientEnd = Color.FromArgb(185, 28, 28);
            HoverStart = Color.FromArgb(239, 68, 68);
            HoverEnd = Color.FromArgb(220, 38, 38);
            BorderColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { _isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

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

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color c1 = _isPressed ? GradientEnd : (_isHovered ? HoverStart : GradientStart);
            Color c2 = _isPressed ? GradientStart : (_isHovered ? HoverEnd : GradientEnd);

            using (GraphicsPath p = UninstallerForm.CreateRoundedRect(r, CornerRadius)) {
                if (c1 != Color.Transparent) {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(r, c1, c2, 90f)) {
                        g.FillPath(lgb, p);
                    }
                }
                if (BorderColor != Color.Transparent) {
                    using (Pen pen = new Pen(BorderColor, 1f)) {
                        pen.Alignment = PenAlignment.Inset;
                        g.DrawPath(pen, p);
                    }
                }
            }

            Color textColor = _isHovered ? Color.White : ForeColor;
            TextRenderer.DrawText(g, Text, Font, r, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public class UninstallerForm : Form {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private ModernDarkProgressBar _pb;
        private Label _lblStatus;
        private TextBox _txtLog;
        private UninstButton _btnUninstall;
        private UninstButton _btnClose;
        private Image _appIcon;
        private bool _isUninstalling = false;

        public UninstallerForm() {
            Text = "Деинсталлятор Claude Anti-Ban Barrier";
            Width = 560;
            Height = 440;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(13, 15, 21);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;

            try {
                Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 12, 12));
            } catch { }

            LoadIconResource();
            BuildUI();
        }

        private void LoadIconResource() {
            try {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_icon.png")) {
                    if (s != null) _appIcon = Image.FromStream(s);
                }
            } catch {}
            if (_appIcon == null) {
                try {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_icon.ico")) {
                        if (s != null) {
                            Icon ico = new Icon(s);
                            _appIcon = ico.ToBitmap();
                            Icon = ico;
                        }
                    }
                } catch {}
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen borderPen = new Pen(Color.FromArgb(34, 39, 58), 1f)) {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        public static GraphicsPath CreateRoundedRect(Rectangle r, int radius) {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void BuildUI() {
            Panel pnlTitle = new Panel() {
                Location = new Point(0, 0),
                Size = new Size(Width, 48),
                BackColor = Color.FromArgb(16, 18, 26)
            };
            pnlTitle.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            Controls.Add(pnlTitle);

            PictureBox picIcon = new PictureBox() {
                Location = new Point(14, 10),
                Size = new Size(28, 28),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _appIcon,
                BackColor = Color.Transparent
            };
            picIcon.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            pnlTitle.Controls.Add(picIcon);

            Label lblHeadTitle = new Label() {
                Text = "УДАЛЕНИЕ CLAUDE ANTI-BAN BARRIER",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(48, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblHeadTitle.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            pnlTitle.Controls.Add(lblHeadTitle);

            Label lblHeadSub = new Label() {
                Text = "Откат правил WFP, включение IPv6 и полная очистка записей системы",
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(49, 27),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            lblHeadSub.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            pnlTitle.Controls.Add(lblHeadSub);

            Label btnCloseWin = new Label() {
                Text = "✕",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(Width - 42, 10),
                Size = new Size(28, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            btnCloseWin.MouseEnter += (s, e) => { btnCloseWin.BackColor = Color.FromArgb(239, 68, 68); btnCloseWin.ForeColor = Color.White; };
            btnCloseWin.MouseLeave += (s, e) => { btnCloseWin.BackColor = Color.Transparent; btnCloseWin.ForeColor = Color.FromArgb(148, 163, 184); };
            btnCloseWin.Click += (s, e) => {
                if (!_isUninstalling) Close();
            };
            pnlTitle.Controls.Add(btnCloseWin);

            Panel cardWarn = new Panel() {
                Location = new Point(24, 62),
                Size = new Size(512, 54),
                BackColor = Color.FromArgb(19, 22, 34)
            };
            cardWarn.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardWarn.Width - 1, cardWarn.Height - 1);
                using (GraphicsPath p = CreateRoundedRect(r, 7)) {
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 58), 1f)) g.DrawPath(pen, p);
                }
            };
            Controls.Add(cardWarn);

            _lblStatus = new Label() {
                Text = "Нажмите «Удалить барьер» для полной очистки WFP и восстановления сети.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(14, 16),
                Size = new Size(484, 22),
                BackColor = Color.Transparent
            };
            cardWarn.Controls.Add(_lblStatus);

            _pb = new ModernDarkProgressBar() {
                Location = new Point(24, 126),
                Size = new Size(512, 14),
                Value = 0
            };
            Controls.Add(_pb);

            _txtLog = new TextBox() {
                Location = new Point(24, 150),
                Size = new Size(512, 210),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(11, 14, 20),
                ForeColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(_txtLog);

            _btnUninstall = new UninstButton() {
                Text = "Удалить барьер",
                Location = new Point(336, 378),
                Size = new Size(200, 42),
                CornerRadius = 7,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                GradientStart = Color.FromArgb(220, 38, 38),
                GradientEnd = Color.FromArgb(185, 28, 28),
                HoverStart = Color.FromArgb(239, 68, 68),
                HoverEnd = Color.FromArgb(220, 38, 38),
                BorderColor = Color.Transparent,
                ForeColor = Color.White
            };
            _btnUninstall.Click += (s, e) => StartUninstall();
            Controls.Add(_btnUninstall);

            _btnClose = new UninstButton() {
                Text = "Отмена",
                Location = new Point(212, 378),
                Size = new Size(114, 42),
                CornerRadius = 7,
                Font = new Font("Segoe UI", 9f),
                GradientStart = Color.FromArgb(24, 28, 40),
                GradientEnd = Color.FromArgb(18, 21, 32),
                HoverStart = Color.FromArgb(36, 42, 60),
                HoverEnd = Color.FromArgb(26, 30, 44),
                BorderColor = Color.FromArgb(42, 50, 72),
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClose);
        }

        private void Log(string msg) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => Log(msg)));
                return;
            }
            _txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
        }

        private void SetProgress(int val) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => SetProgress(val)));
                return;
            }
            _pb.Value = val;
        }

        private void StartUninstall() {
            DialogResult dr = MessageBox.Show(
                "Вы действительно хотите удалить Claude Anti-Ban Barrier и восстановить сетевые параметры Windows?",
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr != DialogResult.Yes) return;

            _isUninstalling = true;
            _btnUninstall.Enabled = false;
            _btnClose.Enabled = false;
            _lblStatus.Text = "Выполняется очистка конфигурации и откат параметров сети...";
            _lblStatus.ForeColor = Color.FromArgb(251, 191, 36);

            System.Threading.ThreadPool.QueueUserWorkItem((st) => {
                try {
                    Log("Завершение активных процессов барьера...");
                    SetProgress(15);
                    try {
                        foreach (string name in new string[] { "ClaudeBarrier", "ProjectXO" }) {
                            foreach (Process p in Process.GetProcessesByName(name)) {
                                try { p.Kill(); p.WaitForExit(1000); } catch {}
                            }
                        }
                    } catch {}

                    Log("Удаление правил Брандмауэра Windows (Claude-Shield-*)...");
                    SetProgress(35);
                    try {
                        Process pFw = new Process();
                        pFw.StartInfo.FileName = "powershell.exe";
                        pFw.StartInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Remove-NetFirewallRule -DisplayName 'Claude-Shield-*' -ErrorAction SilentlyContinue\"";
                        pFw.StartInfo.CreateNoWindow = true;
                        pFw.StartInfo.UseShellExecute = false;
                        pFw.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        pFw.Start();
                        pFw.WaitForExit();
                        Log("Правила WFP ядра успешно удалены.");
                    } catch (Exception ex) {
                        Log("Ошибка очистки WFP: " + ex.Message);
                    }

                    Log("Восстановление сетевых параметров Windows...");
                    SetProgress(55);
                    try {
                        using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient", true)) {
                            if (rk != null) {
                                rk.DeleteValue("DisableSmartNameResolution", false);
                            }
                        }
                        using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", true)) {
                            if (rk != null) {
                                rk.DeleteValue("DisableSmartNameResolution", false);
                            }
                        }
                        Log("Режим разрешения DNS восстановлен.");
                    } catch {}

                    try {
                        Process pIp6 = new Process();
                        pIp6.StartInfo.FileName = "powershell.exe";
                        pIp6.StartInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Enable-NetAdapterBinding -ComponentId ms_tcpip6 -ErrorAction SilentlyContinue\"";
                        pIp6.StartInfo.CreateNoWindow = true;
                        pIp6.StartInfo.UseShellExecute = false;
                        pIp6.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        pIp6.Start();
                        pIp6.WaitForExit();
                        Log("Привязка протокола IPv6 на сетевых адаптерах включена.");
                    } catch {}

                    Log("Удаление из автозагрузки Windows...");
                    SetProgress(75);
                    try {
                        using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                            if (rk != null) {
                                if (rk.GetValue("ClaudeAntiBanBarrier") != null) rk.DeleteValue("ClaudeAntiBanBarrier");
                                if (rk.GetValue("ProjectXO") != null) rk.DeleteValue("ProjectXO");
                                Log("Записи автозагрузки удалены.");
                            }
                        }
                    } catch {}

                    Log("Удаление ярлыков системы...");
                    SetProgress(90);
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    foreach (string lnk in new string[] { "ClaudeBarrier.lnk", "Claude Anti-Ban Barrier.lnk", "Project XO.lnk" }) {
                        string p = Path.Combine(desktop, lnk);
                        if (File.Exists(p)) { try { File.Delete(p); Log("Ярлык на Рабочем столе удален: " + lnk); } catch {} }
                    }

                    string commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                    foreach (string sm in new string[] { "ClaudeBarrier", "Project XO" }) {
                        string sp = Path.Combine(commonPrograms, sm);
                        if (Directory.Exists(sp)) { try { Directory.Delete(sp, true); Log("Папка меню Пуск удалена: " + sm); } catch {} }
                    }

                    try {
                        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeBarrier", false);
                        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO", false);
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeBarrier", false);
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO", false);
                    } catch {}

                    try {
                        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeBarrier");
                        if (Directory.Exists(appDataDir)) Directory.Delete(appDataDir, true);
                    } catch {}

                    try {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\ClaudeBarrier", false);
                    } catch {}

                    try {
                        string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                        foreach (string f in Directory.GetFiles(installDir)) {
                            if (!f.EndsWith("Uninstall_Project_XO.exe", StringComparison.OrdinalIgnoreCase)) {
                                try { File.Delete(f); } catch {}
                            }
                        }
                        string toolsDir = Path.Combine(installDir, "tools");
                        if (Directory.Exists(toolsDir)) {
                            try { Directory.Delete(toolsDir, true); } catch {}
                        }
                        ProcessStartInfo psiDel = new ProcessStartInfo {
                            FileName = "cmd.exe",
                            Arguments = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + installDir + "\"",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psiDel);
                    } catch {}

                    SetProgress(100);
                    Log("==================================================");
                    Log("УДАЛЕНИЕ УСПЕШНО ЗАВЕРШЕНО.");
                    Log("==================================================");

                    Invoke(new Action(() => {
                        _lblStatus.Text = "Барьер удален. Все сетевые параметры восстановлены.";
                        _lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                        _btnClose.Text = "Закрыть";
                        _btnClose.Enabled = true;
                        MessageBox.Show("Барьер успешно удален. Все настройки сети возвращены в исходное состояние.",
                            "Удаление завершено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                } catch (Exception ex) {
                    Log("Ошибка удаления: " + ex.Message);
                    Invoke(new Action(() => {
                        _lblStatus.Text = "Произошла ошибка при деинсталляции.";
                        _lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                        _btnClose.Enabled = true;
                    }));
                }
            });
        }
    }
}
