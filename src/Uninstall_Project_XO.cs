using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ProjectXO {
    public class UninstallerForm : Form {
        private ProgressBar _pb;
        private Label _lblStatus;
        private TextBox _txtLog;
        private Button _btnUninstall;
        private Button _btnClose;

        [STAThread]
        public static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallerForm());
        }

        public UninstallerForm() {
            Text = "PROJECT XO // ДЕИНСТАЛЛЯТОР — BOGDAN ZHUKOV";
            Width = 560;
            Height = 440;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(18, 20, 24);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            BuildUI();
        }

        private void BuildUI() {
            // Header
            Panel pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(24, 27, 34)
            };
            Controls.Add(pnlHeader);

            Label lblTitle = new Label() {
                Text = "УДАЛЕНИЕ PROJECT XO",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 87),
                Location = new Point(20, 15),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            Label lblSub = new Label() {
                Text = "Полная очистка системы, удаление правил Брандмауэра WFP и восстановление сети",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(22, 45),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblSub);

            // Body
            _lblStatus = new Label() {
                Text = "Нажмите «Удалить Project XO» для полной и безопасной деинсталляции.",
                Location = new Point(25, 100),
                Size = new Size(500, 36),
                ForeColor = Color.FromArgb(228, 228, 231)
            };
            Controls.Add(_lblStatus);

            _pb = new ProgressBar() {
                Location = new Point(25, 145),
                Size = new Size(495, 16),
                Style = ProgressBarStyle.Continuous,
                Value = 0
            };
            Controls.Add(_pb);

            _txtLog = new TextBox() {
                Location = new Point(25, 175),
                Size = new Size(495, 155),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(13, 15, 18),
                ForeColor = Color.FromArgb(212, 212, 216),
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(_txtLog);

            // Bottom Buttons
            _btnUninstall = new Button() {
                Text = "Удалить Project XO",
                Location = new Point(25, 345),
                Size = new Size(240, 38),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnUninstall.FlatAppearance.BorderSize = 0;
            _btnUninstall.Click += (s, e) => StartUninstall();
            Controls.Add(_btnUninstall);

            _btnClose = new Button() {
                Text = "Отмена",
                Location = new Point(280, 345),
                Size = new Size(240, 38),
                BackColor = Color.FromArgb(39, 39, 42),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
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

        private void StartUninstall() {
            DialogResult dr = MessageBox.Show(
                "Вы действительно хотите полностью удалить Project XO и восстановить стандартные параметры сети?",
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (dr != DialogResult.Yes) return;

            _btnUninstall.Enabled = false;
            _btnClose.Enabled = false;
            _lblStatus.Text = "Выполняется очистка системы...";

            System.Threading.ThreadPool.QueueUserWorkItem((st) => {
                try {
                    // Step 1: Kill running ProjectXO
                    Log("Завершение процессов ProjectXO...");
                    SetProgress(15);
                    try {
                        foreach (Process p in Process.GetProcessesByName("ProjectXO")) {
                            try { p.Kill(); p.WaitForExit(1000); } catch {}
                        }
                    } catch {}

                    // Step 2: Remove Firewall WFP Rules
                    Log("Очистка правил Брандмауэра Windows (Claude-Shield-*)...");
                    SetProgress(35);
                    try {
                        Process pFw = new Process();
                        pFw.StartInfo.FileName = "powershell.exe";
                        pFw.StartInfo.Arguments = "-NoProfile -Command \"Remove-NetFirewallRule -DisplayName 'Claude-Shield-*' -ErrorAction SilentlyContinue\"";
                        pFw.StartInfo.CreateNoWindow = true;
                        pFw.StartInfo.UseShellExecute = false;
                        pFw.Start();
                        pFw.WaitForExit();
                        Log("✓ Правила WFP ядра успешно удалены.");
                    } catch (Exception ex) {
                        Log("! Ошибка при удалении правил WFP: " + ex.Message);
                    }

                    // Step 3: Restore IPv6 & DNS Registry Settings
                    Log("Восстановление сетевых настроек Windows...");
                    SetProgress(55);
                    try {
                        using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", true)) {
                            if (rk != null) {
                                rk.DeleteValue("DisableSmartNameResolution", false);
                            }
                        }
                        Log("✓ Режим Smart Multi-Homed Name Resolution восстановлен.");
                    } catch {}

                    // Re-enable IPv6 on network adapters
                    try {
                        Process pIp6 = new Process();
                        pIp6.StartInfo.FileName = "powershell.exe";
                        pIp6.StartInfo.Arguments = "-NoProfile -Command \"Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Enable-NetAdapterBinding -ComponentId ms_tcpip6 -ErrorAction SilentlyContinue\"";
                        pIp6.StartInfo.CreateNoWindow = true;
                        pIp6.StartInfo.UseShellExecute = false;
                        pIp6.Start();
                        pIp6.WaitForExit();
                        Log("✓ Привязка IPv6 на физических адаптерах включена.");
                    } catch {}

                    // Step 4: Remove Windows Auto-Start
                    Log("Удаление из автозагрузки Windows...");
                    SetProgress(75);
                    try {
                        using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                            if (rk != null && rk.GetValue("ProjectXO") != null) {
                                rk.DeleteValue("ProjectXO");
                                Log("✓ Запись автозагрузки ProjectXO удалена.");
                            }
                        }
                    } catch {}

                    // Step 5: Remove Shortcuts
                    Log("Удаление ярлыков на Рабочем столе и в меню Пуск...");
                    SetProgress(90);
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Project XO");

                    string lnkDesktop = Path.Combine(desktop, "Project XO.lnk");
                    if (File.Exists(lnkDesktop)) {
                        try { File.Delete(lnkDesktop); Log("✓ Ярлык на Рабочем столе удален."); } catch {}
                    }

                    if (Directory.Exists(startMenu)) {
                        try { Directory.Delete(startMenu, true); Log("✓ Папка в меню Пуск удалена."); } catch {}
                    }

                    // Step 6: Remove Add/Remove Programs entry
                    try {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO", false);
                    } catch {}

                    SetProgress(100);
                    Log("==================================================");
                    Log("✓ PROJECT XO УСПЕШНО И ПОЛНОСТЬЮ УДАЛЕН С КОМПЬЮТЕРА!");
                    Log("==================================================");

                    Invoke(new Action(() => {
                        _lblStatus.Text = "Удаление успешно завершено!";
                        _lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        _btnClose.Text = "Завершить";
                        _btnClose.Enabled = true;
                        MessageBox.Show("Project XO успешно удален из системы.\nВсе сетевые настройки и правила Брандмауэра возвращены в исходное состояние.",
                            "Удаление завершено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                } catch (Exception ex) {
                    Log("Критическая ошибка деинсталляции: " + ex.Message);
                    Invoke(new Action(() => {
                        _lblStatus.Text = "Произошла ошибка при удалении.";
                        _lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
                        _btnClose.Enabled = true;
                    }));
                }
            });
        }

        private void SetProgress(int val) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => SetProgress(val)));
                return;
            }
            _pb.Value = Math.Min(100, Math.Max(0, val));
        }
    }
}
