using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WritingTools.Models;
using WritingTools.Services;

// Disambiguate WPF types from Windows Forms types
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace WritingTools;

public partial class MainWindow : Window
{
    private readonly FlmApiService _flmService;
    private readonly SettingsService _settingsService;
    private readonly GlobalHotkeyService _hotkeyService;

    private AppMode _currentMode = AppMode.QuickFix;
    public AppMode CurrentMode => _currentMode;
    private TonePreset _currentTone = TonePreset.Standard;
    private ThinkingOption _selectedThinkingOption = ThinkingOption.Off;
    private CancellationTokenSource? _streamCts;
    private bool _isGenerating;
    private bool _isStartingServer;
    private bool _isExplicitExit;
    private string _accumulatedResult = string.Empty;
    private long _lastUiStreamUpdateMs;
    private DispatcherTimer? _serverPollTimer;
    private DispatcherTimer? _typingAnimationTimer;
    private DispatcherTimer? _metricsTimer;
    private readonly SystemMetricsService _metricsService;
    private AppMode _editingPromptMode = AppMode.QuickFix;
    private bool _cursorVisible = true;
    private bool _isThinking;
    private int _thinkingChars;

    // Buffer separation: QuickFix, Brainstorm, and DetailedReview share draft text, but each mode maintains its own independent Result buffer
    private string _replyDraftBuffer = string.Empty;
    private string _quickFixResultBuffer = string.Empty;
    private string _brainstormResultBuffer = string.Empty;
    private string _detailedResultBuffer = string.Empty;
    private string _summarizeDraftBuffer = string.Empty;
    private string _summarizeResultBuffer = string.Empty;

    private AppTheme _currentTheme = AppTheme.System;
    private bool _isLightThemeEffective;

