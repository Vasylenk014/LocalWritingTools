using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using WritingTools.Models;
using WritingTools.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace WritingTools;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "WritingTools_log.txt");

    private static void Log(string msg)
    {
        try
        {
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 256 * 1024)
            {
                File.WriteAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log rotated (capped at 256 KB)\r\n");
            }
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
        }
        catch { }
    }

    private static Mutex? _mutex;
    private static bool _hasMutex;
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private FlmApiService? _flmService;
    private SettingsService? _settingsService;
    private GlobalHotkeyService? _hotkeyService;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) => Log($"UnhandledException: {ev.ExceptionObject}");
        DispatcherUnhandledException += (s, ev) => { Log($"DispatcherUnhandledException: {ev.Exception}"); };

        Log("App.OnStartup called");

        // Single instance check
        const string mutexName = "WritingTools_RyzenAI_FastFlowLM_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        _hasMutex = createdNew;

        Log($"Mutex createdNew: {createdNew}");

        if (!createdNew)
        {
            Log("Another instance already running. Exiting.");
            MessageBox.Show(
                "Local Writing Tools is already running in the system tray.\nPress Alt+Shift+G or click the tray icon.",
                "Local Writing Tools",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);

        try
        {
            Log("Initializing Services...");
            _settingsService = new SettingsService();
            _flmService = new FlmApiService();
            _hotkeyService = new GlobalHotkeyService();

            Log("Initializing MainWindow...");
            _mainWindow = new MainWindow(_flmService, _settingsService, _hotkeyService);
            _mainWindow.Closed += (s, e) => ExitApplication();

            Log("Setting up SystemTray...");
            SetupSystemTray();

            Log("Showing MainWindow...");
            _mainWindow.ShowAndActivate();
            Log("MainWindow shown successfully.");
        }
        catch (Exception ex)
        {
            Log($"Error in OnStartup: {ex}");
            throw;
        }
    }

    private void SetupSystemTray()
    {
        var penIcon = IconHelper.CreatePenIcon(32);

        _notifyIcon = new NotifyIcon
        {
            Icon = penIcon,
            Text = "Local Writing Tools\nAlt+Shift+G",
            Visible = true
        };

        // Click to toggle window
        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleMainWindow();
            }
        };

        // Context Menu
        var contextMenu = new ContextMenuStrip();

        var titleItem = new ToolStripMenuItem("Local Writing Tools")
        {
            Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold),
            Enabled = false
        };
        contextMenu.Items.Add(titleItem);

        var openItem = new ToolStripMenuItem("Open (Alt+Shift+G)", null, (s, e) => ToggleMainWindow());
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var autoCopyItem = new ToolStripMenuItem("Auto-Copy Enabled")
        {
            Checked = _settingsService?.Settings.AutoCopy ?? true,
            CheckOnClick = true
        };
        autoCopyItem.CheckedChanged += (s, e) =>
        {
            if (_settingsService != null)
            {
                _settingsService.Settings.AutoCopy = autoCopyItem.Checked;
                _settingsService.Save();
                if (_mainWindow != null)
                {
                    _mainWindow.ChkAutoCopy.IsChecked = autoCopyItem.Checked;
                }
            }
        };
        contextMenu.Items.Add(autoCopyItem);

        var themeMenu = new ToolStripMenuItem("Theme");
        themeMenu.DropDownItems.Add(new ToolStripMenuItem("System (Auto)", null, (s, e) => _mainWindow?.SetTheme(AppTheme.System)));
        themeMenu.DropDownItems.Add(new ToolStripMenuItem("Light", null, (s, e) => _mainWindow?.SetTheme(AppTheme.Light)));
        themeMenu.DropDownItems.Add(new ToolStripMenuItem("Dark", null, (s, e) => _mainWindow?.SetTheme(AppTheme.Dark)));
        contextMenu.Items.Add(themeMenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.WindowState == WindowState.Minimized || !_mainWindow.IsVisible)
        {
            _mainWindow.ShowAndActivate();
        }
        else
        {
            if (_mainWindow.IsActive)
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Activate();
            }
        }
    }

    private void ExitApplication()
    {
        Log("ExitApplication called");
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _hotkeyService?.Dispose();
        _mainWindow?.RequestExit();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"App.OnExit called with code {e.ApplicationExitCode}");
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_hasMutex && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
        }
        _mutex?.Dispose();
        _mutex = null;

        base.OnExit(e);
    }
}
