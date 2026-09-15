using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexPetCredits {
    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder value, int count);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        public static bool IsWindowLifecycleEvent(uint evt, int obj, int child) { return obj == 0 && child == 0 && (evt == 0x8003 || evt == 0x8001); }

        public static HashSet<uint> CodexProcesses() {
            var ids = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName("ChatGPT")) {
                try { var executable = process.MainModule.FileName; if (executable.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0) ids.Add((uint)process.Id); }
                catch { } finally { process.Dispose(); }
            }
            return ids;
        }
        public static IntPtr FindPet(HashSet<uint> ids, double innerWidth, double dpr) {
            var candidates = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr _) {
                uint pid; GetWindowThreadProcessId(hwnd, out pid); if (!ids.Contains(pid)) return true;
                var name = new StringBuilder(128); GetClassName(hwnd, name, name.Capacity);
                long ex = GetWindowLongPtr(hwnd, -20).ToInt64(), style = GetWindowLongPtr(hwnd, -16).ToInt64();
                if (name.ToString() != "Chrome_WidgetWin_1" || (ex & 0x88) != 0x88 || (style & 0xC00000) != 0) return true;
                Rect client; GetClientRect(hwnd, out client);
                // DOM sizes can lag native resizes during a drag. A unique native candidate remains valid.
                candidates.Add(hwnd); return true;
            }, IntPtr.Zero);
            // Ambiguous candidates are not silently attached to an unrelated tool window.
            return candidates.Count == 1 ? candidates[0] : IntPtr.Zero;
        }
        public static Rect WorkArea(int x, int y) {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            GetMonitorInfo(MonitorFromPoint(new Point { X = x, Y = y }, 2), ref info); return info.Work;
        }
    }
}
