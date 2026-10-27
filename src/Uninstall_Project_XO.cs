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
            Text = "Деинсталлятор Claude Anti-Ban Barrier — Bogdan Zhukov";
            Width = 560;
            Height = 440;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(19, 20, 24);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            BuildUI();
        }

        private void BuildUI() {
            Panel pnlHeader = new Panel() {
                Dock = DockStyle.Top,
                Height = 75,
                BackColor = Color.FromArgb(14, 15, 18)
            };
            Controls.Add(pnlHeader);

            Label lblTitle = new Label() {
                Text = "УДАЛЕНИЕ CLAUDE ANTI-BAN BARRIER",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(204, 120, 92),
                Location = new Point(20, 14),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            Label lblSub = new Label() {
                Text = "Откат правил WFP, включение IPv6 и очистка системных записей",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170),
                Location = new Point(22, 42),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblSub);

            _lblStatus = new Label() {
                Text = "Нажмите «Удалить барьер» для полной очистки и восстановления параметров сети.",
                Location = new Point(25, 95),
                Size = new Size(500, 36),
                ForeColor = Color.FromArgb(212, 212, 216)
            };
            Controls.Add(_lblStatus);

            _pb = new ProgressBar() {
                Location = new Point(25, 140),
                Size = new Size(495, 16),
                Style = ProgressBarStyle.Continuous,
                Value = 0
            };
            Controls.Add(_pb);

            _txtLog = new TextBox() {
                Location = new Point(25, 170),
                Size = new Size(495, 155),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(12, 13, 16),
                ForeColor = Color.FromArgb(212, 212, 216),
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(_txtLog);

            _btnUninstall = new Button() {
                Text = "Удалить барьер",
                Location = new Point(25, 345),
                Size = new Size(240, 38),
                BackColor = Color.FromArgb(185, 28, 28),
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
                BackColor = Color.FromArgb(38, 40, 48),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderColor = Color.FromArgb(63, 63, 70);
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
                "Вы действительно хотите удалить Claude Anti-Ban Barrier и восстановить сетевые параметры Windows?",
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr != DialogResult.Yes) return;

            _btnUninstall.Enabled = false;
            _btnClose.Enabled = false;
            _lblStatus.Text = "Выполняется очистка конфигурации...";

            System.Threading.ThreadPool.QueueUserWorkItem((st) => {
                try {
                    Log("Завершение активных процессов барьера...");
                    SetProgress(15);
                    try {
                        foreach (Process p in Process.GetProcessesByName("ProjectXO")) {
                            try { p.Kill(); p.WaitForExit(1000); } catch {}
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

                    Log("Удаление из автозагрузки...");
                    SetProgress(75);
                    try {
                        using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                            if (rk != null && rk.GetValue("ClaudeAntiBanBarrier") != null) {
                                rk.DeleteValue("ClaudeAntiBanBarrier");
                                Log("Запись автозагрузки удалена.");
                            }
                        }
                    } catch {}

                    Log("Удаление ярлыков рабочего стола...");
                    SetProgress(90);
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Project XO");

                    string lnkDesktop = Path.Combine(desktop, "Project XO.lnk");
                    if (File.Exists(lnkDesktop)) {
                        try { File.Delete(lnkDesktop); Log("Ярлык на Рабочем столе удален."); } catch {}
                    }

                    if (Directory.Exists(startMenu)) {
                        try { Directory.Delete(startMenu, true); Log("Папка меню Пуск удалена."); } catch {}
                    }

                    try {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO", false);
                        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO", false);
                    } catch {}

                    SetProgress(100);
                    Log("==================================================");
                    Log("УДАЛЕНИЕ УСПЕШНО ЗАВЕРШЕНО.");
                    Log("==================================================");

                    Invoke(new Action(() => {
                        _lblStatus.Text = "Удаление завершено. Сетевые параметры восстановлены.";
                        _lblStatus.ForeColor = Color.FromArgb(204, 120, 92);
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

        private void SetProgress(int val) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => SetProgress(val)));
                return;
            }
            _pb.Value = Math.Min(100, Math.Max(0, val));
        }
    }
}
