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

namespace ProjectXO.Installer {
    public class SetupProgram {
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

            if (args != null && Array.IndexOf(args, "/uninstall") >= 0) {
                string uninstPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall_Project_XO.exe");
                if (File.Exists(uninstPath)) {
                    Process.Start(uninstPath);
                } else {
                    MessageBox.Show("Файл деинсталлятора не найден.", "Claude Anti-Ban Barrier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

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
                    MessageBox.Show("Для установки требуются права администратора.", "Установка Claude Anti-Ban Barrier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
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
            using (GraphicsPath p = SetupForm.CreateRoundedRect(r, 4)) {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 27, 41))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(35, 43, 64), 1f)) g.DrawPath(pen, p);
            }

            if (_value > 0) {
                int fillW = (int)((Width - 2) * (_value / 100.0));
                if (fillW > 4) {
                    Rectangle fillRect = new Rectangle(1, 1, fillW, Height - 2);
                    using (GraphicsPath fp = SetupForm.CreateRoundedRect(fillRect, 3)) {
                        using (LinearGradientBrush lgb = new LinearGradientBrush(fillRect, Color.FromArgb(37, 99, 235), Color.FromArgb(56, 189, 248), 0f)) {
                            g.FillPath(lgb, fp);
                        }
                    }
                }
            }
        }
    }

    public class SetupButton : Button {
        private bool _isHovered = false;
        private bool _isPressed = false;
        public int CornerRadius { get; set; }
        public Color GradientStart { get; set; }
        public Color GradientEnd { get; set; }
        public Color HoverStart { get; set; }
        public Color HoverEnd { get; set; }
        public Color BorderColor { get; set; }

        public SetupButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            CornerRadius = 7;
            GradientStart = Color.FromArgb(37, 99, 235);
            GradientEnd = Color.FromArgb(29, 78, 216);
            HoverStart = Color.FromArgb(59, 130, 246);
            HoverEnd = Color.FromArgb(37, 99, 235);
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

            using (GraphicsPath p = SetupForm.CreateRoundedRect(r, CornerRadius)) {
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

    public class SetupForm : Form {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        private TextBox _txtPath;
        private ModernDarkProgressBar _pb;
        private Label _lblStatus;
        private SetupButton _btnInstall;
        private SetupButton _btnCancel;
        private Image _appIcon;
        private string _targetDirectory;
        private bool _isInstalling = false;

        public SetupForm() {
            Text = "Установка Claude Anti-Ban Barrier";
            Width = 580;
            Height = 460;
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
                Text = "CLAUDE ANTI-BAN BARRIER",
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
                Text = "Мастер установки автономного ядра безопасности // Релиз v6.0.1",
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

            Label btnClose = new Label() {
                Text = "✕",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(Width - 42, 10),
                Size = new Size(28, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            btnClose.MouseEnter += (s, e) => { btnClose.BackColor = Color.FromArgb(239, 68, 68); btnClose.ForeColor = Color.White; };
            btnClose.MouseLeave += (s, e) => { btnClose.BackColor = Color.Transparent; btnClose.ForeColor = Color.FromArgb(148, 163, 184); };
            btnClose.Click += (s, e) => {
                if (!_isInstalling) Close();
            };
            pnlTitle.Controls.Add(btnClose);

            Panel cardPath = new Panel() {
                Location = new Point(24, 64),
                Size = new Size(532, 68),
                BackColor = Color.FromArgb(19, 22, 34)
            };
            cardPath.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardPath.Width - 1, cardPath.Height - 1);
                using (GraphicsPath p = CreateRoundedRect(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 58), 1f)) g.DrawPath(pen, p);
                }
            };
            Controls.Add(cardPath);

            Label lblPathTitle = new Label() {
                Text = "ПАПКА УСТАНОВКИ",
                Font = new Font("Segoe UI", 7.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardPath.Controls.Add(lblPathTitle);

            _targetDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Claude Anti-Ban Barrier");
            _txtPath = new TextBox() {
                Text = _targetDirectory,
                Location = new Point(14, 32),
                Size = new Size(405, 24),
                BackColor = Color.FromArgb(13, 15, 23),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f),
                ReadOnly = true
            };
            cardPath.Controls.Add(_txtPath);

            SetupButton btnBrowse = new SetupButton() {
                Text = "Обзор...",
                Location = new Point(428, 30),
                Size = new Size(90, 27),
                CornerRadius = 5,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                GradientStart = Color.FromArgb(28, 33, 48),
                GradientEnd = Color.FromArgb(20, 24, 36),
                HoverStart = Color.FromArgb(42, 50, 72),
                HoverEnd = Color.FromArgb(32, 38, 56),
                BorderColor = Color.FromArgb(46, 56, 82),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            btnBrowse.Click += (s, e) => {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog()) {
                    fbd.Description = "Выберите папку для установки Claude Anti-Ban Barrier:";
                    fbd.SelectedPath = _targetDirectory;
                    if (fbd.ShowDialog() == DialogResult.OK) {
                        _targetDirectory = fbd.SelectedPath;
                        _txtPath.Text = _targetDirectory;
                    }
                }
            };
            cardPath.Controls.Add(btnBrowse);

            Panel cardFeatures = new Panel() {
                Location = new Point(24, 142),
                Size = new Size(532, 172),
                BackColor = Color.FromArgb(19, 22, 34)
            };
            cardFeatures.Paint += (s, pe) => {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, cardFeatures.Width - 1, cardFeatures.Height - 1);
                using (GraphicsPath p = CreateRoundedRect(r, 8)) {
                    using (Pen pen = new Pen(Color.FromArgb(34, 39, 58), 1f)) g.DrawPath(pen, p);
                }
            };
            Controls.Add(cardFeatures);

            Label lblFeatTitle = new Label() {
                Text = "КОМПОНЕНТЫ СИСТЕМЫ И АВТОНОМНОГО ЯДРА",
                Font = new Font("Segoe UI", 7.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 10),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            cardFeatures.Controls.Add(lblFeatTitle);

            string[] features = new string[] {
                "Фильтрация прямых подсетей Anthropic (160.79.104.0/23) в ядре WFP",
                "Аварийный сокет-перехватчик и Kill-Switch сессий Claude при обрыве туннеля",
                "Изоляция физических адаптеров и предотвращение утечек IPv6 / Smart DNS",
                "Фоновый агент в системном трее Windows и автозапуск в защищенном режиме",
                "Создание ярлыков на Рабочем столе и в главном меню программ Windows"
            };

            int yOffset = 34;
            for (int i = 0; i < features.Length; i++) {
                Panel row = new Panel() {
                    Location = new Point(14, yOffset),
                    Size = new Size(504, 24),
                    BackColor = Color.Transparent
                };
                Label check = new Label() {
                    Text = "✓",
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(52, 211, 153),
                    Location = new Point(0, 0),
                    Size = new Size(20, 20),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                row.Controls.Add(check);
                Label desc = new Label() {
                    Text = features[i],
                    Font = new Font("Segoe UI", 8.4f),
                    ForeColor = Color.FromArgb(226, 232, 240),
                    Location = new Point(22, 2),
                    Size = new Size(480, 20),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                row.Controls.Add(desc);
                cardFeatures.Controls.Add(row);
                yOffset += 26;
            }

            _pb = new ModernDarkProgressBar() {
                Location = new Point(24, 326),
                Size = new Size(532, 14),
                Visible = false
            };
            Controls.Add(_pb);

            _lblStatus = new Label() {
                Text = "Готово к установке. Нажмите «Установить» для начала развертывания.",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(26, 348),
                Size = new Size(528, 20),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            Controls.Add(_lblStatus);

            _btnInstall = new SetupButton() {
                Text = "Установить",
                Location = new Point(376, 386),
                Size = new Size(180, 42),
                CornerRadius = 7,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                GradientStart = Color.FromArgb(37, 99, 235),
                GradientEnd = Color.FromArgb(29, 78, 216),
                HoverStart = Color.FromArgb(59, 130, 246),
                HoverEnd = Color.FromArgb(37, 99, 235),
                BorderColor = Color.Transparent,
                ForeColor = Color.White
            };
            _btnInstall.Click += (s, e) => DoInstall();
            Controls.Add(_btnInstall);

            _btnCancel = new SetupButton() {
                Text = "Отмена",
                Location = new Point(252, 386),
                Size = new Size(114, 42),
                CornerRadius = 7,
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                GradientStart = Color.FromArgb(22, 26, 38),
                GradientEnd = Color.FromArgb(16, 19, 28),
                HoverStart = Color.FromArgb(34, 40, 58),
                HoverEnd = Color.FromArgb(24, 28, 42),
                BorderColor = Color.FromArgb(44, 54, 78),
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            _btnCancel.Click += (s, e) => Close();
            Controls.Add(_btnCancel);
        }

        private void SetProgress(int val, string text) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => SetProgress(val, text)));
                return;
            }
            _pb.Value = val;
            _lblStatus.Text = text;
        }

        private void ExtractResourceToFile(string resName, string destPath) {
            try {
                using (Stream resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName)) {
                    if (resStream != null) {
                        using (FileStream fs = new FileStream(destPath, FileMode.Create, FileAccess.Write)) {
                            byte[] buf = new byte[8192];
                            int read;
                            while ((read = resStream.Read(buf, 0, buf.Length)) > 0) {
                                fs.Write(buf, 0, read);
                            }
                        }
                        return;
                    }
                }
            } catch {}

            string localSrc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resName);
            if (File.Exists(localSrc)) {
                File.Copy(localSrc, destPath, true);
            }
        }

        private void DoInstall() {
            _isInstalling = true;
            _btnInstall.Enabled = false;
            _btnCancel.Enabled = false;
            _pb.Visible = true;
            _pb.Value = 10;
            _lblStatus.Text = "Подготовка системной директории...";
            _lblStatus.ForeColor = Color.FromArgb(56, 189, 248);

            System.Threading.ThreadPool.QueueUserWorkItem((st) => {
                try {
                    string targetDir = _targetDirectory;
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                    SetProgress(30, "Развертывание исполняемых файлов ядра...");
                    string destExe = Path.Combine(targetDir, "ClaudeBarrier.exe");
                    string legacyExe = Path.Combine(targetDir, "ProjectXO.exe");
                    string destUninst = Path.Combine(targetDir, "Uninstall_Project_XO.exe");
                    string destIco = Path.Combine(targetDir, "app_icon.ico");

                    ExtractResourceToFile("ProjectXO.exe", destExe);
                    try { File.Copy(destExe, legacyExe, true); } catch {}
                    ExtractResourceToFile("Uninstall_Project_XO.exe", destUninst);
                    ExtractResourceToFile("app_icon.ico", destIco);

                    SetProgress(60, "Создание ярлыков в системе...");
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    CreateShortcut(destExe, Path.Combine(desktop, "Claude Anti-Ban Barrier.lnk"), destIco);

                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Claude Anti-Ban Barrier");
                    if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                    CreateShortcut(destExe, Path.Combine(startMenu, "Claude Anti-Ban Barrier.lnk"), destIco);
                    CreateShortcut(destUninst, Path.Combine(startMenu, "Удалить Claude Barrier.lnk"), destIco);

                    SetProgress(80, "Регистрация программы в панели управления Windows...");
                    RegisterUninstall(targetDir, destExe, destUninst);

                    SetProgress(90, "Активация правил Брандмауэра WFP ядра...");
                    ApplyFirewallRules();

                    SetProgress(100, "Установка успешно завершена! Система готова к запуску.");
                    Invoke(new Action(() => {
                        _lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                        _btnInstall.Text = "Запустить";
                        _btnInstall.Enabled = true;
                        _btnInstall.Click -= (s, e) => DoInstall();
                        _btnInstall.Click += (s, e) => {
                            try { Process.Start(destExe); } catch {}
                            Close();
                        };
                        _btnCancel.Text = "Закрыть";
                        _btnCancel.Enabled = true;
                    }));
                } catch (Exception ex) {
                    Invoke(new Action(() => {
                        _lblStatus.Text = "Ошибка установки: " + ex.Message;
                        _lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                        _btnInstall.Enabled = true;
                        _btnCancel.Enabled = true;
                        MessageBox.Show("Ошибка при установке: " + ex.Message, "Ошибка установки", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void RegisterUninstall(string installDir, string mainExe, string uninstallerExe) {
            try {
                using (RegistryKey rk = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeBarrier")) {
                    if (rk != null) {
                        rk.SetValue("DisplayName", "Claude Anti-Ban Barrier");
                        rk.SetValue("DisplayVersion", "6.0.1");
                        rk.SetValue("Publisher", "ClaudeBarrier Security Team");
                        rk.SetValue("DisplayIcon", mainExe + ",0");
                        rk.SetValue("UninstallString", "\"" + uninstallerExe + "\"");
                        rk.SetValue("InstallLocation", installDir);
                        rk.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        rk.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            } catch {}
        }

        private void CreateShortcut(string targetPath, string shortcutPath, string iconPath) {
            try {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.IconLocation = (File.Exists(iconPath) ? iconPath : targetPath) + ",0";
                shortcut.Description = "Claude Anti-Ban Barrier";
                shortcut.Save();
            } catch {}
        }

        private void ApplyFirewallRules() {
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
                Process.Start(psi).WaitForExit();
            } catch {}
        }
    }
}
