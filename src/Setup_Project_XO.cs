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

    public class SetupForm : Form {
        private ProgressBar _pb;
        private Label _lblStatus;
        private Button _btnInstall;

        public SetupForm() {
            Text = "Установка Claude Anti-Ban Barrier — Bogdan Zhukov";
            Width = 540;
            Height = 390;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(19, 20, 24);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            try {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_icon.ico")) {
                    if (s != null) Icon = new Icon(s);
                }
            } catch {}

            Panel pnl = new Panel() { Dock = DockStyle.Top, Height = 75, BackColor = Color.FromArgb(14, 15, 18) };
            Controls.Add(pnl);

            Label l1 = new Label() { Text = "CLAUDE ANTI-BAN BARRIER", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.FromArgb(204, 120, 92), Location = new Point(20, 14), AutoSize = true };
            pnl.Controls.Add(l1);
            Label l2 = new Label() { Text = "Мастер развертывания WFP-барьера и превентивной защиты // Bogdan Zhukov", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(161, 161, 170), Location = new Point(22, 42), AutoSize = true };
            pnl.Controls.Add(l2);

            Label lInfo = new Label() {
                Text = "Программа будет установлена в:\nC:\\Program Files\\Project XO\n\nКомпоненты установки:\n• Исполняемый файл ProjectXO.exe и деинсталлятор Uninstall_Project_XO.exe\n• Ярлыки на Рабочем столе и в меню Пуск\n• Правила Брандмауэра Windows для изоляции подсетей Anthropic на физических картах\n• Регистрация в разделе «Программы и компоненты» панели управления Windows\n• Круглосуточная защита трафика в системном трее",
                Location = new Point(25, 90),
                Size = new Size(480, 140),
                ForeColor = Color.FromArgb(212, 212, 216)
            };
            Controls.Add(lInfo);

            _pb = new ProgressBar() { Location = new Point(25, 245), Size = new Size(475, 18), Visible = false };
            Controls.Add(_pb);

            _lblStatus = new Label() { Location = new Point(25, 270), AutoSize = true, ForeColor = Color.FromArgb(204, 120, 92), Visible = false };
            Controls.Add(_lblStatus);

            _btnInstall = new Button() {
                Text = "Установить",
                Size = new Size(180, 40),
                Location = new Point(320, 295),
                BackColor = Color.FromArgb(204, 120, 92),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
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
                _lblStatus.Text = "Развертывание бинарных модулей...";
                string destExe = Path.Combine(targetDir, "ProjectXO.exe");
                string destUninst = Path.Combine(targetDir, "Uninstall_Project_XO.exe");
                string destIco = Path.Combine(targetDir, "app_icon.ico");

                ExtractResourceToFile("ProjectXO.exe", destExe);
                ExtractResourceToFile("Uninstall_Project_XO.exe", destUninst);
                ExtractResourceToFile("app_icon.ico", destIco);

                string sourceCfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "claude_shield.cfg");
                string destCfg = Path.Combine(targetDir, "claude_shield.cfg");
                if (File.Exists(sourceCfg)) File.Copy(sourceCfg, destCfg, true);

                _pb.Value = 60;
                _lblStatus.Text = "Создание ярлыков рабочего стола...";
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                CreateShortcut(destExe, Path.Combine(desktop, "Project XO.lnk"), destIco);

                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Project XO");
                if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                CreateShortcut(destExe, Path.Combine(startMenu, "Project XO.lnk"), destIco);
                CreateShortcut(destUninst, Path.Combine(startMenu, "Удалить Project XO.lnk"), destIco);

                _pb.Value = 80;
                _lblStatus.Text = "Регистрация в реестре Windows...";
                RegisterUninstall(targetDir, destExe, destUninst);

                _pb.Value = 90;
                _lblStatus.Text = "Применение правил Брандмауэра WFP...";
                ApplyFirewallRules();

                _pb.Value = 100;
                _lblStatus.Text = "Установка успешно завершена!";
                _btnInstall.Text = "Запустить";
                _btnInstall.Enabled = true;
                _btnInstall.Click -= (s, e) => DoInstall();
                _btnInstall.Click += (s, e) => {
                    Process.Start(destExe);
                    Close();
                };
            } catch (Exception ex) {
                MessageBox.Show("Ошибка при установке: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnInstall.Enabled = true;
            }
        }

        private void RegisterUninstall(string installDir, string mainExe, string uninstallerExe) {
            try {
                using (RegistryKey rk = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProjectXO")) {
                    if (rk != null) {
                        rk.SetValue("DisplayName", "Claude Anti-Ban Barrier — Bogdan Zhukov");
                        rk.SetValue("DisplayVersion", "5.0.0");
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
                shortcut.Description = "Claude Anti-Ban Barrier — Bogdan Zhukov";
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
