using System.IO;

namespace WinWhisperFlow.Services;

public static class RuntimePaths
{
    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinWhisperFlow");

    public static string RuntimeRoot { get; } = Path.Combine(AppDataRoot, "runtime");

    public static string UserVenvPython { get; } =
        Path.Combine(RuntimeRoot, ".venv", "Scripts", "python.exe");

    public static string LogPath { get; } = Path.Combine(AppDataRoot, "winwhisper.log");
}
