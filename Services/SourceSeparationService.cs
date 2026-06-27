using System.Diagnostics;
using System.IO;

namespace WinWhisperFlow.Services;

public sealed class SourceSeparationService
{
    public async Task<string> SeparateVocalsAsync(string inputWav, CancellationToken ct = default)
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "winwhisper", "separated");
        Directory.CreateDirectory(outputDir);

        string python = ResolvePython();
        string wrapper = ResolveDemucsWrapper();
        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"\"{wrapper}\" \"{inputWav}\" \"{outputDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Demucs.");

        var stderr = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) stderr.AppendLine(args.Data);
        };
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));

        string stdout;
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Demucs source separation timed out after 10 minutes.");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Demucs failed (exit {process.ExitCode}): {stderr}");

        string vocalsPath = stdout.Trim();
        if (string.IsNullOrEmpty(vocalsPath) || !File.Exists(vocalsPath))
            throw new FileNotFoundException($"Demucs output not found.");

        return vocalsPath;
    }

    private static string ResolveDemucsWrapper()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "stt_engine", "demucs_wrapper.py"),
            Path.Combine(Environment.CurrentDirectory, "stt_engine", "demucs_wrapper.py"),
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not find stt_engine\\demucs_wrapper.py.");
    }

    private static string ResolvePython()
    {
        string[] candidates =
        [
            RuntimePaths.UserVenvPython,
            Path.Combine(Environment.CurrentDirectory, ".venv", "Scripts", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Python interpreter not found. Run setup first.");
    }
}
