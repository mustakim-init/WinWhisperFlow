using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace WinWhisperFlow.Services;

public class UpdateService
{
    private readonly string _repoUrl = "https://github.com/mustakim-init/WinWhisperFlow";
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private UpdateManager? _manager;
    private bool _updateAvailable;
    private string? _newVersion;
    private UpdateInfo? _latestUpdate;

    public bool IsUpdateAvailable => _updateAvailable;
    public string? NewVersion => _newVersion;

    public UpdateManager GetManager()
    {
        if (_manager is null)
        {
            var source = new GithubSource(_repoUrl, null, false);
            _manager = new UpdateManager(source);
        }
        return _manager;
    }

    public async Task<(bool available, string? version)> CheckForUpdatesAsync()
    {
        await _updateGate.WaitAsync();
        try
        {
            var mgr = GetManager();
            var info = await mgr.CheckForUpdatesAsync();
            _latestUpdate = info;
            _updateAvailable = info is not null;
            _newVersion = info?.TargetFullRelease?.Version?.ToString();
            return (_updateAvailable, _newVersion);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            _updateAvailable = false;
            _newVersion = null;
            _latestUpdate = null;
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
            var mgr = GetManager();
            var info = _latestUpdate ?? await mgr.CheckForUpdatesAsync();
            if (info is null) return;
            await mgr.DownloadUpdatesAsync(info, progress);
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
        try
        {
            var mgr = GetManager();
            mgr.ApplyUpdatesAndRestart(_latestUpdate?.TargetFullRelease);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update apply failed: {ex.Message}");
            throw;
        }
    }
}
