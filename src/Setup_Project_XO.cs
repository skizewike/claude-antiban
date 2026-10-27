using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Security.Principal;
using System.Reflection;
using Microsoft.Win32;

namespace ProjectXO.Installer {
    public class SetupProgram {
        [STAThread]
        public static void Main(string[] args) {
            if (args != null && Array.IndexOf(args, "/uninstall") >= 0) {
                string uninstPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Uninstall_Project_XO.exe");
                if (File.Exists(uninstPath)) {
                    Process.Start(uninstPath);
                } else {
                    MessageBox.Show("Файл Uninstall_Project_XO.exe не найден.", "Project XO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Application.ExecutablePath;
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    return;
                } catch {
                    MessageBox.Show("Для установки комплекса требуются права администратора!", "Project XO Setup // BOGDAN ZHUKOV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    public class SetupForm : Form {
        private ProgressBar _pb;
        private Label _lblStatus;
        private Button _btnInstall;

        public SetupForm() {
            Text = "Установка Project XO // BOGDAN ZHUKOV";
            Width = 540;
            Height = 390;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(15, 17, 23);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            try {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_icon.ico")) {
                    if (s != null) Icon = new Icon(s);
                }
            } catch {}

            Panel pnl = new Panel() { Dock = DockStyle.Top, Height = 75, BackColor = Color.FromArgb(11, 13, 18) };
            Controls.Add(pnl);

            Label l1 = new Label() { Text = "PROJECT XO // BOGDAN ZHUKOV", Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.FromArgb(217, 119, 87), Location = new Point(20, 12), AutoSize = true };
            pnl.Controls.Add(l1);
            Label l2 = new Label() { Text = "Мастер установки защитного комплекса Claude (WFP Isolation & Tray Guard)", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(22, 42), AutoSize = true };
            pnl.Controls.Add(l2);

            Label lInfo = new Label() {
                Text = "Комплекс защиты Claude будет установлен в:\nC:\\Program Files\\Project XO\n\nВ процессе установки:\n• Развертываются ProjectXO.exe и деинсталлятор Uninstall_Project_XO.exe\n• Создаются ярлыки с прозрачной векторной иконкой Claude на Рабочем столе\n• Настраивается Брандмауэр Windows (изоляция 160.79.104.0/23 на Wi-Fi)\n• Регистрируется точка деинсталляции в «Установка и удаление программ» Windows\n• Включается круглосуточная защита в системном трее",
                Location = new Point(25, 90),
                Size = new Size(480, 140),
                ForeColor = Color.FromArgb(203, 213, 225)
            };
            Controls.Add(lInfo);

            _pb = new ProgressBar() { Location = new Point(25, 245), Size = new Size(475, 18), Visible = false };
            Controls.Add(_pb);

            _lblStatus = new Label() { Location = new Point(25, 270), AutoSize = true, ForeColor = Color.FromArgb(16, 185, 129), Visible = false };
            Controls.Add(_lblStatus);

            _btnInstall = new Button() {
                Text = "УСТАНОВИТЬ",
                Size = new Size(180, 42),
                Location = new Point(320, 295),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(11, 13, 18),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += (s, e) => DoInstall();
            Controls.Add(_btnInstall);
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

            // Fallback from disk
            string localSrc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resName);
            if (File.Exists(localSrc)) {
                File.Copy(localSrc, destPath, true);
            }
        }

        private void DoInstall() {
            _btnInstall.Enabled = false;
            _pb.Visible = true;
            _lblStatus.Visible = true;
            _pb.Value = 10;
            _lblStatus.Text = "Подготовка директории установки...";

            try {
                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Project XO");
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                _pb.Value = 30;
                _lblStatus.Text = "Развертывание ProjectXO.exe и деинсталлятора...";
                string destExe = Path.Combine(targetDir, "ProjectXO.exe");
                string destUninst = Path.Combine(targetDir, "Uninstall_Project_XO.exe");
                string destIco = Path.Combine(targetDir, "app_icon.ico");

                ExtractResourceToFile("ProjectXO.exe", destExe);
                ExtractResourceToFile("Uninstall_Project_XO.exe", destUninst);
                ExtractResourceToFile("app_icon.ico", destIco);

                // Config copy
                string sourceCfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claude_shield.cfg");
                string destCfg = Path.Combine(targetDir, "claude_shield.cfg");
                if (File.Exists(sourceCfg)) File.Copy(sourceCfg, destCfg, true);

                _pb.Value = 55;
                _lblStatus.Text = "Создание ярлыков рабочего стола...";
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                CreateShortcut(destExe, Path.Combine(desktop, "Project XO.lnk"), destIco);

                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Project XO");
                if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                CreateShortcut(destExe, Path.Combine(startMenu, "Project XO.lnk"), destIco);
                CreateShortcut(destUninst, Path.Combine(startMenu, "Удалить Project XO.lnk"), destIco);

                _pb.Value = 75;
                _lblStatus.Text = "Регистрация в реестре Windows (Add/Remove Programs)...";
                RegisterUninstall(targetDir, destExe, destUninst);

                _pb.Value = 88;
                _lblStatus.Text = "Настройка Брандмауэра Windows и блокировка утечек...";
                ApplyFirewallRules();

                _pb.Value = 100;
                _lblStatus.Text = "Установка Project XO успешно завершена!";
                _btnInstall.Text = "ЗАПУСТИТЬ";
                _btnInstall.Enabled = true;
                _btnInstall.Click -= (s, e) => DoInstall();
                _btnInstall.Click += (s, e) => {
                    Process.Start(destExe);
                    Close();
                };
            } catch (Exception ex) {
                MessageBox.Show("Ошибка при установке: " + ex.Message, "Ошибка установки // BOGDAN ZHUKOV", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnInstall.Enabled = true;
            }
        }

        private void RegisterUninstall(string installDir, string mainExe, string uninstallerExe) {
            try {
                using (RegistryKey rk = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO")) {
                    if (rk != null) {
                        rk.SetValue("DisplayName", "Project XO // BOGDAN ZHUKOV - Claude Anti-Leak Shield");
                        rk.SetValue("DisplayVersion", "4.2.0");
                        rk.SetValue("Publisher", "Bogdan Zhukov");
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
                shortcut.Description = "Project XO // BOGDAN ZHUKOV - Claude Anti-Leak Shield";
                shortcut.Save();
            } catch {}
        }

        private void ApplyFirewallRules() {
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
                psi.UseShellExecute = false;
                Process.Start(psi).WaitForExit();
            } catch {}
        }
    }
}
