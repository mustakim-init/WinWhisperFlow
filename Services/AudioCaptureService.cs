using System.IO;
using NAudio.Wave;

namespace WinWhisperFlow.Services;

public sealed class AudioCaptureService : IDisposable
{
    ~AudioCaptureService() => Stop();
    private const int SpectrumFftSize = 512;
    private const int SpectrumBands = 5;
    private static readonly (int Low, int High)[] SpectrumBandRanges = [
        (2, 7),     //   62-218 Hz   (low)
        (8, 15),    //  250-468 Hz   (low-mid)
        (16, 31),   //  500-968 Hz   (mid)
        (32, 95),   // 1000-2968 Hz  (upper-mid)
        (96, 255),  // 3000-7968 Hz  (high)
    ];
    private static readonly float[] HannWindow = PrecomputeHann();

    private static float[] PrecomputeHann()
    {
        var w = new float[SpectrumFftSize];
        for (int i = 0; i < SpectrumFftSize; i++)
            w[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (SpectrumFftSize - 1)));
        return w;
    }

    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentFile;
    private float _peakLevel;
    private int _deviceId;
    private readonly short[] _spectrumRing = new short[SpectrumFftSize];
    private int _spectrumPos;
    private int _spectrumCounter;

    public event EventHandler<float>? LevelChanged;
    public event EventHandler<float[]>? SpectrumChanged;
    public float LastPeakLevel { get; private set; }
    public int DeviceId { get => _deviceId; set => _deviceId = Math.Clamp(value, 0, WaveInEvent.DeviceCount - 1); }
    public bool IsListening => _waveIn is not null;

    public static string[] GetInputDeviceNames()
    {
        string[] names = new string[WaveInEvent.DeviceCount];
        for (int i = 0; i < names.Length; i++)
        {
            try { names[i] = WaveInEvent.GetCapabilities(i).ProductName; }
            catch (Exception) { names[i] = $"Device {i}"; }
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
            if (_waveIn is null) return null;

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

    public void Cancel()
    {
        string? file = Stop();
        if (file is not null) { try { File.Delete(file); } catch { } }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        float level = CalculateRmsLevel(e.Buffer, e.BytesRecorded);

        lock (_gate)
        {
            _peakLevel = Math.Max(_peakLevel, level);
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        LevelChanged?.Invoke(this, level);

        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i < sampleCount; i++)
        {
            _spectrumRing[_spectrumPos] = BitConverter.ToInt16(e.Buffer, i * 2);
            _spectrumPos = (_spectrumPos + 1) % SpectrumFftSize;
        }

        _spectrumCounter++;
        if (_spectrumCounter >= 2)
        {
            _spectrumCounter = 0;
            var bands = ComputeSpectrumBands();
            SpectrumChanged?.Invoke(this, bands);
        }
    }

    private float[] ComputeSpectrumBands()
    {
        var real = new float[SpectrumFftSize];
        var imag = new float[SpectrumFftSize];

        int pos = _spectrumPos;
        for (int i = 0; i < SpectrumFftSize; i++)
        {
            real[i] = _spectrumRing[pos] / 32768f * HannWindow[i];
            pos = (pos + 1) % SpectrumFftSize;
        }

        FftInPlace(real, imag);

        var mag = new float[SpectrumFftSize / 2];
        for (int i = 0; i < SpectrumFftSize / 2; i++)
            mag[i] = MathF.Sqrt(real[i] * real[i] + imag[i] * imag[i]);

        var bands = new float[SpectrumBands];
        for (int b = 0; b < SpectrumBands; b++)
        {
            var (low, high) = SpectrumBandRanges[b];
            float sum = 0;
            for (int i = low; i <= high && i < mag.Length; i++)
                sum += mag[i];
            float count = Math.Min(high, mag.Length - 1) - low + 1;
            bands[b] = count > 0 ? MathF.Min(sum / count * 3f, 1f) : 0;
        }

        return bands;
    }

    private static void FftInPlace(float[] real, float[] imag)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
            j ^= bit;
            if (i < j)
            {
                (real[j], real[i]) = (real[i], real[j]);
                (imag[j], imag[i]) = (imag[i], imag[j]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = 2 * MathF.PI / len;
            float wRe = MathF.Cos(ang);
            float wIm = -MathF.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float curRe = 1, curIm = 0;
                for (int j = 0; j < len / 2; j++)
                {
                    int u = i + j, v = i + j + len / 2;
                    float tRe = curRe * real[v] - curIm * imag[v];
                    float tIm = curRe * imag[v] + curIm * real[v];
                    real[v] = real[u] - tRe;
                    imag[v] = imag[u] - tIm;
                    real[u] += tRe;
                    imag[u] += tIm;
                    (curRe, curIm) = (curRe * wRe - curIm * wIm, curRe * wIm + curIm * wRe);
                }
            }
        }
    }

    private static float CalculateRmsLevel(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded == 0) return 0;

        double sumSquares = 0;
        int bytesPerSample = 2;
        int sampleCount = bytesRecorded / bytesPerSample;
        for (int offset = 0; offset < bytesRecorded - bytesPerSample + 1; offset += bytesPerSample)
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
        string? file = Stop();
        if (file is not null) { try { File.Delete(file); } catch { } }
    }
}
