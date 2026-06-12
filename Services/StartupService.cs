using Microsoft.Win32;

namespace WinWhisperFlow.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WinWhisperFlow";

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? "";
            key.SetValue(AppName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
            return;
        }

        key.DeleteValue(AppName, false);
    }
}
