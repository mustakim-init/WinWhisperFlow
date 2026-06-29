using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public class UpdateService
{
    private readonly string _repoUrl = "https://api.github.com/repos/mustakim-init/WinWhisperFlow";
    private readonly string _userAgent = "WinWhisperFlow/1.0";
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private bool _updateAvailable;
    private string? _newVersion;
    private string? _downloadUrl;
    private string? _appDir;

    public bool IsUpdateAvailable => _updateAvailable;
    public string? NewVersion => _newVersion;

    private static Version? CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName()?.Version;

    public async Task<(bool available, string? version)> CheckForUpdatesAsync()
    {
        await _updateGate.WaitAsync();
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync($"{_repoUrl}/releases/latest");
            if (!response.IsSuccessStatusCode) return (false, null);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (tagName is null) return (false, null);

            string versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var remoteVersion)) return (false, null);

            var local = CurrentVersion;
            if (local is not null && remoteVersion <= local) return (false, null);

            // Find the portable zip asset
            if (!root.TryGetProperty("assets", out var assets)) return (false, null);
            string? downloadUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name == "WinWhisperFlow-portable.zip")
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }

            if (downloadUrl is null) return (false, null);

            _updateAvailable = true;
            _newVersion = versionStr;
            _downloadUrl = downloadUrl;
            return (true, versionStr);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            _updateAvailable = false;
            _newVersion = null;
            _downloadUrl = null;
            return (false, null);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task DownloadUpdateAsync(Action<int>? progress = null)
    {
        await _updateGate.WaitAsync();
        try
        {
            if (_downloadUrl is null) return;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            client.Timeout = TimeSpan.FromMinutes(10);

            string tempDir = Path.Combine(Path.GetTempPath(), "WinWhisperFlow-Update");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "update.zip");

            using (var response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(zipPath);
                var buffer = new byte[81920];
                long readSoFar = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    readSoFar += bytesRead;
                    if (totalBytes > 0)
                        progress?.Invoke((int)(readSoFar * 100 / totalBytes));
                }
            }

            ZipFile.ExtractToDirectory(zipPath, tempDir);
            File.Delete(zipPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex.Message}");
            throw;
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public void ApplyAndRestart()
    {
        _appDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (_appDir is null) throw new InvalidOperationException("Could not determine app directory.");

        string tempDir = Path.Combine(Path.GetTempPath(), "WinWhisperFlow-Update");
        string batchPath = Path.Combine(Path.GetTempPath(), "WinWhisperFlow-Update.bat");

        var lines = new[]
        {
            "@echo off",
            "chcp 65001 >nul",
            $"set APP_DIR={_appDir}",
            $"set TEMP_DIR={tempDir}",
            "",
            ">nul 2>&1 net session",
            "if %errorlevel% neq 0 (",
            "    powershell -Command \"Start-Process '%~f0' -Verb RunAs\"",
            "    exit /b",
            ")",
            "",
            $"timeout /t 3 /nobreak >nul",
            $"xcopy /E /Y /I \"%TEMP_DIR%\\*\" \"%APP_DIR%\"",
            $"start \"\" \"%APP_DIR%\\WinWhisperFlow.exe\"",
            "del \"%~f0\""
        };
        File.WriteAllText(batchPath, string.Join("\r\n", lines));

        Process.Start(new ProcessStartInfo
        {
            FileName = batchPath,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });

        Environment.Exit(0);
    }
}
