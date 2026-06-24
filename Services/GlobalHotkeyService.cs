using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace WinWhisperFlow.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId;
    private DateTime _lastToggleUtc = DateTime.MinValue;

    private HashSet<int> _chordKeys = new() { KeyInterop.VirtualKeyFromKey(Key.S), 0x11, 0x12 }; // Ctrl+Alt+S

    public event EventHandler? ToggleRequested;

    public GlobalHotkeyService()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using Process currentProcess = Process.GetCurrentProcess();
        using ProcessModule? currentModule = currentProcess.MainModule;
        IntPtr moduleHandle = currentModule is null ? IntPtr.Zero : GetModuleHandle(currentModule.ModuleName);
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, moduleHandle, 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Failed to install global hotkey hook.");
    }

    public void UpdateChord(IReadOnlyList<int> vkCodes)
    {
        _chordKeys = new HashSet<int>(vkCodes);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            bool chordMatch = _chordKeys.Count > 0
                && _chordKeys.Contains(vkCode)
                && _chordKeys.All(k => k == vkCode || IsKeyDown(k));

            if (chordMatch && CanToggle())
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool CanToggle()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastToggleUtc).TotalMilliseconds < 350) return false;
        _lastToggleUtc = now;
        return true;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
