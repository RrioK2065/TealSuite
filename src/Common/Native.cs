// TealSuite 共通: よく使う Win32 API
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TealSuite.Common
{
    public static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        /// <summary>-1 (ASFW_ANY) で、次に前面化しようとする任意のプロセスに許可を渡す</summary>
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>プロセスの exe フルパス。32/64bit や権限の違いがあっても取れる方法を優先し、だめなら null。</summary>
        public static string GetProcessPath(System.Diagnostics.Process p)
        {
            if (AppInfo.IsWindows)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                    if (h != IntPtr.Zero)
                    {
                        var sb = new StringBuilder(1024); int size = sb.Capacity;
                        if (QueryFullProcessImageName(h, 0, sb, ref size)) return sb.ToString(0, size);
                    }
                }
                catch { }
                finally { if (h != IntPtr.Zero) CloseHandle(h); }
            }
            try { return p.MainModule.FileName; } catch { return null; }
        }
    }
}
