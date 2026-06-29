using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace WinWhisperFlow;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private System.Windows.Threading.DispatcherTimer? _doneTimer;
    private bool _webViewReady;
    private object? _pendingMessage;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string overlayDir = Path.GetFullPath(Path.Combine(baseDir, "WebUI"));
            if (!Directory.Exists(overlayDir))
                overlayDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "WebUI"));

            // Must be set BEFORE EnsureCoreWebView2Async for transparency to work
            OverlayWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);

            await OverlayWebView.EnsureCoreWebView2Async();

            OverlayWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            OverlayWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            OverlayWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            OverlayWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // Navigate to overlay.html via file path
            string overlayPath = Path.Combine(overlayDir, "overlay.html");
            if (File.Exists(overlayPath))
            {
                OverlayWebView.CoreWebView2.NavigationCompleted += (_, _) =>
                {
                    _webViewReady = true;
                    FlushPendingMessage();
                };
                OverlayWebView.CoreWebView2.Navigate(overlayPath);
            }

            PositionAboveTaskbar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Overlay WebView2 init failed: {ex.Message}");
            // Overlay is non-critical — transcription still works without it
        }
    }

    public void ShowListening()
    {
        StopDoneTimer();
        PostMessage(new { type = "overlay_state", state = "listening", text = "Listening" });
        Show();
    }

    public void ShowThinking()
    {
        StopDoneTimer();
        PostMessage(new { type = "overlay_state", state = "transcribing", text = "Transcribing..." });
        Show();
    }

    public void ShowDone(string message, int durationMs = 3500)
    {
        PostMessage(new { type = "overlay_state", state = "done", text = message });
        PositionAboveTaskbar();
        Show();

        StopDoneTimer();
        _doneTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _doneTimer.Tick += (_, _) => HideOverlay();
        _doneTimer.Start();
    }

    public void ShowError(string message)
    {
        StopDoneTimer();
        PostMessage(new { type = "overlay_state", state = "error", text = message });
        Show();

        _doneTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
        _doneTimer.Tick += (_, _) => HideOverlay();
        _doneTimer.Start();
    }

    public void HideOverlay()
    {
        StopDoneTimer();
        PostMessage(new { type = "overlay_state", state = "hidden" });
        Hide();
    }

    public void SetAudioLevel(float level)
    {
        PostMessage(new { type = "audio_level", level });
    }

    public void SetSpectrum(float[] bands)
    {
        PostMessage(new { type = "spectrum", bands });
    }

    private void PostMessage(object msg)
    {
        if (!_webViewReady)
        {
            _pendingMessage ??= msg;
            return;
        }
        try
        {
            string json = JsonSerializer.Serialize(msg);
            _ = OverlayWebView.Dispatcher.BeginInvoke(() =>
                OverlayWebView.CoreWebView2?.PostWebMessageAsJson(json));
        }
        catch { }
    }

    private void FlushPendingMessage()
    {
        if (_pendingMessage is null) return;
        var msg = _pendingMessage;
        _pendingMessage = null;
        PostMessage(msg);
    }

    private void PositionAboveTaskbar()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 18;
    }

    private void StopDoneTimer()
    {
        if (_doneTimer is not null)
        {
            _doneTimer.Stop();
            _doneTimer = null;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new WindowInteropHelper(this);
        IntPtr handle = helper.Handle;
        nint style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate | WsExTransparent);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopDoneTimer();
        base.OnClosed(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
