namespace WinWhisperFlow.Services;

public sealed record SttRuntimeOptions(
    string Model,
    string Device,
    string ComputeType,
    int CpuThreads,
    int NumWorkers,
    int BeamSize)
{
    /// <summary>"cuda", "dml", or "cpu"</summary>
    public string Provider { get; init; } = "cpu";
    public bool VadFilter { get; init; } = false;
    public int VadMinSilenceDurationMs { get; init; } = 300;
    public double NoSpeechThreshold { get; init; } = 0.45;
    public double LogProbThreshold { get; init; } = -0.8;

    public static SttRuntimeOptions RecommendedForThisPc
    {
        get
        {
            var gpu = new GpuDetectionService();
            var (provider, _) = gpu.Detect();

            return provider switch
            {
                "cuda" => new("turbo", "cuda", "float16", 4, 1, 1) { Provider = "cuda" },
                "dml" => new("base", "dml", "float16", 4, 1, 1) { Provider = "dml" },
                _ => new("small", "cpu", "int8", 6, 1, 1) { Provider = "cpu" }
            };
        }
    }
}
