namespace WinWhisperFlow.Services;

public sealed record SttRuntimeOptions(
    string Model,
    string Device,
    string ComputeType,
    int CpuThreads,
    int NumWorkers,
    int BeamSize)
{
    public string Provider { get; init; } = "cpu";
    public bool VadFilter { get; init; } = false;
    public int VadMinSilenceDurationMs { get; init; } = 300;
    public double NoSpeechThreshold { get; init; } = 0.45;
    public double LogProbThreshold { get; init; } = -0.8;

    private static CachedGpuResult? _gpuCache;

    public static SttRuntimeOptions RecommendedForThisPc
    {
        get
        {
            var (provider, _) = GetRecommendedGpu();

            return provider switch
            {
                "cuda" => new("turbo", "cuda", "float16", 4, 1, 1) { Provider = "cuda" },
                "dml" => new("small", "dml", "float16", 4, 1, 1) { Provider = "dml" },
                _ => new("small", "cpu", "int8", 6, 1, 1) { Provider = "cpu" }
            };
        }
    }

    public static string GetRecommendedCompositeName()
    {
        var opts = RecommendedForThisPc;
        return $"{opts.Model}-{opts.Provider}";
    }

    public static (string Model, string Device) FromCompositeName(string composite)
    {
        int lastHyphen = composite.LastIndexOf('-');
        if (lastHyphen < 0)
            return (composite, "cpu");

        string provider = composite[(lastHyphen + 1)..];
        string model = composite[..lastHyphen];

        string[] validProviders = { "cpu", "cuda", "dml" };
        if (!validProviders.Contains(provider))
            return (composite, "cpu");

        return (model, provider);
    }

    private static (string Provider, string? Name) GetRecommendedGpu()
    {
        if (_gpuCache is null)
        {
            var gpu = new GpuDetectionService();
            var (provider, name) = gpu.Detect();
            _gpuCache = new CachedGpuResult(provider, name);
        }
        return (_gpuCache.Provider, _gpuCache.Name);
    }

    private sealed record CachedGpuResult(string Provider, string? Name);
}
