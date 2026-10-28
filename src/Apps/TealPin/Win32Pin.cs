// Teal Pin - Win32 の呼び出し (Windows のときだけ使う)
//  画面の位置はすべて「物理 px」(モニターごとの DPI 対応 = Per-Monitor v2) で扱う。
//  Teal Pin の画面 (一覧など) はスイートの決まりどおり System DPI のまま。位置を読む・枠線を動かすときだけ
//  スレッドを一時的に Per-Monitor v2 にする (DpiScope)。古い Windows ではそのまま (何もしない)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TealSuite.Pin
{
    static class PinNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRect() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40,
            SWP_HIDEWINDOW = 0x80, SWP_NOOWNERZORDER = 0x200;
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
            WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x8000000;
        public const int WS_CAPTION = 0xC00000, WS_CHILD = 0x40000000;
        public const uint WS_POPUP = 0x80000000;
        public const uint GA_ROOT = 2, GW_OWNER = 4, GW_HWNDPREV = 3;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr BeginDeferWindowPos(int n);
        [DllImport("user32.dll")] public static extern IntPtr DeferWindowPos(IntPtr info, IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool EndDeferWindowPos(IntPtr info);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        public delegate bool EnumProc(IntPtr h, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lp);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr h, ref POINT pt);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, int key, byte alpha, int flags);

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr p, int flags, StringBuilder sb, ref int size);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);
    }

    /// <summary>このスレッドを一時的に Per-Monitor v2 にする (using で戻す)。対応していない Windows では何もしない</summary>
    public struct DpiScope : IDisposable
    {
        static bool unsupported;
        IntPtr old;

        public static DpiScope Enter()
        {
            var s = new DpiScope();
            if (unsupported) return s;
            try { s.old = PinNative.SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { unsupported = true; }
            catch (DllNotFoundException) { unsupported = true; }
            return s;
        }

        public void Dispose()
        {
            if (old == IntPtr.Zero) return;
            try { PinNative.SetThreadDpiAwarenessContext(old); } catch { }
            old = IntPtr.Zero;
        }
    }

    public class Win32Windows : IWindowApi
    {
        int lastError;
        bool? selfElevated;
        readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int LastError { get { return lastError; } }

        public bool SelfElevated
        {
            get
            {
                if (selfElevated == null) selfElevated = TokenElevated(PinNative.GetCurrentProcess()) == true;
                return selfElevated.Value;
            }
        }

        static bool? TokenElevated(IntPtr proc)
        {
            IntPtr tok;
            if (!PinNative.OpenProcessToken(proc, 0x8 /* TOKEN_QUERY */, out tok)) return null;
            try
            {
                int v, len;
                if (!PinNative.GetTokenInformation(tok, 20 /* TokenElevation */, out v, 4, out len)) return null;
                return v != 0;
            }
            finally { PinNative.CloseHandle(tok); }
        }

        public bool IsWindow(IntPtr h) { return h != IntPtr.Zero && PinNative.IsWindow(h); }
        public bool IsVisible(IntPtr h) { return PinNative.IsWindowVisible(h); }
        public bool IsMinimized(IntPtr h) { return PinNative.IsIconic(h); }
        public bool IsMaximized(IntPtr h) { return PinNative.IsZoomed(h); }

        public bool IsCloaked(IntPtr h)
        {
            try { int v; return PinNative.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out v, 4) == 0 && v != 0; }
            catch { return false; }
        }

        public bool IsTopmost(IntPtr h) { return (PinNative.GetWindowLong(h, PinNative.GWL_EXSTYLE) & PinNative.WS_EX_TOPMOST) != 0; }

        public bool IsAppWindow(IntPtr h)
        {
            if (!IsWindow(h) || !PinNative.IsWindowVisible(h)) return false;
            if (PinNative.GetAncestor(h, PinNative.GA_ROOT) != h) return false;
            if (PinNative.GetWindow(h, PinNative.GW_OWNER) != IntPtr.Zero) return false;
            int ex = PinNative.GetWindowLong(h, PinNative.GWL_EXSTYLE), st = PinNative.GetWindowLong(h, PinNative.GWL_STYLE);
            if ((ex & PinNative.WS_EX_TOOLWINDOW) != 0 || (ex & PinNative.WS_EX_NOACTIVATE) != 0) return false;
            if ((st & PinNative.WS_CAPTION) != PinNative.WS_CAPTION && (ex & PinNative.WS_EX_APPWINDOW) == 0) return false;
            return PinNative.GetWindowTextLength(h) > 0;
        }

        public bool SetTopmost(IntPtr h, bool on)
        {
            bool ok = PinNative.SetWindowPos(h, on ? PinNative.HWND_TOPMOST : PinNative.HWND_NOTOPMOST, 0, 0, 0, 0,
                PinNative.SWP_NOMOVE | PinNative.SWP_NOSIZE | PinNative.SWP_NOACTIVATE);
            lastError = ok ? 0 : Marshal.GetLastWin32Error();
            // UIPI (管理者のウィンドウ) は「成功」を返して何もしないことがあるので、結果を確かめる
            if (ok && IsTopmost(h) != on) { ok = false; if (lastError == 0) lastError = 5; }
            return ok;
        }

        public string Title(IntPtr h)
        {
            int n = PinNative.GetWindowTextLength(h);
            if (n <= 0) return "";
            var sb = new StringBuilder(Math.Min(n, 1024) + 1);
            PinNative.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            PinNative.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public int ProcessId(IntPtr h) { int pid; PinNative.GetWindowThreadProcessId(h, out pid); return pid; }

        public string ExePath(int pid)
        {
            if (pid <= 0) return null;
            var p = PinNative.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (p == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return PinNative.QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { PinNative.CloseHandle(p); }
        }

        public string AppName(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            string n;
            if (nameCache.TryGetValue(exePath, out n)) return n;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                n = !string.IsNullOrWhiteSpace(vi.FileDescription) ? vi.FileDescription : (!string.IsNullOrWhiteSpace(vi.ProductName) ? vi.ProductName : null);
            }
            catch { n = null; }
            if (string.IsNullOrWhiteSpace(n)) n = Path.GetFileNameWithoutExtension(exePath);
            if (nameCache.Count > 200) nameCache.Clear();
            nameCache[exePath] = n;
            return n;
        }

        /// <summary>
        /// 管理者として (高い権限で) 動いているか。ふつうの権限からは中を見られないことが多いので、
        /// プロセスは開けるのにトークンが開けないときも「高い権限」とみなす
        /// </summary>
        public bool IsElevated(int pid)
        {
            if (pid <= 0 || SelfElevated) return false;
            var p = PinNative.OpenProcess(0x1000, false, pid);
            if (p == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;
            try { var e = TokenElevated(p); return e == null || e.Value; }
            finally { PinNative.CloseHandle(p); }
        }

        public Rectangle FrameBounds(IntPtr h)
        {
            using (DpiScope.Enter())
            {
                try
                {
                    PinNative.RECT r;
                    if (PinNative.DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out r, Marshal.SizeOf(typeof(PinNative.RECT))) == 0 && r.Right > r.Left)
                        return r.ToRect();
                }
                catch { }
                PinNative.RECT w;
                return PinNative.GetWindowRect(h, out w) ? w.ToRect() : Rectangle.Empty;
            }
        }

        public Rectangle WindowRect(IntPtr h)
        {
            using (DpiScope.Enter())
            {
                PinNative.RECT w;
                return PinNative.GetWindowRect(h, out w) ? w.ToRect() : Rectangle.Empty;
            }
        }

        public Rectangle WorkArea(IntPtr h)
        {
            using (DpiScope.Enter())
            {
                var mi = new PinNative.MONITORINFO { cbSize = Marshal.SizeOf(typeof(PinNative.MONITORINFO)) };
                var mon = PinNative.MonitorFromWindow(h, 2 /* NEAREST */);
                return mon != IntPtr.Zero && PinNative.GetMonitorInfo(mon, ref mi) ? mi.rcWork.ToRect() : Rectangle.Empty;
            }
        }

        public static Rectangle WorkAreaAt(Point physical)
        {
            using (DpiScope.Enter())
            {
                var mi = new PinNative.MONITORINFO { cbSize = Marshal.SizeOf(typeof(PinNative.MONITORINFO)) };
                var mon = PinNative.MonitorFromPoint(new PinNative.POINT { X = physical.X, Y = physical.Y }, 2);
                return mon != IntPtr.Zero && PinNative.GetMonitorInfo(mon, ref mi) ? mi.rcWork.ToRect() : Rectangle.Empty;
            }
        }

        public static double ScaleAt(Point physical)
        {
            using (DpiScope.Enter())
            {
                try
                {
                    var mon = PinNative.MonitorFromPoint(new PinNative.POINT { X = physical.X, Y = physical.Y }, 2);
                    uint x, y;
                    if (PinNative.GetDpiForMonitor(mon, 0, out x, out y) == 0 && x > 0) return x / 96.0;
                }
                catch { }
                return SystemScale();
            }
        }

        static double SystemScale()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0; } catch { return 1; }
        }

        public double Scale(IntPtr h)
        {
            using (DpiScope.Enter())
            {
                try
                {
                    var mon = PinNative.MonitorFromWindow(h, 2);
                    uint x, y;
                    if (PinNative.GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out x, out y) == 0 && x > 0) return x / 96.0;
                }
                catch { }
                try { var d = PinNative.GetDpiForWindow(h); if (d > 0) return d / 96.0; } catch { }
                return SystemScale();
            }
        }

        public bool Restore(IntPtr h)
        {
            if (PinNative.IsHungAppWindow(h)) { lastError = 1460; return false; }
            return PinNative.ShowWindow(h, 9 /* SW_RESTORE */);
        }

        public bool Move(IntPtr h, Rectangle r)
        {
            if (PinNative.IsHungAppWindow(h)) { lastError = 1460; return false; }
            using (DpiScope.Enter())
            {
                bool ok = PinNative.SetWindowPos(h, IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
                    PinNative.SWP_NOZORDER | PinNative.SWP_NOACTIVATE | PinNative.SWP_NOOWNERZORDER);
                lastError = ok ? 0 : Marshal.GetLastWin32Error();
                return ok;
            }
        }

        public bool Activate(IntPtr h)
        {
            if (PinNative.IsIconic(h)) PinNative.ShowWindow(h, 9);
            PinNative.BringWindowToTop(h);
            return PinNative.SetForegroundWindow(h);
        }

        public IntPtr Foreground() { return PinNative.GetForegroundWindow(); }
        public IntPtr Root(IntPtr h) { var r = PinNative.GetAncestor(h, PinNative.GA_ROOT); return r == IntPtr.Zero ? h : r; }

        public List<IntPtr> TopLevelWindows()
        {
            var list = new List<IntPtr>();
            PinNative.EnumProc cb = (h, lp) => { list.Add(h); return true; };
            PinNative.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return list;
        }

        /// <summary>マウスの位置 (物理 px)</summary>
        public static Point CursorPhysical()
        {
            using (DpiScope.Enter())
            {
                PinNative.POINT p;
                return PinNative.GetCursorPos(out p) ? new Point(p.X, p.Y) : Point.Empty;
            }
        }

        /// <summary>この物理座標にある (いちばん外側の) ウィンドウ</summary>
        public static IntPtr RootAt(Point physical)
        {
            using (DpiScope.Enter())
            {
                var h = PinNative.WindowFromPoint(new PinNative.POINT { X = physical.X, Y = physical.Y });
                if (h == IntPtr.Zero) return IntPtr.Zero;
                var r = PinNative.GetAncestor(h, PinNative.GA_ROOT);
                return r == IntPtr.Zero ? h : r;
            }
        }

        /// <summary>
        /// タイトルバーか (WM_NCHITTEST が HTCAPTION / HTSYSMENU)。
        /// 応答しない・権限が違って聞けないときは null。座標は相手のウィンドウの DPI に合わせて直してから聞く
        /// </summary>
        public static bool? IsCaption(IntPtr root, Point physical)
        {
            var pt = new PinNative.POINT { X = physical.X, Y = physical.Y };
            using (DpiScope.Enter())
            {
                try { PinNative.PhysicalToLogicalPointForPerMonitorDPI(root, ref pt); } catch { }
            }
            var lp = new IntPtr(unchecked((int)(((uint)(pt.Y & 0xFFFF) << 16) | (uint)(pt.X & 0xFFFF))));
            IntPtr res;
            var ok = PinNative.SendMessageTimeout(root, 0x84 /* WM_NCHITTEST */, IntPtr.Zero, lp, 0x2 /* SMTO_ABORTIFHUNG */, 150, out res);
            if (ok == IntPtr.Zero) return null;
            long v = res.ToInt64();
            return v == 2 /* HTCAPTION */ || v == 3 /* HTSYSMENU */;
        }

        /// <summary>聞けないとき (管理者のウィンドウなど) の目安: 見えている枠の上端から、タイトルバーの高さぶん</summary>
        public bool InCaptionBand(IntPtr h, Point physical)
        {
            var f = FrameBounds(h);
            if (!f.Contains(physical)) return false;
            int band = (int)Math.Round(40 * Scale(h));
            return physical.Y < f.Top + band;
        }
    }
}
