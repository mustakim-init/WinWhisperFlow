using System.IO;
using NAudio.Wave;

namespace WinWhisperFlow.Services;

public sealed class SoundEffectService : IDisposable
{
    private readonly Dictionary<string, IWavePlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _soundFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
                StopAll();
        }
    }

    public SoundEffectService()
    {
        string sfxDir = Path.Combine(AppContext.BaseDirectory, "Sfx");
        if (!Directory.Exists(sfxDir))
        {
            sfxDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Sfx");
            if (!Directory.Exists(sfxDir))
                return;
        }

        foreach (var file in Directory.GetFiles(sfxDir, "*.*"))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".wav" or ".mp3")
            {
                string name = Path.GetFileNameWithoutExtension(file);
                _soundFiles[name] = file;
            }
        }
    }

    public void PlayRecordStart() { Play("Start"); }
    public void PlayRecordStop() { Play("Notification"); }
    public void PlayTranscriptionDone() { Play("Done"); }
    public void PlayError() { Play("Error"); }
    public void PlayModelReady() { Play("Notification"); }

    private void Play(string name)
    {
        if (!_enabled) return;
        if (!_soundFiles.TryGetValue(name, out var path)) return;

        Stop(name);

        try
        {
            var reader = CreateReader(path);
            if (reader is null) return;

            var player = new WaveOutEvent { DeviceNumber = -1 };
            player.Init(reader);
            player.PlaybackStopped += (_, _) =>
            {
                reader.Dispose();
                lock (_players) _players.Remove(name);
            };
            player.Play();

            lock (_players) _players[name] = player;
        }
        catch
        {
            // Sound file is corrupt or unsupported — skip silently
        }
    }

    private static WaveStream? CreateReader(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => new Mp3FileReader(path),
            ".wav" => new WaveFileReader(path),
            _ => null
        };
    }

    private void Stop(string name)
    {
        IWavePlayer? existing;
        lock (_players)
        {
            if (!_players.TryGetValue(name, out existing)) return;
            _players.Remove(name);
        }
        try { existing.Stop(); existing.Dispose(); } catch { }
    }

    private void StopAll()
    {
        List<IWavePlayer> all;
        lock (_players)
        {
            all = new List<IWavePlayer>(_players.Values);
            _players.Clear();
        }
        foreach (var p in all)
        {
            try { p.Stop(); p.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        StopAll();
    }
}
