namespace WinWhisperFlow.Services;

public sealed record TranscriptionHistoryEntry(
    DateTime Timestamp,
    string Text,
    string Language,
    double Confidence,
    string Source,
    string Action);
