using System.IO;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public class StoredProfile
{
    public int BeamSize { get; set; } = 1;
    public double Temperature { get; set; } = 0;
    public bool VadFilter { get; set; } = false;
    public double NoSpeechThreshold { get; set; } = 0.45;
    public double LogProbThreshold { get; set; } = -0.8;
    public int BestOf { get; set; } = 5;
    public double RepetitionPenalty { get; set; } = 1;
    public int NoRepeatNgramSize { get; set; } = 0;
    public double LengthPenalty { get; set; } = 1;
    public double CompressionRatioThreshold { get; set; } = 2.4;
    public double PromptResetOnTemperature { get; set; } = 0.5;
    public bool ConditionOnPreviousText { get; set; } = true;
    public string? Hotwords { get; set; }
    public double HallucinationSilenceThreshold { get; set; } = 0;
}

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(RuntimePaths.AppDataRoot, "settings.json");
    private static readonly object _fileLock = new();

    // Flat active settings (always reflect the currently-active profile)
    public static string Theme { get; set; } = "dark";
    public static bool SoundEffectsEnabled { get; set; } = true;
    public static bool AutoPasteEnabled { get; set; } = true;
    public static string? HotkeyChord { get; set; }
    public static int AudioDeviceId { get; set; }
    public static string Language { get; set; } = "en";
    public static bool StartOnBoot { get; set; }
    public static string? ModelDirectory { get; set; }
    public static int BeamSize { get; set; } = 1;
    public static double Temperature { get; set; } = 0;
    public static bool VadFilter { get; set; } = false;
    public static double NoSpeechThreshold { get; set; } = 0.45;
    public static double LogProbThreshold { get; set; } = -0.8;
    public static int BestOf { get; set; } = 5;
    public static double RepetitionPenalty { get; set; } = 1;
    public static int NoRepeatNgramSize { get; set; } = 0;
    public static double LengthPenalty { get; set; } = 1;
    public static double CompressionRatioThreshold { get; set; } = 2.4;
    public static double PromptResetOnTemperature { get; set; } = 0.5;
    public static bool ConditionOnPreviousText { get; set; } = true;
    public static string? Hotwords { get; set; }
    public static double HallucinationSilenceThreshold { get; set; } = 0;

    // Persisted profiles (stored alongside flat settings in JSON)
    public static StoredProfile VoiceProfile { get; set; } = new();
    public static StoredProfile MusicProfile { get; set; } = new()
    {
        BeamSize = 5,
        VadFilter = false,
        NoSpeechThreshold = 0.6,
        LogProbThreshold = -1.0,
        RepetitionPenalty = 1.2,
        NoRepeatNgramSize = 3,
        ConditionOnPreviousText = false,
        HallucinationSilenceThreshold = 2,
    };

    public static void Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string json = File.ReadAllText(FilePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("theme", out var t)) Theme = t.GetString() ?? "dark";
                if (root.TryGetProperty("sfx", out var s)) SoundEffectsEnabled = s.GetBoolean();
                if (root.TryGetProperty("auto_paste", out var ap)) AutoPasteEnabled = ap.GetBoolean();
                if (root.TryGetProperty("hotkey_chord", out var hk) && hk.ValueKind == JsonValueKind.String)
                    HotkeyChord = hk.GetString();
                if (root.TryGetProperty("audio_device", out var ad)) AudioDeviceId = ad.GetInt32();
                if (root.TryGetProperty("language", out var l)) Language = l.GetString() ?? "en";
                if (root.TryGetProperty("start_on_boot", out var sb)) StartOnBoot = sb.GetBoolean();
                if (root.TryGetProperty("model_dir", out var md) && md.ValueKind == JsonValueKind.String)
                    ModelDirectory = md.GetString();
                if (root.TryGetProperty("beam_size", out var bs)) BeamSize = bs.GetInt32();
                if (root.TryGetProperty("temperature", out var tp)) Temperature = tp.GetDouble();
                if (root.TryGetProperty("vad_filter", out var vf)) VadFilter = vf.GetBoolean();
                if (root.TryGetProperty("no_speech_threshold", out var nst)) NoSpeechThreshold = nst.GetDouble();
                if (root.TryGetProperty("log_prob_threshold", out var lpt)) LogProbThreshold = lpt.GetDouble();
                if (root.TryGetProperty("best_of", out var bo)) BestOf = Math.Clamp(bo.GetInt32(), 1, 10);
                if (root.TryGetProperty("repetition_penalty", out var rp)) RepetitionPenalty = Math.Clamp(rp.GetDouble(), 1, 5);
                if (root.TryGetProperty("no_repeat_ngram_size", out var nr)) NoRepeatNgramSize = Math.Max(0, nr.GetInt32());
                if (root.TryGetProperty("length_penalty", out var lp)) LengthPenalty = Math.Max(0, lp.GetDouble());
                if (root.TryGetProperty("compression_ratio_threshold", out var cr)) CompressionRatioThreshold = Math.Max(0, cr.GetDouble());
                if (root.TryGetProperty("prompt_reset_on_temperature", out var pr)) PromptResetOnTemperature = Math.Clamp(pr.GetDouble(), 0, 1);
                if (root.TryGetProperty("condition_on_previous_text", out var cp)) ConditionOnPreviousText = cp.GetBoolean();
                if (root.TryGetProperty("hotwords", out var hw) && hw.ValueKind == JsonValueKind.String)
                    Hotwords = hw.GetString();
                if (root.TryGetProperty("hallucination_silence_threshold", out var hs)) HallucinationSilenceThreshold = Math.Max(0, hs.GetDouble());

                if (root.TryGetProperty("voice_profile", out var vp) && vp.ValueKind == JsonValueKind.Object)
                    VoiceProfile = DeserializeProfile(vp);
                if (root.TryGetProperty("music_profile", out var mp) && mp.ValueKind == JsonValueKind.Object)
                    MusicProfile = DeserializeProfile(mp);
            }
            catch
            {
                // Corrupted file — start with defaults
            }
        }
    }

    public static void Save()
    {
        lock (_fileLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var data = new
                {
                    theme = Theme,
                    sfx = SoundEffectsEnabled,
                    auto_paste = AutoPasteEnabled,
                    hotkey_chord = HotkeyChord,
                    audio_device = AudioDeviceId,
                    language = Language,
                    start_on_boot = StartOnBoot,
                    model_dir = ModelDirectory,
                    beam_size = BeamSize,
                    temperature = Temperature,
                    vad_filter = VadFilter,
                    no_speech_threshold = NoSpeechThreshold,
                    log_prob_threshold = LogProbThreshold,
                    best_of = BestOf,
                    repetition_penalty = RepetitionPenalty,
                    no_repeat_ngram_size = NoRepeatNgramSize,
                    length_penalty = LengthPenalty,
                    compression_ratio_threshold = CompressionRatioThreshold,
                    prompt_reset_on_temperature = PromptResetOnTemperature,
                    condition_on_previous_text = ConditionOnPreviousText,
                    hotwords = Hotwords,
                    hallucination_silence_threshold = HallucinationSilenceThreshold,
                    voice_profile = SerializeProfile(VoiceProfile),
                    music_profile = SerializeProfile(MusicProfile),
                };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Silent fail — settings are non-critical
            }
        }
    }

    private static StoredProfile DeserializeProfile(JsonElement el)
    {
        var p = new StoredProfile();
        if (el.TryGetProperty("beam_size", out var bs)) p.BeamSize = bs.GetInt32();
        if (el.TryGetProperty("temperature", out var tp)) p.Temperature = tp.GetDouble();
        if (el.TryGetProperty("vad_filter", out var vf)) p.VadFilter = vf.GetBoolean();
        if (el.TryGetProperty("no_speech_threshold", out var nst)) p.NoSpeechThreshold = nst.GetDouble();
        if (el.TryGetProperty("log_prob_threshold", out var lpt)) p.LogProbThreshold = lpt.GetDouble();
        if (el.TryGetProperty("best_of", out var bo)) p.BestOf = Math.Clamp(bo.GetInt32(), 1, 10);
        if (el.TryGetProperty("repetition_penalty", out var rp)) p.RepetitionPenalty = Math.Clamp(rp.GetDouble(), 1, 5);
        if (el.TryGetProperty("no_repeat_ngram_size", out var nr)) p.NoRepeatNgramSize = Math.Max(0, nr.GetInt32());
        if (el.TryGetProperty("length_penalty", out var lp)) p.LengthPenalty = Math.Max(0, lp.GetDouble());
        if (el.TryGetProperty("compression_ratio_threshold", out var cr)) p.CompressionRatioThreshold = Math.Max(0, cr.GetDouble());
        if (el.TryGetProperty("prompt_reset_on_temperature", out var pr)) p.PromptResetOnTemperature = Math.Clamp(pr.GetDouble(), 0, 1);
        if (el.TryGetProperty("condition_on_previous_text", out var cp)) p.ConditionOnPreviousText = cp.GetBoolean();
        if (el.TryGetProperty("hotwords", out var hw) && hw.ValueKind == JsonValueKind.String)
            p.Hotwords = hw.GetString();
        if (el.TryGetProperty("hallucination_silence_threshold", out var hs)) p.HallucinationSilenceThreshold = Math.Max(0, hs.GetDouble());
        return p;
    }

    internal static object SerializeProfile(StoredProfile p) => new
    {
        beam_size = p.BeamSize,
        temperature = p.Temperature,
        vad_filter = p.VadFilter,
        no_speech_threshold = p.NoSpeechThreshold,
        log_prob_threshold = p.LogProbThreshold,
        best_of = p.BestOf,
        repetition_penalty = p.RepetitionPenalty,
        no_repeat_ngram_size = p.NoRepeatNgramSize,
        length_penalty = p.LengthPenalty,
        compression_ratio_threshold = p.CompressionRatioThreshold,
        prompt_reset_on_temperature = p.PromptResetOnTemperature,
        condition_on_previous_text = p.ConditionOnPreviousText,
        hotwords = p.Hotwords,
        hallucination_silence_threshold = p.HallucinationSilenceThreshold,
    };
}
