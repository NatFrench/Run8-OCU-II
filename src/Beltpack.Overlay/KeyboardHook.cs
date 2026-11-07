using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Beltpack.Overlay;

/// <summary>
/// Low-level keyboard hook so the OCU can be driven while Run8 has focus. Key releases are needed
/// for momentary controls (horn, RSC), which RegisterHotKey can't report.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private readonly IntPtr _hook;
    private readonly int _ownProcessId = Environment.ProcessId;
    private IntPtr _lastForeground;
    private bool _lastForegroundIsTarget;

    /// <summary>Return true to swallow the key.</summary>
    public Func<Key, bool, bool>? KeyChanged { get; set; }

    public KeyboardHook()
    {
        _proc = HookCallback;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && KeyChanged is not null && ForegroundIsTarget())
        {
            int msg = wParam.ToInt32();
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            if (down || up)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = KeyInterop.KeyFromVirtualKey((int)info.vkCode);
                if (KeyChanged(key, down))
                    return 1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ForegroundIsTarget()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == _lastForeground)
            return _lastForegroundIsTarget;

        _lastForeground = hwnd;
        _lastForegroundIsTarget = false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownProcessId)
        {
            _lastForegroundIsTarget = true;
        }
        else
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                _lastForegroundIsTarget = p.ProcessName.StartsWith("Run-8", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
            }
        }
        return _lastForegroundIsTarget;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
