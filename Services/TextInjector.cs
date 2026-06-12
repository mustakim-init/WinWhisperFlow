using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace WinWhisperFlow.Services;

public sealed class TextInjector
{
    public void CopyUnicodeText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
        }
    }

    public async Task PasteUnicodeTextAsync(string text, IntPtr targetWindow, bool keepClipboard)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        System.Windows.IDataObject? previousClipboard = keepClipboard ? null : TryGetClipboard();
        try
        {
            System.Windows.Clipboard.SetText(text, System.Windows.TextDataFormat.UnicodeText);
            WindowFocusService.TryActivate(targetWindow);
            await Task.Delay(100);
            SendCtrlV();
        }
        finally
        {
            if (!keepClipboard && previousClipboard is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { System.Windows.Clipboard.SetDataObject(previousClipboard); } catch { }
                });
            }
        }
    }

    public bool SendUnicodeText(string text, IntPtr targetWindow)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (!WindowFocusService.TryActivate(targetWindow))
            return false;

        Thread.Sleep(80);

        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = CreateUnicodeInput(text[i], false);
            inputs[i * 2 + 1] = CreateUnicodeInput(text[i], true);
        }

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
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

    private static System.Windows.IDataObject? TryGetClipboard()
    {
        try { return System.Windows.Clipboard.GetDataObject(); }
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

    private static INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = 0x0004u | (keyUp ? 0x0002u : 0u)
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
