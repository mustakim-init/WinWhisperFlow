using System.IO;
using NAudio.Wave;

namespace WinWhisperFlow.Services;

public sealed class AudioCaptureService : IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentFile;
    private float _peakLevel;
    private int _deviceId;

    public event EventHandler<float>? LevelChanged;
    public float LastPeakLevel { get; private set; }
    public int DeviceId { get => _deviceId; set => _deviceId = Math.Clamp(value, 0, WaveInEvent.DeviceCount - 1); }
    public bool IsListening => _waveIn is not null;

    public static string[] GetInputDeviceNames()
    {
        string[] names = new string[WaveInEvent.DeviceCount];
        for (int i = 0; i < names.Length; i++)
        {
            try { names[i] = WaveInEvent.GetCapabilities(i).ProductName; }
            catch { names[i] = $"Device {i}"; }
        }
        return names;
    }

    public static int InputDeviceCount => WaveInEvent.DeviceCount;

    public void Start()
    {
        lock (_gate)
        {
            if (InputDeviceCount == 0)
            {
                throw new InvalidOperationException(
                    "No microphone input devices detected. Check Windows Sound settings > Input.");
            }

            if (_deviceId >= InputDeviceCount)
            {
                _deviceId = 0;
            }

            Stop();
            _peakLevel = 0;
            LastPeakLevel = 0;
            _currentFile = Path.Combine(Path.GetTempPath(), $"winwhisper-{Guid.NewGuid():N}.wav");
            var format = new WaveFormat(16000, 16, 1);

            _writer = new WaveFileWriter(_currentFile, format);
            _waveIn = new WaveInEvent
            {
                WaveFormat = format,
                DeviceNumber = _deviceId,
                BufferMilliseconds = 40,
                NumberOfBuffers = 3
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
        }
    }

    public static int RefreshInputDeviceCount()
    {
        return WaveInEvent.DeviceCount;
    }

    public string? Stop()
    {
        lock (_gate)
        {
            if (_waveIn is null)
            {
                return null;
            }

            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;

            _writer?.Dispose();
            _writer = null;
            LastPeakLevel = _peakLevel;

            string? file = _currentFile;
            _currentFile = null;
            return file is not null && new FileInfo(file).Length > 44 ? file : null;
        }
    }

    public string? GetTemporarySnapshot()
    {
        lock (_gate)
        {
            if (_writer is null || _currentFile is null) return null;

            _writer.Flush();

            var fileInfo = new FileInfo(_currentFile);
            long fileLength = fileInfo.Length;
            if (fileLength <= 44) return null;

            string snapshotPath = Path.Combine(Path.GetTempPath(), $"winwhisper-snap-{Guid.NewGuid():N}.wav");
            File.Copy(_currentFile, snapshotPath, overwrite: true);

            // WaveFileWriter only finalizes the RIFF/data chunk sizes on Dispose().
            // Fix the header in the snapshot so parsers see correct sizes.
            using (var fixStream = new FileStream(snapshotPath, FileMode.Open, FileAccess.ReadWrite))
            {
                uint fileSize = (uint)(fixStream.Length - 8);
                uint dataSize = (uint)(fixStream.Length - 44);

                var sizes = new byte[8];
                sizes[0] = (byte)fileSize;
                sizes[1] = (byte)(fileSize >> 8);
                sizes[2] = (byte)(fileSize >> 16);
                sizes[3] = (byte)(fileSize >> 24);
                sizes[4] = (byte)dataSize;
                sizes[5] = (byte)(dataSize >> 8);
                sizes[6] = (byte)(dataSize >> 16);
                sizes[7] = (byte)(dataSize >> 24);

                fixStream.Position = 4;
                fixStream.Write(sizes, 0, 4);
                fixStream.Position = 40;
                fixStream.Write(sizes, 4, 4);
            }

            return snapshotPath;
        }
    }

    public void Cancel()
    {
        string? file = Stop();
        if (file is not null)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        float level = CalculateRmsLevel(e.Buffer, e.BytesRecorded);
        _peakLevel = Math.Max(_peakLevel, level);

        lock (_gate)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        LevelChanged?.Invoke(this, level);
    }

    private static float CalculateRmsLevel(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded == 0)
        {
            return 0;
        }

        double sumSquares = 0;
        int sampleCount = bytesRecorded / 2;
        for (int offset = 0; offset < bytesRecorded - 1; offset += 2)
        {
            short sample = BitConverter.ToInt16(buffer, offset);
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        return Math.Clamp((float)(rms * 5.0), 0, 1);
    }

    public void Dispose()
    {
        Stop();
    }
}
