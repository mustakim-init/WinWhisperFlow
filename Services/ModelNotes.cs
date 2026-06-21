namespace WinWhisperFlow.Services;

public static class ModelNotes
{
    public static string GetModelNote(string model, string provider)
    {
        bool isGpu = provider is "cuda" or "dml";
        string providerLabel = provider switch
        {
            "cuda" => "CUDA",
            "dml" => "DirectML",
            _ => "CPU"
        };

        return (model, isGpu) switch
        {
            ("tiny", true) => $"{providerLabel}: Fastest, minimal quality.",
            ("tiny", false) => $"{providerLabel}: Fastest but least accurate.",
            ("base", true) => $"{providerLabel}: Fast and decent quality.",
            ("base", false) => $"{providerLabel}: Fast, decent for English.",
            ("small", true) => $"{providerLabel}: Recommended balance.",
            ("small", false) => $"{providerLabel}: Recommended balance.",
            ("medium", true) => $"{providerLabel}: Higher accuracy.",
            ("medium", false) => $"{providerLabel}: Slower but more accurate.",
            ("large-v3", true) => $"{providerLabel}: Maximum accuracy.",
            ("large-v3", false) => $"{providerLabel}: May be too slow on CPU.",
            ("turbo", true) => $"{providerLabel}: Fast + accurate.",
            ("turbo", false) => $"{providerLabel}: Heavy on CPU. Use GPU.",
            _ => $"{providerLabel} mode."
        };
    }
}
