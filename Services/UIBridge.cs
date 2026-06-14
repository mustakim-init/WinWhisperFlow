using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinWhisperFlow.Services;

namespace WinWhisperFlow;

public sealed class UIBridge : IDisposable
{
    private readonly WebView2 _webView;
    private readonly WhisperBridgeService _whisper;
    private readonly AudioCaptureService _audio;
    private readonly PhoneMicService _phoneMic;
    private readonly TextInjector _textInjector;
    private readonly GlobalHotkeyService _hotkey;
    private readonly RuntimeSetupService _runtimeSetup;
    private readonly TranscriptionHistory _history;
    private readonly StartupService _startup;
    private readonly Func<bool> _detectDarkMode;
    private readonly OverlayManager? _overlay;

    private string _selectedLanguage = "en";
    private bool _modelLoaded;
    private string? _modelError;
    private string _loadedModel = "small";
    private string _loadedDevice = "cpu";
    private IntPtr _targetWindow;
    private CancellationTokenSource? _streamCts;

    public UIBridge(
        WebView2 webView,
        WhisperBridgeService whisper,
        AudioCaptureService audio,
        PhoneMicService phoneMic,
        TextInjector textInjector,
        GlobalHotkeyService hotkey,
        RuntimeSetupService runtimeSetup,
        TranscriptionHistory history,
        StartupService startup,
        Func<bool> detectDarkMode,
        OverlayManager? overlay)
    {
        _webView = webView;
        _whisper = whisper;
        _audio = audio;
        _phoneMic = phoneMic;
        _textInjector = textInjector;
        _hotkey = hotkey;
        _runtimeSetup = runtimeSetup;
        _history = history;
        _startup = startup;
        _detectDarkMode = detectDarkMode;
        _overlay = overlay;

        _audio.LevelChanged += (_, level) =>
            Post(new { type = "audio_level", level });

        _phoneMic.AudioReceived += async (_, e) =>
        {
            try
            {
                var result = await _whisper.TranscribeAsync(e.WavPath, GetSelectedLanguage());
                string text = result.Text.Trim();
                e.Result.SetResult(text);
                await PublishTranscriptionAsync(text, result.Language, result.LanguageProbability, "phone");
            }
            catch (Exception ex)
            {
                e.Result.SetException(ex);
                Post(new { type = "log", message = $"Phone mic error: {ex.Message}" });
            }
        };

        _phoneMic.LogMessage += (_, msg) =>
            Post(new { type = "log", message = msg });

        _webView.CoreWebView2.WebMessageReceived += OnWebMessage;
    }

    public bool ModelLoaded => _modelLoaded;
    public string? GetSelectedLanguage() => string.IsNullOrEmpty(_selectedLanguage) ? null : _selectedLanguage;

    public void SetModelLoaded(string model, string device)
    {
        _modelLoaded = true;
        _modelError = null;
        _loadedModel = model;
        _loadedDevice = device;
    }

    public void SetModelError(string error)
    {
        _modelLoaded = false;
        _modelError = error;
    }

    public void Post(object msg)
    {
        try
        {
            var core = _webView.CoreWebView2;
            if (core is null) return;
            string json = JsonSerializer.Serialize(msg);
            _ = _webView.Dispatcher.BeginInvoke(() => core.PostWebMessageAsJson(json));
        }
        catch (Exception ex)
        {
            LogError($"[UIBridge.Post] {ex.Message}");
        }
    }

    public Task ToggleListeningAsync()
    {
        if (_audio.IsListening)
        {
            CancelStreaming();

            string? path = _audio.Stop();
            _overlay?.ShowTranscribing();
            Post(new { type = "listening_status", listening = false });
            Post(new { type = "status_update", text = "Transcribing\u2026", variant = "warning" });
            if (path is not null)
            {
                return FinalizeTranscriptionAsync(path);
            }
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            return Task.CompletedTask;
        }

        if (!_modelLoaded)
        {
            Post(new { type = "log", message = "Model not loaded yet" });
            Post(new { type = "notification", title = "Model not ready", message = "Load or set up the speech model before listening.", variant = "warning" });
            return Task.CompletedTask;
        }

        _targetWindow = WindowFocusService.GetForegroundWindowHandle();
        if (WindowFocusService.BelongsToCurrentProcess(_targetWindow))
        {
            _targetWindow = IntPtr.Zero;
        }

        _streamCts = new CancellationTokenSource();

        _audio.Start();
        _overlay?.ShowListening();
        Post(new { type = "listening_status", listening = true });
        Post(new { type = "status_update", text = "Listening\u2026", variant = "warning" });
        _ = StartStreamingLoopAsync(_streamCts.Token);
        return Task.CompletedTask;
    }

    private void CancelStreaming()
    {
        if (_streamCts is not null)
        {
            _streamCts.Cancel();
            _streamCts.Dispose();
            _streamCts = null;
        }
    }

