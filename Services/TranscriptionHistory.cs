using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public sealed class TranscriptionHistory
{
    private const int MaxEntries = 100;
    private readonly string _persistPath;

    public ObservableCollection<TranscriptionHistoryEntry> Entries { get; } = new();

    public TranscriptionHistory(string? persistPath = null)
    {
        _persistPath = persistPath ?? Path.Combine(RuntimePaths.AppDataRoot, "history.json");
        Load();
    }

    public void Add(TranscriptionHistoryEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_persistPath)) return;
            string json = File.ReadAllText(_persistPath);
            var entries = JsonSerializer.Deserialize<List<TranscriptionHistoryEntry>>(json);
            if (entries is null) return;
            foreach (var e in entries)
                Entries.Add(e);
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var snapshot = Entries.ToList();
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            _ = Task.Run(() => { try { File.WriteAllText(_persistPath, json); } catch { } });
        }
        catch { }
    }

    public static string ActionLabel(string action) => action switch
    {
        "typed" => "[Typed]",
        "copied" => "[Copied]",
        "both" => "[Typed+Copied]",
        _ => "[--]"
    };
}
