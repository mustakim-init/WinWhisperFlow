using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
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
    private readonly SoundEffectService _sfx;
    private readonly UpdateService _updateService;
    private readonly GpuDetectionService _gpuDetect = new();
    private readonly FFmpegService _ffmpeg = new();
    private readonly SourceSeparationService _sourceSeparation = new();
    private readonly SemaphoreSlim _fileTranscribeGate = new(1, 1);
    private CancellationTokenSource? _fileTranscribeCts;

    private string _selectedLanguage = "en";
    private bool _modelLoaded;
    private string? _modelError;
    private string _loadedModel = "small";
    private string _loadedDevice = "cpu";
    private IntPtr _targetWindow;
    private CancellationTokenSource? _streamCts;
    private CancellationTokenSource? _setupCts;
    private bool _disposed;
    private bool _healthCheckRunning;
    private string? _gpuCache;
    private bool _autoPasteEnabled = true;
    private int _beamSize = 1;
    private double _temperature = 0;
    private bool _vadFilter = false;
    private double _noSpeechThreshold = 0.45;
    private double _logProbThreshold = -0.8;
    private int _bestOf = 5;
    private double _repetitionPenalty = 1;
    private int _noRepeatNgramSize = 0;
    private double _lengthPenalty = 1;
    private double _compressionRatioThreshold = 2.4;
    private double _promptResetOnTemperature = 0.5;
    private bool _conditionOnPreviousText = true;
    private string? _hotwords = null;
    private double _hallucinationSilenceThreshold = 0;
    private readonly object _ctsLock = new();

    private readonly EventHandler<float> _onAudioLevel;
    private readonly EventHandler<(string WavPath, TaskCompletionSource<string> Result)> _onPhoneMicAudio;
    private readonly EventHandler<string> _onPhoneMicLog;
    private readonly EventHandler<bool> _onPhoneMicRecording;
    private readonly EventHandler<WhisperBridgeService.ModelDownloadProgressArgs> _onDownloadProgress;
    private readonly EventHandler<IReadOnlyList<RuntimeSetupService.SetupStep>> _onSetupSteps;

    private static readonly string[] AllModelNames = { "tiny", "base", "small", "medium", "large-v3", "turbo" };
    private static readonly string[] CpuModelNames = { "tiny", "base", "small", "medium", "large-v3" };

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
        OverlayManager? overlay,
        SoundEffectService sfx,
        UpdateService updateService)
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
        _sfx = sfx;
        _updateService = updateService;

        _onAudioLevel = (_, level) =>
            Post(new { type = "audio_level", level });
        _audio.LevelChanged += _onAudioLevel;

        _onPhoneMicAudio = async (_, e) =>
        {
            _overlay?.ShowTranscribing();
            Post(new { type = "status_update", text = "Transcribing phone audio\u2026", variant = "warning" });
            try
            {
                var result = await _whisper.TranscribeAsync(e.WavPath, GetSelectedLanguage(), beamSize: _beamSize, temperature: _temperature, vadFilter: _vadFilter, noSpeechThreshold: _noSpeechThreshold, logProbThreshold: _logProbThreshold, extraSettings: BuildExtraSettings());
                string text = result.Text.Trim();
                await PublishTranscriptionAsync(text, result.Language, result.LanguageProbability, "phone");
                Post(new { type = "status_update", text = "Ready", variant = "success" });
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

        _onPhoneMicRecording = (_, recording) =>
        {
            if (recording)
            {
                _overlay?.ShowListening();
                Post(new { type = "listening_status", listening = true, source = "phone" });
                Post(new { type = "status_update", text = "Phone recording\u2026", variant = "warning" });
            }
            else
            {
                _overlay?.Hide();
                Post(new { type = "listening_status", listening = false });
            }
        };
        _phoneMic.RecordingChanged += _onPhoneMicRecording;

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
            _sfx.PlayRecordStop();
            _overlay?.ShowTranscribing();
            Post(new { type = "listening_status", listening = false });
            Post(new { type = "status_update", text = "Transcribing\u2026", variant = "warning" });
            if (path is not null) return FinalizeTranscriptionAsync(path);
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            return Task.CompletedTask;
        }

        if (_phoneMic.IsPhoneRecording)
        {
            Post(new { type = "log", message = "Phone mic is recording — stop it before using desktop mic" });
            Post(new { type = "notification", title = "Phone mic active", message = "Stop phone recording first to use the desktop mic.", variant = "warning" });
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
        _sfx.PlayRecordStart();
        _overlay?.ShowListening();
        Post(new { type = "listening_status", listening = true });
        Post(new { type = "status_update", text = "Listening\u2026", variant = "warning" });
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
            var result = await _whisper.TranscribeAsync(path, GetSelectedLanguage(), beamSize: _beamSize, temperature: _temperature, vadFilter: _vadFilter, noSpeechThreshold: _noSpeechThreshold, logProbThreshold: _logProbThreshold, extraSettings: BuildExtraSettings());
            await PublishTranscriptionAsync(result.Text.Trim(), result.Language, result.LanguageProbability, "mic", inject: _autoPasteEnabled, isPartial: false);
            Post(new { type = "status_update", text = "Ready", variant = "success" });
        }
        catch (Exception ex)
        {
            _sfx.PlayError();
            Post(new { type = "log", message = $"Transcription error: {ex.Message}" });
            Post(new { type = "status_update", text = "Error", variant = "error" });
            _overlay?.ShowError(ex.Message);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    public async Task TranscribeFileAsync(string inputPath, bool isMusic = false)
    {
        if (_disposed) return;

        if (!_modelLoaded)
        {
            Post(new { type = "file_transcribe_progress", status = "error", message = "Model not loaded. Please load a model first." });
            return;
        }

        if (isMusic && !_whisper.IsModelDownloaded("demucs", "htdemucs"))
        {
            Post(new { type = "file_transcribe_progress", status = "error", message = "Demucs not downloaded. Go to the Models page and download 'htdemucs' under Tools." });
            return;
        }

        if (!_ffmpeg.IsAvailable)
        {
            Post(new { type = "file_transcribe_progress", status = "error", message = "FFmpeg not found. Run Setup to download it." });
            return;
        }

        await _fileTranscribeGate.WaitAsync();
        lock (_ctsLock) { _fileTranscribeCts = new CancellationTokenSource(); }
        var ct = _fileTranscribeCts.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            string fileName = Path.GetFileName(inputPath);

            void ReportProgress(string status, string message, double pct)
            {
                Post(new
                {
                    type = "file_transcribe_progress",
                    status,
                    message,
                    progress = pct,
                    elapsed = sw.Elapsed.TotalSeconds,
                    fileName
                });
            }

            ReportProgress("extracting", $"Extracting audio from {fileName}\u2026", 0);
            string extractedWav = await _ffmpeg.ExtractAudioAsync(inputPath);
            ct.ThrowIfCancellationRequested();
            ReportProgress("extracting", $"Extracted audio from {fileName}", 20);

            string audioForWhisper = extractedWav;
            bool cleanupVocals = false;

            if (isMusic)
            {
                ReportProgress("separating", "Separating vocals from music\u2026", 25);
                audioForWhisper = await _sourceSeparation.SeparateVocalsAsync(extractedWav);
                ct.ThrowIfCancellationRequested();
                cleanupVocals = true;
                ReportProgress("separating", "Vocals separated", 55);
            }

            ReportProgress("transcribing", "Transcribing\u2026", 60);
            var result = await _whisper.TranscribeAsync(audioForWhisper, GetSelectedLanguage(), fileMode: true, beamSize: _beamSize, temperature: _temperature, vadFilter: _vadFilter, noSpeechThreshold: _noSpeechThreshold, logProbThreshold: _logProbThreshold, extraSettings: BuildExtraSettings());
            ct.ThrowIfCancellationRequested();
            ReportProgress("transcribing", "Transcription complete", 95);

            string text = result.Text.Trim();
            await PublishTranscriptionAsync(text, result.Language, result.LanguageProbability, "file");

            ReportProgress("done", "Done", 100);
            _overlay?.Hide();

            // Cleanup temp files
            try { File.Delete(extractedWav); } catch { }
            if (cleanupVocals)
            {
                try
                {
                    string? parentDir = Path.GetDirectoryName(audioForWhisper);
                    if (parentDir is not null)
                    {
                        string? demucsRoot = Path.GetDirectoryName(parentDir);
                        string tempDir = Path.GetTempPath().TrimEnd('\\');
                        if (demucsRoot is not null &&
                            demucsRoot.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
                            Directory.Delete(demucsRoot, recursive: true);
                    }
                }
                catch { }
            }
        }
        catch (OperationCanceledException)
        {
            Post(new { type = "file_transcribe_progress", status = "cancelled", message = "Cancelled", progress = 0, elapsed = sw.Elapsed.TotalSeconds });
        }
        catch (Exception ex)
        {
            Post(new { type = "file_transcribe_progress", status = "error", message = ex.Message, progress = 0, elapsed = sw.Elapsed.TotalSeconds });
            Post(new { type = "log", message = $"File transcription error: {ex.Message}" });
            _sfx.PlayError();
        }
        finally
        {
            CancellationTokenSource? old;
            lock (_ctsLock)
            {
                old = _fileTranscribeCts;
                _fileTranscribeCts = null;
            }
            old?.Dispose();
            try { _fileTranscribeGate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public void OpenFileAndTranscribe(bool isMusic = false)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = isMusic ? "Select music audio or video file to transcribe" : "Select speech audio or video file to transcribe",
            Filter = "Media files (*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv)|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.wmv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() == true)
        {
            _ = TranscribeFileAsync(dialog.FileName, isMusic);
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
                        var (dd, dgn, cn, cc, ct, tr) = GetHardwareInfo();
                        Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = true, model = GetCompositeName(), device = _loadedDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames(), audioDeviceIndex = _audio.DeviceId, detectedDevice = dd, detectedGpuName = dgn, cpuName = cn, cpuCores = cc, cpuThreads = ct, totalRam = tr, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
                        Post(new { type = "model_loaded", model = GetCompositeName(), device = _loadedDevice, note = ModelNotes.GetModelNote(_loadedModel, _loadedDevice) });
                    }
                    else if (!_healthCheckRunning)
                    {
                        _healthCheckRunning = true;
                        _ = RunHealthCheckAsync();
                    }
                    break;

                case "setup_runtime":
                    _ = RunAutoSetupAndReportAsync();
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

                case "pause_download":
                {
                    string pauseModel = GetStringProp(root, "model");
                    if (!string.IsNullOrEmpty(pauseModel))
                        _whisper.PauseDownload(pauseModel);
                    SendModelsStatus();
                    break;
                }

                case "resume_download":
                {
                    string resumeModel = GetStringProp(root, "model");
                    if (!string.IsNullOrEmpty(resumeModel))
                        _ = _whisper.DownloadModelAsync(resumeModel, CancellationToken.None);
                    break;
                }

                case "_console":
                {
                    string consoleLevel = GetStringProp(root, "level", "log");
                    string consoleMsg = GetStringProp(root, "args");
                    try { File.AppendAllText(RuntimePaths.LogPath, $"[JS {consoleLevel}] {consoleMsg}{Environment.NewLine}"); } catch { }
                    break;
                }

                case "check_for_updates":
                    _ = CheckForUpdatesAsync();
                    break;

                case "download_update":
                    _ = DownloadUpdateAsync();
                    break;

                case "apply_update":
                    try
                    {
                        _updateService.ApplyAndRestart();
                    }
                    catch (Exception ex)
                    {
                        Post(new { type = "log", message = $"Update apply failed: {ex.Message}" });
                        Post(new { type = "notification", title = "Update failed", message = ex.Message, variant = "error" });
                    }
                    break;

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
                    string tuneProfile = GetStringProp(root, "profile", "");
                    if (settingKey == "start_on_boot")
                    {
                        if (root.TryGetProperty("value", out var startupVal))
                        {
                            _startup.SetEnabled(startupVal.GetBoolean());
                            SettingsStore.StartOnBoot = startupVal.GetBoolean();
                            SettingsStore.Save();
                        }
                    }
                    else if (settingKey == "audio_device")
                    {
                        if (root.TryGetProperty("value", out var audioVal))
                        {
                            _audio.DeviceId = audioVal.GetInt32();
                            SettingsStore.AudioDeviceId = audioVal.GetInt32();
                            SettingsStore.Save();
                        }
                    }
                    else if (settingKey == "sfx")
                    {
                        if (root.TryGetProperty("value", out var sfxVal))
                        {
                            _sfx.Enabled = sfxVal.GetBoolean();
                            SettingsStore.SoundEffectsEnabled = sfxVal.GetBoolean();
                            SettingsStore.Save();
                        }
                    }
                    else if (settingKey == "language")
                    {
                        _selectedLanguage = GetStringProp(root, "value", "en");
                        SettingsStore.Language = _selectedLanguage;
                        SettingsStore.Save();
                        Post(new { type = "settings", settings = new { language = _selectedLanguage } });
                    }
                    else if (settingKey == "theme")
                    {
                        string theme = GetStringProp(root, "value", "dark");
                        SettingsStore.Theme = theme;
                        SettingsStore.Save();
                        Post(new { type = "log", message = $"Theme set to {theme}" });
                    }
                    else if (settingKey == "hotkey_chord")
                    {
                        if (root.TryGetProperty("value", out var chordVal) && chordVal.ValueKind == JsonValueKind.Array)
                        {
                            var vkCodes = new List<int>();
                            foreach (var item in chordVal.EnumerateArray())
                            {
                                string? keyName = item.GetString();
                                if (keyName is not null)
                                {
                                    int vk = KeyNameToVkCode(keyName);
                                    if (vk > 0) vkCodes.Add(vk);
                                }
                            }
                            if (vkCodes.Count > 0)
                                _hotkey.UpdateChord(vkCodes);
                        }
                        // Persist as comma-separated string
                        var keys = new List<string>();
                        if (root.TryGetProperty("value", out var chordArr) && chordArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in chordArr.EnumerateArray())
                            {
                                string? k = item.GetString();
                                if (k is not null) keys.Add(k);
                            }
                        }
                        SettingsStore.HotkeyChord = string.Join(",", keys);
                        SettingsStore.Save();
                    }
                    else if (settingKey == "auto_paste")
                    {
                        if (root.TryGetProperty("value", out var pasteVal))
                        {
                            _autoPasteEnabled = pasteVal.GetBoolean();
                            SettingsStore.AutoPasteEnabled = pasteVal.GetBoolean();
                            SettingsStore.Save();
                        }
                        Post(new { type = "log", message = $"Auto-paste set to {_autoPasteEnabled}" });
                    }
                    else if (settingKey == "beam_size")
                    {
                        if (root.TryGetProperty("value", out var beamVal))
                        {
                            _beamSize = Math.Clamp(beamVal.GetInt32(), 1, 10);
                            SettingsStore.BeamSize = _beamSize;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { beam_size = _beamSize } });
                            Post(new { type = "log", message = $"Beam size set to {_beamSize}" });
                        }
                    }
                    else if (settingKey == "temperature")
                    {
                        if (root.TryGetProperty("value", out var tempVal))
                        {
                            _temperature = Math.Clamp(tempVal.GetDouble(), 0, 1);
                            SettingsStore.Temperature = _temperature;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { temperature = _temperature } });
                            Post(new { type = "log", message = $"Temperature set to {_temperature}" });
                        }
                    }
                    else if (settingKey == "vad_filter")
                    {
                        if (root.TryGetProperty("value", out var vadVal))
                        {
                            _vadFilter = vadVal.GetBoolean();
                            SettingsStore.VadFilter = _vadFilter;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { vad_filter = _vadFilter } });
                            Post(new { type = "log", message = $"VAD filter {( _vadFilter ? "enabled" : "disabled" )}" });
                        }
                    }
                    else if (settingKey == "no_speech_threshold")
                    {
                        if (root.TryGetProperty("value", out var nstVal))
                        {
                            _noSpeechThreshold = Math.Clamp(nstVal.GetDouble(), 0, 1);
                            SettingsStore.NoSpeechThreshold = _noSpeechThreshold;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { no_speech_threshold = _noSpeechThreshold } });
                            Post(new { type = "log", message = $"No-speech threshold set to {_noSpeechThreshold:F2}" });
                        }
                    }
                    else if (settingKey == "log_prob_threshold")
                    {
                        if (root.TryGetProperty("value", out var lptVal))
                        {
                            _logProbThreshold = Math.Clamp(lptVal.GetDouble(), -10, 0);
                            SettingsStore.LogProbThreshold = _logProbThreshold;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { log_prob_threshold = _logProbThreshold } });
                            Post(new { type = "log", message = $"Log-prob threshold set to {_logProbThreshold:F2}" });
                        }
                    }
                    else if (settingKey == "best_of")
                    {
                        if (root.TryGetProperty("value", out var boVal))
                        {
                            _bestOf = Math.Clamp(boVal.GetInt32(), 1, 10);
                            SettingsStore.BestOf = _bestOf;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { best_of = _bestOf } });
                            Post(new { type = "log", message = $"Best-of set to {_bestOf}" });
                        }
                    }
                    else if (settingKey == "repetition_penalty")
                    {
                        if (root.TryGetProperty("value", out var rpVal))
                        {
                            _repetitionPenalty = Math.Clamp(rpVal.GetDouble(), 1, 5);
                            SettingsStore.RepetitionPenalty = _repetitionPenalty;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { repetition_penalty = _repetitionPenalty } });
                            Post(new { type = "log", message = $"Repetition penalty set to {_repetitionPenalty:F2}" });
                        }
                    }
                    else if (settingKey == "no_repeat_ngram_size")
                    {
                        if (root.TryGetProperty("value", out var nrVal))
                        {
                            _noRepeatNgramSize = Math.Max(0, nrVal.GetInt32());
                            SettingsStore.NoRepeatNgramSize = _noRepeatNgramSize;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { no_repeat_ngram_size = _noRepeatNgramSize } });
                            Post(new { type = "log", message = $"No-repeat n-gram size set to {_noRepeatNgramSize}" });
                        }
                    }
                    else if (settingKey == "length_penalty")
                    {
                        if (root.TryGetProperty("value", out var lpVal))
                        {
                            _lengthPenalty = Math.Max(0, lpVal.GetDouble());
                            SettingsStore.LengthPenalty = _lengthPenalty;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { length_penalty = _lengthPenalty } });
                            Post(new { type = "log", message = $"Length penalty set to {_lengthPenalty:F2}" });
                        }
                    }
                    else if (settingKey == "compression_ratio_threshold")
                    {
                        if (root.TryGetProperty("value", out var crVal))
                        {
                            _compressionRatioThreshold = Math.Max(0, crVal.GetDouble());
                            SettingsStore.CompressionRatioThreshold = _compressionRatioThreshold;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { compression_ratio_threshold = _compressionRatioThreshold } });
                            Post(new { type = "log", message = $"Compression ratio threshold set to {_compressionRatioThreshold:F2}" });
                        }
                    }
                    else if (settingKey == "prompt_reset_on_temperature")
                    {
                        if (root.TryGetProperty("value", out var prVal))
                        {
                            _promptResetOnTemperature = Math.Clamp(prVal.GetDouble(), 0, 1);
                            SettingsStore.PromptResetOnTemperature = _promptResetOnTemperature;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { prompt_reset_on_temperature = _promptResetOnTemperature } });
                            Post(new { type = "log", message = $"Prompt reset on temperature set to {_promptResetOnTemperature:F2}" });
                        }
                    }
                    else if (settingKey == "condition_on_previous_text")
                    {
                        if (root.TryGetProperty("value", out var cpVal))
                        {
                            _conditionOnPreviousText = cpVal.GetBoolean();
                            SettingsStore.ConditionOnPreviousText = _conditionOnPreviousText;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { condition_on_previous_text = _conditionOnPreviousText } });
                            Post(new { type = "log", message = $"Condition on previous text: {_conditionOnPreviousText}" });
                        }
                    }
                    else if (settingKey == "hotwords")
                    {
                        if (root.TryGetProperty("value", out var hwVal))
                        {
                            if (hwVal.ValueKind == JsonValueKind.Null)
                            {
                                _hotwords = null;
                                SettingsStore.Hotwords = null;
                                SettingsStore.Save();
                                Post(new { type = "settings", settings = new { hotwords = (string?)null } });
                                Post(new { type = "log", message = "Hotwords cleared" });
                            }
                            else if (hwVal.ValueKind == JsonValueKind.String)
                            {
                                string? val = hwVal.GetString();
                                _hotwords = string.IsNullOrWhiteSpace(val) ? null : val;
                                SettingsStore.Hotwords = _hotwords;
                                SettingsStore.Save();
                                Post(new { type = "settings", settings = new { hotwords = _hotwords } });
                                Post(new { type = "log", message = _hotwords is null ? "Hotwords cleared" : $"Hotwords set to: {_hotwords}" });
                            }
                        }
                    }
                    else if (settingKey == "hallucination_silence_threshold")
                    {
                        if (root.TryGetProperty("value", out var hsVal))
                        {
                            _hallucinationSilenceThreshold = Math.Max(0, hsVal.GetDouble());
                            SettingsStore.HallucinationSilenceThreshold = _hallucinationSilenceThreshold;
                            SettingsStore.Save();
                            Post(new { type = "settings", settings = new { hallucination_silence_threshold = _hallucinationSilenceThreshold } });
                            Post(new { type = "log", message = $"Hallucination silence threshold set to {_hallucinationSilenceThreshold}" });
                        }
                    }
                    else if (settingKey == "model_dir")
                    {
                        string dir = GetStringProp(root, "value");
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            RuntimePaths.ModelsRoot = dir;
                            SettingsStore.ModelDirectory = dir;
                            Post(new { type = "log", message = $"Model directory changed to {dir}" });
                        }
                        else
                        {
                            RuntimePaths.ModelsRoot = "";
                            SettingsStore.ModelDirectory = null;
                            Post(new { type = "log", message = "Model directory reset to default" });
                        }
                        SettingsStore.Save();
                    }

                    // Sync flat values to the named profile (dual-profile persistence)
                    if (tuneProfile == "voice" || tuneProfile == "music")
                    {
                        var profile = tuneProfile == "voice" ? SettingsStore.VoiceProfile : SettingsStore.MusicProfile;
                        profile.BeamSize = _beamSize;
                        profile.Temperature = _temperature;
                        profile.VadFilter = _vadFilter;
                        profile.NoSpeechThreshold = _noSpeechThreshold;
                        profile.LogProbThreshold = _logProbThreshold;
                        profile.BestOf = _bestOf;
                        profile.RepetitionPenalty = _repetitionPenalty;
                        profile.NoRepeatNgramSize = _noRepeatNgramSize;
                        profile.LengthPenalty = _lengthPenalty;
                        profile.CompressionRatioThreshold = _compressionRatioThreshold;
                        profile.PromptResetOnTemperature = _promptResetOnTemperature;
                        profile.ConditionOnPreviousText = _conditionOnPreviousText;
                        profile.Hotwords = _hotwords;
                        profile.HallucinationSilenceThreshold = _hallucinationSilenceThreshold;
                        SettingsStore.Save();
                    }
                    break;
                }

                case "switch_profile":
                {
                    string profileName = GetStringProp(root, "profile");
                    if (profileName != "voice" && profileName != "music") break;
                    var profile = profileName == "voice" ? SettingsStore.VoiceProfile : SettingsStore.MusicProfile;
                    _beamSize = profile.BeamSize;
                    _temperature = profile.Temperature;
                    _vadFilter = profile.VadFilter;
                    _noSpeechThreshold = profile.NoSpeechThreshold;
                    _logProbThreshold = profile.LogProbThreshold;
                    _bestOf = profile.BestOf;
                    _repetitionPenalty = profile.RepetitionPenalty;
                    _noRepeatNgramSize = profile.NoRepeatNgramSize;
                    _lengthPenalty = profile.LengthPenalty;
                    _compressionRatioThreshold = profile.CompressionRatioThreshold;
                    _promptResetOnTemperature = profile.PromptResetOnTemperature;
                    _conditionOnPreviousText = profile.ConditionOnPreviousText;
                    _hotwords = profile.Hotwords;
                    _hallucinationSilenceThreshold = profile.HallucinationSilenceThreshold;
                    SettingsStore.Save();
                    Post(new { type = "log", message = $"Switched to {profileName} profile" });
                    break;
                }

                case "save_profile":
                {
                    string profileName = GetStringProp(root, "profile");
                    if (profileName != "voice" && profileName != "music") break;
                    var profile = profileName == "voice" ? SettingsStore.VoiceProfile : SettingsStore.MusicProfile;
                    if (root.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
                    {
                        if (vals.TryGetProperty("beam_size", out var bs)) profile.BeamSize = bs.GetInt32();
                        if (vals.TryGetProperty("temperature", out var tp)) profile.Temperature = tp.GetDouble();
                        if (vals.TryGetProperty("vad_filter", out var vf)) profile.VadFilter = vf.GetBoolean();
                        if (vals.TryGetProperty("no_speech_threshold", out var nst)) profile.NoSpeechThreshold = nst.GetDouble();
                        if (vals.TryGetProperty("log_prob_threshold", out var lpt)) profile.LogProbThreshold = lpt.GetDouble();
                        if (vals.TryGetProperty("best_of", out var bo)) profile.BestOf = Math.Clamp(bo.GetInt32(), 1, 10);
                        if (vals.TryGetProperty("repetition_penalty", out var rp)) profile.RepetitionPenalty = Math.Clamp(rp.GetDouble(), 1, 5);
                        if (vals.TryGetProperty("no_repeat_ngram_size", out var nr)) profile.NoRepeatNgramSize = Math.Max(0, nr.GetInt32());
                        if (vals.TryGetProperty("length_penalty", out var lp)) profile.LengthPenalty = Math.Max(0, lp.GetDouble());
                        if (vals.TryGetProperty("compression_ratio_threshold", out var cr)) profile.CompressionRatioThreshold = Math.Max(0, cr.GetDouble());
                        if (vals.TryGetProperty("prompt_reset_on_temperature", out var pr)) profile.PromptResetOnTemperature = Math.Clamp(pr.GetDouble(), 0, 1);
                        if (vals.TryGetProperty("condition_on_previous_text", out var cp)) profile.ConditionOnPreviousText = cp.GetBoolean();
                        if (vals.TryGetProperty("hotwords", out var hw) && hw.ValueKind == JsonValueKind.String)
                            profile.Hotwords = hw.GetString();
                        else if (vals.TryGetProperty("hotwords", out var hwNull) && hwNull.ValueKind == JsonValueKind.Null)
                            profile.Hotwords = null;
                        if (vals.TryGetProperty("hallucination_silence_threshold", out var hs)) profile.HallucinationSilenceThreshold = Math.Max(0, hs.GetDouble());
                        SettingsStore.Save();
                        Post(new { type = "log", message = $"{profileName} profile saved" });
                    }
                    break;
                }

                case "pick_directory":
                {
                    string purpose = GetStringProp(root, "purpose");
                    _ = ShowFolderPickerAsync(purpose);
                    break;
                }

                case "open_url":
                {
                    string url = GetStringProp(root, "url");
                    if (!string.IsNullOrEmpty(url))
                    {
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch (Exception ex) { Post(new { type = "log", message = $"Open URL error: {ex.Message}" }); }
                    }
                    break;
                }

                case "open_directory":
                {
                    string openPath = GetStringProp(root, "path");
                    string target = openPath switch
                    {
                        "models" => RuntimePaths.ModelsRoot,
                        "logs" => RuntimePaths.LogPath,
                        _ => RuntimePaths.AppDataRoot
                    };
                    try
                    {
                        if (Directory.Exists(target))
                            Process.Start("explorer.exe", target);
                        else if (File.Exists(target))
                            Process.Start("explorer.exe", $"/select,\"{target}\"");
                    }
                    catch (Exception ex)
                    {
                        Post(new { type = "log", message = $"Open folder error: {ex.Message}" });
                    }
                    break;
                }

                case "set_language":
                    _selectedLanguage = GetStringProp(root, "language", "en");
                    SettingsStore.Language = _selectedLanguage;
                    SettingsStore.Save();
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

                case "transcribe_file":
                {
                    bool musicMode = GetBoolProp(root, "musicMode", false);
                    OpenFileAndTranscribe(musicMode);
                    break;
                }

                case "transcribe_file_path":
                {
                    string filePath = GetStringProp(root, "path");
                    bool musicMode = GetBoolProp(root, "musicMode", false);
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        _ = TranscribeFileAsync(filePath, musicMode);
                    else
                        Post(new { type = "file_transcribe_progress", status = "error", message = "File not found." });
                    break;
                }

                case "transcribe_dropped_file":
                {
                    string base64Data = GetStringProp(root, "data");
                    string fileName = GetStringProp(root, "name", "recording.wav");
                    if (!string.IsNullOrEmpty(base64Data))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(base64Data);
                            string tempDir = Path.Combine(Path.GetTempPath(), "winwhisper");
                            Directory.CreateDirectory(tempDir);
                            string tempFile = Path.Combine(tempDir, $"dropped-{Guid.NewGuid():N}-{fileName}");
                            File.WriteAllBytes(tempFile, bytes);
                            _ = TranscribeFileAsync(tempFile, false).ContinueWith(_ =>
                            {
                                try { File.Delete(tempFile); } catch { }
                            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                        }
                        catch (Exception ex)
                        {
                            Post(new { type = "file_transcribe_progress", status = "error", message = ex.Message });
                        }
                    }
                    break;
                }

                case "cancel_file_transcribe":
                {
                    CancellationTokenSource? old;
                    lock (_ctsLock)
                    {
                        old = _fileTranscribeCts;
                        _fileTranscribeCts = null;
                    }
                    old?.Cancel();
                    old?.Dispose();
                    Post(new { type = "file_transcribe_progress", status = "cancelled", message = "Cancelled", progress = 0, elapsed = 0 });
                    break;
                }

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

    private static bool GetBoolProp(JsonElement root, string key, bool fallback = false)
    {
        if (root.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.String)
            {
                string s = prop.GetString() ?? "";
                if (bool.TryParse(s, out bool result)) return result;
            }
        }
        return fallback;
    }

    private Dictionary<string, object?> BuildExtraSettings()
    {
        var d = new Dictionary<string, object?>();
        if (_bestOf != 5) d["best_of"] = _bestOf;
        if (_repetitionPenalty != 1.0) d["repetition_penalty"] = _repetitionPenalty;
        if (_noRepeatNgramSize != 0) d["no_repeat_ngram_size"] = _noRepeatNgramSize;
        if (_lengthPenalty != 1.0) d["length_penalty"] = _lengthPenalty;
        if (_compressionRatioThreshold != 2.4) d["compression_ratio_threshold"] = _compressionRatioThreshold;
        if (_promptResetOnTemperature != 0.5) d["prompt_reset_on_temperature"] = _promptResetOnTemperature;
        d["condition_on_previous_text"] = _conditionOnPreviousText;
        if (_hotwords is not null) d["hotwords"] = _hotwords;
        if (_hallucinationSilenceThreshold > 0) d["hallucination_silence_threshold"] = _hallucinationSilenceThreshold;
        return d;
    }

    private static int KeyNameToVkCode(string keyName)
    {
        return keyName switch
        {
            "ControlLeft" or "ControlRight" => 0x11,
            "ShiftLeft" or "ShiftRight" => 0x10,
            "Alt" or "AltGr" => 0x12,
            "MetaLeft" or "MetaRight" => 0x5B,
            "Space" => 0x20,
            "Tab" => 0x09,
            "Return" => 0x0D,
            "Backspace" => 0x08,
            "Delete" => 0x2E,
            "Escape" => 0x1B,
            "Insert" => 0x2D,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "UpArrow" => 0x26,
            "DownArrow" => 0x28,
            "LeftArrow" => 0x25,
            "RightArrow" => 0x27,
            "CapsLock" => 0x14,
            _ when keyName.StartsWith("F") && int.TryParse(keyName[1..], out int fn) && fn >= 1 && fn <= 24 => fn + 0x6F,
            _ when keyName.StartsWith("Digit") && int.TryParse(keyName[5..], out int dn) && dn >= 0 && dn <= 9 => 0x30 + dn,
            _ when keyName.StartsWith("Key") && keyName.Length == 4 => char.ToUpperInvariant(keyName[3]),
            _ => 0,
        };
    }

    private async Task RunHealthCheckAsync()
    {
        try
        {
            // Load persisted settings
            SettingsStore.Load();
            if (!string.IsNullOrEmpty(SettingsStore.ModelDirectory))
                RuntimePaths.ModelsRoot = SettingsStore.ModelDirectory;
            _selectedLanguage = SettingsStore.Language;
            _autoPasteEnabled = SettingsStore.AutoPasteEnabled;
            _beamSize = SettingsStore.BeamSize;
            _temperature = SettingsStore.Temperature;
            _vadFilter = SettingsStore.VadFilter;
            _noSpeechThreshold = SettingsStore.NoSpeechThreshold;
            _logProbThreshold = SettingsStore.LogProbThreshold;
            _bestOf = SettingsStore.BestOf;
            _repetitionPenalty = SettingsStore.RepetitionPenalty;
            _noRepeatNgramSize = SettingsStore.NoRepeatNgramSize;
            _lengthPenalty = SettingsStore.LengthPenalty;
            _compressionRatioThreshold = SettingsStore.CompressionRatioThreshold;
            _promptResetOnTemperature = SettingsStore.PromptResetOnTemperature;
            _conditionOnPreviousText = SettingsStore.ConditionOnPreviousText;
            _hotwords = SettingsStore.Hotwords;
            _hallucinationSilenceThreshold = SettingsStore.HallucinationSilenceThreshold;
            _sfx.Enabled = SettingsStore.SoundEffectsEnabled;
            _audio.DeviceId = SettingsStore.AudioDeviceId;
            if (SettingsStore.StartOnBoot) _startup.SetEnabled(true);
            bool ready = await _runtimeSetup.IsReadyAsync();

            if (!ready)
            {
                Post(new { type = "log", message = "Runtime not ready. Running auto-setup\u2026" });
                await RunAutoSetupAsync();
                ready = await _runtimeSetup.IsReadyAsync();
                if (!ready)
                {
                    var (hdd3, hdgn3, hcn3, hcc3, hct3, htr3) = GetHardwareInfo();
                    Post(new { type = "init", darkMode = _detectDarkMode(), ready = false, error = "Setup failed. Check the logs and try again.", detectedDevice = hdd3, detectedGpuName = hdgn3, cpuName = hcn3, cpuCores = hcc3, cpuThreads = hct3, totalRam = htr3, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
                    return;
                }
            }

            // Unblock UI immediately — setup succeeded
            // Detect GPU first so the init message carries the real device (not _loadedDevice, which stays "cpu" until a model loads)
            var recommended = SttRuntimeOptions.RecommendedForThisPc;
            var (hdd4, hdgn4, hcn4, hcc4, hct4, htr4) = GetHardwareInfo();
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = false, model = GetCompositeName(), device = recommended.Provider, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames(), audioDeviceIndex = _audio.DeviceId, detectedDevice = hdd4, detectedGpuName = hdgn4, cpuName = hcn4, cpuCores = hcc4, cpuThreads = hct4, totalRam = htr4, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
            Post(new { type = "settings", settings = new {
                startup = _startup.IsEnabled(),
                language = _selectedLanguage,
                sfx = _sfx.Enabled,
                auto_paste = _autoPasteEnabled,
                theme = SettingsStore.Theme,
                audio_device = _audio.DeviceId,
                beam_size = _beamSize,
                temperature = _temperature,
                vad_filter = _vadFilter,
                no_speech_threshold = _noSpeechThreshold,
                log_prob_threshold = _logProbThreshold,
                best_of = _bestOf,
                repetition_penalty = _repetitionPenalty,
                no_repeat_ngram_size = _noRepeatNgramSize,
                length_penalty = _lengthPenalty,
                compression_ratio_threshold = _compressionRatioThreshold,
                prompt_reset_on_temperature = _promptResetOnTemperature,
                condition_on_previous_text = _conditionOnPreviousText,
                hotwords = _hotwords,
                hallucination_silence_threshold = _hallucinationSilenceThreshold,
            } });

            Post(new { type = "log", message = $"Detected GPU: {recommended.Provider}. Recommended model: {recommended.Model}" });

            // Phase 2: Model loading — non-fatal if it fails (user can load manually later)
            await LoadModelPhaseAsync(recommended);
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Setup failed: {ex.Message}" });
            var (hdd5, hdgn5, hcn5, hcc5, hct5, htr5) = GetHardwareInfo();
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, error = ex.Message, loaded = false, model = GetCompositeName(), device = SttRuntimeOptions.RecommendedForThisPc.Provider, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames(), audioDeviceIndex = _audio.DeviceId, detectedDevice = hdd5, detectedGpuName = hdgn5, cpuName = hcn5, cpuCores = hcc5, cpuThreads = hct5, totalRam = htr5, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
            Post(new { type = "status_update", text = "Setup failed", variant = "error" });
            Post(new { type = "notification", title = "Startup failed", message = ex.Message, variant = "error" });
        }
        finally
        {
            _healthCheckRunning = false;
        }
    }

    private static readonly string[] ModelsBySize = { "turbo", "large-v3", "medium", "small", "base", "tiny" };

    private async Task LoadModelPhaseAsync(SttRuntimeOptions recommended)
    {
        try
        {
            // Pick the best model that's actually on disk
            string preferredProvider = recommended.Provider;
            var (bestModel, actualProvider) = FindBestAvailableModel(preferredProvider, recommended.Model);

            if (bestModel is null || actualProvider is null)
            {
                Post(new { type = "log", message = $"No models downloaded. Go to Models page to download one." });
                Post(new { type = "status_update", text = "No model downloaded", variant = "warning" });
                SendModelsStatus();
                return;
            }

            var loadOpts = bestModel == recommended.Model && actualProvider == preferredProvider
                ? recommended with { Language = _selectedLanguage }
                : BuildRuntimeOptions($"{bestModel}-{actualProvider}", _selectedLanguage);

            Post(new { type = "log", message = $"Loading model {bestModel} on {actualProvider}\u2026" });
            Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });

            await _whisper.StartAsync(loadOpts);
            string actualDevice = _whisper.GetReportedDevice();
            if (actualDevice != actualProvider)
            {
                Post(new { type = "log", message = $"Model loaded on {actualDevice} instead of {actualProvider} — run Setup to fix GPU acceleration" });
                Post(new { type = "notification", title = "GPU acceleration unavailable", message = $"Model loaded on {actualDevice}. Run Setup to enable {actualProvider}.", variant = "warning" });
            }
            SetModelLoaded(bestModel, actualDevice);

            string loadedComposite = $"{bestModel}-{actualDevice}";
            var (hdd, hdgn, hcn, hcc, hct, htr) = GetHardwareInfo();
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = true, model = loadedComposite, device = actualDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames(), audioDeviceIndex = _audio.DeviceId, detectedDevice = hdd, detectedGpuName = hdgn, cpuName = hcn, cpuCores = hcc, cpuThreads = hct, totalRam = htr, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
            Post(new { type = "settings", settings = new {
                startup = _startup.IsEnabled(),
                language = _selectedLanguage,
                sfx = _sfx.Enabled,
                auto_paste = _autoPasteEnabled,
                theme = SettingsStore.Theme,
                audio_device = _audio.DeviceId,
                beam_size = _beamSize,
                temperature = _temperature,
                vad_filter = _vadFilter,
                no_speech_threshold = _noSpeechThreshold,
                log_prob_threshold = _logProbThreshold,
                best_of = _bestOf,
                repetition_penalty = _repetitionPenalty,
                no_repeat_ngram_size = _noRepeatNgramSize,
                length_penalty = _lengthPenalty,
                compression_ratio_threshold = _compressionRatioThreshold,
                prompt_reset_on_temperature = _promptResetOnTemperature,
                condition_on_previous_text = _conditionOnPreviousText,
                hotwords = _hotwords,
                hallucination_silence_threshold = _hallucinationSilenceThreshold,
            } });
            Post(new { type = "clear_history" });
            foreach (var entry in _history.Entries)
                Post(new { type = "history_entry", entry = new { action = TranscriptionHistory.ActionLabel(entry.Action), text = entry.Text, timestamp = entry.Timestamp.ToString("g"), ts = entry.Timestamp.ToString("O") } });
            _sfx.PlayModelReady();
            Post(new { type = "model_loaded", model = loadedComposite, device = actualDevice, note = ModelNotes.GetModelNote(bestModel, actualDevice) });
            Post(new { type = "status_update", text = "Ready", variant = "success" });
            Post(new { type = "log", message = $"Model loaded on {actualDevice}" });
            SendModelsStatus();
        }
        catch (Exception ex)
        {
            SetModelError(ex.Message);
            Post(new { type = "log", message = $"Model load failed: {ex.Message}" });
            var (hdd2, hdgn2, hcn2, hcc2, hct2, htr2) = GetHardwareInfo();
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = false, error = $"Model load failed: {ex.Message}", model = GetCompositeName(), device = _loadedDevice, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames(), audioDeviceIndex = _audio.DeviceId, detectedDevice = hdd2, detectedGpuName = hdgn2, cpuName = hcn2, cpuCores = hcc2, cpuThreads = hct2, totalRam = htr2, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
            Post(new { type = "settings", settings = new {
                startup = _startup.IsEnabled(),
                language = _selectedLanguage,
                sfx = _sfx.Enabled,
                auto_paste = _autoPasteEnabled,
                theme = SettingsStore.Theme,
                audio_device = _audio.DeviceId,
                beam_size = _beamSize,
                temperature = _temperature,
                vad_filter = _vadFilter,
                no_speech_threshold = _noSpeechThreshold,
                log_prob_threshold = _logProbThreshold,
                best_of = _bestOf,
                repetition_penalty = _repetitionPenalty,
                no_repeat_ngram_size = _noRepeatNgramSize,
                length_penalty = _lengthPenalty,
                compression_ratio_threshold = _compressionRatioThreshold,
                prompt_reset_on_temperature = _promptResetOnTemperature,
                condition_on_previous_text = _conditionOnPreviousText,
                hotwords = _hotwords,
                hallucination_silence_threshold = _hallucinationSilenceThreshold,
            } });
            Post(new { type = "status_update", text = $"Model load failed: {ex.Message}", variant = "error" });
            Post(new { type = "notification", title = "Model load failed", message = ex.Message, variant = "error" });
            SendModelsStatus();
        }
    }

    private (string? model, string? provider) FindBestAvailableModel(string preferredProvider, string preferredModel)
    {
        // Try the preferred provider first
        if (_whisper.IsModelDownloaded(preferredProvider, preferredModel))
            return (preferredModel, preferredProvider);

        foreach (var name in ModelsBySize)
        {
            if (_whisper.IsModelDownloaded(preferredProvider, name))
                return (name, preferredProvider);
        }

        // No model found for the preferred provider (e.g. DML/CUDA).
        // Fall back to CPU — always available, always has models.
        if (preferredProvider != "cpu")
        {
            // If the preferred model exists on CPU, use it
            string actualProvider = "cpu";

            if (_whisper.IsModelDownloaded(actualProvider, preferredModel))
                return (preferredModel, actualProvider);

            foreach (var name in ModelsBySize)
            {
                if (_whisper.IsModelDownloaded(actualProvider, name))
                    return (name, actualProvider);
            }
        }

        return (null, null);
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

    private async Task RunAutoSetupAndReportAsync()
    {
        await RunAutoSetupAsync();

        bool gpuOk = await _runtimeSetup.IsGpuProviderReadyAsync();
        bool ready = await _runtimeSetup.IsReadyAsync();

        var (hdd6, hdgn6, hcn6, hcc6, hct6, htr6) = GetHardwareInfo();
        if (ready)
        {
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = true, loaded = false, model = GetCompositeName(), device = _gpuDetect.Detect().provider, gpuName = _gpuDetect.GetGpuName(), audioDevices = AudioCaptureService.GetInputDeviceNames(), audioDeviceIndex = _audio.DeviceId, detectedDevice = hdd6, detectedGpuName = hdgn6, cpuName = hcn6, cpuCores = hcc6, cpuThreads = hct6, totalRam = htr6, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
        }
        else
        {
            Post(new { type = "init", darkMode = _detectDarkMode(), ready = false, error = "Setup still failed. Check the logs and try again.", detectedDevice = hdd6, detectedGpuName = hdgn6, cpuName = hcn6, cpuCores = hcc6, cpuThreads = hct6, totalRam = htr6, voice_profile = SettingsStore.SerializeProfile(SettingsStore.VoiceProfile), music_profile = SettingsStore.SerializeProfile(SettingsStore.MusicProfile) });
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private static long GetTotalPhysicalRam()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
            return (long)mem.ullTotalPhys;
        return 0;
    }

    private static (string name, int cores, int threads) GetCpuInfo()
    {
        string name = "";
        int cores = 0;
        int threads = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                name = obj["Name"]?.ToString()?.Trim() ?? "";
                cores = Convert.ToInt32(obj["NumberOfCores"]);
                threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                break;
            }
        }
        catch { }
        return (name, cores, threads);
    }

    private (string detectedDevice, string detectedGpuName, string cpuName, int cpuCores, int cpuThreads, long totalRam) GetHardwareInfo()
    {
        var detected = _gpuDetect.Detect();
        var cpu = GetCpuInfo();
        return (detected.provider, detected.gpuName, cpu.name, cpu.cores, cpu.threads, GetTotalPhysicalRam());
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    }

    private long GetModelSize(string name, string provider, bool downloaded)
    {
        if (downloaded)
        {
            if (provider is "dml")
            {
                string sherpaDir = Path.Combine(RuntimePaths.ModelsRoot, $"sherpa-onnx-whisper-{name}");
                long size = GetDirectorySize(sherpaDir);
                if (size > 0) return size;
            }
            else
            {
                string localCache = Path.Combine(RuntimePaths.ModelsRoot, $"models--Systran--faster-whisper-{name}");
                long size = GetDirectorySize(localCache);
                if (size > 0) return size;

                string userCache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache", "huggingface", "hub",
                    $"models--Systran--faster-whisper-{name}");
                size = GetDirectorySize(userCache);
                if (size > 0) return size;
            }
        }

        if (provider == "dml")
        {
            return name switch
            {
                "tiny" => 244L * 1024 * 1024,
                "base" => 431L * 1024 * 1024,
                "small" => 1282L * 1024 * 1024,
                "medium" => 3819L * 1024 * 1024,
                "large-v3" => 7585L * 1024 * 1024,
                "turbo" => 4076L * 1024 * 1024,
                _ => 0
            };
        }

        return name switch
        {
            "tiny" => 75L * 1024 * 1024,
            "base" => 141L * 1024 * 1024,
            "small" => 464L * 1024 * 1024,
            "medium" => 1460L * 1024 * 1024,
            "large-v3" => 2948L * 1024 * 1024,
            "turbo" => 809L * 1024 * 1024,
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
            "cuda" => "CUDA",
            "dml" => "DirectML",
            _ => provider
        };

        return $"{modelLabel} ({providerLabel})";
    }

    private void SendModelsStatus()
    {
        _gpuCache ??= _gpuDetect.Detect().provider;
        string detectedProvider = _gpuCache;
        var models = new List<object>();

        // Base models — always shown as CPU
        foreach (var name in CpuModelNames)
        {
            string composite = $"{name}-cpu";
            bool downloaded = _whisper.IsModelDownloaded("cpu", name);
            models.Add(new
            {
                name = composite,
                displayName = GetDisplayName(name, "cpu"),
                size = GetModelSize(name, "cpu", downloaded),
                downloaded,
                loaded = name == _loadedModel && _loadedDevice == "cpu" && _modelLoaded,
                provider = "cpu"
            });
        }

        // CUDA models — shown if CUDA GPU detected
        if (detectedProvider == "cuda")
        {
            foreach (var name in AllModelNames)
            {
                bool downloaded = _whisper.IsModelDownloaded("cuda", name);
                string composite = $"{name}-cuda";
                models.Add(new
                {
                    name = composite,
                    displayName = GetDisplayName(name, "cuda"),
                    size = GetModelSize(name, "cuda", downloaded),
                    downloaded,
                    loaded = name == _loadedModel && _loadedDevice == "cuda" && _modelLoaded,
                    provider = "cuda"
                });
            }
        }

        // DML models — shown if DirectML GPU detected
        if (detectedProvider == "dml")
        {
            foreach (var name in AllModelNames)
            {
                bool downloaded = _whisper.IsModelDownloaded("dml", name);
                string composite = $"{name}-dml";
                models.Add(new
                {
                    name = composite,
                    displayName = GetDisplayName(name, "dml"),
                    size = GetModelSize(name, "dml", downloaded),
                    downloaded,
                    loaded = name == _loadedModel && _loadedDevice == "dml" && _modelLoaded,
                    provider = "dml"
                });
            }
        }

        // Demucs model — always shown
        {
            bool demucsDownloaded = _whisper.IsModelDownloaded("demucs", "htdemucs");
            models.Add(new
            {
                name = "demucs-htdemucs",
                displayName = "Demucs Source Separator",
                size = 80_000_000L,
                downloaded = demucsDownloaded,
                loaded = false,
                provider = "demucs"
            });
        }

        Post(new { type = "models_status", models });
    }

    private async Task LoadModelAsync(string compositeName)
    {
        var (model, device) = SttRuntimeOptions.FromCompositeName(compositeName);

        if ((device is "dml" or "cuda") && !await _runtimeSetup.IsGpuProviderReadyAsync())
        {
            string msg = $"GPU packages not installed. Run Setup, then try again. (Expected provider: {device})";
            SetModelError(msg);
            Post(new { type = "log", message = $"Model load failed: {msg}" });
            Post(new { type = "status_update", text = "GPU not ready", variant = "error" });
            return;
        }

        Post(new { type = "status_update", text = "Loading model\u2026", variant = "warning" });
        Post(new { type = "log", message = $"Loading model {model} on {device}\u2026" });

        try
        {
            var loadOpts = BuildRuntimeOptions(compositeName, _selectedLanguage);
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
            _sfx.PlayError();
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
                Post(new { type = "transcription_result", text = "", meta = "No speech detected" , isPartial = false });
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

        _sfx.PlayTranscriptionDone();

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

    private static SttRuntimeOptions BuildRuntimeOptions(string compositeName, string language = "en")
    {
        var (model, device) = SttRuntimeOptions.FromCompositeName(compositeName);

        if (device == "cuda")
            return new SttRuntimeOptions(model, "cuda", "float16", 4, 1, 1) { Provider = "cuda", Language = language };

        if (device == "dml")
            return new SttRuntimeOptions(model, "dml", "float16", 4, 1, 1) { Provider = "dml", Language = language };

        int beam = GetRecommendedBeamSize(model);
        return new SttRuntimeOptions(model, "cpu", "int8", 6, 1, beam) { Provider = "cpu", Language = language };
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

    private async Task ShowFolderPickerAsync(string purpose)
    {
        await _webView.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = purpose == "models"
                    ? "Select models storage directory"
                    : "Select a directory";
                dialog.UseDescriptionForTitle = true;

                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                    Post(new { type = "directory_picked", path = dialog.SelectedPath });
                else
                    Post(new { type = "directory_picked", path = (string?)null });
            }
            catch (Exception ex)
            {
                Post(new { type = "log", message = $"Folder picker error: {ex.Message}" });
                Post(new { type = "directory_picked", path = (string?)null });
            }
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        var (available, version) = await _updateService.CheckForUpdatesAsync();
        Post(new
        {
            type = "update_available",
            available,
            version,
        });
        if (available)
        {
            Post(new { type = "log", message = $"Update available: {version}" });
        }
    }

    private async Task DownloadUpdateAsync()
    {
        Post(new { type = "update_download_started" });
        try
        {
            Action<int>? progressAction = p =>
            {
                Post(new { type = "update_download_progress", progress = p / 100.0 });
            };
            await _updateService.DownloadUpdateAsync(progressAction);
            Post(new { type = "update_download_complete" });
        }
        catch (Exception ex)
        {
            Post(new { type = "update_download_error", error = ex.Message });
        }
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
        _phoneMic.RecordingChanged -= _onPhoneMicRecording;
        _whisper.DownloadProgress -= _onDownloadProgress;
        _runtimeSetup.StepsChanged -= _onSetupSteps;

        CancellationTokenSource? oldCts;
        lock (_ctsLock)
        {
            oldCts = _fileTranscribeCts;
            _fileTranscribeCts = null;
        }
        oldCts?.Cancel();
        oldCts?.Dispose();
        try { _fileTranscribeGate.Dispose(); } catch { }
    }
}
