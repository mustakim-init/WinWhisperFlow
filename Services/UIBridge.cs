using System.Linq;
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
    private readonly GpuDetectionService _gpuDetect = new();

    private string _selectedLanguage = "en";
    private bool _modelLoaded;
    private string? _modelError;
    private string _loadedModel = "small";
    private string _loadedDevice = "cpu";
    private IntPtr _targetWindow;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _setupCts;
    private bool _disposed;
    private string? _gpuCache;
    private readonly object _ctsLock = new();

    private readonly EventHandler<float> _onAudioLevel;
    private readonly EventHandler<(string WavPath, TaskCompletionSource<string> Result)> _onPhoneMicAudio;
    private readonly EventHandler<string> _onPhoneMicLog;
    private readonly EventHandler<WhisperBridgeService.ModelDownloadProgressArgs> _onDownloadProgress;
    private readonly EventHandler<IReadOnlyList<RuntimeSetupService.SetupStep>> _onSetupSteps;

    private static readonly string[] AllModelNames = { "tiny", "base", "small", "medium", "large-v3", "turbo" };

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

        _onAudioLevel = (_, level) =>
            Post(new { type = "audio_level", level });
        _audio.LevelChanged += _onAudioLevel;

        _onPhoneMicAudio = async (_, e) =>
        {
            try
            {
                var result = await _whisper.TranscribeAsync(e.WavPath, GetSelectedLanguage());
                string text = result.Text.Trim();
                await PublishTranscriptionAsync(text, result.Language, result.LanguageProbability, "phone");
                e.Result.TrySetResult(text);
            }
            catch (Exception ex)
            {
                e.Result.TrySetException(ex);
                Post(new { type = "log", message = $"Phone mic error: {ex.Message}" });
            }
        };
        _phoneMic.AudioReceived += _onPhoneMicAudio;

        _onPhoneMicLog = (_, msg) =>
            Post(new { type = "log", message = msg });
        _phoneMic.LogMessage += _onPhoneMicLog;

        _onDownloadProgress = (_, args) =>
        {
            Post(new { type = "model_download_progress", model = args.Model, downloaded = args.Downloaded, total = args.Total, status = args.Status, error = args.Error, compositeName = args.CompositeName, speed = args.Speed });
            if (args.Status is "done" or "error")
                SendModelsStatus();
        };
        _whisper.DownloadProgress += _onDownloadProgress;

        _onSetupSteps = (_, steps) =>
        {
            var overall = steps.Count == 0 ? 0 : (int)(steps.Count(s => s.Status == "done") / (double)steps.Count * 100);
            Post(new { type = "setup_progress", steps = steps.Select(s => new { id = s.Id, label = s.Label, status = s.Status, error = s.Error }), overall });
        };
        _runtimeSetup.StepsChanged += _onSetupSteps;

        _webView.CoreWebView2.WebMessageReceived += OnWebMessage;
    }

    public bool ModelLoaded => _modelLoaded;
    public string? GetSelectedLanguage() => string.IsNullOrEmpty(_selectedLanguage) ? null : _selectedLanguage;

    private string GetCompositeName() => $"{_loadedModel}-{_loadedDevice}";

    private void SetModelLoaded(string model, string device)
    {
        _ = _webView.Dispatcher.InvokeAsync(() =>
        {
            _modelLoaded = true;
            _modelError = null;
            _loadedModel = model;
            _loadedDevice = device;
        });
    }

    private void SetModelError(string error)
    {
        _ = _webView.Dispatcher.InvokeAsync(() =>
        {
            _modelLoaded = false;
            _modelError = error;
        });
    }

    public void Post(object msg)
    {
        if (_disposed) return;
        try
        {
            string json = JsonSerializer.Serialize(msg);
            _ = _webView.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                var core = _webView.CoreWebView2;
                if (core is not null)
                {
                    core.PostWebMessageAsJson(json);
                }
            });
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
            if (path is not null) return FinalizeTranscriptionAsync(path);
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            return Task.CompletedTask;
        }

        if (!_modelLoaded)
        {
            Post(new { type = "log", message = "Model not loaded yet" });
            Post(new { type = "notification", title = "Model not ready", message = "Download and load a model from the Models page before listening.", variant = "warning" });
            return Task.CompletedTask;
        }

        _targetWindow = WindowFocusService.GetForegroundWindowHandle();
        if (WindowFocusService.BelongsToCurrentProcess(_targetWindow)) _targetWindow = IntPtr.Zero;

        lock (_ctsLock)
        {
            _streamCts = new CancellationTokenSource();
        }
        _audio.Start();
        _overlay?.ShowListening();
        Post(new { type = "listening_status", listening = true });
        Post(new { type = "status_update", text = "Listening\u2026", variant = "warning" });
        _ = StartStreamingLoopAsync(_streamCts.Token);
        return Task.CompletedTask;
    }

    private void CancelStreaming()
    {
        CancellationTokenSource? old;
        lock (_ctsLock)
        {
            old = _streamCts;
            _streamCts = null;
        }
        old?.Cancel();
        old?.Dispose();
    }

    private async Task FinalizeTranscriptionAsync(string path)
    {
        try
        {
            var result = await _whisper.TranscribeAsync(path, GetSelectedLanguage());
            await PublishTranscriptionAsync(result.Text.Trim(), result.Language, result.LanguageProbability, "mic", inject: true, isPartial: false);
        }
        catch (Exception ex)
        {
            Post(new { type = "log", message = $"Transcription error: {ex.Message}" });
            Post(new { type = "status_update", text = "Error", variant = "error" });
            _overlay?.ShowError(ex.Message);
        }
        finally
        {
            try { File.Delete(path); } catch { }
            Post(new { type = "status_update", text = "Ready", variant = "success" });
        }
    }

    private async Task StartStreamingLoopAsync(CancellationToken ct)
    {
        while (_audio.IsListening && !ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
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
                    await PublishTranscriptionAsync(text, result.Language, result.LanguageProbability, "mic", inject: false, isPartial: true);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Post(new { type = "log", message = $"Streaming partial error: {ex.Message}" }); }
            finally { try { File.Delete(snapPath); } catch { } }
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
                    if (_modelLoaded)
                    {
                        Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = true, model = GetCompositeName(), device = _loadedDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames() });
                        Post(new { type = "model_loaded", model = GetCompositeName(), device = _loadedDevice, note = ModelNotes.GetModelNote(_loadedModel, _loadedDevice) });
                    }
                    else
                    {
                        _ = RunHealthCheckAsync();
                    }
                    break;

                case "setup_runtime":
                    _ = RunHealthCheckAsync();
                    break;

                case "cancel_download":
                {
                    string cancelModel = GetStringProp(root, "model");
                    if (!string.IsNullOrEmpty(cancelModel))
                        _whisper.CancelDownload(cancelModel);
                    else
                        _setupCts?.Cancel();
                    Post(new { type = "log", message = "Download cancelled" });
                    SendModelsStatus();
                    break;
                }

                case "_console":
                {
                    string consoleLevel = GetStringProp(root, "level", "log");
                    string consoleMsg = GetStringProp(root, "args");
                    try { File.AppendAllText(RuntimePaths.LogPath, $"[JS {consoleLevel}] {consoleMsg}{Environment.NewLine}"); } catch { }
                    break;
                }

                case "toggle_listening":
                    _ = ToggleListeningAsync();
                    break;

                case "load_model":
                {
                    string composite = GetStringProp(root, "model", SttRuntimeOptions.GetRecommendedCompositeName());
                    _ = LoadModelAsync(composite);
                    break;
                }

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
                {
                    string settingKey = GetStringProp(root, "key");
                    if (settingKey == "startup")
                    {
                        if (root.TryGetProperty("value", out var startupVal))
                            _startup.SetEnabled(startupVal.GetBoolean());
                    }
                    else if (settingKey == "language")
                    {
                        _selectedLanguage = GetStringProp(root, "value", "en");
                    }
                    break;
                }

                case "set_language":
                    _selectedLanguage = GetStringProp(root, "language", "en");
                    break;

                case "get_model_note":
                {
                    string composite = GetStringProp(root, "model", "small-cpu");
                    var (modelName, provider) = SttRuntimeOptions.FromCompositeName(composite);
                    Post(new { type = "model_note", note = ModelNotes.GetModelNote(modelName, provider) });
                    break;
                }

                case "copy_text":
                {
                    string copyText = GetStringProp(root, "text");
                    _textInjector.CopyUnicodeText(copyText);
                    Post(new { type = "log", message = "Copied to clipboard" });
                    break;
                }

                case "download_model":
                {
                    string dlModel = GetStringProp(root, "model", "small-cpu");
                    _ = _whisper.DownloadModelAsync(dlModel, CancellationToken.None);
                    break;
                }

                case "delete_model":
                {
                    string delModel = GetStringProp(root, "model");
                    _whisper.DeleteModel(delModel);
                    Post(new { type = "log", message = $"Deleted model {delModel}" });
                    SendModelsStatus();
                    break;
                }

                case "delete_history_entry":
                {
                    string delTs = GetStringProp(root, "ts");
                    string delText = GetStringProp(root, "text");
                    if (_history.Remove(delTs, delText))
                        Post(new { type = "log", message = "Deleted history entry" });
                    else
                        Post(new { type = "log", message = "Failed to delete history entry" });
                    break;
                }

                case "get_models_status":
                    SendModelsStatus();
                    break;

                default:
                    Post(new { type = "log", message = $"Unknown message type: {type}" });
                    break;
            }
        }
        catch (Exception ex)
        {
            Post(new { type = "log", message = $"Bridge error: {ex.Message}" });
        }
    }

    private static string GetStringProp(JsonElement root, string key, string fallback = "")
    {
        return root.TryGetProperty(key, out var prop) ? prop.GetString() ?? fallback : fallback;
    }

    private async Task RunHealthCheckAsync()
    {
        try
        {
            bool ready = await _runtimeSetup.IsReadyAsync();

            if (!ready)
            {
                Post(new { type = "log", message = "Runtime not ready. Running auto-setup\u2026" });
                await RunAutoSetupAsync();
                ready = await _runtimeSetup.IsReadyAsync();
                if (!ready)
                {
                    Post(new { type = "init", darkMode = _detectDarkMode(), ready = false, error = "Setup failed. Check the logs and try again." });
                    return;
                }
            }

            // Unblock UI immediately — setup succeeded
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = false, model = GetCompositeName(), device = _loadedDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames() });
            Post(new { type = "settings", settings = new { startup = _startup.IsEnabled(), language = _selectedLanguage } });

            var recommended = SttRuntimeOptions.RecommendedForThisPc;
            Post(new { type = "log", message = $"Detected GPU: {recommended.Provider}. Recommended model: {recommended.Model}" });

            // Phase 2: Model loading — non-fatal if it fails (user can load manually later)
            await LoadModelPhaseAsync(recommended);
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Setup failed: {ex.Message}" });
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, error = ex.Message, loaded = false, model = GetCompositeName(), device = _loadedDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames() });
            Post(new { type = "status_update", text = "Setup failed", variant = "error" });
            Post(new { type = "notification", title = "Startup failed", message = ex.Message, variant = "error" });
        }
    }

    private async Task LoadModelPhaseAsync(SttRuntimeOptions recommended)
    {
        try
        {
            if (recommended.Device is "cuda" or "dml")
            {
                Post(new { type = "log", message = "Verifying GPU model files\u2026" });
                Post(new { type = "status_update", text = "Verifying model files\u2026", variant = "warning" });

                string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{recommended.Model}");
                if (Directory.Exists(modelsDir))
                {
                    bool corrupt = await _whisper.VerifyGpuModelFilesAsync(recommended.Model);
                    if (corrupt)
                    {
                        Post(new { type = "log", message = "Model files corrupt. Re-downloading\u2026" });
                        Post(new { type = "status_update", text = "Re-downloading corrupt model\u2026", variant = "warning" });
                        try { Directory.Delete(modelsDir, recursive: true); } catch { }
                    }
                }
            }

            Post(new { type = "log", message = $"Loading model {recommended.Model} on {recommended.Device}\u2026" });
            Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });

            await _whisper.StartAsync(recommended);
            string actualDevice = _whisper.GetReportedDevice();
            if (actualDevice != recommended.Device)
            {
                Post(new { type = "log", message = $"Model loaded on {actualDevice} instead of {recommended.Device} — run Setup to fix GPU acceleration" });
                Post(new { type = "notification", title = "GPU acceleration unavailable", message = $"Model loaded on {actualDevice}. Run Setup to enable {recommended.Device}.", variant = "warning" });
            }
            SetModelLoaded(recommended.Model, actualDevice);

            // Use recommended/actual values directly — not GetCompositeName() or _loadedDevice —
            // because SetModelLoaded dispatches to the UI thread and hasn't run yet.
            string loadedComposite = $"{recommended.Model}-{actualDevice}";
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = true, model = loadedComposite, device = actualDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames() });
            Post(new { type = "settings", settings = new { startup = _startup.IsEnabled(), language = _selectedLanguage } });
            foreach (var entry in _history.Entries)
                Post(new { type = "history_entry", entry = new { action = TranscriptionHistory.ActionLabel(entry.Action), text = entry.Text, timestamp = entry.Timestamp.ToString("g"), ts = entry.Timestamp.ToString("O") } });
            Post(new { type = "model_loaded", model = loadedComposite, device = actualDevice, note = ModelNotes.GetModelNote(recommended.Model, actualDevice) });
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            Post(new { type = "log", message = $"Model loaded on {actualDevice}" });
            SendModelsStatus();
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Model load failed: {ex.Message}" });
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = false, error = $"Model load failed: {ex.Message}", model = GetCompositeName(), device = _loadedDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames() });
            Post(new { type = "settings", settings = new { startup = _startup.IsEnabled(), language = _selectedLanguage } });
            Post(new { type = "status_update", text = $"Model load failed: {ex.Message}", variant = "error" });
            Post(new { type = "notification", title = "Model load failed", message = ex.Message, variant = "error" });
            SendModelsStatus();
        }
    }

    private async Task RunAutoSetupAsync()
    {
        var oldSetup = _setupCts;
        _setupCts = new CancellationTokenSource();
        oldSetup?.Cancel();
        oldSetup?.Dispose();
        try
        {
            await _runtimeSetup.AutoSetupAsync(_setupCts.Token);
        }
        catch (OperationCanceledException)
        {
            Post(new { type = "log", message = "Setup was cancelled" });
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    }

    private long GetCpuModelSize(string name, bool downloaded)
    {
        if (downloaded)
        {
            string userCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "huggingface", "hub",
                $"models--Systran--faster-whisper-{name}");
            string localCache = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"models--Systran--faster-whisper-{name}");
            long size = GetDirectorySize(userCache);
            if (size == 0) size = GetDirectorySize(localCache);
            if (size > 0) return size;
        }
        return name switch
        {
            "tiny" => 41L * 1024 * 1024,
            "base" => 74L * 1024 * 1024,
            "small" => 244L * 1024 * 1024,
            "medium" => 769L * 1024 * 1024,
            "large-v3" => 1540L * 1024 * 1024,
            "turbo" => 809L * 1024 * 1024,
            _ => 0
        };
    }

    private long GetGpuModelSize(string name, bool downloaded)
    {
        if (downloaded)
        {
            string dir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{name}");
            long size = GetDirectorySize(dir);
            if (size > 0) return size;
        }
        return name switch
        {
            "tiny" => 151L * 1024 * 1024,
            "base" => 256L * 1024 * 1024,
            "small" => 716L * 1024 * 1024,
            "medium" => 1480L * 1024 * 1024,
            "large-v3" => 3090L * 1024 * 1024,
            "turbo" => 4470L * 1024 * 1024,
            _ => 0
        };
    }

    private static string GetDisplayName(string model, string provider)
    {
        string modelLabel = model switch
        {
            "tiny" => "Tiny",
            "base" => "Base",
            "small" => "Small",
            "medium" => "Medium",
            "large-v3" => "Large V3",
            "turbo" => "Turbo",
            _ => model
        };

        string providerLabel = provider switch
        {
            "cpu" => "CPU",
            "cuda" => "GPU \u2014 CUDA",
            "dml" => "GPU \u2014 DirectML",
            _ => provider
        };

        return $"{modelLabel} ({providerLabel})";
    }

    private void SendModelsStatus()
    {
        _gpuCache ??= _gpuDetect.Detect().provider;
        string detectedProvider = _gpuCache;
        var models = new List<object>();

        // CPU models — always shown
        foreach (var name in AllModelNames)
        {
            string composite = $"{name}-cpu";
            bool downloaded = _whisper.IsModelDownloaded("cpu", name);
            models.Add(new
            {
                name = composite,
                displayName = GetDisplayName(name, "cpu"),
                size = GetCpuModelSize(name, downloaded),
                downloaded,
                loaded = name == _loadedModel && _loadedDevice == "cpu" && _modelLoaded,
                provider = "cpu"
            });
        }

        // CUDA models — shown if CUDA detected
        if (detectedProvider == "cuda")
        {
            foreach (var name in AllModelNames)
            {
                string composite = $"{name}-cuda";
                bool downloaded = _whisper.IsModelDownloaded("cuda", name) || _whisper.IsModelDownloaded("dml", name);
                models.Add(new
                {
                    name = composite,
                    displayName = GetDisplayName(name, "cuda"),
                    size = GetGpuModelSize(name, downloaded),
                    downloaded,
                    loaded = name == _loadedModel && _loadedDevice == "cuda" && _modelLoaded,
                    provider = "cuda"
                });
            }
        }

        // DML models — shown if DML or CUDA detected (CUDA users can use DML models too)
        if (detectedProvider is "dml" or "cuda")
        {
            foreach (var name in AllModelNames)
            {
                string composite = $"{name}-dml";
                bool downloaded = _whisper.IsModelDownloaded("dml", name) || _whisper.IsModelDownloaded("cuda", name);
                models.Add(new
                {
                    name = composite,
                    displayName = GetDisplayName(name, "dml"),
                    size = GetGpuModelSize(name, downloaded),
                    downloaded,
                    loaded = name == _loadedModel && _loadedDevice == "dml" && _modelLoaded,
                    provider = "dml"
                });
            }
        }

        Post(new { type = "models_status", models });
    }

    private async Task LoadModelAsync(string compositeName)
    {
        var (model, device) = SttRuntimeOptions.FromCompositeName(compositeName);
        Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });
        Post(new { type = "log", message = $"Loading model {model} on {device}\u2026" });

        try
        {
            var loadOpts = BuildRuntimeOptions(compositeName);
            await _whisper.RestartAsync(loadOpts);
            string actualDevice = _whisper.GetReportedDevice();
            if (actualDevice != loadOpts.Device)
            {
                Post(new { type = "log", message = $"Model loaded on {actualDevice} instead of {loadOpts.Device} — run Setup to fix GPU acceleration" });
            }
            SetModelLoaded(loadOpts.Model, actualDevice);

            string loadedComposite = $"{loadOpts.Model}-{actualDevice}";
            Post(new { type = "model_loaded", model = loadedComposite, device = actualDevice, note = ModelNotes.GetModelNote(loadOpts.Model, actualDevice) });
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            Post(new { type = "log", message = $"Model loaded on {actualDevice}" });
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Model load failed: {ex.Message}" });
            Post(new { type = "status_update", text = "Model load failed", variant = "error" });
            Post(new { type = "notification", title = "Model load failed", message = ex.Message, variant = "error" });
        }
    }

    private async Task PublishTranscriptionAsync(string text, string language, double confidence, string source, bool inject = false, bool isPartial = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!isPartial)
            {
                Post(new { type = "log", message = "No speech detected" });
                _overlay?.Hide();
            }
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
        else _textInjector.CopyUnicodeText(text);

        var entry = new TranscriptionHistoryEntry(DateTime.Now, text, language, confidence, source, action);
        _history.Add(entry);
        _overlay?.ShowDone("Done");
            Post(new { type = "history_entry", entry = new { action = TranscriptionHistory.ActionLabel(action), text, timestamp = entry.Timestamp.ToString("g"), ts = entry.Timestamp.ToString("O"), source } });
        }

        private static SttRuntimeOptions BuildRuntimeOptions(string compositeName)
    {
        var (model, device) = SttRuntimeOptions.FromCompositeName(compositeName);

        if (device is "cuda" or "dml")
        {
            return new SttRuntimeOptions(model, device, "float16", 4, 1, 1) { Provider = device };
        }

        int beam = GetRecommendedBeamSize(model);
        return new SttRuntimeOptions(model, "cpu", "int8", 6, 1, beam) { Provider = "cpu" };
    }

    private static int GetRecommendedBeamSize(string model) => model.ToLowerInvariant() switch
    {
        "tiny" or "base" or "small" => 1,
        "medium" or "large" or "large-v1" or "large-v2" or "large-v3" or "turbo" => 2,
        _ => 1
    };

    private static void LogError(string message)
    {
        try { File.AppendAllText(RuntimePaths.LogPath, $"{message}{Environment.NewLine}"); } catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelStreaming();

        var oldSetup = _setupCts;
        _setupCts = null;
        oldSetup?.Cancel();
        oldSetup?.Dispose();

        try
        {
            if (_webView?.CoreWebView2 is not null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessage;
        }
        catch { }

        _audio.LevelChanged -= _onAudioLevel;
        _phoneMic.AudioReceived -= _onPhoneMicAudio;
        _phoneMic.LogMessage -= _onPhoneMicLog;
        _whisper.DownloadProgress -= _onDownloadProgress;
        _runtimeSetup.StepsChanged -= _onSetupSteps;
    }
}
