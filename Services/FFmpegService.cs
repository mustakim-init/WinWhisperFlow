using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace WinWhisperFlow.Services;

public sealed class FFmpegService
{
    private string? _ffmpegPath;

    public bool IsAvailable => ResolveFFmpeg() is not null;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".m4v", ".ts", ".mts"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wma", ".aiff", ".ape"
    };

    public bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));
    public bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    public string? ResolveFFmpeg()
    {
        if (_ffmpegPath is not null) return _ffmpegPath;

        string projectRoot = Path.GetDirectoryName(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;
        string projectDotFfmpeg = Path.Combine(projectRoot, ".ffmpeg", "ffmpeg.exe");
        if (File.Exists(projectDotFfmpeg))
        {
            _ffmpegPath = projectDotFfmpeg;
            return _ffmpegPath;
        }

        string runtimeDir = RuntimePaths.RuntimeRoot;
        string runtimeDotFfmpeg = Path.Combine(runtimeDir!, ".ffmpeg", "ffmpeg.exe");
        if (runtimeDir is not null && File.Exists(runtimeDotFfmpeg))
        {
            _ffmpegPath = runtimeDotFfmpeg;
            return _ffmpegPath;
        }

        try
        {
            var psi = new ProcessStartInfo("where", "ffmpeg.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(2000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    string firstMatch = output.Split([ '\r', '\n' ], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (File.Exists(firstMatch))
                    {
                        _ffmpegPath = firstMatch;
                        return _ffmpegPath;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<string> ExtractAudioAsync(string inputPath, CancellationToken ct = default)
    {
        string ffmpeg = ResolveFFmpeg() ?? throw new InvalidOperationException("FFmpeg not found.");

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input file not found.", inputPath);

        string tempDir = Path.Combine(Path.GetTempPath(), "winwhisper");
        Directory.CreateDirectory(tempDir);

        string outputPath = Path.Combine(tempDir, $"audio-{Guid.NewGuid():N}.wav");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-i \"{inputPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 -y \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFmpeg.");

        var stdoutTask = ReadAllTextAsync(process.StandardOutput);
        var stderrTask = ReadAllTextAsync(process.StandardError);

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("FFmpeg audio extraction timed out.");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        string stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            string errorDetail = ExtractFfmpegError(stderr);
            throw new InvalidOperationException($"FFmpeg extraction failed (exit {process.ExitCode}): {errorDetail}");
        }

        return outputPath;
    }

    private static string ExtractFfmpegError(string stderr)
    {
        var match = Regex.Match(stderr, @"(Error|error|Invalid|invalid).*", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : stderr.Length > 200 ? stderr[..200] + "..." : stderr;
    }

    private static async Task<string> ReadAllTextAsync(StreamReader reader)
    {
        return await reader.ReadToEndAsync();
    }
}
