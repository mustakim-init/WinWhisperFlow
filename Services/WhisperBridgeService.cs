using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public sealed class WhisperBridgeService : IDisposable
{
    private readonly List<string> _stderrLines = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();
    private readonly ConcurrentDictionary<string, Process> _downloadProcesses = new();
    private readonly object _loadCtsLock = new();
    private Process? _process;
    private SttRuntimeOptions _options = SttRuntimeOptions.RecommendedForThisPc;
    private CancellationTokenSource? _loadCts;
    private bool _workerReady;
    private string _reportedDevice = "cpu";

    public event EventHandler<ModelDownloadProgressArgs>? DownloadProgress;

    public record ModelDownloadProgressArgs(string Model, long Downloaded, long Total, string Status, string? Error = null, string? CompositeName = null, double Speed = 0);

    public async Task StartAsync(SttRuntimeOptions? options = null)
    {
        lock (_loadCtsLock) { _loadCts ??= new CancellationTokenSource(); }
        await _startGate.WaitAsync(_loadCts.Token);
        try
        {
            if (options is not null) _options = options;

            if (_process is { HasExited: false } && _workerReady) return;

            if (_process is { HasExited: false }) KillWorker();

            _stderrLines.Clear();
            CancellationToken ct = _loadCts?.Token ?? CancellationToken.None;

            if (_options.Device is "cuda" or "dml")
            {
                string composite = $"{_options.Model}-{_options.Device}";
                await EnsureGpuModelDownloadedAsync(composite, ct);
                ct.ThrowIfCancellationRequested();
                FireModelDownloaded(_options.Model, composite);
            }

            string scriptPath = ResolveWorkerPath();
            bool isExe = scriptPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            var startInfo = new ProcessStartInfo
            {
                FileName = isExe ? scriptPath : ResolvePython(),
                Arguments = isExe ? "" : $"\"{scriptPath}\"",
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["WINWHISPER_MODEL"] = _options.Model;
            startInfo.Environment["WINWHISPER_DEVICE"] = _options.Device;
            startInfo.Environment["WINWHISPER_COMPUTE"] = _options.ComputeType;
            startInfo.Environment["WINWHISPER_CPU_THREADS"] = _options.CpuThreads.ToString();
            startInfo.Environment["WINWHISPER_NUM_WORKERS"] = _options.NumWorkers.ToString();
            startInfo.Environment["WINWHISPER_BEAM_SIZE"] = _options.BeamSize.ToString();
            startInfo.Environment["WINWHISPER_LANGUAGE"] = "en";
            startInfo.Environment["WINWHISPER_MODELS_DIR"] = Path.Combine(RuntimePaths.RuntimeRoot, "models");
            startInfo.Environment["WINWHISPER_VAD_FILTER"] = _options.VadFilter ? "1" : "0";
            startInfo.Environment["WINWHISPER_VAD_MIN_SILENCE"] = _options.VadMinSilenceDurationMs.ToString();
            startInfo.Environment["WINWHISPER_NO_SPEECH_THRESHOLD"] = _options.NoSpeechThreshold.ToString("F4");
            startInfo.Environment["WINWHISPER_LOG_PROB_THRESHOLD"] = _options.LogProbThreshold.ToString("F4");

            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Python STT worker.");
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    lock (_stderrLines)
                    {
                        if (_stderrLines.Count >= 1000)
                            _stderrLines.RemoveAt(0);
                        _stderrLines.Add(args.Data);
                    }
                    Debug.WriteLine(args.Data);
                }
            };
            _process.BeginErrorReadLine();
            await WaitUntilReadyAsync(ct);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task RestartAsync(SttRuntimeOptions options)
    {
        CancellationToken ct;
        lock (_loadCtsLock)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            ct = _loadCts.Token;
        }
        await _gate.WaitAsync(ct);
        try
        {
            KillWorker();
            _options = options;
            await StartAsync(options);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, string? language = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StartAsync();
            var proc = _process;
            if (proc is null || proc.HasExited) throw new InvalidOperationException("STT worker is not running.");

            var request = new { type = "transcribe", audio_path = audioPath, language };
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            string? line = await ReadLineWithTimeoutAsync(proc.StandardOutput, TimeSpan.FromSeconds(90), ct);
            if (line is null)
            {
                KillWorker();
                throw new InvalidOperationException("STT worker did not return a result within 90 seconds.");
            }

            JsonDocument? doc;
            try { doc = JsonDocument.Parse(line.TrimStart('\uFEFF')); }
            catch (JsonException)
            {
                KillWorker();
                string preview = line.Length > 200 ? line[..200] + "..." : line;
                throw new InvalidOperationException($"STT worker returned invalid JSON: {preview}");
            }
            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("error", out JsonElement error))
                    throw new InvalidOperationException(error.GetString());

                return new TranscriptionResult(
                    root.GetProperty("text").GetString() ?? "",
                    root.TryGetProperty("language", out JsonElement resultLanguage) ? resultLanguage.GetString() ?? "" : "",
                    root.TryGetProperty("language_probability", out JsonElement languageProbability) ? languageProbability.GetDouble() : 0,
                    root.TryGetProperty("segment_count", out JsonElement segmentCount) ? segmentCount.GetInt32() : 0,
                    root.TryGetProperty("avg_log_probability", out JsonElement avgLogProbability) && avgLogProbability.ValueKind != JsonValueKind.Null
                        ? avgLogProbability.GetDouble() : null,
                    root.TryGetProperty("no_speech_probability", out JsonElement noSpeechProbability) && noSpeechProbability.ValueKind != JsonValueKind.Null
                        ? noSpeechProbability.GetDouble() : null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var proc = _process;
            if (proc is null || proc.HasExited) return false;
            var request = new { type = "ping" };
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            string? line = await ReadLineWithTimeoutAsync(proc.StandardOutput, TimeSpan.FromSeconds(5), ct);
            if (line is null) { KillWorker(); return false; }
            using JsonDocument doc = JsonDocument.Parse(line.TrimStart('\uFEFF'));
            return doc.RootElement.TryGetProperty("pong", out JsonElement pong) && pong.GetBoolean();
        }
        catch { return false; }
        finally { _gate.Release(); }
    }

    public async Task DownloadModelAsync(string compositeName, CancellationToken ct = default)
    {
        var (model, provider) = SttRuntimeOptions.FromCompositeName(compositeName);
        bool isGpu = provider is "cuda" or "dml";

        var dlCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_activeDownloads.TryAdd(compositeName, dlCts))
        {
            DownloadProgress?.Invoke(this, new(model, 0, 0, "error", "Download already in progress", CompositeName: compositeName));
            return;
        }

        // Notify UI immediately that download has started
        DownloadProgress?.Invoke(this, new(model, 0, 0, "downloading", CompositeName: compositeName));

        try
        {
            if (isGpu)
            {
                var original = _options;
                _options = _options with { Model = model, Device = provider };

                try
                {
                    await EnsureGpuModelDownloadedAsync(compositeName, dlCts.Token);
                    var modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{model}");
                    long totalSize = 0;
                    if (Directory.Exists(modelsDir))
                        totalSize = Directory.GetFiles(modelsDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

                    DownloadProgress?.Invoke(this, new(model, totalSize, totalSize, "done", CompositeName: compositeName));
                }
                catch (Exception ex)
                {
                    DeleteModel(compositeName);
                    string errMsg = ex is OperationCanceledException ? "Download cancelled" : ex.Message;
                    DownloadProgress?.Invoke(this, new(model, 0, 0, "error", errMsg, CompositeName: compositeName));
                }
                finally
                {
                    _options = original;
                }
            }
            else
            {
                try
                {
                    await EnsureCpuModelDownloadedAsync(compositeName, dlCts.Token);
                    DownloadProgress?.Invoke(this, new(model, 1, 1, "done", CompositeName: compositeName));
                }
                catch (Exception ex)
                {
                    DeleteModel(compositeName);
                    string errMsg = ex is OperationCanceledException ? "Download cancelled" : ex.Message;
                    DownloadProgress?.Invoke(this, new(model, 0, 0, "error", errMsg, CompositeName: compositeName));
                }
            }
        }
        finally
        {
            if (_activeDownloads.TryRemove(compositeName, out var cts))
                cts.Dispose();
        }
    }

    public void CancelDownload(string compositeName)
    {
        if (_activeDownloads.TryRemove(compositeName, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_downloadProcesses.TryRemove(compositeName, out var proc))
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            proc.Dispose();
        }

        DeleteModel(compositeName);
    }

    public bool IsModelDownloaded(string provider, string model)
    {
        if (provider is "cuda" or "dml")
        {
            return GpuModelIsComplete(model);
        }

        return CpuModelIsComplete(model);
    }

    public void DeleteModel(string compositeName)
    {
        var (model, provider) = SttRuntimeOptions.FromCompositeName(compositeName);

        try
        {
            if (provider is "cuda" or "dml")
            {
                string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{model}");
                if (Directory.Exists(modelsDir))
                    Directory.Delete(modelsDir, recursive: true);
            }
            else
            {
                // CPU model cleanup
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache", "huggingface", "hub");
                string modelDir = $"models--Systran--faster-whisper-{model}";
                string cpuPath = Path.Combine(cacheDir, modelDir);
                if (Directory.Exists(cpuPath))
                    Directory.Delete(cpuPath, recursive: true);

                string altPath = Path.Combine(RuntimePaths.RuntimeRoot, "models", modelDir);
                if (Directory.Exists(altPath))
                    Directory.Delete(altPath, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete model {compositeName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if GPU model files are corrupt (should be re-downloaded).
    /// Returns false if files are healthy or don't exist (no action needed).
    /// </summary>
    public async Task<bool> VerifyGpuModelFilesAsync(string model)
    {
        string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{model}");
        if (!Directory.Exists(modelsDir)) return false;

        string prefix = model;
        string encoder = Path.Combine(modelsDir, $"{prefix}-encoder.onnx");
        string decoder = Path.Combine(modelsDir, $"{prefix}-decoder.onnx");
        string tokens = Path.Combine(modelsDir, $"{prefix}-tokens.txt");

        // Check all required files exist
        if (!File.Exists(encoder) || !File.Exists(decoder) || !File.Exists(tokens))
            return true;

        // Check that small .onnx shells have their external .weights files.
        // Some models (turbo, large-v3) ship thin encoder/decoder <10 MB that
        // load weights from a corresponding .weights file in the same directory.
        long encoderSize = new FileInfo(encoder).Length;
        long decoderSize = new FileInfo(decoder).Length;

        if (encoderSize < 10L * 1024 * 1024)
        {
            string encoderWeights = Path.Combine(modelsDir, $"{prefix}-encoder.weights");
            if (!File.Exists(encoderWeights))
                return true;
        }

        if (decoderSize < 10L * 1024 * 1024)
        {
            string decoderWeights = Path.Combine(modelsDir, $"{prefix}-decoder.weights");
            if (!File.Exists(decoderWeights))
                return true;
        }

        // Self-contained .onnx files (>10 MB): verify against expected minimum per-file sizes.
        // Models that use external .weights files (shells <10 MB) are checked above.
        long encoderMin = model switch
        {
            "tiny" => 20L * 1024 * 1024,
            "base" => 80L * 1024 * 1024,
            "small" => 200L * 1024 * 1024,
            _ => 10L * 1024 * 1024
        };

        long decoderMin = model switch
        {
            "tiny" => 50L * 1024 * 1024,
            "base" => 170L * 1024 * 1024,
            "small" => 400L * 1024 * 1024,
            _ => 10L * 1024 * 1024
        };

        if (encoderSize >= 10L * 1024 * 1024 && encoderSize < encoderMin)
            return true;
        if (decoderSize >= 10L * 1024 * 1024 && decoderSize < decoderMin)
            return true;

        return false;
    }

    public string GetLoadedModel() => _options.Model;
    public string GetLoadedDevice() => _options.Device;
    public string GetReportedDevice() => _reportedDevice;

    private async Task WaitUntilReadyAsync(CancellationToken ct = default)
    {
        var proc = _process;
        if (proc is null) return;

        TimeSpan readyTimeout = _options.Device is "cuda" or "dml"
            ? TimeSpan.FromMinutes(15) : TimeSpan.FromSeconds(120);

        string? line;
        try { line = await ReadLineWithTimeoutAsync(proc.StandardOutput, readyTimeout, ct); }
        catch (OperationCanceledException)
        {
            KillWorker();
            throw new InvalidOperationException("Model load was cancelled by user.");
        }

        if (line is null) { KillWorker(); throw new InvalidOperationException(BuildStartupError("STT worker failed to initialize.")); }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line.TrimStart('\uFEFF')); }
        catch (JsonException)
        {
            KillWorker();
            string preview = line.Length > 200 ? line[..200] + "..." : line;
            throw new InvalidOperationException($"STT worker returned invalid JSON on startup: {preview}");
        }
        using (doc)
        {
            if (doc.RootElement.TryGetProperty("ready", out JsonElement ready) && ready.GetBoolean())
            {
                _workerReady = true;
                _reportedDevice = doc.RootElement.TryGetProperty("device", out var devProp)
                    ? NormalizeDevice(devProp.GetString() ?? "cpu")
                    : _options.Device;
                return;
            }
        }

        KillWorker();
        throw new InvalidOperationException(BuildStartupError("STT worker did not report ready."));
    }

    private string BuildStartupError(string fallback)
    {
        string stderr = string.Join(" ", _stderrLines.TakeLast(20));
        if (stderr.Contains("No module named", StringComparison.OrdinalIgnoreCase))
        {
            if (stderr.Contains("faster_whisper", StringComparison.OrdinalIgnoreCase))
                return "Python dependency missing (faster_whisper). Run Setup to install packages.";
            if (stderr.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase))
                return "GPU Python dependency missing (onnxruntime). Run Setup to install GPU packages.";
            return "Python dependency missing. Run Setup to install packages.";
        }
        if (!string.IsNullOrWhiteSpace(stderr))
            return $"{fallback} Python said: {stderr}";
        return fallback;
    }

    private static bool GpuModelIsComplete(string model)
    {
        string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{model}");
        string prefix = model;

        if (!Directory.Exists(modelsDir)) return false;

        bool HasFile(string name) => File.Exists(Path.Combine(modelsDir, name));

        // Must have the core files in at least one format
        bool hasFp32 = HasFile($"{prefix}-encoder.onnx") && HasFile($"{prefix}-decoder.onnx") && HasFile($"{prefix}-tokens.txt");
        bool hasInt8 = HasFile($"{prefix}-encoder.int8.onnx") && HasFile($"{prefix}-decoder.int8.onnx") && HasFile($"{prefix}-tokens.txt");
        if (!hasFp32 && !hasInt8) return false;

        // If .onnx files are shells (<10 MB), they need corresponding .weights
        string encoderPath = Path.Combine(modelsDir, $"{prefix}-encoder.onnx");
        if (File.Exists(encoderPath) && new FileInfo(encoderPath).Length < 10L * 1024 * 1024)
        {
            if (!HasFile($"{prefix}-encoder.weights")) return false;
        }

        string decoderPath = Path.Combine(modelsDir, $"{prefix}-decoder.onnx");
        if (File.Exists(decoderPath) && new FileInfo(decoderPath).Length < 10L * 1024 * 1024)
        {
            if (!HasFile($"{prefix}-decoder.weights")) return false;
        }

        return true;
    }

    private async Task EnsureGpuModelDownloadedAsync(string compositeName, CancellationToken ct = default)
    {
        if (GpuModelIsComplete(_options.Model)) return;

        // Delete existing files so the download script doesn't treat partial
        // downloads (from interrupted sessions) as "cached" and skip them.
        string cleanDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{_options.Model}");
        if (Directory.Exists(cleanDir))
        {
            Directory.Delete(cleanDir, recursive: true);
        }

        ct.ThrowIfCancellationRequested();

        string downloadScript = ResolveDownloadScriptPath();
        string python = ResolvePython();
        string persistentModelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models");

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"-u \"{downloadScript}\" {_options.Model} \"{persistentModelsDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start download script.");

        _downloadProcesses[compositeName] = process;

        try
        {
            process.OutputDataReceived += (_, _) => { };
            process.BeginOutputReadLine();

            long totalCompleted = 0;
            var progressLock = new object();
            var stderrCapture = new System.Text.StringBuilder();

            process.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data))
                    return;
                stderrCapture.AppendLine(args.Data);
                try
                {
                    using var doc = JsonDocument.Parse(args.Data);
                    var root = doc.RootElement;
                    string type = root.GetProperty("type").GetString() ?? "";

                    if (type == "dl_progress")
                    {
                        long downloaded = root.GetProperty("downloaded").GetInt64();
                        long total = root.GetProperty("total").GetInt64();
                        double speed = root.TryGetProperty("speed", out var speedProp) ? speedProp.GetDouble() : 0.0;
                        long cumDown, cumTotal;
                        lock (progressLock)
                        {
                            cumDown = totalCompleted + downloaded;
                            cumTotal = totalCompleted + max(total, 1);
                        }
                        DownloadProgress?.Invoke(this, new(_options.Model, cumDown, cumTotal, "downloading", CompositeName: compositeName, Speed: speed));
                    }
                    else if (type == "file_done")
                    {
                        long fileSize = root.GetProperty("size").GetInt64();
                        lock (progressLock)
                        {
                            totalCompleted += fileSize;
                        }
                        DownloadProgress?.Invoke(this, new(_options.Model, totalCompleted, totalCompleted, "downloading", CompositeName: compositeName));
                    }
                    else if (type == "file_cached")
                    {
                        // Cached files are already on disk — count their size toward cumulative progress
                        string? fileName = root.TryGetProperty("file", out var fileProp) ? fileProp.GetString() : null;
                        if (fileName is not null)
                        {
                            string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{_options.Model}");
                            string filePath = Path.Combine(modelsDir, fileName);
                            if (File.Exists(filePath))
                            {
                                long fileSize = new FileInfo(filePath).Length;
                                lock (progressLock)
                                {
                                    totalCompleted += fileSize;
                                }
                                DownloadProgress?.Invoke(this, new(_options.Model, totalCompleted, totalCompleted, "downloading", CompositeName: compositeName));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Download stderr parse] {ex.Message} — raw: {args.Data}");
                }
            };
            process.BeginErrorReadLine();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(30));
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("Model download was cancelled or timed out.");
            }

            if (process.ExitCode != 0)
            {
                string stderr = stderrCapture.ToString();
                string detail = stderr.Length > 200 ? stderr[..200] + "..." : stderr;
                throw new InvalidOperationException($"GPU model download failed. Python said: {detail}");
            }

        }
        finally
        {
            _downloadProcesses.TryRemove(compositeName, out _);
            process.Dispose();
        }
    }

    private static bool CpuModelIsComplete(string model)
    {
        string[] cacheRoots = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface", "hub"),
            Path.Combine(RuntimePaths.RuntimeRoot, "models")
        };

        foreach (var root in cacheRoots)
        {
            string modelDir = Path.Combine(root, $"models--Systran--faster-whisper-{model}");
            if (!Directory.Exists(modelDir)) continue;

            string refsMain = Path.Combine(modelDir, "refs", "main");
            if (!File.Exists(refsMain)) continue;

            string commitHash = File.ReadAllText(refsMain).Trim();
            if (string.IsNullOrEmpty(commitHash)) continue;

            string snapshotDir = Path.Combine(modelDir, "snapshots", commitHash);
            if (!Directory.Exists(snapshotDir)) continue;

            string modelBin = Path.Combine(snapshotDir, "model.bin");
            string configJson = Path.Combine(snapshotDir, "config.json");

            if (File.Exists(modelBin) && new FileInfo(modelBin).Length > 0 && File.Exists(configJson))
            {
                return true;
            }
        }

        return false;
    }

    private async Task EnsureCpuModelDownloadedAsync(string compositeName, CancellationToken ct = default)
    {
        var (model, _) = SttRuntimeOptions.FromCompositeName(compositeName);
        if (CpuModelIsComplete(model)) return;

        ct.ThrowIfCancellationRequested();

        string downloadScript = ResolveCpuDownloadScriptPath();
        string python = ResolvePython();
        string persistentModelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models");

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"-u \"{downloadScript}\" {model} \"{persistentModelsDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start CPU download script.");

        _downloadProcesses[compositeName] = process;

        try
        {
            process.OutputDataReceived += (_, _) => { };
            process.BeginOutputReadLine();

            var stderrCapture = new System.Text.StringBuilder();

            process.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data))
                    return;
                stderrCapture.AppendLine(args.Data);
                try
                {
                    using var doc = JsonDocument.Parse(args.Data);
                    var root = doc.RootElement;
                    string type = root.GetProperty("type").GetString() ?? "";

                    if (type == "dl_progress")
                    {
                        long downloaded = root.GetProperty("downloaded").GetInt64();
                        long total = root.GetProperty("total").GetInt64();
                        double speed = root.TryGetProperty("speed", out var speedProp) ? speedProp.GetDouble() : 0.0;
                        DownloadProgress?.Invoke(this, new(model, downloaded, total, "downloading", CompositeName: compositeName, Speed: speed));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CPU Download stderr parse] {ex.Message} — raw: {args.Data}");
                }
            };
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            if (process.ExitCode != 0)
            {
                string stderr = stderrCapture.ToString();
                string detail = stderr.Length > 200 ? stderr[..200] + "..." : stderr;
                throw new InvalidOperationException($"CPU model download failed. Python said: {detail}");
            }
        }
        finally
        {
            _downloadProcesses.TryRemove(compositeName, out _);
            process.Dispose();
        }
    }

    private static string ResolveCpuDownloadScriptPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "download_cpu_model.py"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "download_cpu_model.py")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException("Could not find stt_engine\\download_cpu_model.py.");
    }

    private static long max(long a, long b) => a > b ? a : b;

    private static string ResolveDownloadScriptPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "download_gpu_model.py"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "download_gpu_model.py")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException("Could not find stt_engine\\download_gpu_model.py.");
    }

    private static string ResolvePython()
    {
        string[] candidates =
        {
            RuntimePaths.UserVenvPython,
            Path.Combine(Environment.CurrentDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Python interpreter not found. Run setup first.");
    }

    private string ResolveWorkerPath()
    {
        string workerName = _options.Device is "cuda" or "dml" ? "whisper_worker_gpu" : "whisper_worker";
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "dist", $"{workerName}.exe"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "dist", $"{workerName}.exe"),
            Path.Combine(AppContext.BaseDirectory, "stt_engine", $"{workerName}.py"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", $"{workerName}.py")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException($"Could not find stt_engine executable or script for {workerName}.");
    }

    private void FireModelDownloaded(string model, string composite)
    {
        string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models", $"sherpa-onnx-whisper-{model}");
        long totalSize = 0;
        if (Directory.Exists(modelsDir))
            totalSize = Directory.GetFiles(modelsDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        DownloadProgress?.Invoke(this, new(model, totalSize, totalSize, "done", CompositeName: composite));
    }

    private static string NormalizeDevice(string device) => device switch
    {
        "CPUExecutionProvider" => "cpu",
        "CUDAExecutionProvider" => "cuda",
        "DmlExecutionProvider" => "dml",
        _ => device
    };

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { return await reader.ReadLineAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    private static async Task<int> RunSilentAsync(string fileName, string arguments, string? workingDir, int timeoutMs, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return -1;

        // Drain stdout/stderr asynchronously to prevent deadlock from full pipe buffers
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return -1;
        }
    }

    public void Dispose()
    {
        // Cancel all active downloads
        foreach (var kvp in _activeDownloads)
        {
            if (_activeDownloads.TryRemove(kvp.Key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        // Kill all tracked download processes
        foreach (var kvp in _downloadProcesses)
        {
            if (_downloadProcesses.TryRemove(kvp.Key, out var proc))
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                proc.Dispose();
            }
        }

        lock (_loadCtsLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
        }

        KillWorker();
        _process?.Dispose();
        try { _gate.Release(); } catch { }
        _gate.Dispose();
        try { _startGate.Release(); } catch { }
        _startGate.Dispose();
    }

    private void KillWorker()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.CancelErrorRead();
                try { _process.CancelOutputRead(); } catch { }
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _process?.Dispose();
        _process = null;
        _workerReady = false;
    }
}
