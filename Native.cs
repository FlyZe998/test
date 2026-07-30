using System;
using System.Runtime.InteropServices;

namespace KeySpammer;

/// <summary>
/// Raw Win32 interop. Mirrors the ctypes SendInput struct layout from the
/// Python version -- INPUT must be laid out exactly like this or Windows
/// silently drops/corrupts events (this is the c_ulong vs c_ulonglong bug
/// from before: on x64, the union + type field must total the right size).
/// </summary>
internal static class Native
{
    public const int INPUT_KEYBOARD = 1;
    public const int INPUT_MOUSE = 0;

    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;
    public const uint MAPVK_VK_TO_VSC = 0x00;

    public const int WH_MOUSE_LL = 14;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const short XBUTTON1 = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    // Windows defaults to ~15.6ms scheduler granularity -- Thread.Sleep(1)
    // can actually oversleep by up to 15x without requesting finer timer
    // resolution first. This is the standard fix for tight timing loops.
    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    // The managed ThreadPriority enum tops out at Highest, which maps to a
    // lower native priority than THREAD_PRIORITY_TIME_CRITICAL -- this gets
    // the send thread the highest priority Windows exposes without touching
    // process-level Realtime (which needs elevation and can genuinely
    // starve the rest of the system, including your mouse/keyboard input).
    public const int THREAD_PRIORITY_TIME_CRITICAL = 15;

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

    // Keys that require KEYEVENTF_EXTENDEDKEY per the SendInput docs -- without
    // it, Windows maps the scan code back to the wrong (non-extended) key,
    // e.g. Right Ctrl would register as Left Ctrl.
    private static readonly System.Collections.Generic.HashSet<ushort> ExtendedKeys = new()
    {
        0xA3, 0xA5,             // Right Ctrl, Right Alt
        0x21, 0x22, 0x23, 0x24, // Page Up/Down, End, Home
        0x25, 0x26, 0x27, 0x28, // Arrow Left/Up/Right/Down
        0x2C, 0x2D, 0x2E,       // Print Screen, Insert, Delete
        0x90,                   // Num Lock
        0x6F                    // Numpad /
    };

    /// <summary>Builds one key-down + key-up pair for the given virtual-key code.</summary>
    public static INPUT[] BuildKeyPress(ushort vk)
    {
        ushort scan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
        uint extended = ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0;

        return new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_SCANCODE | extended, time = 0, dwExtraInfo = IntPtr.Zero } }
            },
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP | extended, time = 0, dwExtraInfo = IntPtr.Zero } }
            }
        };
    }
}