    private async Task FinalizeTranscriptionAsync(string path)
    {
        try
        {
            var result = await _whisper.TranscribeAsync(path, GetSelectedLanguage());
            await PublishTranscriptionAsync(
                result.Text.Trim(),
                result.Language,
                result.LanguageProbability,
                "mic",
                inject: true,
                isPartial: false);
        }
        catch (Exception ex)
        {
            Post(new { type = "log", message = $"Transcription error: {ex.Message}" });
            Post(new { type = "status_update", text = "Error", variant = "error" });
        }
        finally
        {
            try { File.Delete(path); } catch (Exception ex) { LogError($"[UIBridge] Cleanup delete failed: {ex.Message}"); }
            Post(new { type = "status_update", text = "Ready", variant = "success" });
        }
    }

    private async Task StartStreamingLoopAsync(CancellationToken ct)
    {
        while (_audio.IsListening && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) { break; }

            if (!_audio.IsListening || ct.IsCancellationRequested) break;

            string? snapPath = _audio.GetTemporarySnapshot();
            if (snapPath is null) continue;

            try
            {
                var result = await _whisper.TranscribeAsync(snapPath, GetSelectedLanguage(), ct);
                string text = result.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text) && _audio.IsListening && !ct.IsCancellationRequested)
                {
                    _overlay?.ShowTranscribing();
                    await PublishTranscriptionAsync(
                        text,
                        result.Language,
                        result.LanguageProbability,
                        "mic",
                        inject: false,
                        isPartial: true);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Post(new { type = "log", message = $"Streaming partial error: {ex.Message}" });
            }
            finally
            {
                try { File.Delete(snapPath); } catch (Exception ex) { LogError($"[UIBridge] Snapshot delete failed: {ex.Message}"); }
            }
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string? payload = e.TryGetWebMessageAsString();
            if (payload is null) return;
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                Post(new { type = "log", message = "Bridge message missing 'type'" });
                return;
            }
            string type = typeProp.GetString() ?? "";

            switch (type)
            {
                case "bridge_ready":
                    SendInitState();
                    break;
                case "_console":
                    string level = root.GetProperty("level").GetString() ?? "log";
                    string msgText = root.GetProperty("args").GetString() ?? "";
                    try { File.AppendAllText(RuntimePaths.LogPath, $"[JS {level}] {msgText}{Environment.NewLine}"); }
                    catch { }
                    break;
                case "toggle_listening":
                    _ = ToggleListeningAsync();
                    break;
                case "load_model":
                    _ = LoadModelAsync(
                        root.GetProperty("model").GetString() ?? "small",
                        root.GetProperty("gpu").GetBoolean());
                    break;
                case "setup_runtime":
                    _ = SetupRuntimeAsync();
                    break;
                case "phone_mic_toggle":
                    if (_phoneMic.IsRunning)
                    {
                        _phoneMic.Stop();
                        Post(new { type = "phone_mic_status", running = false });
                    }
                    else
                    {
                        _phoneMic.Start();
                        Post(new { type = "phone_mic_url", url = _phoneMic.GetUrl() });
                        Post(new { type = "phone_mic_status", running = true });
                    }
                    break;
                case "set_setting":
                    string key = root.GetProperty("key").GetString() ?? "";
                    if (key == "startup")
                    {
                        _startup.SetEnabled(root.GetProperty("value").GetBoolean());
                    }
                    else if (key == "language")
                    {
                        _selectedLanguage = root.GetProperty("value").GetString() ?? "en";
                    }
                    break;
                case "set_language":
                    _selectedLanguage = root.GetProperty("language").GetString() ?? "en";
                    break;
                case "get_model_note":
                    string m = root.GetProperty("model").GetString() ?? "small";
                    bool g = root.GetProperty("gpu").GetBoolean();
                    Post(new { type = "model_note", note = ModelNotes.GetModelNote(m, g) });
                    break;
                case "copy_text":
                    string copyText = root.GetProperty("text").GetString() ?? "";
                    _textInjector.CopyUnicodeText(copyText);
                    Post(new { type = "log", message = "Copied to clipboard" });
                    break;
            }
        }
        catch (Exception ex)
        {
            Post(new { type = "log", message = $"Bridge error: {ex.Message}" });
        }
    }

    private void SendInitState()
    {
        bool darkMode = _detectDarkMode();
        Post(new
        {
            type = "init",
            darkMode,
            loaded = _modelLoaded,
            error = _modelError,
            model = _loadedModel,
            device = _loadedDevice
        });

        Post(new
        {
            type = "settings",
            settings = new
            {
                startup = _startup.IsEnabled(),
                language = _selectedLanguage
            }
        });

        foreach (var entry in _history.Entries)
        {
            Post(new
            {
                type = "history_entry",
                entry = new
                {
                    action = TranscriptionHistory.ActionLabel(entry.Action),
                    text = entry.Text,
                    timestamp = entry.Timestamp.ToString("g")
                }
            });
        }

        if (_modelLoaded)
        {
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            Post(new { type = "model_loaded", model = _loadedModel, device = _loadedDevice, note = ModelNotes.GetModelNote(_loadedModel, _loadedDevice != "cpu") });
        }
        else if (_modelError is not null)
        {
            Post(new { type = "status_update", text = "Setup required", variant = "error" });
            Post(new { type = "log", message = _modelError });
        }
        else
        {
            Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });
        }
    }

    private async Task LoadModelAsync(string model, bool gpu)
    {
        Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });
        var loadOpts = BuildRuntimeOptions(model, gpu);
        Post(new { type = "log", message = $"Loading model {model} on {loadOpts.Device}\u2026" });
        try
        {
            await _whisper.RestartAsync(loadOpts);
            SetModelLoaded(model, loadOpts.Device);
            Post(new { type = "model_loaded", model, device = loadOpts.Device, note = ModelNotes.GetModelNote(loadOpts.Model, loadOpts.Device != "cpu") });
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            Post(new { type = "log", message = $"Model loaded on {loadOpts.Device}" });
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Model load failed: {ex.Message}" });
            Post(new { type = "status_update", text = "Setup required", variant = "error" });
            Post(new { type = "notification", title = "Model load failed", message = ex.Message, variant = "error" });
        }
    }

    private async Task SetupRuntimeAsync()
    {
        Post(new { type = "status_update", text = "Setting up...", variant = "warning" });
        await _runtimeSetup.SetupAsync(msg => Post(new { type = "log", message = msg }));
        Post(new { type = "log", message = "Setup complete. Detecting best configuration\u2026" });
        Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });
        try
        {
            var setupOpts = SttRuntimeOptions.RecommendedForThisPc;
            Post(new { type = "log", message = $"Loading model {setupOpts.Model} on {setupOpts.Device}\u2026" });
            await _whisper.RestartAsync(setupOpts);
            SetModelLoaded(setupOpts.Model, setupOpts.Device);
            Post(new { type = "model_loaded", model = setupOpts.Model, device = setupOpts.Device, note = ModelNotes.GetModelNote(setupOpts.Model, setupOpts.Device != "cpu") });
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            Post(new { type = "log", message = $"Model loaded on {setupOpts.Device}" });
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Model load failed: {ex.Message}" });
            Post(new { type = "status_update", text = "Setup required", variant = "error" });
            Post(new { type = "notification", title = "Setup failed", message = ex.Message, variant = "error" });
        }
    }

    private async Task PublishTranscriptionAsync(
        string text,
        string language,
        double confidence,
        string source,
        bool inject = false,
        bool isPartial = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!isPartial) Post(new { type = "log", message = "No speech detected" });
            return;
        }

        string meta = $"{language} ({confidence:P0})";
        Post(new { type = "transcription_result", text, meta, isPartial });

        if (isPartial)
        {
            _overlay?.ShowTranscribing();
            return;
        }

        string preview = text.Length > 60 ? text[..60] : text;
        Post(new { type = "log", message = $"Transcribed: {preview}" });

        string action = "copied";
        if (inject && _targetWindow != IntPtr.Zero)
        {
            await _textInjector.PasteUnicodeTextAsync(text, _targetWindow, keepClipboard: false);
            action = "typed";
        }
        else
        {
            _textInjector.CopyUnicodeText(text);
        }

        var entry = new TranscriptionHistoryEntry(
            DateTime.Now,
            text,
            language,
            confidence,
            source,
            action);
        _history.Add(entry);
        _overlay?.ShowDone("Done");
        Post(new
        {
            type = "history_entry",
            entry = new
            {
                action = TranscriptionHistory.ActionLabel(action),
                text,
                timestamp = entry.Timestamp.ToString("g")
            }
        });
    }

    private static SttRuntimeOptions BuildRuntimeOptions(string model, bool gpu)
    {
        if (gpu)
        {
            var cachedOptions = SttRuntimeOptions.RecommendedForThisPc;
            string device = cachedOptions.Provider switch
            {
                "cuda" => "cuda",
                "dml" => "dml",
                _ => "cpu"
            };
            if (device == "cpu")
            {
                return new SttRuntimeOptions(model, "cpu", "int8", 6, 1, 1) { Provider = "cpu" };
            }
            return new SttRuntimeOptions(model, device, "float16", 4, 1, 1) { Provider = device };
        }
        int beam = GetRecommendedBeamSize(model);
        return new SttRuntimeOptions(model, "cpu", "int8", 6, 1, beam) { Provider = "cpu" };
    }

    private static int GetRecommendedBeamSize(string model)
    {
        return model.ToLowerInvariant() switch
        {
            "tiny" or "base" or "small" => 1,
            "medium" or "large" or "large-v1" or "large-v2" or "large-v3" or "turbo" => 2,
            _ => 1
        };
    }

    private static void LogError(string message)
    {
        try
        {
            File.AppendAllText(RuntimePaths.LogPath, $"{message}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        CancelStreaming();
        try
        {
            if (_webView?.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessage;
            }
        }
        catch { }
    }
}
