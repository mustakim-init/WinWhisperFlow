using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public sealed class WhisperBridgeService : IDisposable
{
    private readonly List<string> _stderrLines = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private Process? _process;
    private SttRuntimeOptions _options = SttRuntimeOptions.RecommendedForThisPc;
    private CancellationTokenSource? _loadCts;
    private bool _workerReady;

    public async Task StartAsync(SttRuntimeOptions? options = null)
    {
        _loadCts ??= new CancellationTokenSource();
        await _startGate.WaitAsync(_loadCts.Token);
        try
        {
            if (options is not null)
            {
                _options = options;
            }

            if (_process is { HasExited: false } && _workerReady)
            {
                return;
            }

            if (_process is { HasExited: false })
            {
                KillWorker();
            }

            _stderrLines.Clear();
            CancellationToken ct = _loadCts?.Token ?? CancellationToken.None;

            if (_options.Device is "cuda" or "dml")
            {
                await EnsureGpuModelDownloadedAsync(ct);
                ct.ThrowIfCancellationRequested();
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
                    _stderrLines.Add(args.Data);
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
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        KillWorker();
        _options = options;
        await StartAsync(options);
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, string? language = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StartAsync();
            if (_process is null)
            {
                throw new InvalidOperationException("STT worker is not running.");
            }

            var request = new
            {
                type = "transcribe",
                audio_path = audioPath,
                language
            };

            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            string? line = await ReadLineWithTimeoutAsync(_process.StandardOutput, TimeSpan.FromSeconds(90), ct);
            if (line is null)
            {
                KillWorker();
                throw new InvalidOperationException("STT worker did not return a result within 90 seconds.");
            }

            using JsonDocument doc = JsonDocument.Parse(line.TrimStart('\uFEFF'));
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException(error.GetString());
            }

            return new TranscriptionResult(
                root.GetProperty("text").GetString() ?? "",
                root.TryGetProperty("language", out JsonElement resultLanguage) ? resultLanguage.GetString() ?? "" : "",
                root.TryGetProperty("language_probability", out JsonElement languageProbability) ? languageProbability.GetDouble() : 0,
                root.TryGetProperty("segment_count", out JsonElement segmentCount) ? segmentCount.GetInt32() : 0,
                root.TryGetProperty("avg_log_probability", out JsonElement avgLogProbability) && avgLogProbability.ValueKind != JsonValueKind.Null
                    ? avgLogProbability.GetDouble()
                    : null,
                root.TryGetProperty("no_speech_probability", out JsonElement noSpeechProbability) && noSpeechProbability.ValueKind != JsonValueKind.Null
                    ? noSpeechProbability.GetDouble()
                    : null);
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
            if (_process is null || _process.HasExited) return false;

            var request = new { type = "ping" };
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            
            string? line = await ReadLineWithTimeoutAsync(_process.StandardOutput, TimeSpan.FromSeconds(5), ct);
            if (line is null)
            {
                KillWorker();
                return false;
            }

            using JsonDocument doc = JsonDocument.Parse(line.TrimStart('\uFEFF'));
            return doc.RootElement.TryGetProperty("pong", out JsonElement pong) && pong.GetBoolean();
        }
        catch
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken ct = default)
    {
        if (_process is null)
        {
            return;
        }

        TimeSpan readyTimeout = _options.Device is "cuda" or "dml"
            ? TimeSpan.FromMinutes(15)
            : TimeSpan.FromSeconds(120);

        string? line;
        try
        {
            line = await ReadLineWithTimeoutAsync(_process.StandardOutput, readyTimeout, ct);
        }
        catch (OperationCanceledException)
        {
            KillWorker();
            throw new InvalidOperationException("Model load was cancelled by user.");
        }

        if (line is null)
        {
            KillWorker();
            throw new InvalidOperationException(BuildStartupError("STT worker failed to initialize."));
        }

        using JsonDocument doc = JsonDocument.Parse(line.TrimStart('\uFEFF'));
        if (doc.RootElement.TryGetProperty("ready", out JsonElement ready) && ready.GetBoolean())
        {
            _workerReady = true;
            return;
        }

        KillWorker();
        throw new InvalidOperationException(BuildStartupError("STT worker did not report ready."));
    }

    private string BuildStartupError(string fallback)
    {
        string stderr = string.Join(" ", _stderrLines.TakeLast(5));
        if (stderr.Contains("No module named 'faster_whisper'", StringComparison.OrdinalIgnoreCase))
        {
            return "Python dependency missing. Run: .\\.venv\\Scripts\\python -m pip install -r stt_engine\\requirements.txt";
        }

        if (stderr.Contains("No module named 'sherpa_onnx'", StringComparison.OrdinalIgnoreCase))
        {
            return "GPU Python dependency missing. Run Setup to install GPU packages.";
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return $"{fallback} Python said: {stderr}";
        }

        return fallback;
    }

    private async Task EnsureGpuModelDownloadedAsync(CancellationToken ct = default)
    {
        string modelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models",
            $"sherpa-onnx-whisper-{_options.Model}");
        string prefix = _options.Model;

        bool HasFile(string name) =>
            Directory.Exists(modelsDir) && File.Exists(Path.Combine(modelsDir, name));

        bool hasFp32 = HasFile($"{prefix}-encoder.onnx") &&
                       HasFile($"{prefix}-decoder.onnx") &&
                       HasFile($"{prefix}-tokens.txt");

        bool hasInt8 = HasFile($"{prefix}-encoder.int8.onnx") &&
                       HasFile($"{prefix}-decoder.int8.onnx") &&
                       HasFile($"{prefix}-tokens.txt");

        if (hasFp32 || hasInt8)
            return;

        ct.ThrowIfCancellationRequested();

        string downloadScript = ResolveDownloadScriptPath();
        string python = ResolvePython();

        string persistentModelsDir = Path.Combine(RuntimePaths.RuntimeRoot, "models");
        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"\"{downloadScript}\" {_options.Model} \"{persistentModelsDir}\"",
            WorkingDirectory = Path.GetDirectoryName(downloadScript) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start GPU model download.");

        // Wait for download or cancellation
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("Model download was cancelled.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("GPU model download failed. Check the console window for details.");
        }
    }

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
            ?? throw new FileNotFoundException(
                "Python interpreter not found. Run setup first to create a virtual environment.");
    }

    private string ResolveWorkerPath()
    {
        string workerName = _options.Device is "cuda" or "dml"
            ? "whisper_worker_gpu"
            : "whisper_worker";

        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "dist", $"{workerName}.exe"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "dist", $"{workerName}.exe"),
            Path.Combine(AppContext.BaseDirectory, "stt_engine", $"{workerName}.py"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", $"{workerName}.py")
        };

        string? scriptPath = candidates.FirstOrDefault(File.Exists);
        if (scriptPath is null)
        {
            throw new FileNotFoundException($"Could not find stt_engine executable or script for {workerName}.");
        }

        return scriptPath;
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(
        StreamReader reader, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
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
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        _process?.Dispose();
        _process = null;
        _workerReady = false;
    }
}
