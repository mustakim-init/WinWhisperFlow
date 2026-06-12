using System.Diagnostics;
using System.IO;

namespace WinWhisperFlow.Services;

public sealed class RuntimeSetupService
{
    public async Task<bool> IsReadyAsync()
    {
        if (!File.Exists(RuntimePaths.UserVenvPython) && !File.Exists(FindProjectVenvPython()))
        {
            return false;
        }

        string python = File.Exists(RuntimePaths.UserVenvPython)
            ? RuntimePaths.UserVenvPython
            : FindProjectVenvPython();

        var gpu = new GpuDetectionService();
        var (provider, _) = gpu.Detect();
        string imports = provider is "cuda" or "dml"
            ? "import faster_whisper; import requests; import sherpa_onnx"
            : "import faster_whisper; import requests; import onnxruntime";

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"-c \"{imports}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return false;
        var timeout = Task.Delay(30000);
        var exited = process.WaitForExitAsync();
        await Task.WhenAny(exited, timeout);
        if (!process.HasExited)
        {
            try { process.Kill(); } catch { }
            return false;
        }
        return process.ExitCode == 0;
    }

    public async Task SetupAsync(Action<string> log)
    {
        Directory.CreateDirectory(RuntimePaths.RuntimeRoot);
        string requirementsPath = ResolveRequirementsPath();
        string venvPath = Path.Combine(RuntimePaths.RuntimeRoot, ".venv");
        string venvPython = Path.Combine(venvPath, "Scripts", "python.exe");
        string batPath = Path.Combine(RuntimePaths.RuntimeRoot, "setup.bat");

        bool needsVenv = !File.Exists(RuntimePaths.UserVenvPython);

        log("A console window will open showing setup progress. Close it when done.");
        log(needsVenv ? "Step 1: Creating Python virtual environment..." : "Step 1: Virtual env exists, skipping.");

        var lines = new List<string>
        {
            "@echo off",
            "title WinWhisperFlow Setup",
            "cd /d \"" + RuntimePaths.RuntimeRoot + "\"",
            "echo ===== WinWhisperFlow Setup =====",
            "echo.",
        };

        if (needsVenv)
        {
            lines.Add("echo [Step 1] Creating Python virtual environment...");
            lines.Add("python -m venv \"" + venvPath + "\"");
            lines.Add("if errorlevel 1 (");
            lines.Add("    echo.");
            lines.Add("    echo FAILED: Could not create virtual environment.");
            lines.Add("    echo Make sure Python 3.10+ is installed and available as 'python'.");
            lines.Add("    pause");
            lines.Add("    exit /b 1");
            lines.Add(")");
            lines.Add("echo [OK] Virtual environment created.");
            lines.Add("echo.");
        }

        lines.Add("echo [Step 2] Upgrading pip...");
        lines.Add("\"" + venvPython + "\" -m pip install --upgrade pip");
        lines.Add("if errorlevel 1 (");
        lines.Add("    echo.");
        lines.Add("    echo FAILED: Could not upgrade pip.");
        lines.Add("    pause");
        lines.Add("    exit /b 1");
        lines.Add(")");
        lines.Add("echo [OK] Pip upgraded.");
        lines.Add("echo.");

        lines.Add("echo [Step 3] Installing CPU Python packages...");
        lines.Add("\"" + venvPython + "\" -m pip install -r \"" + requirementsPath + "\"");
        lines.Add("if errorlevel 1 (");
        lines.Add("    echo.");
        lines.Add("    echo FAILED: pip install CPU packages failed. Check the error above.");
        lines.Add("    pause");
        lines.Add("    exit /b 1");
        lines.Add(")");
        lines.Add("echo [OK] CPU packages installed.");
        lines.Add("echo.");

        var gpu = new GpuDetectionService();
        var (provider, gpuName) = gpu.Detect();
        string gpuPackage = provider switch
        {
            "cuda" => "onnxruntime-gpu>=1.20.0",
            "dml" => "onnxruntime-directml>=1.24.0",
            _ => ""
        };

        if (!string.IsNullOrEmpty(gpuPackage))
        {
            lines.Add("echo [Step 4] Installing GPU Python packages for detected GPU...");
            lines.Add("echo Detected: " + gpuName);
            lines.Add("echo Package: " + gpuPackage.Replace(">", "^>"));
            lines.Add("\"" + venvPython + "\" -m pip install \"" + gpuPackage + "\"");
            lines.Add("if errorlevel 1 (");
            lines.Add("    echo.");
            lines.Add("    echo WARNING: GPU package failed to install.");
            lines.Add("    echo For CUDA: install NVIDIA CUDA Toolkit 12.x and cuDNN");
            lines.Add("    echo For DirectML: update your GPU drivers");
            lines.Add("    pause");
            lines.Add(") else (");
            lines.Add("    echo [OK] GPU package installed.");
            lines.Add(")");
            lines.Add("echo.");
        }
        else
        {
            lines.Add("echo [Step 4] No compatible GPU detected. Skipping GPU packages.");
            lines.Add("echo.");
        }

        lines.Add("echo.");
        lines.Add("echo ===== Setup complete! ===== ");
        lines.Add("timeout /t 3 /nobreak >nul");
        lines.Add("exit /b 0");

        await File.WriteAllLinesAsync(batPath, lines);

        var psi = new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start setup script.");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Setup failed (exit code {process.ExitCode}). Check the console window output for details.");
        }

        if (!File.Exists(venvPython))
        {
            throw new InvalidOperationException(
                "Python virtual environment was not created. Make sure Python 3.10+ is installed.");
        }

        log("Setup completed successfully.");
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
