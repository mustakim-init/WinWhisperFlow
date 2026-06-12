namespace WinWhisperFlow.Services;

public sealed record TranscriptionResult(
    string Text,
    string Language,
    double LanguageProbability,
    int SegmentCount,
    double? AverageLogProbability,
    double? NoSpeechProbability);
