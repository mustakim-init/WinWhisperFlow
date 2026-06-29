using System.Diagnostics;
using System.IO;

namespace WinWhisperFlow.Services;

public sealed class RuntimeSetupService
{
    public record SetupStep(string Id, string Label, string Status, string? Error = null);

    public event EventHandler<IReadOnlyList<SetupStep>>? StepsChanged;

    public async Task<bool> IsReadyAsync()
    {
        string python = ResolvePython();
        if (python is null) return false;

        // Check that basic Python packages are importable
        string code = "import sys; import faster_whisper; import requests; import soundfile";
        return await RunSilentAsync(python, $"-c \"{code}\"", null, 30000) == 0;
    }

    public Task<bool> IsGpuProviderReadyAsync()
    {
        var gpu = new GpuDetectionService();
        var (provider, _) = gpu.Detect();
        return Task.FromResult(provider is "cuda" or "dml");
    }

    private static string ResolvePython()
    {
        string[] candidates =
        [
            RuntimePaths.RuntimePython,
            RuntimePaths.UserVenvPython,
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "python", "python.exe"),
            Path.Combine(Environment.CurrentDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Python interpreter not found. Reinstall WinWhisperFlow.");
    }

    private static string ResolveBundledPython()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "python", "python.exe"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "python", "python.exe"),
            "python",
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Python interpreter not found. Reinstall WinWhisperFlow.");
    }

    public async Task AutoSetupAsync(CancellationToken ct = default)
    {
        var steps = new List<SetupStep>
        {
            new("python", "Setting up Python runtime...", "pending"),
            new("venv", "Creating virtual environment...", "pending"),
            new("packages", "Installing dependencies...", "pending"),
            new("gpu-packages", "Installing GPU dependencies...", "pending"),
            new("gpu", "Detecting GPU...", "pending"),
        };

        void Emit() => StepsChanged?.Invoke(this, steps.AsReadOnly());

        // Step 0: Find bundled Python
        steps[0] = steps[0] with { Status = "running" };
        Emit();

        string bundledPython = ResolveBundledPython();
        steps[0] = steps[0] with { Status = "done" };
        Emit();
        ct.ThrowIfCancellationRequested();

        // Step 1: Copy bundled Python to user-writable location if needed
        steps[1] = steps[1] with { Status = "running" };
        Emit();

        string pythonDir = Path.Combine(RuntimePaths.RuntimeRoot, "python");
        string userPythonPath = Path.Combine(pythonDir, "python.exe");

        if (!File.Exists(userPythonPath))
        {
            Directory.CreateDirectory(RuntimePaths.RuntimeRoot);
            string bundledPythonDir = Path.GetDirectoryName(bundledPython)!;
            try
            {
                CopyDirectory(bundledPythonDir, pythonDir);
            }
            catch (Exception ex)
            {
                try { Directory.Delete(pythonDir, recursive: true); } catch { }
                steps[1] = steps[1] with { Status = "error", Error = $"Failed to copy Python runtime: {ex.Message}" };
                Emit();
                return;
            }
        }

        // Use the user copy for pip (not the read-only bundled one)
        string runtimePython = File.Exists(userPythonPath) ? userPythonPath : bundledPython;
        steps[1] = steps[1] with { Status = "done" };
        Emit();
        ct.ThrowIfCancellationRequested();

        // Step 2: Install packages into user Python copy
        steps[2] = steps[2] with { Status = "running" };
        Emit();

        string requirementsPath = ResolveRequirementsPath();
        int pipResult = await RunSilentAsync(runtimePython, $"-m pip install -r \"{requirementsPath}\"", null, 600000, ct);
        if (pipResult != 0)
        {
            steps[2] = steps[2] with { Status = "error", Error = "pip install failed. Check your internet connection." };
            Emit();
            return;
        }
        steps[2] = steps[2] with { Status = "done" };
        Emit();

        // Step 3b: GPU packages — install one at a time so a single failure
        // doesn't lose already-installed packages (e.g. librosa's numpy
        // version conflict should not block onnxruntime-directml).
        steps[3] = steps[3] with { Status = "running" };
        Emit();

        var gpuInfo = new GpuDetectionService();
        var (gpuProvider, gpuCard) = gpuInfo.Detect();
        if (steps[2].Status == "done" && gpuProvider is "cuda" or "dml")
        {
            string[] gpuPackages = gpuProvider switch
            {
                "dml" => [
                    "onnxruntime-directml>=1.16.0",
                    "librosa>=0.10.0"
                ],
                "cuda" => [
                    "librosa>=0.10.0"
                ],
                _ => []
            };

            var failedPackages = new List<string>();
            foreach (var pkg in gpuPackages)
            {
                bool ok = false;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    if (attempt > 1)
                    {
                        steps[3] = steps[3] with { Label = $"Retrying {pkg} (attempt {attempt}/2)" };
                        Emit();
                    }
                    int exitCode = await RunSilentAsync(runtimePython, $"-m pip install \"{pkg}\"", null, 600000, ct);
                    if (exitCode == 0) { ok = true; break; }
                }
                if (!ok) failedPackages.Add(pkg.Split('>', '<', '=')[0]);
            }

            if (failedPackages.Count > 0)
            {
                string detail = string.Join(", ", failedPackages);
                steps[3] = steps[3] with { Status = "error", Error = $"GPU packages failed: {detail}. CPU models work. Retry Setup to try again." };
                Emit();
            }
            else
            {
                steps[3] = steps[3] with { Status = "done", Label = "GPU dependencies installed" };
                Emit();
            }
        }
        else
        {
            steps[3] = steps[3] with { Status = "done", Label = "No GPU detected, using CPU" };
            Emit();
        }
        ct.ThrowIfCancellationRequested();

        // Step 4: GPU detection
        steps[4] = steps[4] with { Status = "running" };
        Emit();

        string label = gpuCard;
        if (!string.IsNullOrEmpty(label))
        {
            steps[4] = steps[4] with { Status = "done", Label = $"GPU: {label}" };
        }
        else
        {
            steps[4] = steps[4] with { Status = "done", Label = "No GPU acceleration detected. Using CPU." };
        }
        Emit();
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

    private static string ResolveRequirementsPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "requirements.txt"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "requirements.txt")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException("Could not find stt_engine\\requirements.txt.");
    }

    private static string? FindProjectVenvPython()
    {
        string[] candidates =
        {
            Path.Combine(Environment.CurrentDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string dest = file.Replace(source, destination);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