    public MainWindow(FlmApiService flmService, SettingsService settingsService, GlobalHotkeyService hotkeyService)
    {
        InitializeComponent();

        _flmService = flmService;
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _metricsService = new SystemMetricsService();

        _currentTheme = _settingsService.Settings.Theme;
        ApplyTheme(_currentTheme);

        // Set application icon from IconHelper
        try
        {
            Icon = IconHelper.CreatePenImageSource(48);
        }
        catch { }

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(_currentTheme);
        ApplyDwmAttributes();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        // Register Global Hotkey (Alt+Shift+G)
        try
        {
            _hotkeyService.Register(this);
            _hotkeyService.HotkeyTriggered += HotkeyService_HotkeyTriggered;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: {ex.Message}");
        }

        // Apply settings & configure AI provider
        ChkAutoCopy.IsChecked = _settingsService.Settings.AutoCopy;
        UpdateHistoryBadge();

        var provider = _settingsService.Settings.Provider;
        _flmService.ConfigureProvider(provider, _settingsService.Settings.GetEffectiveBaseUrl(), _settingsService.Settings.CustomApiKey);
        _metricsService.SetProvider(provider);
        UpdateProviderUi(provider);

        // Start system telemetry monitoring (RAM and AMD Ryzen AI NPU / GPU)
        _metricsService.Start();
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _metricsTimer.Tick += (s, ev) => UpdateSystemMetricsUI();
        _metricsTimer.Start();
        UpdateSystemMetricsUI();

        // Select default mode (always start with QuickFix on launch)
        SetMode(AppMode.QuickFix);

        // Load models and check server
        await RefreshModelsAndServerAsync();

        // Start background health polling timer (every 2.5 seconds)
        _serverPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _serverPollTimer.Tick += async (s, ev) => await PollServerHealthAsync();
        _serverPollTimer.Start();

        TxtDraft.Focus();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            // Close button hides window to system tray and pauses background UI polling for 0% idle CPU
            e.Cancel = true;
            _serverPollTimer?.Stop();
            _metricsTimer?.Stop();
            _metricsService.SetPaused(true);
            Hide();
            return;
        }

        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _serverPollTimer?.Stop();
        _typingAnimationTimer?.Stop();
        _metricsTimer?.Stop();
        _metricsService.Dispose();
        _hotkeyService.Dispose();
        _settingsService.Settings.AutoCopy = ChkAutoCopy.IsChecked ?? true;
        _settingsService.Save();
    }

    public void RequestExit()
    {
        _isExplicitExit = true;
        Close();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_currentTheme == AppTheme.System)
        {
            Dispatcher.BeginInvoke(() => ApplyTheme(AppTheme.System));
        }
    }

    public static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
            {
                return val == 1;
            }
        }
        catch { }
        return false;
    }

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        set => SetTheme(value, saveSettings: false);
    }

    public bool IsLightThemeEffective => _isLightThemeEffective;

    public void SetTheme(AppTheme theme, bool saveSettings = true)
    {
        _currentTheme = theme;
        if (saveSettings)
        {
            _settingsService.Settings.Theme = theme;
            _settingsService.Save();
        }
        ApplyTheme(theme);
    }

    private void ApplyTheme(AppTheme theme)
    {
        _isLightThemeEffective = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => IsWindowsLightTheme()
        };

        if (_isLightThemeEffective)
        {
            Resources["BgDarkBrush"] = new SolidColorBrush(Color.FromRgb(244, 246, 249));
            Resources["BgSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            Resources["BgCardBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            Resources["BgSegmentTrackBrush"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            Resources["BgSegmentActiveBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            Resources["BgPillBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            Resources["BgPillHoverBrush"] = new SolidColorBrush(Color.FromRgb(237, 242, 247));
            Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            Resources["BorderLightBrush"] = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(79, 70, 229));
            Resources["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(67, 56, 202));
            Resources["AccentActiveBrush"] = new SolidColorBrush(Color.FromRgb(238, 242, 255));
            Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            Resources["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            Resources["GreenBrush"] = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            Resources["RedBrush"] = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            Resources["YellowBrush"] = new SolidColorBrush(Color.FromRgb(217, 119, 6));
            Resources["OverlayScrimBrush"] = new SolidColorBrush(Color.FromArgb(110, 15, 23, 42));
            Resources["ToastBgBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }
        else
        {
            Resources["BgDarkBrush"] = new SolidColorBrush(Color.FromRgb(10, 10, 13));
            Resources["BgSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(19, 19, 24));
            Resources["BgCardBrush"] = new SolidColorBrush(Color.FromRgb(14, 14, 19));
            Resources["BgSegmentTrackBrush"] = new SolidColorBrush(Color.FromRgb(24, 24, 32));
            Resources["BgSegmentActiveBrush"] = new SolidColorBrush(Color.FromRgb(46, 46, 62));
            Resources["BgPillBrush"] = new SolidColorBrush(Color.FromRgb(26, 26, 36));
            Resources["BgPillHoverBrush"] = new SolidColorBrush(Color.FromRgb(36, 36, 50));
            Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(37, 37, 51));
            Resources["BorderLightBrush"] = new SolidColorBrush(Color.FromRgb(52, 52, 70));
            Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(99, 102, 241));
            Resources["AccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(79, 70, 229));
            Resources["AccentActiveBrush"] = new SolidColorBrush(Color.FromRgb(30, 27, 75));
            Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(244, 244, 246));
            Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(161, 161, 170));
            Resources["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(113, 113, 122));
            Resources["GreenBrush"] = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            Resources["RedBrush"] = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            Resources["YellowBrush"] = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            Resources["OverlayScrimBrush"] = new SolidColorBrush(Color.FromArgb(176, 0, 0, 0));
            Resources["ToastBgBrush"] = new SolidColorBrush(Color.FromRgb(24, 24, 34));
        }

        if (TxtThemeBadge != null)
        {
            TxtThemeBadge.Text = theme switch
            {
                AppTheme.Light => "☀️ Light",
                AppTheme.Dark => "🌙 Dark",
                _ => "🌗 Auto"
            };
        }

        UpdateThemeSegmentButtons();
        ApplyDwmAttributes();

        if (IsInitialized)
        {
            UpdateToneHighlights();
            SetThinkingOption(_selectedThinkingOption);
            UpdateSegmentButton(BtnModeQuickFix, _currentMode == AppMode.QuickFix);
            UpdateSegmentButton(BtnModeBrainstorm, _currentMode == AppMode.Brainstorm);
            UpdateSegmentButton(BtnModeDetailed, _currentMode == AppMode.DetailedReview);
            UpdateSegmentButton(BtnModeSummarize, _currentMode == AppMode.Summarize);
            UpdateContextVisualState();

            // Refresh active diff or styled markdown view so its foregrounds match the new theme
            if (RtfDiff != null && RtfDiff.Visibility == Visibility.Visible)
            {
                if (_currentMode == AppMode.QuickFix)
                {
                    RenderVisualDiff(TxtDraft.Text, TxtResult.Text);
                }
                else if (!string.IsNullOrWhiteSpace(TxtResult.Text))
                {
                    RenderMarkdownView(TxtResult.Text);
                }
            }
            else if (BtnViewText != null && BtnViewDiff != null)
            {
                SetDiffView(RtfDiff?.Visibility == Visibility.Visible);
            }
        }
    }

    private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var nextTheme = _currentTheme switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.System
        };
        SetTheme(nextTheme, saveSettings: true);
        var modeDesc = nextTheme == AppTheme.System
            ? $"System Auto ({(_isLightThemeEffective ? "Light" : "Dark")})"
            : nextTheme.ToString();
        TxtStatus.Text = $"Theme: {modeDesc}";
    }

    private void BtnThemeOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            SetTheme(theme, saveSettings: true);
        }
    }

    private void UpdateThemeSegmentButtons()
    {
        if (BtnThemeSystem == null || BtnThemeLight == null || BtnThemeDark == null) return;
        UpdateSegmentButton(BtnThemeSystem, _currentTheme == AppTheme.System);
        UpdateSegmentButton(BtnThemeLight, _currentTheme == AppTheme.Light);
        UpdateSegmentButton(BtnThemeDark, _currentTheme == AppTheme.Dark);
    }

    private void ApplyDwmAttributes()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int darkMode = _isLightThemeEffective ? 0 : 1;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // Windows 11 rounded window corners
                int cornerRound = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerRound, sizeof(int));
            }
        }
        catch
        {
            // Ignore on older Windows builds
        }
    }

    #region Window Controls & Navigation

    private void BtnQuit_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitExit = true;
        Close();
        Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (OverlaySettings.Visibility == Visibility.Visible)
            {
                OverlaySettings.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (OverlayHistory.Visibility == Visibility.Visible)
            {
                OverlayHistory.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (ExpContext.IsExpanded)
            {
                ExpContext.IsExpanded = false;
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Enter -> Improve
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            BtnImprove_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+C -> Smart Copy
        if (e.Key == Key.C &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            BtnSmartCopy_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Ctrl+1..4 -> Fast Mode Switching
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == 0 &&
            (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            if (e.Key == Key.D1 || e.Key == Key.NumPad1) { SetMode(AppMode.QuickFix); e.Handled = true; return; }
            if (e.Key == Key.D2 || e.Key == Key.NumPad2) { SetMode(AppMode.Brainstorm); e.Handled = true; return; }
            if (e.Key == Key.D3 || e.Key == Key.NumPad3) { SetMode(AppMode.DetailedReview); e.Handled = true; return; }
            if (e.Key == Key.D4 || e.Key == Key.NumPad4) { SetMode(AppMode.Summarize); e.Handled = true; return; }
        }
    }

    public void ShowAndActivate(string? initialText = null)
    {
        bool wasHidden = !IsVisible || WindowState == WindowState.Minimized;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();

        // Resume background health & telemetry timers when window is visible
        _metricsService.SetPaused(false);
        _metricsTimer?.Start();
        _serverPollTimer?.Start();

        if (wasHidden)
        {
            SetMode(AppMode.QuickFix);
        }

        if (!string.IsNullOrWhiteSpace(initialText))
        {
            TxtDraft.Text = initialText.Trim();
            TxtDraft.CaretIndex = TxtDraft.Text.Length;
        }

        TxtDraft.Focus();
    }

    private void HotkeyService_HotkeyTriggered(string? copiedText)
    {
        Dispatcher.Invoke(async () =>
        {
            ShowAndActivate(copiedText);

            if (!string.IsNullOrWhiteSpace(TxtDraft.Text) && !_isGenerating)
            {
                await RunImproveAsync();
            }
        });
    }

    #endregion

    #region Server & Model Management

    private DispatcherTimer? _notificationTimer;

    private void ShowNotificationBanner(string message, bool isSuccess)
    {
        _notificationTimer?.Stop();
        TxtNotificationIcon.Text = isSuccess ? "✓" : "⚠";
        TxtNotificationIcon.Foreground = (SolidColorBrush)FindResource(isSuccess ? "GreenBrush" : "RedBrush");
        BdNotificationToast.BorderBrush = (SolidColorBrush)FindResource(isSuccess ? "GreenBrush" : "RedBrush");
        TxtNotificationMessage.Text = message;
        BdNotificationToast.Visibility = Visibility.Visible;

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _notificationTimer.Tick += (s, e) =>
        {
            BdNotificationToast.Visibility = Visibility.Collapsed;
            _notificationTimer?.Stop();
            _notificationTimer = null;
        };
        _notificationTimer.Start();
    }

    private async Task RefreshModelsAndServerAsync()
    {
        bool isOnline = await _flmService.CheckServerHealthAsync();
        UpdateServerStatusUi(isOnline ? ServerState.Online : ServerState.Offline);

        var models = await _flmService.GetModelsAsync();
        CmbModels.ItemsSource = models;
        CmbModels.DisplayMemberPath = nameof(FlmModelInfo.DisplayName);

        if (models.Count > 0)
        {
            var targetId = _flmService.CurrentProvider == LlmProvider.LmStudio
                ? (!string.IsNullOrEmpty(_settingsService.Settings.SelectedLmStudioModel) ? _settingsService.Settings.SelectedLmStudioModel : _settingsService.Settings.SelectedModel)
                : _settingsService.Settings.SelectedModel;

            var match = models.FirstOrDefault(m => m.ModelId.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                     ?? models.FirstOrDefault(m => m.Installed)
                     ?? models[0];

            CmbModels.SelectedItem = match;
            UpdateThinkingControlForModel(match);
        }
        else
        {
            UpdateThinkingControlForModel(null);
        }
    }

    private async Task PollServerHealthAsync()
    {
        if (_isStartingServer || _isGenerating) return;

        bool isOnline = await _flmService.CheckServerHealthAsync();
        UpdateServerStatusUi(isOnline ? ServerState.Online : ServerState.Offline);
    }

    private void UpdateServerStatusUi(ServerState state, string? message = null)
    {
        var provider = _flmService.CurrentProvider;
        UpdateProviderUi(provider);

        var selected = CmbModels?.SelectedItem as FlmModelInfo;
        var activeModel = _flmService.ActiveModel ?? _flmService.DetectRunningModel();
        bool isOnline = state == ServerState.Online;

        if (provider != LlmProvider.FastFlowLmNpu)
        {
            // LM Studio / Custom Provider mode
            StatusDot.Fill = (SolidColorBrush)FindResource(isOnline ? "GreenBrush" : "RedBrush");
            var provName = provider == LlmProvider.LmStudio ? "LM Studio" : "Custom Server";
            StatusDot.ToolTip = isOnline
                ? $"{provName} is Online ({_flmService.CurrentBaseUrl})"
                : $"{provName} is Offline ({_flmService.CurrentBaseUrl})";

            if (TxtLmStudioStatus != null)
            {
                TxtLmStudioStatus.Text = isOnline
                    ? (provider == LlmProvider.LmStudio ? "LM Studio :1234" : "Server Online")
                    : (provider == LlmProvider.LmStudio ? "LM Studio (Offline)" : "Server Offline");
                TxtLmStudioStatus.Foreground = (SolidColorBrush)FindResource(isOnline ? "GreenBrush" : "RedBrush");
            }

            if (!_isGenerating && string.IsNullOrEmpty(TxtHeaderStats.Text))
            {
                TxtStatus.Text = message ?? (isOnline ? $"{provName} online ({_flmService.CurrentBaseUrl})" : $"{provName} offline");
            }

            _metricsService?.SetServerRunning(isOnline);
            return;
        }

        // FastFlowLM NPU mode
        bool modelMismatch = isOnline && selected != null &&
                             !string.IsNullOrEmpty(activeModel) &&
                             !string.Equals(selected.ModelId, activeModel, StringComparison.OrdinalIgnoreCase);

        switch (state)
        {
            case ServerState.Online:
                StatusDot.Fill = (SolidColorBrush)FindResource("GreenBrush");
                StatusDot.ToolTip = $"FastFlowLM Server is Online (port 52625, model: {activeModel ?? "unknown"})";

                if (modelMismatch)
                {
                    // Allow switching model on-the-fly
                    BtnStartServer.IsEnabled = true;
                    TxtStartServerBtn.Text = "🔄 Switch";
                    TxtStartServerBtn.Foreground = (SolidColorBrush)FindResource("AccentBrush");
                    BtnStartServer.ToolTip = $"Switch running NPU model from '{activeModel}' to '{selected!.DisplayName}'";
                }
                else
                {
                    BtnStartServer.IsEnabled = false;
                    TxtStartServerBtn.Text = "▶ Start";
                    TxtStartServerBtn.Foreground = (SolidColorBrush)FindResource("GreenBrush");
                    BtnStartServer.ToolTip = $"FastFlowLM is running with '{activeModel ?? selected?.DisplayName}'";
                }

                BtnStopServer.IsEnabled = true;
                if (string.IsNullOrEmpty(TxtHeaderStats.Text))
                {
                    TxtStatus.Text = message ?? (modelMismatch
                        ? $"Running '{activeModel}' (click Switch to load '{selected!.DisplayName}')"
                        : $"Server online ({activeModel})");
                }
                break;

            case ServerState.Starting:
                StatusDot.Fill = (SolidColorBrush)FindResource("YellowBrush");
                StatusDot.ToolTip = "FastFlowLM Server is Starting...";
                BtnStartServer.IsEnabled = false;
                BtnStopServer.IsEnabled = true;
                TxtStatus.Text = message ?? "Starting server...";
                break;

            case ServerState.Offline:
            default:
                StatusDot.Fill = (SolidColorBrush)FindResource("RedBrush");
                StatusDot.ToolTip = "FastFlowLM Server is Offline (click ▶ Start to launch)";
                BtnStartServer.IsEnabled = true;
                TxtStartServerBtn.Text = "▶ Start";
                TxtStartServerBtn.Foreground = (SolidColorBrush)FindResource("GreenBrush");
                BtnStartServer.ToolTip = "Start FastFlowLM server with selected model";
                BtnStopServer.IsEnabled = false;
                if (!_isGenerating)
                {
                    TxtStatus.Text = message ?? "Server offline";
                }
                break;
        }

        _metricsService?.SetServerRunning(state == ServerState.Online);
    }

    private void UpdateProviderUi(LlmProvider provider)
    {
        if (TxtProviderBadge == null) return;

        switch (provider)
        {
            case LlmProvider.LmStudio:
                TxtProviderBadge.Text = "🎮 LM Studio";
                if (PanelFlmControls != null) PanelFlmControls.Visibility = Visibility.Collapsed;
                if (PanelLmStudioControls != null) PanelLmStudioControls.Visibility = Visibility.Visible;
                if (LblComputeDevice != null) LblComputeDevice.Text = "GPU:";
                break;

            case LlmProvider.Custom:
                TxtProviderBadge.Text = "🌐 Custom";
                if (PanelFlmControls != null) PanelFlmControls.Visibility = Visibility.Collapsed;
                if (PanelLmStudioControls != null) PanelLmStudioControls.Visibility = Visibility.Collapsed;
                if (LblComputeDevice != null) LblComputeDevice.Text = "GPU:";
                break;

            case LlmProvider.FastFlowLmNpu:
            default:
                TxtProviderBadge.Text = "⚡ NPU";
                if (PanelFlmControls != null) PanelFlmControls.Visibility = Visibility.Visible;
                if (PanelLmStudioControls != null) PanelLmStudioControls.Visibility = Visibility.Collapsed;
                if (LblComputeDevice != null) LblComputeDevice.Text = "NPU:";
                break;
        }
    }

    private void UpdateSystemMetricsUI()
    {
        if (_metricsService == null || TxtRamStats == null || TxtNpuStats == null) return;

        var m = _metricsService.LatestMetrics;
        TxtRamStats.Text = m.RamText;
        TxtNpuStats.Text = m.NpuText;

        if (LblComputeDevice != null)
        {
            LblComputeDevice.Text = m.DeviceLabel;
        }

        if (BdSystemMetrics != null)
        {
            BdSystemMetrics.ToolTip = $"System Telemetry: Physical RAM & {m.DeviceLabel.TrimEnd(':')} Load";
        }

        if (m.IsComputeActive)
        {
            DotNpuTelemetry.Fill = (SolidColorBrush)FindResource("GreenBrush");
        }
        else
        {
            DotNpuTelemetry.Fill = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
        }
    }

    private async void BtnStartServer_Click(object sender, RoutedEventArgs e)
    {
        if (_isStartingServer) return;

        string modelToLoad = "gemma4-it:e4b";
        if (CmbModels.SelectedItem is FlmModelInfo selected)
        {
            modelToLoad = selected.ModelId;
        }

        _isStartingServer = true;
        var activeModel = _flmService.ActiveModel ?? _flmService.DetectRunningModel();
        bool isSwitching = _flmService.CurrentState == ServerState.Online &&
                           !string.IsNullOrEmpty(activeModel) &&
                           !string.Equals(activeModel, modelToLoad, StringComparison.OrdinalIgnoreCase);

        UpdateServerStatusUi(ServerState.Starting, isSwitching
            ? $"Switching NPU model from '{activeModel}' to '{modelToLoad}' (~15s)..."
            : $"Starting FLM with '{modelToLoad}' (~15s)...");

        var progress = new Progress<string>(msg =>
        {
            TxtStatus.Text = msg;
        });

        try
        {
            bool started = await _flmService.StartServerAsync(modelToLoad, progress);
            UpdateServerStatusUi(started ? ServerState.Online : ServerState.Offline);
            if (started)
            {
                await RefreshModelsAndServerAsync();
                ShowNotificationBanner(isSwitching
                    ? $"✓ Successfully switched NPU model to '{modelToLoad}'."
                    : $"✓ FastFlowLM server online with '{modelToLoad}'.", true);
            }
            else
            {
                ShowNotificationBanner($"⚠ Failed to start server with '{modelToLoad}'.", false);
            }
        }
        finally
        {
            _isStartingServer = false;
        }
    }

    private async void BtnStopServer_Click(object sender, RoutedEventArgs e)
    {
        BtnStopServer.IsEnabled = false;
        TxtStatus.Text = "Stopping FastFlowLM server and freeing NPU memory...";

        var progress = new Progress<string>(msg => TxtStatus.Text = msg);
        bool stopped = await _flmService.StopServerAsync(progress);

        UpdateServerStatusUi(ServerState.Offline);

        if (stopped)
        {
            ShowNotificationBanner("✓ FastFlowLM server stopped successfully. NPU memory released.", true);
            TxtStatus.Text = "Server stopped (NPU freed)";
        }
        else
        {
            ShowNotificationBanner("⚠ Server stop incomplete. Check Task Manager.", false);
            TxtStatus.Text = "Server stop warning";
        }
    }

    private async void BtnUnloadLmStudio_Click(object sender, RoutedEventArgs e)
    {
        BtnUnloadLmStudio.IsEnabled = false;
        TxtStatus.Text = "Unloading models from LM Studio and freeing GPU memory...";
        var progress = new Progress<string>(msg => TxtStatus.Text = msg);

        bool stopped = await _flmService.StopServerAsync(progress);
        await RefreshModelsAndServerAsync();
        BtnUnloadLmStudio.IsEnabled = true;

        if (stopped)
        {
            ShowNotificationBanner("✓ LM Studio models unloaded. GPU VRAM released.", true);
            TxtStatus.Text = "Models unloaded (VRAM freed)";
        }
        else
        {
            ShowNotificationBanner("⚠ Unload warning. Make sure LM Studio is running.", false);
            TxtStatus.Text = "Unload warning";
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Refreshing models & checking NPU server...";
        await RefreshModelsAndServerAsync();

        bool isOnline = _flmService.CurrentState == ServerState.Online;
        var activeModel = _flmService.ActiveModel ?? _flmService.DetectRunningModel();
        var modelCount = CmbModels.Items.Count;

        if (isOnline)
        {
            ShowNotificationBanner($"✓ {modelCount} models detected. Server ONLINE with '{activeModel ?? "selected"}'.", true);
            TxtStatus.Text = $"Refreshed: Online ({activeModel})";
        }
        else
        {
            ShowNotificationBanner($"✓ {modelCount} models detected. Server is OFFLINE.", false);
            TxtStatus.Text = $"Refreshed: {modelCount} models, server offline";
        }
    }

    private void CmbModels_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbModels.SelectedItem is FlmModelInfo selected)
        {
            if (_flmService.CurrentProvider == LlmProvider.LmStudio)
            {
                _settingsService.Settings.SelectedLmStudioModel = selected.ModelId;
            }
            _settingsService.Settings.SelectedModel = selected.ModelId;
            _settingsService.Save();

            UpdateThinkingControlForModel(selected);

            if (_flmService.CurrentState == ServerState.Online)
            {
                var active = _flmService.ActiveModel ?? _flmService.DetectRunningModel();
                UpdateServerStatusUi(ServerState.Online);
                if (_flmService.CurrentProvider == LlmProvider.FastFlowLmNpu &&
                    !string.IsNullOrEmpty(active) && !string.Equals(active, selected.ModelId, StringComparison.OrdinalIgnoreCase))
                {
                    ShowNotificationBanner($"Selected '{selected.DisplayName}'. Ready to switch on NPU.", true);
                }
            }
        }
    }

    private void UpdateThinkingControlForModel(FlmModelInfo? model)
    {
        bool showThinking = model != null && (
            _flmService.CurrentProvider == LlmProvider.LmStudio ||
            _flmService.CurrentProvider == LlmProvider.Custom ||
            model.SupportsThinking
        );

        if (!showThinking || model == null)
        {
            RowThinking.Visibility = Visibility.Collapsed;
            return;
        }

        RowThinking.Visibility = Visibility.Visible;

        bool supportsOff = model.ReasoningAllowedOptions == null ||
                           model.ReasoningAllowedOptions.Count == 0 ||
                           model.ReasoningAllowedOptions.Contains("off", StringComparer.OrdinalIgnoreCase);

        BtnThinkingOff.IsEnabled = supportsOff;
        BtnThinkingOff.Opacity = supportsOff ? 1.0 : 0.35;
        BtnThinkingOff.ToolTip = supportsOff
            ? "Disable model internal reasoning (faster, direct output)"
            : $"This model requires reasoning and cannot be turned off. (Supported: {string.Join(", ", model.ReasoningAllowedOptions ?? new())})";

        if (_settingsService.Settings.ThinkingModePerModel.TryGetValue(model.ModelId, out var savedStr) &&
            Enum.TryParse<ThinkingOption>(savedStr, out var savedOption))
        {
            if (savedOption == ThinkingOption.Off && !supportsOff)
            {
                savedOption = ThinkingOption.Auto;
            }
            SetThinkingOption(savedOption);
        }
        else
        {
            var defaultOpt = string.Equals(model.ReasoningDefault, "on", StringComparison.OrdinalIgnoreCase)
                ? ThinkingOption.On
                : (supportsOff ? ThinkingOption.Off : ThinkingOption.Auto);
            SetThinkingOption(defaultOpt);
        }
    }

    private void BtnThinking_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<ThinkingOption>(tag, out var opt))
        {
            if (CmbModels.SelectedItem is FlmModelInfo model)
            {
                bool supportsOff = model.ReasoningAllowedOptions == null ||
                                   model.ReasoningAllowedOptions.Count == 0 ||
                                   model.ReasoningAllowedOptions.Contains("off", StringComparer.OrdinalIgnoreCase);

                if (opt == ThinkingOption.Off && !supportsOff)
                {
                    TxtStatus.Text = $"Model '{model.Name}' requires reasoning and does not support 'Off'. Switched to 'Auto'.";
                    opt = ThinkingOption.Auto;
                }

                SetThinkingOption(opt);
                _settingsService.Settings.ThinkingModePerModel[model.ModelId] = opt.ToString();
                _settingsService.Save();
            }
            else
            {
                SetThinkingOption(opt);
            }
        }
    }

    private void SetThinkingOption(ThinkingOption option)
    {
        _selectedThinkingOption = option;

        UpdateSegmentButton(BtnThinkingAuto, option == ThinkingOption.Auto);
        UpdateSegmentButton(BtnThinkingOff, option == ThinkingOption.Off);
        UpdateSegmentButton(BtnThinkingOn, option == ThinkingOption.On || option == ThinkingOption.Low || option == ThinkingOption.Medium || option == ThinkingOption.High);
    }

    private void UpdateSegmentButton(Button btn, bool isActive)
    {
        if (isActive)
        {
            btn.Background = (SolidColorBrush)FindResource("BgSegmentActiveBrush");
            btn.Foreground = _isLightThemeEffective
                ? (SolidColorBrush)FindResource("AccentBrush")
                : (SolidColorBrush)FindResource("TextPrimaryBrush");
            btn.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
            btn.FontWeight = FontWeights.Normal;
        }
    }

    #endregion

    #region Mode & Tone Presets

    private void BtnMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modeStr && Enum.TryParse<AppMode>(modeStr, out var mode))
        {
            SetMode(mode);
        }
    }

    public string DraftInputText
    {
        get => TxtDraft.Text;
        set => TxtDraft.Text = value;
    }

    public string ResultOutputText
    {
        get => TxtResult.Text;
        set
        {
            TxtResult.Text = value;
            _accumulatedResult = value;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(value) ? Visibility.Visible : Visibility.Collapsed;
            if (_currentMode == AppMode.Summarize)
                _summarizeResultBuffer = value;
            else if (_currentMode == AppMode.DetailedReview)
                _detailedResultBuffer = value;
            else if (_currentMode == AppMode.Brainstorm)
                _brainstormResultBuffer = value;
            else
                _quickFixResultBuffer = value;
        }
    }

    public string ContextInputText
    {
        get => TxtContext.Text;
        set => TxtContext.Text = value;
    }

    public bool IsContextBadgeVisible => BadgeContextActive.Visibility == Visibility.Visible;

    public string EmptyResultWatermarkText
    {
        get => TxtResultEmpty.Text;
        set => TxtResultEmpty.Text = value;
    }

    public TonePreset CurrentTone
    {
        get => _currentTone;
        set
        {
            _currentTone = value;
            UpdateToneHighlights();
            UpdateDraftLabelsForModeAndTone();
        }
    }

    public void ClearDraftAndResult() => BtnDraftClear_Click(this, new RoutedEventArgs());

    public void SetMode(AppMode mode)
    {
        // 1. Save current active tab's buffers before switching
        if (_currentMode == AppMode.Summarize)
        {
            _summarizeDraftBuffer = TxtDraft.Text;
            _summarizeResultBuffer = TxtResult.Text;
        }
        else if (_currentMode == AppMode.DetailedReview)
        {
            _replyDraftBuffer = TxtDraft.Text;
            _detailedResultBuffer = TxtResult.Text;
        }
        else if (_currentMode == AppMode.Brainstorm)
        {
            _replyDraftBuffer = TxtDraft.Text;
            _brainstormResultBuffer = TxtResult.Text;
        }
        else
        {
            _replyDraftBuffer = TxtDraft.Text;
            _quickFixResultBuffer = TxtResult.Text;
        }

        _currentMode = mode;

        // Clear any leftover streaming status text on the empty result overlay
        if (!_isGenerating)
        {
            TxtResultEmpty.Text = string.Empty;
        }

        // 2. Restore the target mode's text and results (strictly separated per tab)
        if (mode == AppMode.Summarize)
        {
            TxtDraft.Text = _summarizeDraftBuffer;
            TxtResult.Text = _summarizeResultBuffer;
            _accumulatedResult = _summarizeResultBuffer;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(_summarizeResultBuffer) ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (mode == AppMode.DetailedReview)
        {
            TxtDraft.Text = _replyDraftBuffer;
            TxtResult.Text = _detailedResultBuffer;
            _accumulatedResult = _detailedResultBuffer;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(_detailedResultBuffer) ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (mode == AppMode.Brainstorm)
        {
            TxtDraft.Text = _replyDraftBuffer;
            TxtResult.Text = _brainstormResultBuffer;
            _accumulatedResult = _brainstormResultBuffer;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(_brainstormResultBuffer) ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            TxtDraft.Text = _replyDraftBuffer;
            TxtResult.Text = _quickFixResultBuffer;
            _accumulatedResult = _quickFixResultBuffer;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(_quickFixResultBuffer) ? Visibility.Visible : Visibility.Collapsed;
        }
        TxtDraft.CaretIndex = TxtDraft.Text.Length;

        // Update clean full-width segment highlights
        UpdateSegmentButton(BtnModeQuickFix, mode == AppMode.QuickFix);
        UpdateSegmentButton(BtnModeBrainstorm, mode == AppMode.Brainstorm);
        UpdateSegmentButton(BtnModeDetailed, mode == AppMode.DetailedReview);
        UpdateSegmentButton(BtnModeSummarize, mode == AppMode.Summarize);

        // Context & Custom Instructions box is available across all modes
        BdContextContainer.Visibility = Visibility.Visible;
        ExpContext.Visibility = Visibility.Visible;

        switch (mode)
        {
            case AppMode.QuickFix:
                PanelTones.Visibility = Visibility.Visible;
                UpdateDraftLabelsForModeAndTone();
                BtnViewText.Content = "Text";
                BtnViewDiff.Content = "Diff";
                PanelDiffSwitch.Visibility = Visibility.Visible;
                RtfDiff.Document.Blocks.Clear();
                if (!string.IsNullOrWhiteSpace(_quickFixResultBuffer))
                {
                    RenderVisualDiff(_replyDraftBuffer, _quickFixResultBuffer);
                }
                else
                {
                    SetDiffView(false);
                }
                break;

            case AppMode.Brainstorm:
                PanelTones.Visibility = Visibility.Visible;
                UpdateDraftLabelsForModeAndTone();
                BtnViewText.Content = "Text";
                BtnViewDiff.Content = "Styled";
                PanelDiffSwitch.Visibility = Visibility.Visible;
                RtfDiff.Document.Blocks.Clear();
                if (!string.IsNullOrWhiteSpace(_brainstormResultBuffer))
                {
                    RenderMarkdownView(_brainstormResultBuffer);
                }
                else
                {
                    SetDiffView(false);
                }
                break;

            case AppMode.DetailedReview:
                PanelTones.Visibility = Visibility.Visible;
                UpdateDraftLabelsForModeAndTone();
                BtnViewText.Content = "Raw";
                BtnViewDiff.Content = "Styled";
                PanelDiffSwitch.Visibility = Visibility.Visible;
                RtfDiff.Document.Blocks.Clear();
                if (!string.IsNullOrWhiteSpace(_detailedResultBuffer))
                {
                    RenderMarkdownView(_detailedResultBuffer);
                }
                else
                {
                    SetDiffView(false);
                }
                break;

            case AppMode.Summarize:
                PanelTones.Visibility = Visibility.Collapsed;
                LblInputTitle.Text = "Customer's message";
                TxtDraftWatermark.Text = "Paste the customer's verbose inquiry or ticket to summarize...";
                TxtImproveBtn.Text = "Summarize";
                BtnViewText.Content = "Raw";
                BtnViewDiff.Content = "Styled";
                PanelDiffSwitch.Visibility = Visibility.Visible;
                RtfDiff.Document.Blocks.Clear();
                if (!string.IsNullOrWhiteSpace(_summarizeResultBuffer))
                {
                    RenderMarkdownView(_summarizeResultBuffer);
                }
                else
                {
                    SetDiffView(false);
                }
                break;
        }
    }

    private void BtnTone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toneStr && Enum.TryParse<TonePreset>(toneStr, out var tone))
        {
            _currentTone = tone;
            UpdateToneHighlights();
            UpdateDraftLabelsForModeAndTone();
        }
    }

    private void UpdateToneHighlights()
    {
        HighlightTone(BdToneStandard, TxtToneStandard, _currentTone == TonePreset.Standard);
        HighlightTone(BdToneFriendly, TxtToneFriendly, _currentTone == TonePreset.Friendly);
        HighlightTone(BdToneConcise, TxtToneConcise, _currentTone == TonePreset.Concise);
        HighlightTone(BdToneDeescalate, TxtToneDeescalate, _currentTone == TonePreset.Deescalate);
        HighlightTone(BdToneFormal, TxtToneFormal, _currentTone == TonePreset.Formal);
        HighlightTone(BdToneRephrase, TxtToneRephrase, _currentTone == TonePreset.Rephrase);
        HighlightTone(BdToneInternal, TxtToneInternal, _currentTone == TonePreset.Internal);
    }

    private void UpdateDraftLabelsForModeAndTone()
    {
        if (_currentMode == AppMode.Summarize) return;

        if (_currentMode == AppMode.Brainstorm)
        {
            if (_currentTone == TonePreset.Internal)
            {
                LblInputTitle.Text = "Rough Internal Thoughts (Brainstorm)";
                TxtDraftWatermark.Text = "Write rough findings, shorthand, or chaotic notes in your own words — AI will structure them into a clear note for colleagues...";
                TxtImproveBtn.Text = "Structure Internal";
            }
            else
            {
                LblInputTitle.Text = "Rough Thoughts / Key Points (Brainstorm)";
                TxtDraftWatermark.Text = "Write roughly in your own words what needs to be explained to the user — AI will break it down step by step...";
                TxtImproveBtn.Text = "Structure Reply";
            }
            return;
        }

        if (_currentTone == TonePreset.Internal)
        {
            LblInputTitle.Text = "Internal Note / Colleague Message";
            TxtDraftWatermark.Text = "Type or paste your internal ticket note or message to a colleague...";
            TxtImproveBtn.Text = _currentMode == AppMode.DetailedReview ? "Review Internal" : "Polish Internal";
        }
        else if (_currentTone == TonePreset.Rephrase && _currentMode == AppMode.QuickFix)
        {
            LblInputTitle.Text = "Draft to Rephrase";
            TxtDraftWatermark.Text = "Paste the reply you want to reword with fresh phrasing so it doesn't look repeated...";
            TxtImproveBtn.Text = "Rephrase";
        }
        else
        {
            LblInputTitle.Text = "Draft";
            TxtDraftWatermark.Text = "Type or paste your customer reply draft here...";
            TxtImproveBtn.Text = _currentMode == AppMode.DetailedReview ? "Review" : "Improve";
        }
    }

    private void HighlightTone(Border bd, TextBlock tb, bool isSelected)
    {
        if (isSelected)
        {
            bd.Background = (SolidColorBrush)FindResource("AccentActiveBrush");
            bd.BorderBrush = (SolidColorBrush)FindResource("AccentBrush");
            tb.Foreground = (SolidColorBrush)FindResource("AccentBrush");
            tb.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            bd.Background = (SolidColorBrush)FindResource("BgPillBrush");
            bd.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
            tb.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
            tb.FontWeight = FontWeights.Normal;
        }
    }

    #endregion

    #region Settings & Provider Configuration Modal

    private LlmProvider _modalSelectedProvider = LlmProvider.FastFlowLmNpu;

    private void BtnSettingsConfig_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsModal(showServerTab: true);
    }

    private void BtnPromptsConfig_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsModal(showServerTab: false);
    }

    public void OpenSettingsModal(bool showServerTab = true)
    {
        _modalSelectedProvider = _settingsService.Settings.Provider;
        UpdateProviderCardsHighlight();

        TxtSettingsServerUrl.Text = _settingsService.Settings.GetEffectiveBaseUrl();
        TxtSettingsApiKey.Text = _settingsService.Settings.CustomApiKey;
        TxtTestConnectionResult.Text = "";

        _editingPromptMode = _currentMode;
        UpdatePromptTabsStyling();
        TxtPromptEditor.Text = _settingsService.Settings.GetSystemPrompt(_editingPromptMode);

        SetSettingsActiveTab(showServerTab);
        OverlaySettings.Visibility = Visibility.Visible;
    }

    private void SetSettingsActiveTab(bool isServerTab)
    {
        if (isServerTab)
        {
            PanelSettingsServer.Visibility = Visibility.Visible;
            PanelSettingsPrompts.Visibility = Visibility.Collapsed;
            BtnResetPrompt.Visibility = Visibility.Collapsed;
            UpdateSegmentButton(BtnSettingsTabServer, true);
            UpdateSegmentButton(BtnSettingsTabPrompts, false);
            TxtSettingsSubheader.Text = " (Server & AI Backend)";
        }
        else
        {
            PanelSettingsServer.Visibility = Visibility.Collapsed;
            PanelSettingsPrompts.Visibility = Visibility.Visible;
            BtnResetPrompt.Visibility = Visibility.Visible;
            UpdateSegmentButton(BtnSettingsTabServer, false);
            UpdateSegmentButton(BtnSettingsTabPrompts, true);
            TxtSettingsSubheader.Text = " (System Prompts)";
            TxtPromptEditor.Focus();
        }
    }

    private void BtnSettingsTabServer_Click(object sender, RoutedEventArgs e)
    {
        SetSettingsActiveTab(true);
    }

    private void BtnSettingsTabPrompts_Click(object sender, RoutedEventArgs e)
    {
        SetSettingsActiveTab(false);
    }

    private void BtnProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<LlmProvider>(tag, out var provider))
        {
            _modalSelectedProvider = provider;
            UpdateProviderCardsHighlight();

            // Suggest appropriate URL if currently matching default
            if (provider == LlmProvider.LmStudio && (string.IsNullOrWhiteSpace(TxtSettingsServerUrl.Text) || TxtSettingsServerUrl.Text.Contains("52625")))
            {
                TxtSettingsServerUrl.Text = _settingsService.Settings.LmStudioUrl;
            }
            else if (provider == LlmProvider.FastFlowLmNpu && (string.IsNullOrWhiteSpace(TxtSettingsServerUrl.Text) || TxtSettingsServerUrl.Text.Contains("1234")))
            {
                TxtSettingsServerUrl.Text = _settingsService.Settings.FastFlowLmUrl;
            }
            else if (provider == LlmProvider.Custom && string.IsNullOrWhiteSpace(TxtSettingsServerUrl.Text))
            {
                TxtSettingsServerUrl.Text = _settingsService.Settings.CustomUrl;
            }
        }
    }

    private void UpdateProviderCardsHighlight()
    {
        var accentBrush = (SolidColorBrush)FindResource("AccentBrush");
        var borderBrush = (SolidColorBrush)FindResource("BorderBrush");

        BdProviderCardNpu.BorderBrush = _modalSelectedProvider == LlmProvider.FastFlowLmNpu ? accentBrush : borderBrush;
        BdProviderCardLmStudio.BorderBrush = _modalSelectedProvider == LlmProvider.LmStudio ? accentBrush : borderBrush;
        BdProviderCardCustom.BorderBrush = _modalSelectedProvider == LlmProvider.Custom ? accentBrush : borderBrush;
    }

    private void BtnPresetUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url)
        {
            TxtSettingsServerUrl.Text = url;
        }
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
        BtnTestConnection.IsEnabled = false;
        TxtTestConnectionResult.Foreground = (SolidColorBrush)FindResource("AccentBrush");
        TxtTestConnectionResult.Text = "Testing connection to endpoint...";

        try
        {
            var res = await FlmApiService.TestConnectionAsync(TxtSettingsServerUrl.Text.Trim(), TxtSettingsApiKey.Text.Trim());
            if (res.Success)
            {
                TxtTestConnectionResult.Foreground = (SolidColorBrush)FindResource("GreenBrush");
                TxtTestConnectionResult.Text = res.Message;
            }
            else
            {
                TxtTestConnectionResult.Foreground = (SolidColorBrush)FindResource("RedBrush");
                TxtTestConnectionResult.Text = res.Message;
            }
        }
        finally
        {
            BtnTestConnection.IsEnabled = true;
        }
    }

    private async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Settings.Provider = _modalSelectedProvider;

        var url = TxtSettingsServerUrl.Text.Trim();
        switch (_modalSelectedProvider)
        {
            case LlmProvider.LmStudio:
                _settingsService.Settings.LmStudioUrl = string.IsNullOrWhiteSpace(url) ? "http://localhost:1234" : url;
                break;
            case LlmProvider.FastFlowLmNpu:
                _settingsService.Settings.FastFlowLmUrl = string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:52625" : url;
                break;
            case LlmProvider.Custom:
                _settingsService.Settings.CustomUrl = string.IsNullOrWhiteSpace(url) ? "http://localhost:11434" : url;
                break;
        }

        _settingsService.Settings.CustomApiKey = TxtSettingsApiKey.Text.Trim();
        _settingsService.Settings.SetSystemPrompt(_editingPromptMode, TxtPromptEditor.Text);
        _settingsService.Save();

        _flmService.ConfigureProvider(_modalSelectedProvider, _settingsService.Settings.GetEffectiveBaseUrl(), _settingsService.Settings.CustomApiKey);
        _metricsService.SetProvider(_modalSelectedProvider);
        UpdateProviderUi(_modalSelectedProvider);

        OverlaySettings.Visibility = Visibility.Collapsed;

        await RefreshModelsAndServerAsync();

        ShowNotificationBanner($"✓ Settings saved. Active provider: {_modalSelectedProvider}", true);
        TxtStatus.Text = $"Active provider: {_modalSelectedProvider}";
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        OverlaySettings.Visibility = Visibility.Collapsed;
    }

    private void BtnPromptTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modeStr && Enum.TryParse<AppMode>(modeStr, out var mode))
        {
            // Temporarily update current tab text in memory
            _settingsService.Settings.SetSystemPrompt(_editingPromptMode, TxtPromptEditor.Text);
            _editingPromptMode = mode;
            UpdatePromptTabsStyling();
            TxtPromptEditor.Text = _settingsService.Settings.GetSystemPrompt(_editingPromptMode);
        }
    }

    private void UpdatePromptTabsStyling()
    {
        HighlightPromptTab(BtnPromptTabQuickFix, _editingPromptMode == AppMode.QuickFix);
        HighlightPromptTab(BtnPromptTabBrainstorm, _editingPromptMode == AppMode.Brainstorm);
        HighlightPromptTab(BtnPromptTabDetailed, _editingPromptMode == AppMode.DetailedReview);
        HighlightPromptTab(BtnPromptTabSummarize, _editingPromptMode == AppMode.Summarize);
    }

    private void HighlightPromptTab(Button btn, bool isSelected)
    {
        if (isSelected)
        {
            btn.Background = (SolidColorBrush)FindResource("BgSegmentActiveBrush");
            btn.Foreground = (SolidColorBrush)FindResource("AccentBrush");
            btn.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
            btn.FontWeight = FontWeights.Normal;
        }
    }

    private void BtnResetPrompt_Click(object sender, RoutedEventArgs e)
    {
        TxtPromptEditor.Text = AppSettings.GetDefaultPrompt(_editingPromptMode);
    }

    private void BtnSavePrompts_Click(object sender, RoutedEventArgs e) => BtnSaveSettings_Click(sender, e);
    private void BtnClosePrompts_Click(object sender, RoutedEventArgs e) => BtnCloseSettings_Click(sender, e);

    #endregion

    #region Provider Switcher Menu

    private void BtnProviderMenu_Click(object sender, RoutedEventArgs e)
    {
        MenuProvider.PlacementTarget = BtnProviderMenu;
        MenuProvider.IsOpen = true;
    }

    private async void MenuProviderNpu_Click(object sender, RoutedEventArgs e)
    {
        await SwitchProviderAsync(LlmProvider.FastFlowLmNpu);
    }

    private async void MenuProviderLmStudio_Click(object sender, RoutedEventArgs e)
    {
        await SwitchProviderAsync(LlmProvider.LmStudio);
    }

    private async void MenuProviderCustom_Click(object sender, RoutedEventArgs e)
    {
        await SwitchProviderAsync(LlmProvider.Custom);
    }

    private void MenuProviderSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsModal(showServerTab: true);
    }

    public async Task SwitchProviderAsync(LlmProvider provider)
    {
        if (_isGenerating || _isStartingServer) return;

        _settingsService.Settings.Provider = provider;
        _flmService.ConfigureProvider(provider, _settingsService.Settings.GetEffectiveBaseUrl(), _settingsService.Settings.CustomApiKey);
        _metricsService.SetProvider(provider);
        _settingsService.Save();

        UpdateProviderUi(provider);
        TxtStatus.Text = $"Switched provider to {provider}...";

        await RefreshModelsAndServerAsync();

        var provName = provider switch
        {
            LlmProvider.LmStudio => "LM Studio (GPU / Desktop PC)",
            LlmProvider.FastFlowLmNpu => "FastFlowLM (AMD Ryzen AI NPU)",
            _ => "Custom Server"
        };
        ShowNotificationBanner($"✓ Switched AI backend to {provName}.", true);
    }

    #endregion

    #region Input Actions & Transformations

    private static int CountWords(ReadOnlySpan<char> span)
    {
        int count = 0;
        bool inWord = false;
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }

    private void TxtDraft_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TxtDraft.Text;
        TxtDraftWatermark.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

        int words = CountWords(text.AsSpan());
        TxtInputCount.Text = $" words · {text.Length} chars";

        // Keep mode-specific buffer in sync
        if (_currentMode == AppMode.Summarize)
        {
            _summarizeDraftBuffer = text;
        }
        else
        {
            _replyDraftBuffer = text;
        }
    }

    private void TxtResult_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtResultCount == null) return;
        var text = TxtResult.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            TxtResultCount.Text = string.Empty;
            return;
        }
        int words = CountWords(text.AsSpan());
        TxtResultCount.Text = $" words · {text.Length} chars";
    }
    private void TxtContext_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateContextVisualState();
    }

    private void UpdateContextVisualState()
    {
        if (TxtContext == null || BadgeContextActive == null || BdContextContainer == null || LblContextHeader == null)
            return;

        var raw = TxtContext.Text;
        bool hasContext = !string.IsNullOrWhiteSpace(raw);

        if (TxtContextWatermark != null)
        {
            TxtContextWatermark.Visibility = string.IsNullOrEmpty(raw) ? Visibility.Visible : Visibility.Collapsed;
        }

        BadgeContextActive.Visibility = hasContext ? Visibility.Visible : Visibility.Collapsed;
        TxtContextPreview.Visibility = hasContext ? Visibility.Visible : Visibility.Collapsed;
        BtnHeaderContextClear.Visibility = hasContext ? Visibility.Visible : Visibility.Collapsed;

        if (hasContext)
        {
            var trimmed = raw.Trim();
            int wordCount = CountWords(trimmed.AsSpan());
            TxtContextBadge.Text = $"● Active ({wordCount} {(wordCount == 1 ? "word" : "words")})";

            var singleLine = trimmed.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            if (singleLine.Length > 60)
            {
                singleLine = singleLine.Substring(0, 60) + "…";
            }
            TxtContextPreview.Text = $"— \"{singleLine}\"";

            if (_isLightThemeEffective)
            {
                BdContextContainer.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244)); // #F0FDF4
                BdContextContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));   // #16A34A
                BdContextContainer.BorderThickness = new Thickness(1.2);
                LblContextHeader.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));      // #15803D
                LblContextHeader.FontWeight = FontWeights.SemiBold;
                BadgeContextActive.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));  // #DCFCE7
                BadgeContextActive.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtContextBadge.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
            }
            else
            {
                BdContextContainer.Background = new SolidColorBrush(Color.FromRgb(18, 33, 28));
                BdContextContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                BdContextContainer.BorderThickness = new Thickness(1.2);
                LblContextHeader.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                LblContextHeader.FontWeight = FontWeights.SemiBold;
                BadgeContextActive.Background = new SolidColorBrush(Color.FromRgb(20, 53, 37));
                BadgeContextActive.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                TxtContextBadge.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
            }
        }
        else
        {
            TxtContextPreview.Text = string.Empty;
            BdContextContainer.Background = new SolidColorBrush(Colors.Transparent);
            BdContextContainer.BorderBrush = new SolidColorBrush(Colors.Transparent);
            BdContextContainer.BorderThickness = new Thickness(1);
            LblContextHeader.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
            LblContextHeader.FontWeight = FontWeights.Normal;
        }
    }

    private void BtnDraftPaste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            TxtDraft.Text = Clipboard.GetText();
            TxtDraft.CaretIndex = TxtDraft.Text.Length;
        }
    }

    private void BtnDraftClear_Click(object sender, RoutedEventArgs e)
    {
        // Clears both Draft AND Result for current mode!
        TxtDraft.Clear();
        TxtResult.Clear();
        RtfDiff.Document.Blocks.Clear();
        _accumulatedResult = string.Empty;
        TxtStats.Text = string.Empty;
        TxtHeaderStats.Text = string.Empty;
        TxtResultEmpty.Text = string.Empty;
        TxtResultEmpty.Visibility = Visibility.Visible;
        BtnViewDiff.Content = _currentMode == AppMode.QuickFix ? "Diff" : "Styled";
        SetDiffView(false);
        TxtStatus.Text = "Cleared";

        if (_currentMode == AppMode.Summarize)
        {
            _summarizeDraftBuffer = string.Empty;
            _summarizeResultBuffer = string.Empty;
        }
        else
        {
            _replyDraftBuffer = string.Empty;
            _quickFixResultBuffer = string.Empty;
            _brainstormResultBuffer = string.Empty;
            _detailedResultBuffer = string.Empty;
        }

        TxtDraft.Focus();
    }

    private void BtnContextPaste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            TxtContext.Text = Clipboard.GetText();
        }
    }

    private void BtnContextClear_Click(object sender, RoutedEventArgs e)
    {
        TxtContext.Clear();
    }

    private void BtnTransform_Click(object sender, RoutedEventArgs e)
    {
        MenuTransform.PlacementTarget = BtnTransform;
        MenuTransform.IsOpen = true;
    }

    private void ApplyTransform(Func<string, string> transformFunc)
    {
        if (string.IsNullOrEmpty(TxtDraft.Text)) return;

        var transformed = transformFunc(TxtDraft.Text);
        TxtDraft.Text = transformed;
        TxtResult.Text = transformed;
        _accumulatedResult = transformed;
        TxtResultEmpty.Text = string.Empty;
        TxtResultEmpty.Visibility = Visibility.Collapsed;

        if (_currentMode == AppMode.Summarize)
        {
            _summarizeDraftBuffer = transformed;
            _summarizeResultBuffer = transformed;
        }
        else if (_currentMode == AppMode.DetailedReview)
        {
            _replyDraftBuffer = transformed;
            _detailedResultBuffer = transformed;
        }
        else if (_currentMode == AppMode.Brainstorm)
        {
            _replyDraftBuffer = transformed;
            _brainstormResultBuffer = transformed;
        }
        else
        {
            _replyDraftBuffer = transformed;
            _quickFixResultBuffer = transformed;
        }

        if (ChkAutoCopy.IsChecked == true)
        {
            Clipboard.SetText(transformed);
            TxtStatus.Text = "Transformed & copied to clipboard";
        }
    }
    private void MenuTransform_UpperCase(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.ToUpperCase);
    private void MenuTransform_LowerCase(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.ToLowerCase);
    private void MenuTransform_Capitalize(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.CapitalizeWords);
    private void MenuTransform_SentenceCase(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.ToSentenceCase);
    private void MenuTransform_CleanSpaces(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.CleanExtraSpaces);
    private void MenuTransform_StraightenQuotes(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.StraightenQuotes);
    private void MenuTransform_RemoveLineBreaks(object sender, RoutedEventArgs e) => ApplyTransform(TextTransformService.RemoveLineBreaks);

    #endregion

    #region Execution & Streaming

    private async void BtnImprove_Click(object sender, RoutedEventArgs e)
    {
        await RunImproveAsync();
    }

    private async Task RunImproveAsync()
    {
        if (_isGenerating) return;

        var draftText = TxtDraft.Text.Trim();
        if (string.IsNullOrWhiteSpace(draftText))
        {
            TxtStatus.Text = "Please enter draft text first.";
            return;
        }

        if (CmbModels.SelectedItem is not FlmModelInfo model)
        {
            TxtStatus.Text = "Please select a model.";
            return;
        }

        var isNpu = _flmService.CurrentProvider == LlmProvider.FastFlowLmNpu;
        var devName = isNpu ? "NPU" : "GPU";

        if (isNpu)
        {
            // Seamless on-the-fly model launch / switch if not already running this model
            bool isOnline = await _flmService.CheckServerHealthAsync();
            var activeModel = _flmService.ActiveModel ?? _flmService.DetectRunningModel();

            if (!isOnline || !string.Equals(activeModel, model.ModelId, StringComparison.OrdinalIgnoreCase))
            {
                _isStartingServer = true;
                UpdateServerStatusUi(ServerState.Starting, !isOnline
                    ? $"Starting FLM with {model.DisplayName}..."
                    : $"Switching model to {model.DisplayName} (~15s)...");

                ShowNotificationBanner(!isOnline
                    ? $"Starting FastFlowLM with {model.DisplayName}..."
                    : $"Switching NPU model to {model.DisplayName} (~15s)...", true);

                var serverProgress = new Progress<string>(msg => TxtStatus.Text = msg);
                bool ready = await _flmService.StartServerAsync(model.ModelId, serverProgress);
                _isStartingServer = false;

                if (!ready)
                {
                    TxtStatus.Text = $"Failed to start server with {model.DisplayName}";
                    UpdateServerStatusUi(ServerState.Offline);
                    ShowNotificationBanner($"Failed to start FLM with {model.DisplayName}", false);
                    return;
                }

                UpdateServerStatusUi(ServerState.Online, $"Server ready with {model.DisplayName}");
            }
        }
        else
        {
            // LM Studio / Custom: server runs externally, verify health
            bool isOnline = await _flmService.CheckServerHealthAsync();
            if (!isOnline)
            {
                var provName = _flmService.CurrentProvider == LlmProvider.LmStudio ? "LM Studio" : "Local Server";
                TxtStatus.Text = $"Cannot connect to {provName} at {_flmService.CurrentBaseUrl}. Start server first.";
                UpdateServerStatusUi(ServerState.Offline);
                ShowNotificationBanner($"⚠ {provName} is offline. Start the server in LM Studio on port 1234.", false);
                return;
            }
            UpdateServerStatusUi(ServerState.Online, $"{_flmService.CurrentProvider} online");
        }

        // Prepare UI state
        _isGenerating = true;
        _metricsService.SetGenerating(true);
        UpdateSystemMetricsUI();
        BtnImprove.Visibility = Visibility.Collapsed;
        BtnStop.Visibility = Visibility.Visible;
        TxtResult.Clear();
        TxtResultEmpty.Visibility = Visibility.Visible;
        TxtResultEmpty.Text = $"⚡ Generating response on {devName}... ▋";
        TxtStats.Text = "";
        TxtHeaderStats.Text = "⚡ Generating...";
        TxtStatus.Text = $"Generating response on {devName}...";
        _accumulatedResult = string.Empty;

        SetDiffView(false);

        // Build System Prompt
        string systemPrompt;
        if (_currentMode == AppMode.QuickFix && _currentTone == TonePreset.Rephrase)
        {
            systemPrompt = AppSettings.DefaultRephrasePrompt;
        }
        else if (_currentMode == AppMode.QuickFix && _currentTone == TonePreset.Internal)
        {
            systemPrompt = AppSettings.DefaultInternalPrompt;
        }
        else if (_currentMode == AppMode.Brainstorm && _currentTone == TonePreset.Internal)
        {
            systemPrompt = AppSettings.DefaultInternalBrainstormPrompt;
        }
        else if (_currentMode == AppMode.DetailedReview && _currentTone == TonePreset.Internal)
        {
            systemPrompt = AppSettings.DefaultInternalDetailedReviewPrompt;
        }
        else
        {
            systemPrompt = _settingsService.Settings.GetSystemPrompt(_currentMode);

            if (_currentMode != AppMode.Summarize)
            {
                var toneInstruction = _currentTone switch
                {
                    TonePreset.Friendly => "\n\nTone: Ensure a warm, approachable, empathetic, and friendly customer support tone.",
                    TonePreset.Concise => "\n\nTone: Be as concise, direct, and efficient as possible, removing unnecessary words while keeping key details.",
                    TonePreset.Deescalate => "\n\nTone: De-escalate customer frustration using calm, reassuring, highly empathetic, and respectful language without admitting unverified fault.",
                    TonePreset.Formal => "\n\nTone: Maintain a formal, strictly professional, and polished business tone.",
                    TonePreset.Rephrase => "\n\nTone: Rephrase using fresh wording and varied sentence structure so the reply does not look like a copy-paste of a previous message.",
                    _ => ""
                };
                systemPrompt += toneInstruction;
            }
        }

        if (_selectedThinkingOption == ThinkingOption.Off)
        {
            systemPrompt += "\n\nImportant: Do not output internal reasoning, thought process, or thought tags. Provide the final response directly.";
        }
        else if (_selectedThinkingOption == ThinkingOption.On)
        {
            systemPrompt += "\n\nKeep your internal thinking concise (under 150 words), then output the final rewritten text.";
        }

        // Build User Prompt (context & specific processing instructions supported across all modes)
        string userPrompt;
        var contextText = TxtContext.Text.Trim();

        if (!string.IsNullOrWhiteSpace(contextText))
        {
            userPrompt = $"<context>\n{contextText}\n</context>\n\n<draft>\n{draftText}\n</draft>";
        }
        else
        {
            userPrompt = $"<draft>\n{draftText}\n</draft>";
        }

        _streamCts = new CancellationTokenSource();

        // Start typing animation cursor (blinks every 350ms while writing)
        _cursorVisible = true;
        _isThinking = false;
        _thinkingChars = 0;
        _typingAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _typingAnimationTimer.Tick += (s, ev) =>
        {
            if (_isGenerating)
            {
                _cursorVisible = !_cursorVisible;
                if (!string.IsNullOrEmpty(_accumulatedResult))
                {
                    TxtResultEmpty.Visibility = Visibility.Collapsed;
                    TxtResult.Text = _accumulatedResult + (_cursorVisible ? " ▋" : "  ");
                }
                else if (_isThinking)
                {
                    TxtResultEmpty.Visibility = Visibility.Visible;
                    TxtResultEmpty.Text = $"🧠 Thinking... ({_thinkingChars} chars)" + (_cursorVisible ? " ▋" : "  ");
                }
                else
                {
                    TxtResultEmpty.Visibility = Visibility.Visible;
                    TxtResultEmpty.Text = _cursorVisible ? $"⚡ Generating response on {devName}... ▋" : $"⚡ Generating response on {devName}...  ";
                }
            }
        };
        _typingAnimationTimer.Start();

        var progress = new Progress<StreamChunkEventArgs>(args =>
        {
            if (!string.IsNullOrEmpty(args.StatusMessage))
            {
                TxtStatus.Text = args.StatusMessage;
                if (string.IsNullOrEmpty(_accumulatedResult))
                {
                    TxtResultEmpty.Visibility = Visibility.Visible;
                    TxtResultEmpty.Text = $"{args.StatusMessage} ▋";
                }
            }

            if (args.IsThinking)
            {
                _isThinking = true;
                _thinkingChars = args.ThinkingCharCount;
                TxtStatus.Text = string.IsNullOrEmpty(args.StatusMessage) ? $"Thinking... ({args.ThinkingCharCount} chars)" : args.StatusMessage;
                TxtHeaderStats.Text = $"Thinking... ({args.ThinkingCharCount} chars)";
                if (string.IsNullOrEmpty(_accumulatedResult))
                {
                    TxtResultEmpty.Visibility = Visibility.Visible;
                    TxtResultEmpty.Text = $"🧠 Thinking... ({args.ThinkingCharCount} chars) ▋";
                }
            }
            else if (!string.IsNullOrEmpty(args.Content))
            {
                _isThinking = false;
                TxtResultEmpty.Visibility = Visibility.Collapsed;
                TxtStatus.Text = _currentMode == AppMode.Summarize ? "Summarizing..." : "Writing...";
                _accumulatedResult += args.Content;

                // Throttle UI layout & ScrollToEnd updates to ~30 FPS during high-speed streaming
                long nowTicks = Environment.TickCount64;
                if (nowTicks - _lastUiStreamUpdateMs >= 33)
                {
                    _lastUiStreamUpdateMs = nowTicks;
                    TxtResult.Text = _accumulatedResult + " ▋";
                    TxtResult.ScrollToEnd();
                }
            }

            if (args.IsDone && args.Stats != null)
            {
                string summary = args.Stats.FormatSummary();
                TxtHeaderStats.Text = summary;
                TxtStats.Text = summary;
            }
        });

        try
        {
            if (_flmService.CurrentProvider == LlmProvider.LmStudio)
            {
                var lmProgress = new Progress<string>(msg =>
                {
                    TxtStatus.Text = msg;
                    if (string.IsNullOrEmpty(_accumulatedResult))
                    {
                        TxtResultEmpty.Visibility = Visibility.Visible;
                        TxtResultEmpty.Text = $"{msg} ▋";
                    }
                });

                await _flmService.EnsureLmStudioModelReadyAsync(model.ModelId, lmProgress, _streamCts.Token);
            }

            double requestTemp = (_currentMode != AppMode.Summarize && _currentTone == TonePreset.Rephrase)
                ? 0.55
                : (_currentMode == AppMode.Brainstorm ? 0.3 : 0.2);

            await Task.Run(async () =>
            {
                await _flmService.StreamCompletionAsync(
                    model.ModelId,
                    systemPrompt,
                    userPrompt,
                    _selectedThinkingOption,
                    progress,
                    _streamCts.Token,
                    requestTemp
                ).ConfigureAwait(false);
            }, _streamCts.Token);

            _typingAnimationTimer?.Stop();
            _typingAnimationTimer = null;

            var cleanedOutput = SmartCopyService.CleanArtifacts(_accumulatedResult);
            if (string.IsNullOrWhiteSpace(cleanedOutput))
            {
                TxtResultEmpty.Visibility = Visibility.Visible;
                TxtResultEmpty.Text = "⚠ No rewritten text produced. For writing tools, setting Thinking to 'Off' is recommended.";
                TxtStatus.Text = "Generation complete (no text produced)";
                TxtResult.Clear();
            }
            else
            {
                TxtResultEmpty.Text = string.Empty;
                TxtResultEmpty.Visibility = Visibility.Collapsed;
                TxtResult.Text = cleanedOutput;
                TxtResult.ScrollToEnd();
                _accumulatedResult = cleanedOutput;
                TxtStatus.Text = "Generation complete";
            }

            if (_currentMode == AppMode.Summarize)
            {
                _summarizeResultBuffer = cleanedOutput;
            }
            else if (_currentMode == AppMode.DetailedReview)
            {
                _detailedResultBuffer = cleanedOutput;
            }
            else if (_currentMode == AppMode.Brainstorm)
            {
                _brainstormResultBuffer = cleanedOutput;
            }
            else
            {
                _quickFixResultBuffer = cleanedOutput;
            }

            if (_currentMode == AppMode.QuickFix)
            {
                RenderVisualDiff(draftText, cleanedOutput);
            }
            else
            {
                RenderMarkdownView(cleanedOutput);
            }

            var historyItem = new HistoryItem
            {
                Mode = _currentMode,
                OriginalText = draftText,
                ContextText = string.IsNullOrWhiteSpace(contextText) ? null : contextText,
                ResultText = cleanedOutput,
                StatsText = TxtHeaderStats.Text,
                ModelId = model.ModelId
            };
            _settingsService.AddHistory(historyItem);
            UpdateHistoryBadge();

            if (ChkAutoCopy.IsChecked == true && !string.IsNullOrWhiteSpace(cleanedOutput))
            {
                var textToCopy = SmartCopyService.GetSmartCopyText(cleanedOutput, _currentMode);
                Clipboard.SetText(textToCopy);
                TxtStatus.Text = "Complete & auto-copied to clipboard!";
            }
        }
        catch (OperationCanceledException)
        {
            _typingAnimationTimer?.Stop();
            _typingAnimationTimer = null;
            TxtResultEmpty.Text = string.Empty;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(_accumulatedResult) ? Visibility.Visible : Visibility.Collapsed;
            TxtResult.Text = _accumulatedResult;
            TxtStatus.Text = "Stopped by user";
            ShowNotificationBanner("Generation stopped by user.", false);
        }
        catch (Exception ex)
        {
            _typingAnimationTimer?.Stop();
            _typingAnimationTimer = null;
            TxtResultEmpty.Text = string.Empty;
            TxtResultEmpty.Visibility = string.IsNullOrEmpty(_accumulatedResult) ? Visibility.Visible : Visibility.Collapsed;
            TxtStatus.Text = $"Error: {ex.Message}";
            ShowNotificationBanner(ex.Message, false);
        }
        finally
        {
            _typingAnimationTimer?.Stop();
            _typingAnimationTimer = null;
            _isGenerating = false;
            _metricsService.SetGenerating(false);
            UpdateSystemMetricsUI();
            BtnImprove.Visibility = Visibility.Visible;
            BtnStop.Visibility = Visibility.Collapsed;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _streamCts?.Cancel();
    }

    #endregion

    #region Visual Diff

    private void RenderVisualDiff(string original, string modified)
    {
        var diffTokens = DiffService.ComputeDiff(original, modified);

        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 13,
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush")
        };

        // Standard line height to match TxtResult without vertical stretching
        var paragraph = new Paragraph { Margin = new Thickness(0) };

        // Theme-adaptive diff colors for crisp readability in both Dark and Light mode
        var addedBg = _isLightThemeEffective
            ? new SolidColorBrush(Color.FromArgb(55, 22, 163, 74))
            : new SolidColorBrush(Color.FromArgb(60, 34, 197, 94));
        var addedFg = _isLightThemeEffective
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
            : new SolidColorBrush(Color.FromRgb(74, 222, 128));

        var removedBg = _isLightThemeEffective
            ? new SolidColorBrush(Color.FromArgb(50, 220, 38, 38))
            : new SolidColorBrush(Color.FromArgb(60, 239, 68, 68));
        var removedFg = _isLightThemeEffective
            ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
            : new SolidColorBrush(Color.FromRgb(252, 165, 165));

        bool hasChanges = diffTokens.Any(t => t.Type == DiffTokenType.Added || t.Type == DiffTokenType.Removed);
        bool isIdentical = !hasChanges || string.Equals(original.Trim(), modified.Trim(), StringComparison.Ordinal);

        if (_currentMode == AppMode.QuickFix)
        {
            if (isIdentical && (!string.IsNullOrWhiteSpace(original) || !string.IsNullOrWhiteSpace(modified)))
            {
                BtnViewDiff.Content = "Diff (0)";
                BtnViewDiff.ToolTip = "No changes needed — the draft is already optimal and identical to result.";

                var banner = new Border
                {
                    Background = _isLightThemeEffective
                        ? new SolidColorBrush(Color.FromRgb(240, 253, 244))
                        : new SolidColorBrush(Color.FromArgb(40, 34, 197, 94)),
                    BorderBrush = _isLightThemeEffective
                        ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                        : new SolidColorBrush(Color.FromArgb(120, 34, 197, 94)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var stack = new StackPanel { Orientation = Orientation.Vertical };
                var headerText = new TextBlock
                {
                    Text = "✓ No changes needed — draft is already optimal!",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = addedFg
                };
                var subText = new TextBlock
                {
                    Text = "The generated result is 100% identical to your draft.",
                    FontSize = 11,
                    Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 3, 0, 0)
                };
                stack.Children.Add(headerText);
                stack.Children.Add(subText);
                banner.Child = stack;

                doc.Blocks.Add(new BlockUIContainer(banner));
            }
            else
            {
                int additions = diffTokens.Count(t => t.Type == DiffTokenType.Added);
                int removals = diffTokens.Count(t => t.Type == DiffTokenType.Removed);
                int totalChanges = additions + removals;
                BtnViewDiff.Content = totalChanges > 0 ? $"Diff ({totalChanges})" : "Diff";
                BtnViewDiff.ToolTip = totalChanges > 0 ? $"{totalChanges} difference(s) ({additions} added, {removals} removed)" : null;
            }
        }

        foreach (var token in diffTokens)
        {
            var run = new Run(token.Text);

            switch (token.Type)
            {
                case DiffTokenType.Added:
                    run.Foreground = addedFg;
                    run.Background = addedBg;
                    run.FontWeight = FontWeights.Medium;
                    break;

                case DiffTokenType.Removed:
                    run.Foreground = removedFg;
                    run.Background = removedBg;
                    run.TextDecorations = TextDecorations.Strikethrough;
                    break;

                case DiffTokenType.Unchanged:
                default:
                    run.Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush");
                    break;
            }

            paragraph.Inlines.Add(run);
        }

        doc.Blocks.Add(paragraph);
        RtfDiff.Document = doc;
    }

    private void RenderMarkdownView(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;
        var doc = MarkdownDocumentRenderer.Render(markdown, _isLightThemeEffective);
        RtfDiff.Document = doc;
        SetDiffView(true);
    }

    private void BtnViewText_Click(object sender, RoutedEventArgs e)
    {
        SetDiffView(false);
    }

    private void BtnViewDiff_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMode != AppMode.QuickFix)
        {
            var text = _currentMode switch
            {
                AppMode.Summarize => _summarizeResultBuffer,
                AppMode.Brainstorm => _brainstormResultBuffer,
                _ => _detailedResultBuffer
            };
            if (string.IsNullOrEmpty(text)) text = TxtResult.Text;
            if (!string.IsNullOrEmpty(text))
            {
                RenderMarkdownView(text);
                return;
            }
        }
        else
        {
            var orig = TxtDraft.Text;
            var mod = !string.IsNullOrEmpty(_accumulatedResult) ? _accumulatedResult : TxtResult.Text;
            if (!string.IsNullOrEmpty(orig) || !string.IsNullOrEmpty(mod))
            {
                RenderVisualDiff(orig, mod);
            }
        }
        SetDiffView(true);
    }
    private void SetDiffView(bool showDiff)
    {
        if (showDiff)
        {
            TxtResult.Visibility = Visibility.Collapsed;
            RtfDiff.Visibility = Visibility.Visible;
            UpdateSegmentButton(BtnViewDiff, true);
            UpdateSegmentButton(BtnViewText, false);
        }
        else
        {
            TxtResult.Visibility = Visibility.Visible;
            RtfDiff.Visibility = Visibility.Collapsed;
            UpdateSegmentButton(BtnViewText, true);
            UpdateSegmentButton(BtnViewDiff, false);
        }
    }

    #endregion

    #region Copy Actions

    private void BtnSmartCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = TxtResult.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var smartText = SmartCopyService.GetSmartCopyText(text, _currentMode);
        Clipboard.SetText(smartText);
        TxtStatus.Text = _currentMode == AppMode.DetailedReview
            ? "Copied CORRECTED section to clipboard"
            : "Copied result to clipboard";
    }

    #endregion

    #region History Drawer

    private void BtnHistory_Click(object sender, RoutedEventArgs e)
    {
        RenderHistoryList();
        OverlayHistory.Visibility = Visibility.Visible;
    }

    private void BtnCloseHistory_Click(object sender, RoutedEventArgs e)
    {
        OverlayHistory.Visibility = Visibility.Collapsed;
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ClearHistory();
        RenderHistoryList();
        UpdateHistoryBadge();
    }

    private void UpdateHistoryBadge()
    {
        TxtHistoryCount.Text = _settingsService.Settings.RecentHistory.Count.ToString();
    }

    private void RenderHistoryList()
    {
        ListHistoryItems.Children.Clear();
        var history = _settingsService.Settings.RecentHistory;

        if (history.Count == 0)
        {
            ListHistoryItems.Children.Add(new TextBlock
            {
                Text = "No history items yet.",
                Foreground = (SolidColorBrush)FindResource("TextMutedBrush"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(10)
            });
            return;
        }

        foreach (var item in history)
        {
            var card = new Border
            {
                Background = (SolidColorBrush)FindResource("BgDarkBrush"),
                BorderBrush = (SolidColorBrush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var contentStack = new StackPanel();

            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            topRow.Children.Add(new Border
            {
                Background = (SolidColorBrush)FindResource("AccentActiveBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                Child = new TextBlock
                {
                    Text = item.Mode.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)FindResource("AccentBrush")
                }
            });

            topRow.Children.Add(new TextBlock
            {
                Text = $"{item.FormattedTime} • {item.ModelId}",
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            contentStack.Children.Add(topRow);

            contentStack.Children.Add(new TextBlock
            {
                Text = $"Draft: \"{item.PreviewOriginal}\"",
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 2)
            });

            contentStack.Children.Add(new TextBlock
            {
                Text = $"Result: \"{item.PreviewResult}\"",
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            Grid.SetColumn(contentStack, 0);
            grid.Children.Add(contentStack);

            // Action Buttons
            var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnRestore = new Button
            {
                Content = "Restore",
                FontSize = 11,
                Style = (Style)FindResource("DarkPillButton"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            btnRestore.Click += (_, _) =>
            {
                SetMode(item.Mode);
                TxtDraft.Text = item.OriginalText;
                if (!string.IsNullOrEmpty(item.ContextText))
                {
                    TxtContext.Text = item.ContextText;
                    ExpContext.IsExpanded = true;
                }
                TxtResult.Text = item.ResultText;
                TxtStats.Text = item.StatsText;
                TxtHeaderStats.Text = item.StatsText;
                _accumulatedResult = item.ResultText;
                TxtResultEmpty.Text = string.Empty;
                TxtResultEmpty.Visibility = Visibility.Collapsed;
                if (item.Mode == AppMode.Summarize)
                    _summarizeResultBuffer = item.ResultText;
                else if (item.Mode == AppMode.DetailedReview)
                    _detailedResultBuffer = item.ResultText;
                else if (item.Mode == AppMode.Brainstorm)
                    _brainstormResultBuffer = item.ResultText;
                else
                    _quickFixResultBuffer = item.ResultText;

                if (item.Mode == AppMode.QuickFix)
                {
                    RenderVisualDiff(item.OriginalText, item.ResultText);
                }
                else
                {
                    RenderMarkdownView(item.ResultText);
                }
                OverlayHistory.Visibility = Visibility.Collapsed;
            };

            var btnCopy = new Button
            {
                Content = "Copy",
                FontSize = 11,
                Style = (Style)FindResource("DarkPillButton"),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand
            };
            btnCopy.Click += (_, _) =>
            {
                Clipboard.SetText(SmartCopyService.GetSmartCopyText(item.ResultText, item.Mode));
                TxtStatus.Text = "Copied item from history";
            };

            btnStack.Children.Add(btnRestore);
            btnStack.Children.Add(btnCopy);

            Grid.SetColumn(btnStack, 1);
            grid.Children.Add(btnStack);

            card.Child = grid;
            ListHistoryItems.Children.Add(card);
        }
    }

    #endregion
}