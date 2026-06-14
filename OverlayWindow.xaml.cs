using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace WinWhisperFlow;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private Storyboard? _storyboard;
    private System.Timers.Timer? _doneTimer;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PositionAboveTaskbar();
        };
    }

    public void ShowListening()
    {
        StopDoneTimer();
        OverlayText.Text = "Listening";
        DonePanel.Visibility = Visibility.Collapsed;
        BlobPanel.Visibility = Visibility.Visible;
        ShowOverlay();
    }

    public void ShowThinking()
    {
        StopDoneTimer();
        OverlayText.Text = "Transcribing";
        DonePanel.Visibility = Visibility.Collapsed;
        BlobPanel.Visibility = Visibility.Visible;
        ShowOverlay();
    }

    public void ShowDone(string message, int durationMs = 3500)
    {
        _storyboard?.Stop(this);
        DoneText.Text = message;
        BlobPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        PositionAboveTaskbar();
        Show();

        StopDoneTimer();
        _doneTimer = new System.Timers.Timer(durationMs) { AutoReset = false };
        _doneTimer.Elapsed += (_, _) =>
            Dispatcher.InvokeAsync(HideOverlay);
        _doneTimer.Start();
    }

    public void HideOverlay()
    {
        StopDoneTimer();
        _storyboard?.Stop(this);
        Hide();
    }

    public void SetAudioLevel(float level)
    {
        level = Math.Clamp(level, 0, 1);
        double primaryScale = 0.92 + level * 0.85;
        double secondaryScale = 0.9 + level * 0.55;
        double tertiaryScale = 0.95 + level * 0.7;

        Bubble1Scale.ScaleX = secondaryScale;
        Bubble1Scale.ScaleY = secondaryScale;
        Bubble2Scale.ScaleX = primaryScale;
        Bubble2Scale.ScaleY = primaryScale;
        Bubble3Scale.ScaleX = tertiaryScale;
        Bubble3Scale.ScaleY = tertiaryScale;
    }

    private void ShowOverlay()
    {
        PositionAboveTaskbar();
        Show();
        _storyboard ??= (Storyboard)FindResource("ListenAnim");
        _storyboard.Begin(this, true);
    }

    private void PositionAboveTaskbar()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 18;
    }

    private void ApplyOsdWindowStyles()
    {
        var helper = new WindowInteropHelper(this);
        IntPtr handle = helper.Handle;
        nint style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate | WsExTransparent);
    }

    private void StopDoneTimer()
    {
        if (_doneTimer is not null)
        {
            _doneTimer.Stop();
            _doneTimer.Dispose();
            _doneTimer = null;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyOsdWindowStyles();
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
