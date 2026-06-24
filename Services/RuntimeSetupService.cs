using System.Diagnostics;
using System.IO;

namespace WinWhisperFlow.Services;

public sealed class RuntimeSetupService
{
    public record SetupStep(string Id, string Label, string Status, string? Error = null);

    public event EventHandler<IReadOnlyList<SetupStep>>? StepsChanged;

    public async Task<bool> IsReadyAsync()
    {
        string userPython = RuntimePaths.UserVenvPython;
        string projectPython = FindProjectVenvPython();
        string? python = File.Exists(userPython) ? userPython : (File.Exists(projectPython) ? projectPython : null);
        if (python is null) return false;

        var gpu = new GpuDetectionService();
        var (provider, _) = gpu.Detect();

        // Check basic imports
        // GPU provider availability is verified at model load time by the worker,
        // which auto-falls back to CPU if the provider isn't actually available.
        string imports = provider is "cuda" or "dml"
            ? "import faster_whisper; import requests; import soundfile; import onnxruntime"
            : "import faster_whisper; import requests; import soundfile";

        string code = $"import sys; {imports}";
        int exitCode = await RunSilentAsync(python, $"-c \"{code}\"", null, 30000);
        return exitCode == 0;
    }

    public async Task AutoSetupAsync(CancellationToken ct = default)
    {
        var steps = new List<SetupStep>
        {
            new("python", "Checking Python...", "pending"),
            new("venv", "Creating virtual environment...", "pending"),
            new("packages", "Installing dependencies...", "pending"),
            new("gpu", "Detecting GPU...", "pending"),
        };

        void Emit() => StepsChanged?.Invoke(this, steps.AsReadOnly());

        // Step 1: Check Python
        steps[0] = steps[0] with { Status = "running" };
        Emit();

        bool pythonFound = await CheckPythonExistsAsync(ct);
        if (!pythonFound)
        {
            steps[0] = steps[0] with { Status = "error", Error = "Python 3.10+ not found. Install from python.org" };
            Emit();
            return;
        }
        steps[0] = steps[0] with { Status = "done" };
        Emit();
        ct.ThrowIfCancellationRequested();

        // Step 2: Create venv if needed
        steps[1] = steps[1] with { Status = "running" };
        Emit();

        string venvPath = Path.Combine(RuntimePaths.RuntimeRoot, ".venv");
        string venvPython = Path.Combine(venvPath, "Scripts", "python.exe");

        if (!File.Exists(venvPython))
        {
            Directory.CreateDirectory(RuntimePaths.RuntimeRoot);
            int venvResult = await RunSilentAsync("python", $" -m venv \"{venvPath}\"", RuntimePaths.RuntimeRoot, 60000, ct);
            if (venvResult != 0 || !File.Exists(venvPython))
            {
                try { Directory.Delete(venvPath, recursive: true); } catch { }
                steps[1] = steps[1] with { Status = "error", Error = "Failed to create virtual environment." };
                Emit();
                return;
            }
        }
        steps[1] = steps[1] with { Status = "done" };
        Emit();
        ct.ThrowIfCancellationRequested();

        // Step 3: Install packages
        steps[2] = steps[2] with { Status = "running" };
        Emit();

        string requirementsPath = ResolveRequirementsPath();
        int pipResult = await RunSilentAsync(venvPython, $"-m pip install -r \"{requirementsPath}\"", null, 300000, ct);
        if (pipResult != 0)
        {
            steps[2] = steps[2] with { Status = "error", Error = "pip install failed. Check your internet connection." };
            Emit();
            return;
        }

        // Step 3b: GPU packages
        var gpuInfo = new GpuDetectionService();
        var (gpuProvider, gpuCard) = gpuInfo.Detect();
        if (steps[2].Status != "error" && gpuProvider is "cuda" or "dml")
        {
            string gpuRequirementsPath = ResolveGpuRequirementsPath();
            int gpuPipResult = await RunSilentAsync(venvPython, $"-m pip install -r \"{gpuRequirementsPath}\"", null, 180000, ct);
            if (gpuPipResult != 0)
            {
                steps[2] = steps[2] with { Status = "error", Error = $"GPU package install failed (exit code {gpuPipResult}). GPU acceleration disabled." };
                Emit();
            }
        }

        if (steps[2].Status != "error")
        {
            steps[2] = steps[2] with { Status = "done" };
            Emit();
        }
        ct.ThrowIfCancellationRequested();

        // Step 4: GPU detection
        steps[3] = steps[3] with { Status = "running" };
        Emit();

        string label = gpuCard;
        if (!string.IsNullOrEmpty(label))
        {
            steps[3] = steps[3] with { Status = "done", Label = $"GPU: {label}" };
        }
        else
        {
            steps[3] = steps[3] with { Status = "done", Label = "No GPU acceleration detected. Using CPU." };
        }
        Emit();
    }

    private static async Task<bool> CheckPythonExistsAsync(CancellationToken ct)
    {
        int exitCode = await RunSilentAsync("python", "--version", null, 10000, ct);
        return exitCode == 0;
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

    private static string ResolveGpuRequirementsPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "requirements_gpu.txt"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "requirements_gpu.txt")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException("Could not find stt_engine\\requirements_gpu.txt.");
    }

    private static string FindProjectVenvPython()
    {
        string[] candidates =
        {
            Path.Combine(Environment.CurrentDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }
}
