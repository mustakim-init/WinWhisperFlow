using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace WinWhisperFlow.Services;

public sealed class TranscriptionHistory
{
    private const int MaxEntries = 100;
    private const int DebounceMs = 500;
    private readonly string _persistPath;
    private readonly ConcurrentQueue<TranscriptionHistoryEntry> _pending = new();
    private CancellationTokenSource? _debounceCts;

    public ObservableCollection<TranscriptionHistoryEntry> Entries { get; } = new();

    public TranscriptionHistory(string? persistPath = null)
    {
        _persistPath = persistPath ?? Path.Combine(RuntimePaths.AppDataRoot, "history.json");
        Load();
    }

    public void Add(TranscriptionHistoryEntry entry)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        });
        DebouncedSave();
    }

    public bool Remove(string timestampIso, string text)
    {
        bool found = false;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var match = Entries.FirstOrDefault(e =>
                e.Timestamp.ToString("O") == timestampIso && e.Text == text);
            if (match is not null)
            {
                Entries.Remove(match);
                found = true;
            }
        });
        if (found) SaveNow();
        return found;
    }

    public void Clear()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Clear();
        });
        SaveNow();
    }

    private void DebouncedSave()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(DebounceMs, token); } catch { return; }
            if (!token.IsCancellationRequested)
                SaveNow();
        });
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
        catch (Exception ex)
        {
            try { File.AppendAllText(RuntimePaths.LogPath, $"[TranscriptionHistory.Load] {ex.Message}{Environment.NewLine}"); } catch { }
        }
    }

    private void SaveNow()
    {
        try
        {
            List<TranscriptionHistoryEntry>? snapshot = null;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                snapshot = Entries.ToList();
            });
            if (snapshot is null) return;

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_persistPath, json);
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(RuntimePaths.LogPath, $"[TranscriptionHistory.Save] {ex.Message}{Environment.NewLine}"); } catch { }
        }
    }

    public static string ActionLabel(string action) => action switch
    {
        "typed" => "[Typed]",
        "copied" => "[Copied]",
        "both" => "[Typed+Copied]",
        _ => "[--]"
    };
}
