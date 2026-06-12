using System;
using System.Threading;
using System.Windows;
using WinWhisperFlow.Services;

namespace WinWhisperFlow;

public sealed class OverlayManager : IDisposable
{
    private readonly AudioCaptureService _audio;
    private readonly object _gate = new();

    private OverlayWindow? _window;
    private bool _disposed;
    private readonly SynchronizationContext? _syncContext;

    public OverlayManager(AudioCaptureService audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _syncContext = SynchronizationContext.Current;

        _audio.LevelChanged += OnLevelChanged;
    }

    public void ShowListening()
    {
        EnsureWindow();
        _window!.Dispatcher.Invoke(() => _window.ShowListening());
    }

    public void ShowTranscribing()
    {
        EnsureWindow();
        _window!.Dispatcher.Invoke(() => _window.ShowThinking());
    }

    public void ShowDone(string message)
    {
        EnsureWindow();
        _window!.Dispatcher.Invoke(() => _window.ShowDone(message));
    }

    public void Hide()
    {
        OverlayWindow? w = null;
        lock (_gate)
        {
            w = _window;
        }

        if (w is null) return;
        w.Dispatcher.Invoke(() => w.HideOverlay());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _audio.LevelChanged -= OnLevelChanged;

        OverlayWindow? w = null;
        lock (_gate)
        {
            w = _window;
        }

        try
        {
            if (w is not null)
            {
                w.Dispatcher.Invoke(() => w.Close());
            }
        }
        catch
        {
            // ignore
        }
    }

    private void OnLevelChanged(object? sender, float level)
    {
        OverlayWindow? w;
        lock (_gate)
        {
            w = _window;
        }

        if (w is null) return;

        try
        {
            w.Dispatcher.BeginInvoke(new Action(() => w.SetAudioLevel(level)));
        }
        catch
        {
            // ignore
        }
    }

    private void EnsureWindow()
    {
        if (_disposed) return;

        lock (_gate)
        {
            if (_window is not null) return;

            // Must be created on UI thread
            _syncContext?.Post(_ =>
            {
                lock (_gate)
                {
                    _window ??= new OverlayWindow();
                }
            }, null);

            // If no sync context, create synchronously on current dispatcher.
            if (_syncContext is null)
            {
                _window = new OverlayWindow();
            }
        }

        // If created async via sync context, ensure it's created.
        if (_window is null)
        {
            // Best-effort wait without blocking UI thread too much
            int attempts = 10;
            while (_window is null && attempts-- > 0)
            {
                Thread.Sleep(10);
            }
            _window ??= new OverlayWindow();
        }
    }
}
