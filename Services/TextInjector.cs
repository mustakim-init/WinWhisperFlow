using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace WinWhisperFlow.Services;

public sealed class TextInjector
{
    public void CopyUnicodeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
                    return;
                }
                catch (Exception ex) when (attempt == 0)
                {
                    Debug.WriteLine($"[TextInjector] Clipboard set failed: {ex.Message}");
                }
            }
        });
    }

    public async Task PasteUnicodeTextAsync(string text, IntPtr targetWindow, bool keepClipboard)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        System.Windows.IDataObject? previousClipboard = keepClipboard ? null : TryGetClipboard();
        try
        {
            RetryClipboardSet(text);
            WindowFocusService.TryActivate(targetWindow);
            await Task.Delay(100);
            SendCtrlV();
        }
        finally
        {
            if (!keepClipboard && previousClipboard is not null &&
                System.Windows.Application.Current?.Dispatcher is { } restoreDispatcher)
            {
                await restoreDispatcher.InvokeAsync(() =>
                {
                    try { System.Windows.Clipboard.SetDataObject(previousClipboard); }
                    catch (Exception ex) { Debug.WriteLine($"[TextInjector] Clipboard restore failed: {ex.Message}"); }
                });
            }
        }
    }

    public string GetTargetWindowTitle(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero) return "";
        return WindowFocusService.GetWindowTitle(targetWindow);
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            CreateKeyInput(Key.LeftCtrl, false),
            CreateKeyInput(Key.V, false),
            CreateKeyInput(Key.V, true),
            CreateKeyInput(Key.LeftCtrl, true)
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void RetryClipboardSet(string text)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText); return; }
                catch (Exception ex) when (attempt == 0)
                {
                    Debug.WriteLine($"[TextInjector] Clipboard retry: {ex.Message}");
                }
            }
        });
    }

    private static System.Windows.IDataObject? TryGetClipboard()
    {
        try { return System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Clipboard.GetDataObject()); }
        catch { return null; }
    }

    private static INPUT CreateKeyInput(Key key, bool keyUp)
    {
        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)KeyInterop.VirtualKeyFromKey(key),
                    dwFlags = keyUp ? 0x0002u : 0u
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public char wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
