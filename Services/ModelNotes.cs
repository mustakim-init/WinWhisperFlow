namespace WinWhisperFlow.Services;

public static class ModelNotes
{
    public static string GetModelNote(string model, bool gpu)
    {
        if (gpu)
        {
            return model switch
            {
                "tiny" => "GPU: Fastest, minimal quality.",
                "base" => "GPU: Fast and decent quality.",
                "small" => "GPU: Recommended balance.",
                "medium" => "GPU: Higher accuracy.",
                "large-v3" => "GPU: Maximum accuracy.",
                "turbo" => "GPU: Fast + accurate.",
                _ => "GPU mode."
            };
        }
        return model switch
        {
            "tiny" => "CPU: Fastest but least accurate.",
            "base" => "CPU: Fast, decent for English.",
            "small" => "CPU: Recommended balance.",
            "medium" => "CPU: Slower but more accurate.",
            "large-v3" => "CPU: May be too slow. Enable GPU.",
            "turbo" => "CPU: Heavy. Enable GPU.",
            _ => "CPU mode."
        };
    }
}
