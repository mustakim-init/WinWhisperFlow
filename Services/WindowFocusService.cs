using System.Runtime.InteropServices;

namespace WinWhisperFlow.Services;

public static class WindowFocusService
{
    public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

    public static bool TryActivate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;

        SwitchToThisWindow(windowHandle, true);

        for (int i = 0; i < 5; i++)
        {
            uint currentThread = GetCurrentThreadId();
            uint targetThread = GetWindowThreadProcessId(windowHandle, out _);

            bool attached = AttachThreadInput(currentThread, targetThread, true);
            if (attached)
            {
                try
                {
                    SetForegroundWindow(windowHandle);
                    ShowWindowAsync(windowHandle, SW_RESTORE);
                }
                finally
                {
                    AttachThreadInput(currentThread, targetThread, false);
                }
            }
            else
            {
                SetForegroundWindow(windowHandle);
                ShowWindowAsync(windowHandle, SW_RESTORE);
            }

            if (GetForegroundWindow() == windowHandle)
                return true;

            Thread.Sleep(50 * (i + 1));
        }

        return GetForegroundWindow() == windowHandle;
    }

    public static bool BelongsToCurrentProcess(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        GetWindowThreadProcessId(windowHandle, out uint processId);
        return processId == Environment.ProcessId;
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        try
        {
            int len = GetWindowTextLength(hWnd) + 1;
            var sb = new System.Text.StringBuilder(len);
            GetWindowText(hWnd, sb, len);
            return sb.ToString();
        }
        catch { return ""; }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
