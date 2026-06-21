using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using WinWhisperFlow.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WinWhisperFlow;

public partial class MainWindow : Window
{
    private readonly WhisperBridgeService _whisperBridge = new();
    private readonly AudioCaptureService _audioCapture = new();
    private readonly GlobalHotkeyService _hotkeyService = new();
    private readonly RuntimeSetupService _runtimeSetup = new();
    private readonly StartupService _startupService = new();
    private readonly TextInjector _textInjector = new();
    private readonly PhoneMicService _phoneMic = new();
    private readonly TranscriptionHistory _transcriptionHistory = new();
    private UIBridge? _bridge;
    private OverlayManager? _overlay;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.Invoke(() => ApplyThemeToTitleBar());
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyThemeToTitleBar();
            CreateTrayIcon();
            AppendLog($"BaseDirectory: {AppContext.BaseDirectory}");

            string webviewData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinWhisperFlow", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, webviewData, new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-features=msWebView2PdfDownload"
            });
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.IsScriptEnabled = true;
            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
#if DEBUG
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            string distPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "WebUI", "dist"));
            if (!Directory.Exists(distPath))
                distPath = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "WebUI", "dist"));

            AppendLog($"Dist folder: {distPath}");

            string overlayPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "WebUI"));
            if (!Directory.Exists(overlayPath))
                overlayPath = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "WebUI"));

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.winwhisper", distPath, CoreWebView2HostResourceAccessKind.Allow);
            WebView.CoreWebView2.Navigate("https://app.winwhisper/index.html");

            WebView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                AppendLog($"Navigation completed: IsSuccess={args.IsSuccess}");
                if (!args.IsSuccess) return;

                _overlay = new OverlayManager(_audioCapture, WebView.CoreWebView2, overlayPath);
                _bridge = new UIBridge(
                    WebView, _whisperBridge, _audioCapture, _phoneMic,
                    _textInjector, _hotkeyService, _runtimeSetup,
                    _transcriptionHistory, _startupService, DetectWindowsDarkMode,
                    _overlay);
            };

            _hotkeyService.ToggleRequested += async (_, _) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    try { if (_bridge is not null) await _bridge.ToggleListeningAsync(); }
                    catch (Exception ex) { AppendLog($"Hotkey error: {ex.Message}"); }
                });
            };
            _hotkeyService.Start();
        }
        catch (Exception ex)
        {
            AppendLog($"Fatal startup error: {ex}");
            System.Windows.MessageBox.Show($"Startup failed:\n\n{ex}", "WinWhisper Flow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool DetectWindowsDarkMode()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is 0;
        }
        catch { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private void ApplyThemeToTitleBar()
    {
        bool isDark = DetectWindowsDarkMode();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int useImmersiveDarkMode = isDark ? 1 : 0;
        int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        if (result != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
    }

    private void AppendLog(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimePaths.LogPath)!);
            File.AppendAllText(RuntimePaths.LogPath, line + Environment.NewLine);
        }
        catch (Exception ex) { Debug.WriteLine($"[AppendLog] Failed to write log: {ex.Message}"); }
    }

    public void SetStartMinimized()
    {
        if (_notifyIcon is null)
            CreateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        if (_notifyIcon is not null) return;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() => { _isExiting = true; Close(); }));

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Icon.ico");
        Drawing.Icon trayIcon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : Drawing.SystemIcons.Application;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "WinWhisper Flow",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        WindowFocusService.TryActivate(handle);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting) { e.Cancel = true; Hide(); return; }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hotkeyService.Dispose();
        _bridge?.Dispose();
        _overlay?.Dispose();
        _whisperBridge.Dispose();
        _audioCapture.Dispose();
        _phoneMic.Dispose();
        if (_notifyIcon is not null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
    }
}
