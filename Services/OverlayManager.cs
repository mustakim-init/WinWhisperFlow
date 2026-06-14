using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WinWhisperFlow.Services;

namespace WinWhisperFlow;

public sealed class OverlayManager : IDisposable
{
    private readonly AudioCaptureService _audio;
    private readonly object _gate = new();

    private OverlayWindow? _window;
    private bool _disposed;
    private readonly Dispatcher? _dispatcher;

    public OverlayManager(AudioCaptureService audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _dispatcher = Dispatcher.CurrentDispatcher;

        _audio.LevelChanged += OnLevelChanged;
    }

    public void ShowListening()
    {
        var w = GetOrCreateWindow();
        if (w is null) return;
        w.Dispatcher.Invoke(() => w.ShowListening());
    }

    public void ShowTranscribing()
    {
        var w = GetOrCreateWindow();
        if (w is null) return;
        w.Dispatcher.Invoke(() => w.ShowThinking());
    }

    public void ShowDone(string message)
    {
        var w = GetOrCreateWindow();
        if (w is null) return;
        w.Dispatcher.Invoke(() => w.ShowDone(message));
    }

    public void Hide()
    {
        OverlayWindow? w;
        lock (_gate) { w = _window; }

        if (w is null) return;
        w.Dispatcher.Invoke(() => w.HideOverlay());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _audio.LevelChanged -= OnLevelChanged;

        OverlayWindow? w;
        lock (_gate) { w = _window; }

        try
        {
            if (w is not null)
            {
                w.Dispatcher.Invoke(() => w.Close());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayManager.Dispose] {ex.Message}");
        }
    }

    private void OnLevelChanged(object? sender, float level)
    {
        OverlayWindow? w;
        lock (_gate) { w = _window; }

        if (w is null) return;

        try
        {
            w.Dispatcher.BeginInvoke(new Action(() => w.SetAudioLevel(level)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayManager.OnLevelChanged] {ex.Message}");
        }
    }

    private OverlayWindow? GetOrCreateWindow()
    {
        if (_disposed) return null;

        lock (_gate)
        {
            if (_window is not null) return _window;
        }

        if (_dispatcher is not null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => CreateWindowOnUiThread());
        }
        else
        {
            CreateWindowOnUiThread();
        }

        lock (_gate)
        {
            return _window;
        }
    }

    private void CreateWindowOnUiThread()
    {
        lock (_gate)
        {
            if (_window is not null) return;
            _window = new OverlayWindow();
        }
    }
}
