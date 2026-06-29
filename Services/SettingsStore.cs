using System.IO;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(RuntimePaths.AppDataRoot, "settings.json");
    private static readonly object _fileLock = new();

    // All settings with defaults
    public static string Theme { get; set; } = "dark";
    public static bool SoundEffectsEnabled { get; set; } = true;
    public static bool AutoPasteEnabled { get; set; } = true;
    public static string? HotkeyChord { get; set; }
    public static int AudioDeviceId { get; set; }
    public static string Language { get; set; } = "en";
    public static bool StartOnBoot { get; set; }
    public static string? ModelDirectory { get; set; }

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
                    model_dir = ModelDirectory
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
}
