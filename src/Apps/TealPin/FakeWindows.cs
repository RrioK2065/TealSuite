// Teal Pin - 見本のウィンドウ (Windows 以外・TEALSUITE_DEMO=1 のとき・テスト)。本物の画面には何もしない
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TealSuite.Pin
{
    public class FakeWindow
    {
        public IntPtr Handle;
        public string Title = "", ClassName = "Window", ExePath = "", AppName;
        public int Pid;
        public Rectangle Rect;
        /// <summary>見えない縁 (Windows 10/11 は左右と下に 7px ほど)</summary>
        public int InvisibleEdge = 7;
        public bool Visible = true, Minimized, Maximized, Cloaked, Topmost, Elevated, Owned, Tool;
        /// <summary>SetWindowPos を失敗させる (エラー番号)</summary>
        public int FailWith;
        public int Monitor;
    }

    public class FakeWindows : IWindowApi
    {
        public readonly List<FakeWindow> Windows = new List<FakeWindow>();
        public readonly List<Rectangle> Monitors = new List<Rectangle> { new Rectangle(0, 0, 1920, 1080) };
        public readonly List<double> Scales = new List<double> { 1.0 };
        public int TaskbarHeight = 48;
        public IntPtr ForegroundHandle;
        public bool Elevated;
        public int Moves;
        int next = 0x1000, lastError;

        public FakeWindow Add(string title, string exe, Rectangle rect, int pid = 0)
        {
            var w = new FakeWindow { Handle = new IntPtr(next += 0x10), Title = title, ExePath = exe, Rect = rect, Pid = pid != 0 ? pid : 4000 + Windows.Count };
            Windows.Add(w);
            return w;
        }

        public FakeWindow Get(IntPtr h) { return Windows.FirstOrDefault(w => w.Handle == h); }
        public void Close(IntPtr h) { Windows.RemoveAll(w => w.Handle == h); }

        /// <summary>見本: 一覧のスクリーンショット用</summary>
        public static FakeWindows Demo()
        {
            var f = new FakeWindows();
            f.Add("会議メモ.txt - メモ帳", @"C:\Windows\System32\notepad.exe", new Rectangle(1380, 640, 500, 360)).AppName = "メモ帳";
            f.Add("Teal Plan - 今日の予定", @"C:\Program Files\TealSuite\apps\TealPlan\TealPlan.exe", new Rectangle(120, 80, 900, 700)).AppName = "Teal Plan";
            f.Add("電卓", @"C:\Windows\System32\calc.exe", new Rectangle(1500, 60, 340, 520)).AppName = "電卓";
            f.Add("資料 - Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", new Rectangle(200, 120, 1400, 860)).AppName = "Microsoft Edge";
            f.Add("ペイント", @"C:\Windows\System32\mspaint.exe", new Rectangle(300, 200, 900, 600)).AppName = "ペイント";
            var admin = f.Add("管理者: コマンド プロンプト", @"C:\Windows\System32\cmd.exe", new Rectangle(240, 300, 800, 450));
            admin.AppName = "Windows コマンド プロセッサ"; admin.Elevated = true;
            f.ForegroundHandle = f.Windows[0].Handle;
            return f;
        }

        public int LastError { get { return lastError; } }
        public bool SelfElevated { get { return Elevated; } }
        public bool IsWindow(IntPtr h) { return Get(h) != null; }
        public bool IsVisible(IntPtr h) { var w = Get(h); return w != null && w.Visible; }
        public bool IsMinimized(IntPtr h) { var w = Get(h); return w != null && w.Minimized; }
        public bool IsMaximized(IntPtr h) { var w = Get(h); return w != null && w.Maximized; }
        public bool IsCloaked(IntPtr h) { var w = Get(h); return w != null && w.Cloaked; }
        public bool IsTopmost(IntPtr h) { var w = Get(h); return w != null && w.Topmost; }
        public bool IsAppWindow(IntPtr h) { var w = Get(h); return w != null && w.Visible && !w.Owned && !w.Tool && w.Title.Length > 0; }

        public bool SetTopmost(IntPtr h, bool on)
        {
            var w = Get(h);
            if (w == null) { lastError = 1400; return false; }
            if (w.FailWith != 0) { lastError = w.FailWith; return false; }
            if (w.Elevated && !Elevated) { lastError = 5; return false; }
            w.Topmost = on; lastError = 0;
            return true;
        }

        public string Title(IntPtr h) { var w = Get(h); return w == null ? "" : w.Title; }
        public string ClassName(IntPtr h) { var w = Get(h); return w == null ? "" : w.ClassName; }
        public int ProcessId(IntPtr h) { var w = Get(h); return w == null ? 0 : w.Pid; }
        public string ExePath(int pid) { var w = Windows.FirstOrDefault(x => x.Pid == pid); return w == null ? null : w.ExePath; }
        public string AppName(string exe)
        {
            var w = Windows.FirstOrDefault(x => x.ExePath == exe && !string.IsNullOrEmpty(x.AppName));
            return w != null ? w.AppName : System.IO.Path.GetFileNameWithoutExtension(exe ?? "");
        }
        public bool IsElevated(int pid) { return Windows.Any(x => x.Pid == pid && x.Elevated); }

        public Rectangle FrameBounds(IntPtr h)
        {
            var w = Get(h);
            if (w == null) return Rectangle.Empty;
            if (w.Maximized) return WorkOf(w);
            return w.Rect;
        }

        public Rectangle WindowRect(IntPtr h)
        {
            var w = Get(h);
            if (w == null) return Rectangle.Empty;
            var f = FrameBounds(h);
            int e = w.InvisibleEdge;
            return Rectangle.FromLTRB(f.Left - e, f.Top, f.Right + e, f.Bottom + e);
        }

        Rectangle WorkOf(FakeWindow w)
        {
            var m = Monitors[Math.Max(0, Math.Min(Monitors.Count - 1, MonitorOf(w)))];
            return new Rectangle(m.Left, m.Top, m.Width, m.Height - TaskbarHeight);
        }

        int MonitorOf(FakeWindow w)
        {
            int i = PresetMath.PickMonitor(w.Rect, Monitors);
            return i < 0 ? 0 : i;
        }

        public Rectangle WorkArea(IntPtr h) { var w = Get(h); return w == null ? Rectangle.Empty : WorkOf(w); }
        public double Scale(IntPtr h) { var w = Get(h); if (w == null) return 1; int i = MonitorOf(w); return i < Scales.Count ? Scales[i] : 1; }
        public bool Restore(IntPtr h) { var w = Get(h); if (w == null) return false; w.Minimized = false; w.Maximized = false; return true; }

        public bool Move(IntPtr h, Rectangle r)
        {
            var w = Get(h);
            if (w == null) { lastError = 1400; return false; }
            if (w.Elevated && !Elevated) { lastError = 5; return false; }
            int e = w.InvisibleEdge;
            w.Rect = Rectangle.FromLTRB(r.Left + e, r.Top, r.Right - e, r.Bottom - e);
            Moves++;
            return true;
        }

        public bool Activate(IntPtr h) { if (!IsWindow(h)) return false; ForegroundHandle = h; return true; }
        public IntPtr Foreground() { return ForegroundHandle; }
        public IntPtr Root(IntPtr h) { return h; }
        public List<IntPtr> TopLevelWindows() { return Windows.Select(w => w.Handle).ToList(); }
    }

    /// <summary>枠線の代わり (置いた場所を覚えるだけ)</summary>
    public class FakeBorder : IBorder
    {
        public Rectangle[] Strips = new Rectangle[0];
        public int Places, Hides;
        public bool Disposed, Order = true;
        bool visible;
        public void Place(Rectangle[] strips, IntPtr target) { Strips = strips; visible = true; Places++; }
        public void Hide() { visible = false; Hides++; }
        public bool Visible { get { return visible; } }
        public bool OrderOk(IntPtr target) { return Order; }
        public void Dispose() { Disposed = true; visible = false; }
    }
}
