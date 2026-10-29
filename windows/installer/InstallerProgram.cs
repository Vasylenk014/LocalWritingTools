using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WritingTools.Setup
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;
    }

    internal static class InstallerProgram
    {
        public const string AppName = "Local Writing Tools";
        public const string AppExeName = "WritingTools.exe";
        public const string UninstallExeName = "Uninstall.exe";
        public const string AppVersion = "2.1.0";
        public const string Publisher = "Local Writing Tools (100% Local AI)";
        public const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalWritingTools";
        public const string RunRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "LocalWritingTools";

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isUninstall = false;
            bool isSilent = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/u", StringComparison.OrdinalIgnoreCase))
                {
                    isUninstall = true;
                }
                if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase))
                {
                    isSilent = true;
                }
            }

            string currentExeName = Path.GetFileName(Application.ExecutablePath);
            if (string.Equals(currentExeName, UninstallExeName, StringComparison.OrdinalIgnoreCase))
            {
                isUninstall = true;
            }

            if (isUninstall)
            {
                return RunUninstaller(isSilent);
            }

            if (isSilent)
            {
                string defaultDir = GetDefaultInstallDir();
                try
                {
                    PerformInstall(defaultDir, true, true, false, null);
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            using (var form = new SetupForm())
            {
                Application.Run(form);
                return form.ExitCode;
            }
        }

        public static string GetDefaultInstallDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Programs", "LocalWritingTools");
        }

        public static void StopRunningAppProcesses()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("WritingTools");
                foreach (Process p in procs)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch { }
        }

        public static void PerformInstall(
            string installDir,
            bool createDesktopShortcut,
            bool createStartMenuShortcut,
            bool launchOnStartup,
            Action<int, string> progressCallback)
        {
            if (progressCallback != null) progressCallback(5, "Stopping any running Local Writing Tools instances...");
            StopRunningAppProcesses();
            Thread.Sleep(400);

            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            if (progressCallback != null) progressCallback(12, "Reading embedded application package...");
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream payloadStream = asm.GetManifestResourceStream("LocalWritingTools.payload.zip"))
            {
                if (payloadStream == null)
                {
                    throw new InvalidOperationException("Embedded payload 'LocalWritingTools.payload.zip' was not found in the setup binary.");
                }

                using (var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    int index = 0;
                    long totalBytes = 0;

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        index++;
                        string destPath = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
                        if (!destPath.StartsWith(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destPath);
                            continue;
                        }

                        string parentDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }

                        // Remove existing file first to ensure a fresh NTFS inode
                        if (File.Exists(destPath))
                        {
                            try { File.Delete(destPath); } catch { }
                        }

                        entry.ExtractToFile(destPath, true);
                        totalBytes += entry.Length;

                        int pct = 15 + (int)((index * 70.0) / Math.Max(1, total));
                        if (index % 5 == 0 || index == total)
                        {
                            if (progressCallback != null)
                            {
                                progressCallback(pct, string.Format("Extracting files ({0}/{1}): {2}", index, total, entry.Name));
                            }
                        }
                    }

                    if (progressCallback != null) progressCallback(88, "Configuring shortcuts...");
                    string targetExe = Path.Combine(installDir, AppExeName);

                    if (createStartMenuShortcut)
                    {
                        string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                        string lnkPath = Path.Combine(startMenuDir, AppName + ".lnk");
                        CreateShortcut(lnkPath, targetExe, installDir, "Local Writing Tools - 100% Local AI Writing Assistant");
                    }

                    if (createDesktopShortcut)
                    {
                        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string lnkPath = Path.Combine(desktopDir, AppName + ".lnk");
                        CreateShortcut(lnkPath, targetExe, installDir, "Local Writing Tools - 100% Local AI Writing Assistant");
                    }

                    if (progressCallback != null) progressCallback(94, "Configuring Windows startup & uninstaller registry...");
                    ConfigureStartupRegistry(launchOnStartup, targetExe);
                    RegisterUninstaller(installDir, targetExe, (int)(totalBytes / 1024));
                }
            }

            if (progressCallback != null) progressCallback(100, "Installation completed successfully!");
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        public static void ConfigureStartupRegistry(bool enable, string targetExePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        key.SetValue(RunValueName, "\"" + targetExePath + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }
            }
            catch { }
        }

        public static void RegisterUninstaller(string installDir, string targetExePath, int estimatedSizeKb)
        {
            try
            {
                string uninstallExe = Path.Combine(installDir, UninstallExeName);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallRegKey))
                {
                    if (key == null) return;
                    key.SetValue("DisplayName", AppName, RegistryValueKind.String);
                    key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
                    key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                    key.SetValue("DisplayIcon", targetExePath, RegistryValueKind.String);
                    key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
                    key.SetValue("UninstallString", "\"" + uninstallExe + "\" --uninstall", RegistryValueKind.String);
                    key.SetValue("QuietUninstallString", "\"" + uninstallExe + "\" --uninstall --silent", RegistryValueKind.String);
                    key.SetValue("EstimatedSize", Math.Max(1024, estimatedSizeKb), RegistryValueKind.DWord);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        public static int RunUninstaller(bool isSilent)
        {
            string installDir = GetDefaultInstallDir();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallRegKey))
                {
                    object val = key != null ? key.GetValue("InstallLocation") : null;
                    string regLoc = val as string;
                    if (!string.IsNullOrWhiteSpace(regLoc))
                    {
                        installDir = regLoc;
                    }
                }
            }
            catch { }

            if (!isSilent)
            {
                DialogResult res = MessageBox.Show(
                    "Are you sure you want to completely remove Local Writing Tools from your computer?\n\n(Your local AI models in LM Studio / FastFlowLM will not be affected.)",
                    "Uninstall Local Writing Tools",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (res != DialogResult.Yes)
                {
                    return 0;
                }
            }

            StopRunningAppProcesses();
            Thread.Sleep(500);

            // Remove shortcuts
            try
            {
                string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
                if (File.Exists(startLnk)) File.Delete(startLnk);

                string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
                if (File.Exists(deskLnk)) File.Delete(deskLnk);
            }
            catch { }

            // Remove registry entries
            ConfigureStartupRegistry(false, "");
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallRegKey, false);
            }
            catch { }

            // Delete installed files except self (if running from installDir), then schedule self-deletion via cmd.exe
            if (Directory.Exists(installDir))
            {
                string selfPath = Path.GetFullPath(Application.ExecutablePath);
                try
                {
                    foreach (string file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                    {
                        if (!string.Equals(Path.GetFullPath(file), selfPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                catch { }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = string.Format("/C ping 127.0.0.1 -n 2 > nul & rmdir /S /Q \"{0}\"", installDir),
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
            }

            if (!isSilent)
            {
                MessageBox.Show(
                    "Local Writing Tools has been successfully uninstalled.",
                    "Uninstall Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return 0;
        }
    }

    internal sealed class SetupForm : Form
    {
        public int ExitCode { get; private set; }

        private readonly TextBox _txtPath;
        private readonly Button _btnBrowse;
        private readonly CheckBox _chkDesktopShortcut;
        private readonly CheckBox _chkStartMenuShortcut;
        private readonly CheckBox _chkLaunchStartup;
        private readonly CheckBox _chkLaunchAfter;
        private readonly ProgressBar _progressBar;
        private readonly Label _lblStatus;
        private readonly Button _btnInstall;
        private readonly Button _btnCancel;
        private bool _isInstalling;
        private bool _installFinished;

        private static readonly Color BgDark = Color.FromArgb(14, 14, 19);
        private static readonly Color BgCard = Color.FromArgb(22, 22, 30);
        private static readonly Color BorderCol = Color.FromArgb(42, 42, 58);
        private static readonly Color AccentCol = Color.FromArgb(99, 102, 241);
        private static readonly Color TextPrimary = Color.FromArgb(244, 244, 246);
        private static readonly Color TextSecondary = Color.FromArgb(161, 161, 170);

        public SetupForm()
        {
            Text = "Local Writing Tools Setup (v" + InstallerProgram.AppVersion + ")";
            ClientSize = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try
            {
                using (Stream icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LocalWritingTools.app.ico"))
                {
                    if (icoStream != null)
                    {
                        Icon = new Icon(icoStream);
                    }
                }
            }
            catch { }

            // Header Banner
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = BgCard
            };
            headerPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(BorderCol, 1))
                {
                    e.Graphics.DrawLine(pen, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
                }
            };

            var picIcon = new PictureBox
            {
                Location = new Point(22, 18),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            if (Icon != null)
            {
                picIcon.Image = Icon.ToBitmap();
            }

            var lblTitle = new Label
            {
                Text = "Local Writing Tools",
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(76, 16),
                BackColor = Color.Transparent
            };

            var lblSubtitle = new Label
            {
                Text = "100% Local AI Writing Assistant (FastFlowLM NPU & LM Studio GPU)",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                ForeColor = TextSecondary,
                AutoSize = true,
                Location = new Point(78, 46),
                BackColor = Color.Transparent
            };

            headerPanel.Controls.Add(picIcon);
            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubtitle);
            Controls.Add(headerPanel);

            // Install Folder Section
            var lblFolder = new Label
            {
                Text = "Installation Folder (no administrator rights required):",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(24, 100)
            };
            Controls.Add(lblFolder);

            _txtPath = new TextBox
            {
                Text = InstallerProgram.GetDefaultInstallDir(),
                Location = new Point(24, 126),
                Size = new Size(412, 27),
                BackColor = BgCard,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f)
            };
            Controls.Add(_txtPath);

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(446, 124),
                Size = new Size(90, 29),
                FlatStyle = FlatStyle.Flat,
                BackColor = BgCard,
                ForeColor = TextPrimary,
                Cursor = Cursors.Hand
            };
            _btnBrowse.FlatAppearance.BorderColor = BorderCol;
            _btnBrowse.Click += BtnBrowse_Click;
            Controls.Add(_btnBrowse);

            // Options Card
            var lblOptions = new Label
            {
                Text = "Setup Options:",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(24, 168)
            };
            Controls.Add(lblOptions);

            _chkDesktopShortcut = CreateOptionCheckBox("Create a Desktop shortcut", 194, true);
            _chkStartMenuShortcut = CreateOptionCheckBox("Create a Start Menu shortcut", 224, true);
            _chkLaunchStartup = CreateOptionCheckBox("Start automatically when Windows logs in (System Tray)", 254, false);
            _chkLaunchAfter = CreateOptionCheckBox("Launch Local Writing Tools immediately after installation", 284, true);

            Controls.Add(_chkDesktopShortcut);
            Controls.Add(_chkStartMenuShortcut);
            Controls.Add(_chkLaunchStartup);
            Controls.Add(_chkLaunchAfter);

            // Status & Progress Bar
            _lblStatus = new Label
            {
                Text = "Ready to install standalone package (.NET 8 runtime included).",
                ForeColor = TextSecondary,
                AutoSize = false,
                Location = new Point(24, 326),
                Size = new Size(512, 22),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Location = new Point(24, 350),
                Size = new Size(512, 16),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(_progressBar);

            // Bottom Action Buttons
            _btnInstall = new Button
            {
                Text = "Install Now",
                Location = new Point(396, 386),
                Size = new Size(140, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentCol,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += BtnInstall_Click;
            Controls.Add(_btnInstall);

            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(294, 386),
                Size = new Size(92, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = BgCard,
                ForeColor = TextSecondary,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = BorderCol;
            _btnCancel.Click += (s, e) =>
            {
                if (!_isInstalling)
                {
                    ExitCode = 1;
                    Close();
                }
            };
            Controls.Add(_btnCancel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int round = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch { }
        }

        private CheckBox CreateOptionCheckBox(string text, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Location = new Point(28, y),
                ForeColor = TextPrimary,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select destination folder for Local Writing Tools:";
                dlg.SelectedPath = _txtPath.Text;
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    _txtPath.Text = dlg.SelectedPath;
                }
            }
        }

        private async void BtnInstall_Click(object sender, EventArgs e)
        {
            if (_installFinished)
            {
                if (_chkLaunchAfter.Checked)
                {
                    string exePath = Path.Combine(_txtPath.Text.Trim(), InstallerProgram.AppExeName);
                    if (File.Exists(exePath))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = exePath,
                                WorkingDirectory = _txtPath.Text.Trim(),
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }
                Close();
                return;
            }

            string targetDir = _txtPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                MessageBox.Show(this, "Please specify a valid installation folder.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _isInstalling = true;
            _txtPath.Enabled = false;
            _btnBrowse.Enabled = false;
            _chkDesktopShortcut.Enabled = false;
            _chkStartMenuShortcut.Enabled = false;
            _chkLaunchStartup.Enabled = false;
            _btnInstall.Enabled = false;
            _btnCancel.Enabled = false;
            _btnInstall.Text = "Installing...";

            bool desk = _chkDesktopShortcut.Checked;
            bool startMenu = _chkStartMenuShortcut.Checked;
            bool startup = _chkLaunchStartup.Checked;

            try
            {
                await Task.Run(() =>
                {
                    InstallerProgram.PerformInstall(targetDir, desk, startMenu, startup, (pct, msg) =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _progressBar.Value = Math.Max(0, Math.Min(100, pct));
                                _lblStatus.Text = msg;
                            }));
                        }
                    });
                });

                _installFinished = true;
                _isInstalling = false;
                _progressBar.Value = 100;
                _lblStatus.ForeColor = Color.FromArgb(74, 222, 128);
                _lblStatus.Text = "Local Writing Tools v" + InstallerProgram.AppVersion + " installed successfully!";
                _btnInstall.Text = "Finish";
                _btnInstall.BackColor = Color.FromArgb(22, 163, 74);
                _btnInstall.Enabled = true;
            }
            catch (Exception ex)
            {
                _isInstalling = false;
                _txtPath.Enabled = true;
                _btnBrowse.Enabled = true;
                _chkDesktopShortcut.Enabled = true;
                _chkStartMenuShortcut.Enabled = true;
                _chkLaunchStartup.Enabled = true;
                _btnInstall.Enabled = true;
                _btnCancel.Enabled = true;
                _btnInstall.Text = "Retry Install";
                _lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                _lblStatus.Text = "Error: " + ex.Message;
                MessageBox.Show(this, "Installation failed:\n\n" + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
