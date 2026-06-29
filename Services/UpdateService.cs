using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace WinWhisperFlow.Services;

public class UpdateService
{
    private readonly string _repoUrl = "https://github.com/mustakim-init/WinWhisperFlow";
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
        try
        {
            var mgr = GetManager();
            _latestUpdate = await mgr.CheckForUpdatesAsync();
            _updateAvailable = _latestUpdate is not null;
            _newVersion = _latestUpdate?.TargetFullRelease?.Version?.ToString();
            return (_updateAvailable, _newVersion);
        }
        catch
        {
            _updateAvailable = false;
            _newVersion = null;
            _latestUpdate = null;
            return (false, null);
        }
    }

    public async Task DownloadUpdateAsync(Action<int>? progress = null)
    {
        try
        {
            var mgr = GetManager();
            if (_latestUpdate is null)
            {
                _latestUpdate = await mgr.CheckForUpdatesAsync();
            }
            if (_latestUpdate is null) return;
            await mgr.DownloadUpdatesAsync(_latestUpdate, progress);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update download failed: {ex.Message}");
            throw;
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
