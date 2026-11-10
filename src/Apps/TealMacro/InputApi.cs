// Teal Macro - マウス・キーボードの入力を送る (SendInput) / 画面の位置・ウィンドウを調べる
//  - IInputApi … 本物 (Win32Input) とテスト・デモ用 (FakeInput) を差し替えられるように
//  - 位置はすべて物理 px。Teal Macro の画面はスイートの決まりどおり System DPI のまま、位置を扱うときだけ
//    スレッドを一時的に Per-Monitor v2 にする (DpiScope。Teal Pin と同じやり方。古い Windows では何もしない)
//  - 入力は SendInput。マウスの移動は仮想デスクトップ全体に対する絶対座標 (MOUSEEVENTF_VIRTUALDESK) で送る
//  - 文字の入力は KEYEVENTF_UNICODE (IME やキー配列に関係なくそのままの文字が入る)。改行は Enter、タブは Tab
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    /// <summary>ウィンドウ 1 つ (どのウィンドウの中の位置かを覚えるため)</summary>
    public class WindowInfo
    {
        public IntPtr Handle;
        public string Exe, WindowClass, Title;
        /// <summary>クライアント領域の左上 (物理 px)</summary>
        public Point ClientOrigin;
        public int Pid;
    }

    public interface IInputApi
    {
        Point CursorPos();
        void MoveTo(Point p);
        void Button(MouseBtn b, bool down);
        void Wheel(int delta);
        void Key(Keys vk, bool down);
        void Text(string s);
        /// <summary>その位置の一番上のウィンドウが Teal Macro のものなら true (そこはクリックしない)</summary>
        bool IsOwnWindowAt(Point p);
        /// <summary>その位置のウィンドウ (トップレベル)。なければ null</summary>
        WindowInfo WindowAt(Point p);
        /// <summary>覚えたウィンドウを今の画面から探す (exe とクラスが同じもの。タイトルも同じものを優先)。なければ null</summary>
        WindowInfo FindWindow(TargetPoint t);
    }

    /// <summary>テスト・デモ用: 送った入力を文字で記録するだけ</summary>
    public class FakeInput : IInputApi
    {
        public readonly List<string> Log = new List<string>();
        public Point Cursor = new Point(500, 400);
        public Rectangle OwnWindow = Rectangle.Empty;
        public readonly List<WindowInfo> Windows = new List<WindowInfo>();
        public readonly List<Rectangle> WindowRects = new List<Rectangle>();
        readonly object gate = new object();

        void Add(string s) { lock (gate) Log.Add(s); }
        public List<string> Snapshot() { lock (gate) return Log.ToList(); }
        public int Count(string prefix) { lock (gate) return Log.Count(l => l.StartsWith(prefix)); }

        public Point CursorPos() { return Cursor; }
        public void MoveTo(Point p) { Cursor = p; Add("move " + p.X + "," + p.Y); }
        public void Button(MouseBtn b, bool down) { Add((down ? "down " : "up ") + MacroStep.BtnName(b) + " " + Cursor.X + "," + Cursor.Y); }
        public void Wheel(int delta) { Add("wheel " + delta); }
        public void Key(Keys vk, bool down) { Add((down ? "keydown " : "keyup ") + vk); }
        public void Text(string s) { Add("text " + s); }
        public bool IsOwnWindowAt(Point p) { return OwnWindow.Contains(p); }

        public WindowInfo AddWindow(string exe, string cls, string title, Rectangle client)
        {
            var w = new WindowInfo { Handle = new IntPtr(1000 + Windows.Count), Exe = exe, WindowClass = cls, Title = title, ClientOrigin = client.Location };
            Windows.Add(w); WindowRects.Add(client);
            return w;
        }

        public WindowInfo WindowAt(Point p)
        {
            for (int i = Windows.Count - 1; i >= 0; i--) if (WindowRects[i].Contains(p)) return Windows[i];
            return null;
        }

        public WindowInfo FindWindow(TargetPoint t) { return WindowMatch.Best(Windows, t); }
    }

    public static class WindowMatch
    {
        /// <summary>exe とクラスが同じウィンドウの中から、タイトルも同じものを優先して選ぶ</summary>
        public static WindowInfo Best(IEnumerable<WindowInfo> list, TargetPoint t)
        {
            if (t == null || !t.HasWindow) return null;
            var same = list.Where(w => w != null
                && (string.IsNullOrEmpty(t.Exe) || string.Equals(w.Exe, t.Exe, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(t.WindowClass) || string.Equals(w.WindowClass, t.WindowClass, StringComparison.Ordinal))).ToList();
            return same.FirstOrDefault(w => string.Equals(w.Title, t.Title, StringComparison.Ordinal)) ?? same.FirstOrDefault();
        }

        /// <summary>ピックした位置から TargetPoint を作る (ウィンドウがあればその中の位置も)</summary>
        public static TargetPoint Make(Point screen, WindowInfo w)
        {
            var t = new TargetPoint { X = screen.X, Y = screen.Y };
            if (w != null)
            {
                t.Exe = w.Exe; t.WindowClass = w.WindowClass; t.Title = w.Title;
                t.RelX = screen.X - w.ClientOrigin.X; t.RelY = screen.Y - w.ClientOrigin.Y;
            }
            return t;
        }
    }

    // ================================================================ 本物 (Windows)
    public struct MacroDpiScope : IDisposable
    {
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        IntPtr old; bool on;
        public static MacroDpiScope Enter()
        {
            var s = new MacroDpiScope();
            if (!AppInfo.IsWindows) return s;
            try { s.old = SetThreadDpiAwarenessContext(new IntPtr(-4)); s.on = s.old != IntPtr.Zero; } catch { }
            return s;
        }
        public void Dispose()
        {
            if (!on) return;
            try { SetThreadDpiAwarenessContext(old); } catch { }
        }
    }

    public class Win32Input : IInputApi
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc fn, IntPtr l);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10,
            MOUSEEVENTF_MIDDLEDOWN = 0x20, MOUSEEVENTF_MIDDLEUP = 0x40, MOUSEEVENTF_WHEEL = 0x800, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4, KEYEVENTF_SCANCODE = 0x8;
        /// <summary>Teal Macro が送った入力の印 (dwExtraInfo)。記録のフックで見分けるため</summary>
        public static readonly IntPtr Signature = new IntPtr(0x7EA1AC0);

        readonly uint selfPid;
        public Win32Input() { try { selfPid = (uint)Process.GetCurrentProcess().Id; } catch { } }

        static void Send(params INPUT[] inputs)
        {
            if (inputs.Length == 0) return;
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0)
        {
            var i = new INPUT { type = INPUT_MOUSE };
            i.u.mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = Signature };
            return i;
        }

        public Point CursorPos()
        {
            using (MacroDpiScope.Enter())
            {
                POINT p;
                return GetCursorPos(out p) ? new Point(p.X, p.Y) : Point.Empty;
            }
        }

        /// <summary>仮想デスクトップに対する 0〜65535 の値 (物理 px から)</summary>
        public static int Normalize(int v, int origin, int size)
        {
            if (size <= 1) return 0;
            long n = ((long)(v - origin) * 65536 + size - 1) / size;   // 切り上げ (戻すと v の px にぴったり入る)
            return (int)Math.Max(0, Math.Min(65535, n));
        }

        public void MoveTo(Point p)
        {
            using (MacroDpiScope.Enter())
            {
                int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
                Send(Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, Normalize(p.X, vx, vw), Normalize(p.Y, vy, vh)));
            }
        }

        public void Button(MouseBtn b, bool down)
        {
            uint f = b == MouseBtn.Right ? (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP)
                   : b == MouseBtn.Middle ? (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP)
                   : (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
            Send(Mouse(f));
        }

        public void Wheel(int delta) { Send(Mouse(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta))); }

        static bool Extended(Keys k)
        {
            switch (k)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.Insert: case Keys.Delete:
                case Keys.Home: case Keys.End: case Keys.Prior: case Keys.Next: case Keys.RControlKey: case Keys.RMenu:
                case Keys.NumLock: case Keys.Divide: case Keys.Apps: case Keys.LWin: case Keys.RWin: case Keys.PrintScreen:
                    return true;
                default: return false;
            }
        }

        public void Key(Keys vk, bool down)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            uint flags = (down ? 0u : KEYEVENTF_KEYUP) | (Extended(vk) ? KEYEVENTF_EXTENDEDKEY : 0u);
            i.u.ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey((uint)vk, 0), dwFlags = flags, dwExtraInfo = Signature };
            Send(i);
        }

        public void Text(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var list = new List<INPUT>();
            foreach (var ch in s.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (ch == '\n' || ch == '\t')
                {
                    var vk = ch == '\n' ? Keys.Return : Keys.Tab;
                    foreach (var up in new[] { false, true })
                    {
                        var k = new INPUT { type = INPUT_KEYBOARD };
                        k.u.ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey((uint)vk, 0), dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = Signature };
                        list.Add(k);
                    }
                    continue;
                }
                foreach (var up in new[] { false, true })
                {
                    var k = new INPUT { type = INPUT_KEYBOARD };
                    k.u.ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = Signature };
                    list.Add(k);
                }
            }
            // 長い文は少しずつ (相手のアプリが取りこぼさないように)
            for (int i = 0; i < list.Count; i += 64) { Send(list.Skip(i).Take(64).ToArray()); if (i + 64 < list.Count) System.Threading.Thread.Sleep(5); }
        }

        IntPtr RootAt(Point p)
        {
            var h = WindowFromPoint(new POINT { X = p.X, Y = p.Y });
            if (h == IntPtr.Zero) return IntPtr.Zero;
            var r = GetAncestor(h, 2 /* GA_ROOT */);
            return r == IntPtr.Zero ? h : r;
        }

        public bool IsOwnWindowAt(Point p)
        {
            using (MacroDpiScope.Enter())
            {
                var h = RootAt(p);
                if (h == IntPtr.Zero) return false;
                uint pid; GetWindowThreadProcessId(h, out pid);
                return pid == selfPid;
            }
        }

        static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
        static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

        static string ExeOf(uint pid)
        {
            var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? Path.GetFileName(sb.ToString()).ToLowerInvariant() : null;
            }
            finally { CloseHandle(h); }
        }

        WindowInfo Info(IntPtr h)
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            var o = new POINT();
            ClientToScreen(h, ref o);
            return new WindowInfo { Handle = h, Pid = (int)pid, Exe = ExeOf(pid), WindowClass = ClassOf(h), Title = TitleOf(h), ClientOrigin = new Point(o.X, o.Y) };
        }

        public WindowInfo WindowAt(Point p)
        {
            using (MacroDpiScope.Enter())
            {
                var h = RootAt(p);
                if (h == IntPtr.Zero) return null;
                var w = Info(h);
                if (w.Pid == selfPid) return null;
                return w;
            }
        }

        public WindowInfo FindWindow(TargetPoint t)
        {
            if (t == null || !t.HasWindow) return null;
            using (MacroDpiScope.Enter())
            {
                var list = new List<WindowInfo>();
                EnumWindows((h, l) =>
                {
                    try
                    {
                        if (!IsWindowVisible(h) || IsIconic(h)) return true;
                        if (!string.IsNullOrEmpty(t.WindowClass) && ClassOf(h) != t.WindowClass) return true;
                        var w = Info(h);
                        if (w.Pid != selfPid) list.Add(w);
                    }
                    catch { }
                    return list.Count < 200;
                }, IntPtr.Zero);
                return WindowMatch.Best(list, t);
            }
        }
    }
}
