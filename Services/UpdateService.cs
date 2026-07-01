using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace WinWhisperFlow.Services;

public class UpdateService
{
    // ── Constants ────────────────────────────────────────────────────────────
    private const string RepoUrl        = "https://api.github.com/repos/mustakim-init/WinWhisperFlow";
    private const string UserAgent      = "WinWhisperFlow/1.0";
    private const string AssetName      = "WinWhisperFlow-portable.zip";
    private const string RegKeyPath     = @"Software\WinWhisperFlow";
    private const string RegInstallDir  = "InstallDir";

    // ── State ─────────────────────────────────────────────────────────────
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool    _updateAvailable;
    private string? _newVersion;
    private string? _downloadUrl;
    private string? _extractedUpdateDir; // set after a successful download

    public bool    IsUpdateAvailable => _updateAvailable;
    public string? NewVersion        => _newVersion;

    private static Version? CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName()?.Version;

    // ── Public API ───────────────────────────────────────────────────────

    public async Task<(bool available, string? version)> CheckForUpdatesAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var client = MakeClient(TimeSpan.FromSeconds(15));

            var response = await client.GetAsync($"{RepoUrl}/releases/latest");
            if (!response.IsSuccessStatusCode) return (false, null);

            var json = await response.Content.ReadAsStringAsync();
            using var doc  = JsonDocument.Parse(json);
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
                if (name == AssetName)
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
            if (downloadUrl is null) return (false, null);

            _updateAvailable = true;
            _newVersion      = versionStr;
            _downloadUrl     = downloadUrl;
            return (true, versionStr);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Check failed: {ex.Message}");
            _updateAvailable = false;
            _newVersion      = null;
            _downloadUrl     = null;
            return (false, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DownloadUpdateAsync(Action<int>? progress = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_downloadUrl is null)
                throw new InvalidOperationException("No update URL available. Run CheckForUpdatesAsync first.");

            using var client = MakeClient(TimeSpan.FromMinutes(10));

            string tempDir = Path.Combine(Path.GetTempPath(), "WinWhisperFlow-Update");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "update.zip");

            // Stream download with progress
            using (var response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total    = response.Content.Headers.ContentLength ?? -1;
                long readSoFar = 0;
                var  buffer   = new byte[81920];

                using var src  = await response.Content.ReadAsStreamAsync();
                using var dest = File.Create(zipPath);
                int bytes;
                while ((bytes = await src.ReadAsync(buffer)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, bytes));
                    readSoFar += bytes;
                    if (total > 0) progress?.Invoke((int)(readSoFar * 100 / total));
                }
            }

            // Extract to a subfolder so we can point robocopy at it cleanly
            string extractDir = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            File.Delete(zipPath);

            _extractedUpdateDir = extractDir;
        }
        catch
        {
            _extractedUpdateDir = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes a self-deleting PowerShell updater script, launches it elevated if necessary,
    /// then exits the current process. The script waits for the app to die, mirrors the new
    /// files into the install directory using robocopy /MIR, and restarts the app.
    /// </summary>
    public void ApplyAndRestart()
    {
        if (_extractedUpdateDir is null)
            throw new InvalidOperationException("No downloaded update found. Run DownloadUpdateAsync first.");

        string installDir = ResolveInstallDir();
        string updateDir  = _extractedUpdateDir;
        string exePath    = Path.Combine(installDir, "WinWhisperFlow.exe");
        string psPath     = Path.Combine(Path.GetTempPath(), "WinWhisperFlow-Updater.ps1");

        // PowerShell script:
        //  1. Waits for WinWhisperFlow.exe processes to exit (up to 30 s)
        //  2. robocopy /MIR syncs new → old (adds new, updates changed, removes deleted)
        //  3. Restarts the app
        //  4. Deletes itself
        string ps = $$"""
            $ErrorActionPreference = 'Stop'
            Add-Type -AssemblyName PresentationFramework
            $appDir   = '{{EscapePs(installDir)}}'
            $srcDir   = '{{EscapePs(updateDir)}}'
            $exePath  = '{{EscapePs(exePath)}}'

            # Wait for the main process to exit
            $deadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $deadline) {
                $procs = Get-Process -Name 'WinWhisperFlow' -ErrorAction SilentlyContinue
                if (-not $procs) { break }
                Start-Sleep -Milliseconds 500
            }

            # Mirror update into install dir
            # /MIR = mirror (copies new, updates changed, deletes removed)
            # /XD __pycache__ = skip Python cache dirs
            # /XF *.pyc       = skip compiled bytecode
            # /NFL /NDL /NJH /NJS /NC /NS = silent output
            & robocopy $srcDir $appDir /MIR /XD __pycache__ /XF *.pyc /NFL /NDL /NJH /NJS /NC /NS /R:2 /W:1
            # robocopy exit codes 0–7 are success (8+ are errors)
            if ($LASTEXITCODE -ge 8) {
                [System.Windows.MessageBox]::Show("Update failed during file copy (robocopy exit $LASTEXITCODE).`nPlease reinstall manually.", "WinWhisper Flow Updater", 0, 48)
                exit 1
            }

            # Clean up downloaded temp files
            Remove-Item -Recurse -Force (Split-Path $srcDir -Parent) -ErrorAction SilentlyContinue

            # Restart the app
            Start-Process $exePath

            # Self-delete this script
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            """;

        File.WriteAllText(psPath, ps, System.Text.Encoding.UTF8);

        // Determine whether we need elevation:
        // If the install dir is under %ProgramFiles% or %ProgramFiles(x86)%, we need admin.
        bool needsElevation = IsUnderProgramFiles(installDir);

        var psi = new ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{psPath}\"",
            CreateNoWindow  = true,
            UseShellExecute = needsElevation, // required for Verb = RunAs
        };
        if (needsElevation) psi.Verb = "runas";

        Process.Start(psi);
        Environment.Exit(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static HttpClient MakeClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// Reads the install directory from the registry key written by the Inno Setup installer.
    /// Falls back to the directory of the running executable (portable / dev run).
    /// </summary>
    private static string ResolveInstallDir()
    {
        // HKCU first (per-user install), then HKLM (machine-wide install)
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(RegKeyPath);
            if (key?.GetValue(RegInstallDir) is string dir && Directory.Exists(dir))
                return dir;
        }

        // Portable / development fallback
        string? exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        return exeDir ?? AppContext.BaseDirectory;
    }

    private static bool IsUnderProgramFiles(string path)
    {
        string[] pfRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];
        return pfRoots.Any(pf =>
            !string.IsNullOrEmpty(pf) &&
            path.StartsWith(pf, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Escapes a path for embedding in a single-quoted PowerShell string.</summary>
    private static string EscapePs(string path) => path.Replace("'", "''");
}